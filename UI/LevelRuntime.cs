using System;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Единый контроллер исполнения любых уровней (классических, свободных, «Ротора»,
/// вращающихся арен и «Волнового фронта»). Используется и в режиме «Уровни» (PlayView),
/// и при запуске «ТЕСТ» в редакторе (EditorView), гарантируя 100% идентичное поведение.
/// </summary>
public sealed class LevelRuntime
{
    private readonly GameCanvas canvas;
    private LevelData activeLevel;
    private bool running;
    private bool waitingForFirstDrop;
    private Vec2 rotorCenter = new(640, 360);
    private double baseR0 = 128.0;
    private int totalRings;
    private Vec2 liveWaveSource = new(640, 360);
    private double savedBallR = 12.0;
    private bool savedBallRValid;

    /// <summary>Вызывается при снятии технической паузы кликом (старт «Ротора») или пробое колец.</summary>
    public event Action StateChanged;

    public bool IsWaitingForFirstDrop => waitingForFirstDrop;
    public int InitialRingCount => totalRings;

    public LevelRuntime(GameCanvas canvas)
    {
        this.canvas = canvas;
        canvas.BallPlaced += OnBallPlaced;
        canvas.Simulated += OnSimulated;
        canvas.SourcePicked += OnSourcePicked;
    }

    public void LoadAndStart(LevelData lvl)
    {
        lvl.Sanitize();
        activeLevel = lvl;
        canvas.LoadLevel(lvl);
        ApplyRuntimeSetup();
    }

    public void RestartCurrent()
    {
        if (activeLevel == null) activeLevel = canvas.Design;
        activeLevel.Sanitize();
        ApplyRuntimeSetup(restartSim: true);
    }

    /// <summary>Выход из режима «ТЕСТ» обратно в чистое редактирование.</summary>
    public void StopToEditor()
    {
        running = false;
        waitingForFirstDrop = false;
        if (savedBallRValid)
        {
            canvas.NewBallR = savedBallR;
            savedBallRValid = false;
        }
        canvas.WaveMode = false;
        canvas.UseWaveFront = false;
        canvas.WaveSource = null;
        canvas.InstantDropNoAim = false;
        canvas.ClampDropPos = null;
        canvas.Paused = false;
        canvas.SuppressPauseBanner = false;
        canvas.PromptTitle = null;
        canvas.PromptSub = null;
        canvas.HubCenter = null;
        canvas.HubText = null;
        canvas.SpinSpeed = 0;
        canvas.RotorSkin = canvas.Design.RotorRules;
        canvas.RestartSim();
        canvas.Invalidate();
    }

    private void ApplyRuntimeSetup(bool restartSim = false)
    {
        var lvl = activeLevel ?? canvas.Design;
        running = true;
        waitingForFirstDrop = false;

        if (savedBallRValid && !lvl.WaveEmitter)
        {
            canvas.NewBallR = savedBallR;
            savedBallRValid = false;
        }

        canvas.SpinSpeed = lvl.SpinSpeed;
        canvas.RotorSkin = lvl.RotorRules;
        canvas.WaveMode = lvl.WaveEmitter;
        canvas.UseWaveFront = lvl.WaveContinuous;
        canvas.InstantDropNoAim = lvl.InstantDrop && !lvl.WaveEmitter;
        canvas.ClampDropPos = canvas.InstantDropNoAim ? ClampDropPosition : null;
        canvas.Paused = false;
        canvas.SuppressPauseBanner = false;
        canvas.PromptTitle = null;
        canvas.PromptSub = null;

        if (restartSim)
            canvas.RestartSim();

        canvas.Sim.GravityOn = lvl.Gravity;
        canvas.Sim.GravityScale = lvl.GravityScale <= 0 ? 1.0 : lvl.GravityScale;
        canvas.Sim.ElasticBounce = lvl.ElasticBounce;
        canvas.Sim.FreezeSpinInGap = lvl.FreezeSpinInGap;
        canvas.Sim.HasBorder = lvl.Border;
        canvas.Sim.BallCollisions = !lvl.NoBallCollisions;
        canvas.Sim.SpinSpeed = lvl.SpinSpeed;

        // 1. Волновой бильярд / дифракционный излучатель
        if (lvl.WaveEmitter)
        {
            if (!savedBallRValid)
            {
                savedBallR = canvas.NewBallR;
                savedBallRValid = true;
            }
            liveWaveSource = lvl.WaveSourcePos;
            EmitWave(lvl, liveWaveSource);
            canvas.HubCenter = null;
            canvas.HubText = null;
            StateChanged?.Invoke();
            canvas.Invalidate();
            return;
        }

        // 2. Геометрия колец и центрального счётчика «Ротора»
        totalRings = canvas.Sim.Arcs.Count;
        if (totalRings > 0)
        {
            rotorCenter = canvas.Sim.Arcs[0].Center;
            baseR0 = lvl.RotorBaseR0 > 10.0 ? lvl.RotorBaseR0 : canvas.Sim.Arcs[0].Radius;
        }
        else
        {
            rotorCenter = new Vec2(LevelData.W / 2, LevelData.H / 2);
            baseR0 = 128.0;
        }

        if (lvl.RotorRules && totalRings > 1)
        {
            canvas.HubCenter = rotorCenter;
            canvas.HubRadius = Math.Max(18.0, baseR0 - 8.0);
            canvas.HubText = () => canvas.Sim.Arcs.Count > 0 ? canvas.Sim.Arcs.Count.ToString() : "";
        }
        else
        {
            canvas.HubCenter = null;
            canvas.HubText = null;
        }

        // 3. Клик-старт с нулевой скоростью и технической паузой до первого шара
        if (canvas.InstantDropNoAim && lvl.Balls.Count == 0)
        {
            waitingForFirstDrop = true;
            canvas.Paused = true;
            canvas.SuppressPauseBanner = true;
            canvas.PromptTitle = "КЛИКНИТЕ ЛКМ, ЧТОБЫ ПОСТАВИТЬ ШАР";
            canvas.PromptSub = (lvl.SpinSpeed != 0 || totalRings > 0)
                ? "шар появится с нулевой скоростью и запустит вращение"
                : "шар появится в точке клика с нулевой начальной скоростью";
        }

        StateChanged?.Invoke();
        canvas.Invalidate();
    }

    private void EmitWave(LevelData lvl, Vec2 src)
    {
        if (canvas.Sim.Balls.Count > 0)
            canvas.RestartSim();

        double rayR = Math.Clamp(lvl.WaveRayRadius, 1.0, 16.0);
        int rays = Math.Clamp(lvl.WaveRays, 10, 30000);
        double speed = Math.Clamp(lvl.WaveSpeed, 40.0, 1800.0);
        bool spectrum = lvl.WaveArgb == 0;

        canvas.NewBallR = rayR;
        canvas.WaveSource = src;
        canvas.Sim.BallCollisions = false;
        canvas.Sim.GravityOn = lvl.Gravity;
        canvas.Sim.GravityScale = lvl.GravityScale <= 0 ? 1.0 : lvl.GravityScale;
        canvas.Sim.HasBorder = lvl.Border;

        if (lvl.WaveContinuous)
        {
            canvas.WaveFront.ResetFrom(lvl, src);
            canvas.WaveFront.GravityOn = lvl.Gravity;
            canvas.WaveFront.GravityScale = lvl.GravityScale <= 0 ? 1.0 : lvl.GravityScale;
            canvas.WaveFront.HasBorder = lvl.Border;
            canvas.WaveFront.EmitPointSource(src, rays, speed, spectrum, lvl.WaveArgb);
        }
        else
        {
            for (int i = 0; i < rays; i++)
            {
                double a = i * 2.0 * Math.PI / rays;
                var vel = new Vec2(Math.Cos(a), Math.Sin(a)) * speed;
                if (spectrum)
                    canvas.Sim.AddBall(src, vel, rayR, hue: (int)(i * 360.0 / rays));
                else
                    canvas.Sim.AddBall(src, vel, rayR, argb: lvl.WaveArgb);
            }
        }
    }

    private void OnSourcePicked(Vec2 picked)
    {
        if (!running || activeLevel == null || !activeLevel.WaveEmitter) return;
        liveWaveSource = picked;
        canvas.RestartSim();
        EmitWave(activeLevel, liveWaveSource);
        canvas.Paused = false;
        StateChanged?.Invoke();
        canvas.Invalidate();
    }

    private Vec2 ClampDropPosition(Vec2 p)
    {
        if (activeLevel != null && activeLevel.RotorRules && canvas.Sim.Arcs.Count > 0)
        {
            var arc0 = canvas.Sim.Arcs[0];
            double maxDist = Math.Max(4.0, arc0.Radius - canvas.NewBallR - arc0.HalfWidth - 3.5);
            var d = p - arc0.Center;
            double len = d.Len;
            if (len > maxDist && len > 1e-6)
                return arc0.Center + d * (maxDist / len);
            return p;
        }
        return new Vec2(
            Math.Clamp(p.X, canvas.NewBallR + 6, LevelData.W - canvas.NewBallR - 6),
            Math.Clamp(p.Y, canvas.NewBallR + 6, LevelData.H - canvas.NewBallR - 6));
    }

    private void OnBallPlaced(SimBall _)
    {
        if (!running) return;
        if (waitingForFirstDrop)
        {
            waitingForFirstDrop = false;
            canvas.Paused = false;
            canvas.SuppressPauseBanner = false;
            canvas.PromptTitle = null;
            canvas.PromptSub = null;
            StateChanged?.Invoke();
        }
    }

    private void OnSimulated(double dt)
    {
        if (!running || activeLevel == null || !activeLevel.RotorRules) return;
        var arcs = canvas.Sim.Arcs;
        var balls = canvas.Sim.Balls;
        if (arcs.Count == 0) return;

        int broken = 0;
        while (arcs.Count > 0 && balls.Count > 0)
        {
            var arc0 = arcs[0];
            bool lastRing = arcs.Count == 1;
            bool escaped = false;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (b.MinCenterDist >= arc0.Radius - 2.0) continue;
                double dist = Vec2.Dist(b.Pos, arc0.Center);
                double exitDist = lastRing ? (arc0.Radius + b.R + arc0.HalfWidth) : (arc0.Radius + 1.2);
                if (dist > exitDist)
                {
                    escaped = true;
                    break;
                }
            }
            if (!escaped) break;

            canvas.DissolveArc(arc0, inwardMix: !lastRing);
            broken++;
        }

        if (broken > 0)
        {
            if (activeLevel.ReverseOnBreak)
            {
                canvas.SpinSpeed = -canvas.SpinSpeed;
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }
            if (activeLevel.AccelOnBreak > 1.0001)
            {
                double sign = Math.Sign(canvas.SpinSpeed);
                double mag = Math.Abs(canvas.SpinSpeed) * Math.Pow(activeLevel.AccelOnBreak, broken);
                canvas.SpinSpeed = (sign == 0 ? 1.0 : sign) * Math.Min(3.2, mag);
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }
        }

        if (activeLevel.ContractRings && totalRings > 1 && arcs.Count > 0 && arcs[0].Radius > baseR0)
        {
            var arc0 = arcs[0];
            var c = arc0.Center;
            double minAllowedR0 = baseR0;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                double dx = b.Pos.X - c.X, dy = b.Pos.Y - c.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= arc0.Radius + 1.2 && !arc0.IsInSolid(Math.Atan2(dy, dx)))
                    minAllowedR0 = Math.Max(minAllowedR0, dist + 2.5);
            }

            double diff = arc0.Radius - minAllowedR0;
            if (diff > 0.01)
            {
                double oldR0 = arc0.Radius;
                double speed = Math.Max(240.0, diff * 8.0);
                double dr = Math.Min(diff, speed * dt);
                for (int k = 0; k < arcs.Count; k++)
                    arcs[k].Radius -= dr;

                double wallInwardV = dt > 1e-5 ? Math.Min(90.0, (dr / dt) * 0.30) : 0.0;
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    double dx = b.Pos.X - c.X, dy = b.Pos.Y - c.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double lim = arc0.Radius - b.R - arc0.HalfWidth - 0.02;
                    if (dist > lim && dist <= oldR0 + 1.2 && dist > 1e-6 && arc0.IsInSolid(Math.Atan2(dy, dx)))
                    {
                        double rx = dx / dist, ry = dy / dist;
                        b.Pos = new Vec2(c.X + rx * lim, c.Y + ry * lim);
                        double vr = b.Vel.X * rx + b.Vel.Y * ry;
                        double uWall = b.Vel.Len < 620.0 ? wallInwardV : 0.0;
                        if (vr > -uWall)
                        {
                            double newVr = -Math.Max(0.0, vr) - uWall;
                            b.Vel += new Vec2(rx, ry) * (newVr - vr);
                        }
                        if (lim < b.MinCenterDist) b.MinCenterDist = lim;
                    }
                }
            }
        }

        if (totalRings > 1 && arcs.Count > 0)
        {
            const double MaxChamberV = 1150.0;
            double damp = Math.Max(0.5, 1.0 - 3.5 * dt);
            for (int bi = 0; bi < balls.Count; bi++)
            {
                double vl = balls[bi].Vel.Len;
                if (vl > MaxChamberV)
                    balls[bi].Vel *= (MaxChamberV + (vl - MaxChamberV) * damp) / vl;
            }
        }

        if (broken > 0)
            StateChanged?.Invoke();
    }
}

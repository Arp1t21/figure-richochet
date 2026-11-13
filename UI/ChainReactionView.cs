using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Цепная реакция» (Clip 3): две точки отскакивают под гравитацией
/// и при каждом столкновении друг с другом порождают новую точку в центре
/// с экспоненциальным заполнением N = 2 → 1000 и взрывным прорывом контура.
/// </summary>
public class ChainReactionView : UserControl, IModeView
{
    public const int PresetCount = 10;

    private static readonly string[] PresetNames =
    {
        "Столкновение (N = 2)",
        "Эффект бабочки (сдвиг)",
        "Клон от стенки (+1)",
        "Цепной старт (N = 3)",
        "Треугольная камера",
        "Шестигранный улей",
        "Взрывное деление (+2)",
        "Невесомый реактор (0G)",
        "Упругий квадрат ×1.06",
        "Стенка-дубликатор (+2)",
    };

    private static readonly string[] PresetBadges =
    {
        "N=2", "ХАОС", "+1 СТЕНКА", "N=3", "3 ГРАНИ",
        "6 ГРАНЕЙ", "×2 ВЗРЫВ", "0G РЕАКТОР", "4 ГРАНИ", "+2 СТЕНКА",
    };

    private struct SpawnRequest
    {
        public Vec2 Origin;
        public int ColorIdx;
    }

    private struct PulseRing
    {
        public float X, Y;
        public float Age, Life;
        public Color Col;
    }

    private readonly Toolbar bar = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnPrev = new() { Icon = IconPainter.ChevronLeft, Size = new Size(34, 34), IconPx = 18 };
    private readonly LevelSelectButton lblPreset = new() { Size = new Size(240, 34), Role = AccentRole.Violet };
    private readonly NeoButton btnNext = new() { Icon = IconPainter.ChevronRight, Size = new Size(34, 34), IconPx = 18 };
    private readonly SliderButton targetBtn = new()
    {
        Min = 20,
        Max = 1500,
        Step = 5,
        Prefix = "ЦЕЛЬ: ",
        Format = static v => ((int)Math.Round(v)).ToString(),
        Size = new Size(100, 34),
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnPulse = new() { Text = "ПУЛЬС", IsToggle = true, IsOn = true, Role = AccentRole.Orange, Size = new Size(72, 34), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly NeoButton btnSandbox = new() { Text = "ПЕСОЧНИЦА", Icon = IconPainter.Sliders, Role = AccentRole.Accent, Size = new Size(104, 34), IconPx = 15, Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
    private readonly NeoButton btnPerturb = new() { Text = "НОВЫЙ ХАОС", Size = new Size(108, 34), Font = new Font("Segoe UI", 8.3F, FontStyle.Bold) };
    private readonly NeoButton btnRestart = new() { Text = "ЗАНОВО", Icon = IconPainter.Restart, Size = new Size(92, 34), IconPx = 16 };
    private readonly NeoButton btnPause = new() { Text = "ПАУЗА", Icon = IconPainter.Pause, IsToggle = true, Size = new Size(86, 34), IconPx = 16 };

    private readonly GameCanvas canvas = new()
    {
        Dock = DockStyle.Fill,
        RotorSkin = true,
        InstantDropNoAim = true,
        SuppressCircleDraw = true,
        SuppressWallDraw = true,
    };

    public GameCanvas Canvas => canvas;

    private int presetIndex;
    private int targetCount = 500;
    private int chaosNonce;
    private int initialBallCountOverride;
    private double ballRadiusOverride;
    private int spawnsPerHit = 1;
    private double spawnCooldownDur = 0.085;
    private double gravityScale = 0.72;
    private double wallRestitution = 1.0;
    private double ballRestitution = 1.0;
    private bool spawnAtCenter;
    private bool pulseOn = true;
    private bool wasPaused;

    private readonly Vec2 center = new(640, 338);
    private double arenaR = 276;
    private int polySides;
    private bool cloneOnWall;
    private PolyData arenaPoly;
    private PointF[] arenaPts = Array.Empty<PointF>();

    private double simTime;
    private bool shattered;
    private int nextPaletteIdx;
    private Random rng = new(42);
    private readonly Dictionary<SimBall, double> spawnCooldown = new();
    private readonly Queue<SpawnRequest> pendingSpawns = new();
    private readonly List<PulseRing> pulses = new(64);

    private readonly Pen ringGlowPen = new(Color.FromArgb(62, 105, 145, 255), 8.5f) { LineJoin = LineJoin.Round };
    private readonly Pen ringCorePen = new(Color.FromArgb(245, 118, 152, 255), 2.8f) { LineJoin = LineJoin.Round };
    private readonly Pen pulsePen = new(Color.Gold, 2.2f);
    private readonly Font counterFont = new("Segoe UI", 13F, FontStyle.Bold);
    private readonly SolidBrush counterBrush = new(Color.FromArgb(240, 244, 255));
    private readonly SolidBrush subHudBrush = new(Color.FromArgb(175, 190, 225));
    private readonly Font subHudFont = new("Segoe UI", 9.5F, FontStyle.Regular);

    public int PresetIndex => presetIndex;
    public int TargetCount
    {
        get => targetCount;
        set
        {
            targetCount = Math.Clamp(value, 20, 1500);
            targetBtn.Value = targetCount;
            canvas.Invalidate();
        }
    }
    public bool IsShattered => shattered;

    public ChainReactionView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        targetBtn.Value = targetCount;

        bar.AddLeft(btnPrev);
        bar.AddLeft(lblPreset);
        bar.AddLeft(btnNext);
        bar.AddLeft(targetBtn);
        bar.AddLeft(btnPulse);
        bar.AddLeft(btnSandbox);
        bar.AddRight(btnPerturb);
        bar.AddRight(btnPause);
        bar.AddRight(btnRestart);

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий сценарий");
        tips.SetToolTip(lblPreset, "Выбрать сценарий цепной реакции из сетки с мини-превью");
        tips.SetToolTip(btnNext, "Следующий сценарий");
        tips.SetToolTip(targetBtn, "Порог заполнения N, при котором кольцо рассыпается (ползунок и ввод)");
        tips.SetToolTip(btnPulse, "Вспышки рождения новых шаров в центре");
        tips.SetToolTip(btnSandbox, "Тонкая настройка физики и цепного спавна");
        tips.SetToolTip(btnPerturb, "Разбросать стартовые шары по новым случайным позициям");
        tips.SetToolTip(btnRestart, "Начать заново в исходной позиции (R)");
        tips.SetToolTip(btnPause, "Пауза / продолжить (Пробел)");

        lblPreset.ItemsProvider = () =>
        {
            var items = new List<LevelCardItem>(PresetCount);
            for (int i = 0; i < PresetCount; i++)
            {
                items.Add(new LevelCardItem
                {
                    Title = $"{i + 1}. {PresetNames[i]}",
                    Badge = PresetBadges[i],
                    Preview = CreatePreviewLevel(i),
                    Role = AccentRole.Violet,
                });
            }
            return (presetIndex, items);
        };
        lblPreset.LevelSelected += ShowPreset;

        btnPrev.Click += (_, _) => ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
        btnNext.Click += (_, _) => ShowPreset((presetIndex + 1) % PresetCount);
        targetBtn.Changed += v => { targetCount = Math.Clamp((int)Math.Round(v), 20, 1500); canvas.Invalidate(); };
        btnPulse.Click += (_, _) => { pulseOn = btnPulse.IsOn; canvas.Invalidate(); };
        SandboxPopup.Attach(btnSandbox, "Песочница · Цепная реакция", BuildSandboxEntries);
        btnPerturb.Click += (_, _) => RandomizeChaos();
        btnRestart.Click += (_, _) => ShowPreset(presetIndex);
        btnPause.Click += (_, _) => SetPaused(btnPause.IsOn);

        canvas.ClampDropPos = ClampInside;
        canvas.Sim.BallsCollided += OnBallsCollided;
        canvas.Sim.WallBounced += OnWallBounced;
        canvas.Simulated += OnSimulated;
        canvas.BallPlaced += OnBallPlaced;
        canvas.RenderOverlay += OnRenderOverlay;
        canvas.RenderScreenOverlay += OnRenderScreenOverlay;

        Action onTheme = () =>
        {
            BackColor = Theme.Bg;
            bar.BackColor = Theme.Panel;
            bar.Invalidate(true);
            Invalidate(true);
        };
        Theme.Changed += onTheme;
        Disposed += (_, _) =>
        {
            Theme.Changed -= onTheme;
            canvas.Sim.BallsCollided -= OnBallsCollided;
            canvas.Sim.WallBounced -= OnWallBounced;
            canvas.Simulated -= OnSimulated;
            canvas.BallPlaced -= OnBallPlaced;
            canvas.RenderOverlay -= OnRenderOverlay;
            canvas.RenderScreenOverlay -= OnRenderScreenOverlay;
            ringGlowPen.Dispose();
            ringCorePen.Dispose();
            pulsePen.Dispose();
            counterFont.Dispose();
            counterBrush.Dispose();
            subHudBrush.Dispose();
            subHudFont.Dispose();
        };

        Controls.Add(canvas);
        Controls.Add(bar);

        ShowPreset(0);
    }

    public bool GravityOn
    {
        get => canvas.Sim.GravityOn;
        set { canvas.Sim.GravityOn = value; canvas.Design.Gravity = value; canvas.Invalidate(); }
    }

    public bool TrailsOn
    {
        get => canvas.Trails;
        set { canvas.Trails = value; canvas.Invalidate(); }
    }

    public void SetActive(bool active)
    {
        if (!active)
        {
            wasPaused = canvas.Paused;
            canvas.Paused = true;
        }
        else
        {
            canvas.Paused = wasPaused;
            canvas.Invalidate();
        }
    }

    public bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.R:
                ShowPreset(presetIndex);
                return true;
            case Keys.Space:
                SetPaused(!canvas.Paused);
                return true;
            case Keys.G:
                GravityOn = !GravityOn;
                return true;
        }
        return false;
    }

    private void SetPaused(bool p)
    {
        canvas.Paused = p;
        btnPause.IsOn = p;
        btnPause.Icon = p ? IconPainter.Play : IconPainter.Pause;
        btnPause.Text = p ? "ПУСК" : "ПАУЗА";
        btnPause.Invalidate();
        canvas.Invalidate();
    }

    public void ShowPreset(int index)
    {
        int target = Math.Clamp(index, 0, PresetCount - 1);
        if (target != presetIndex) ResetSandboxDefaults(target);
        BuildPreset(target, randomizeStart: false);
    }

    /// <summary>Перезапуск со случайными непересекающимися позициями и скоростями шаров («Новый хаос»).</summary>
    public void RandomizeChaos() => BuildPreset(presetIndex, randomizeStart: true);

    private void ResetSandboxDefaults(int targetPreset)
    {
        initialBallCountOverride = 0;
        ballRadiusOverride = 0;
        spawnsPerHit = (targetPreset == 6 || targetPreset == 9) ? 2 : 1;
        spawnCooldownDur = 0.085;
        gravityScale = targetPreset == 7 ? 0.0 : 0.72;
        wallRestitution = targetPreset == 8 ? 1.06 : 1.0;
        ballRestitution = 1.0;
        spawnAtCenter = targetPreset == 1 || targetPreset == 2 || targetPreset == 6 || targetPreset == 9;
    }

    private double BallRadiusForTarget()
    {
        if (ballRadiusOverride > 0) return ballRadiusOverride;
        return TargetCount switch
        {
            <= 120 => 11.5,
            <= 250 => 10.0,
            <= 500 => 8.0,
            _ => 6.2,
        };
    }

    private void ApplySandboxPhysics()
    {
        canvas.Sim.GravityScale = gravityScale;
        canvas.Sim.WallRestitution = wallRestitution;
        canvas.Sim.BallRestitution = ballRestitution;
        canvas.Design.GravityScale = gravityScale;
        canvas.Design.WallRestitution = wallRestitution;
        canvas.Design.BallRestitution = ballRestitution;
        canvas.Invalidate();
    }

    private Vec2 CurrentArenaCenter() => polySides == 3 ? center + new Vec2(0, 28) : center;

    private static int DefaultInitialBalls(int p) => p switch
    {
        2 => 1,
        3 or 5 => 3,
        7 => 4,
        _ => 2,
    };

    private static (int Sides, double R, double Rot) ArenaSpec(int p) => p switch
    {
        4 => (3, 306, -Math.PI / 2),
        5 => (6, 294, -Math.PI / 2),
        8 or 9 => (4, 326, Math.PI / 4),
        _ => (0, 276, 0),
    };

    private static LevelData CreatePreviewLevel(int p)
    {
        var (sides, r, rot) = ArenaSpec(p);
        var c = sides == 3 ? new Vec2(640, 366) : new Vec2(640, 338);
        var lvl = new LevelData { Name = PresetNames[Math.Clamp(p, 0, PresetCount - 1)] };
        if (sides >= 3)
            lvl.Walls.Add(new PolyData { Closed = true, Pts = LevelShapes.RegularPolygonPts(c, r, sides, rot), Pivot = c });
        else
            lvl.Circles.Add(new CircleData { Center = c, Radius = r });
        return lvl;
    }

    private void SpawnSandboxBurst(int count)
    {
        double br = BallRadiusForTarget();
        var arenaC = CurrentArenaCenter();
        var used = new List<Vec2>(canvas.Sim.Balls.Count + count);
        foreach (var b in canvas.Sim.Balls) used.Add(b.Pos);
        for (int i = 0; i < count; i++)
        {
            if (canvas.Sim.Balls.Count >= TargetCount) break;
            var pos = SampleRandomInside(arenaC, br, used, rng);
            used.Add(pos);
            double ang = rng.NextDouble() * Math.PI * 2.0;
            double sp = 160.0 + rng.NextDouble() * 140.0;
            var nb = canvas.Sim.AddBall(pos, new Vec2(Math.Cos(ang) * sp, Math.Sin(ang) * sp), br,
                argb: GameCanvas.SpectrumPalette24[(nextPaletteIdx++) % GameCanvas.SpectrumPalette24.Length]);
            spawnCooldown[nb] = simTime + spawnCooldownDur;
        }
        canvas.Invalidate();
    }

    private List<SandboxEntry> BuildSandboxEntries()
    {
        int defInitial = DefaultInitialBalls(presetIndex);
        return new List<SandboxEntry>
        {
            new SandboxSlider { Label = "Цель шаров (N)", Min = 20, Max = 1500, Step = 5, Get = () => targetCount, Set = v => { TargetCount = (int)Math.Round(v); } },
            new SandboxSlider { Label = "Стартовых шаров", Min = 1, Max = 12, Step = 1, Get = () => initialBallCountOverride > 0 ? initialBallCountOverride : defInitial, Set = v => { initialBallCountOverride = Math.Clamp((int)Math.Round(v), 1, 12); } },
            new SandboxSlider { Label = "Радиус шара", Min = 3.0, Max = 24.0, Step = 0.5, Format = v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), Get = () => BallRadiusForTarget(), Set = v => { ballRadiusOverride = v; canvas.NewBallR = v; foreach (var b in canvas.Sim.Balls) b.R = v; canvas.Invalidate(); } },
            new SandboxSlider { Label = "Рождений за удар", Min = 1, Max = 4, Step = 1, Get = () => spawnsPerHit, Set = v => spawnsPerHit = Math.Clamp((int)Math.Round(v), 1, 4) },
            new SandboxSlider { Label = "Пауза спавна (с)", Min = 0.01, Max = 0.35, Step = 0.01, Get = () => spawnCooldownDur, Set = v => spawnCooldownDur = v },
            new SandboxSlider { Label = "Гравитация", Min = 0, Max = 2.5, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => gravityScale, Set = v => { gravityScale = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Упругость стен", Min = 0.2, Max = 1.35, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => wallRestitution, Set = v => { wallRestitution = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Упругость шаров", Min = 0.2, Max = 1.35, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => ballRestitution, Set = v => { ballRestitution = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Скорость симуляции", Min = 0.1, Max = 4.0, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => GameCanvas.SimSpeed, Set = v => { GameCanvas.SimSpeed = v; canvas.Invalidate(); } },
            new SandboxToggle { Label = "Клон при ударе о стену", Get = () => cloneOnWall, Set = v => cloneOnWall = v },
            new SandboxToggle { Label = "Рождение в центре", Get = () => spawnAtCenter, Set = v => spawnAtCenter = v },
            new SandboxToggle { Label = "Пульс вспышки", Get = () => pulseOn, Set = v => { pulseOn = v; btnPulse.IsOn = v; btnPulse.Invalidate(); } },
            new SandboxAction { Label = "+ 5 шаров", Role = AccentRole.Green, RefreshAllAfter = false, Run = () => SpawnSandboxBurst(5) },
            new SandboxAction { Label = "Новый хаос", Role = AccentRole.Accent, RefreshAllAfter = false, Run = RandomizeChaos },
            new SandboxAction { Label = "Сбросить параметры", Role = AccentRole.Orange, Run = () => { GameCanvas.SimSpeed = 1.0; ResetSandboxDefaults(presetIndex); TargetCount = 500; BuildPreset(presetIndex, false); } },
        };
    }

    private Vec2 SampleRandomInside(Vec2 arenaC, double br, List<Vec2> used, Random rand)
    {
        for (int attempt = 0; attempt < 48; attempt++)
        {
            Vec2 cand;
            if (polySides >= 3 && arenaPoly != null && arenaPoly.Pts.Count >= 3)
            {
                int n = arenaPoly.Pts.Count;
                int seg = rand.Next(n);
                double u = rand.NextDouble();
                double v = rand.NextDouble();
                if (u + v > 1.0) { u = 1.0 - u; v = 1.0 - v; }
                var p = arenaC * (1.0 - u - v) + arenaPoly.Pts[seg] * u + arenaPoly.Pts[(seg + 1) % n] * v;
                cand = arenaC + (p - arenaC) * 0.68;
            }
            else
            {
                double maxR = Math.Max(16, arenaR - br - 18) * 0.84;
                double rad = Math.Sqrt(rand.NextDouble()) * maxR;
                double ang = rand.NextDouble() * Math.PI * 2.0;
                cand = new Vec2(arenaC.X + Math.Cos(ang) * rad, arenaC.Y + Math.Sin(ang) * rad);
            }

            double minSep = br * 2.6 + 6.0;
            bool ok = true;
            for (int i = 0; i < used.Count; i++)
            {
                if ((cand - used[i]).Len < minSep) { ok = false; break; }
            }
            if (ok) return cand;
        }
        return arenaC + new Vec2((rand.NextDouble() - 0.5) * 40, (rand.NextDouble() - 0.5) * 40);
    }

    private void BuildPreset(int index, bool randomizeStart)
    {
        presetIndex = Math.Clamp(index, 0, PresetCount - 1);
        var (sides, rad, rot) = ArenaSpec(presetIndex);
        polySides = sides;
        arenaR = rad;
        cloneOnWall = presetIndex == 2 || presetIndex == 9;

        var lvl = new LevelData
        {
            Border = false,
            Gravity = true,
            GravityScale = gravityScale,
            WallRestitution = wallRestitution,
            BallRestitution = ballRestitution,
            NoBallCollisions = false,
        };

        var arenaC = CurrentArenaCenter();
        if (polySides >= 3)
        {
            var pts = LevelShapes.RegularPolygonPts(arenaC, arenaR, polySides, rot);
            arenaPoly = new PolyData { Closed = true, Pts = pts, Pivot = arenaC };
            lvl.Walls.Add(arenaPoly);
            arenaPts = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++) arenaPts[i] = new PointF((float)pts[i].X, (float)pts[i].Y);
        }
        else
        {
            arenaPoly = null;
            arenaPts = Array.Empty<PointF>();
            lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        }

        lvl.Name = $"{presetIndex + 1}/{PresetCount} · {PresetNames[presetIndex]}";
        lblPreset.Text = lvl.Name;

        canvas.Trails = false;
        double br = BallRadiusForTarget();
        canvas.NewBallR = br;
        canvas.LoadLevel(lvl);

        simTime = 0;
        shattered = false;
        nextPaletteIdx = 3;
        int seed = randomizeStart
            ? unchecked(Environment.TickCount * 397 ^ (++chaosNonce) * 7919 ^ Guid.NewGuid().GetHashCode())
            : 1009 + presetIndex * 137;
        rng = new Random(seed);
        spawnCooldown.Clear();
        pendingSpawns.Clear();
        pulses.Clear();

        int defCount = DefaultInitialBalls(presetIndex);
        int startCount = initialBallCountOverride > 0 ? initialBallCountOverride : defCount;

        if (randomizeStart)
        {
            var used = new List<Vec2>(startCount);
            for (int i = 0; i < startCount; i++)
            {
                var pos = SampleRandomInside(arenaC, br, used, rng);
                used.Add(pos);
                double ang = rng.NextDouble() * Math.PI * 2.0;
                double sp = 175.0 + rng.NextDouble() * 145.0;
                int argb = GameCanvas.SpectrumPalette24[(i * 6 + rng.Next(24)) % GameCanvas.SpectrumPalette24.Length];
                var b = canvas.Sim.AddBall(pos, new Vec2(Math.Cos(ang) * sp, Math.Sin(ang) * sp), br, argb: argb);
                spawnCooldown[b] = 0.05;
            }
        }
        else if (initialBallCountOverride > 0 || presetIndex >= 5)
        {
            for (int i = 0; i < startCount; i++)
            {
                double a = -Math.PI * 0.5 + i * (Math.PI * 2.0 / startCount);
                double rr = startCount == 1 ? 0 : Math.Min(arenaR * 0.38, 112);
                var pos = arenaC + new Vec2(Math.Cos(a) * rr, Math.Sin(a) * rr - (startCount == 1 ? 80 : 0));
                double va = a + Math.PI * 0.44;
                int argb = i == 0 ? 0 : GameCanvas.SpectrumPalette24[(i * 5) % GameCanvas.SpectrumPalette24.Length];
                var b = canvas.Sim.AddBall(pos, new Vec2(Math.Cos(va) * 215, Math.Sin(va) * 215), br, argb: argb);
                spawnCooldown[b] = 0.05;
            }
        }
        else if (presetIndex == 2)
        {
            var b = canvas.Sim.AddBall(new Vec2(center.X - 35, center.Y - 120), new Vec2(210, 35), br, argb: 0);
            spawnCooldown[b] = 0.05;
        }
        else if (presetIndex == 3)
        {
            var b1 = canvas.Sim.AddBall(new Vec2(center.X - 110, center.Y - 40), new Vec2(160, -85), br, argb: 0);
            var b2 = canvas.Sim.AddBall(new Vec2(center.X + 110, center.Y - 30), new Vec2(-165, -75), br, argb: GameCanvas.SpectrumPalette24[5]);
            var b3 = canvas.Sim.AddBall(new Vec2(center.X, center.Y - 145), new Vec2(75, 110), br, argb: GameCanvas.SpectrumPalette24[12]);
            spawnCooldown[b1] = 0.05; spawnCooldown[b2] = 0.05; spawnCooldown[b3] = 0.05;
        }
        else
        {
            double vxOff = presetIndex == 1 ? 45.0 : 0.0;
            var b1 = canvas.Sim.AddBall(new Vec2(center.X - 122, center.Y - 36), new Vec2(215 + vxOff, -42), br, argb: 0);
            var b2 = canvas.Sim.AddBall(new Vec2(center.X + 126, center.Y - 22), new Vec2(-205, -58), br, argb: 0);
            spawnCooldown[b1] = 0.05; spawnCooldown[b2] = 0.05;
        }

        SetPaused(false);
        bar.Relayout();
        canvas.Invalidate();
    }

    private Vec2 ClampInside(Vec2 p)
    {
        if (shattered) return p;
        double maxD = Math.Max(20, (polySides >= 3 ? arenaR * Math.Cos(Math.PI / polySides) : arenaR) - canvas.NewBallR - 8);
        var c = CurrentArenaCenter();
        var d = p - c;
        if (d.Len > maxD) return c + d.Norm() * maxD;
        return p;
    }

    private void OnBallPlaced(SimBall b)
    {
        int pal = (nextPaletteIdx++) % GameCanvas.SpectrumPalette24.Length;
        b.Argb = GameCanvas.SpectrumPalette24[pal];
        double ang = rng.NextDouble() * Math.PI * 2.0;
        b.Vel = new Vec2(Math.Cos(ang) * 190, Math.Sin(ang) * 190);
        spawnCooldown[b] = simTime + 0.08;
    }

    private void OnBallsCollided(SimBall a, SimBall b, Vec2 contactPt)
    {
        if (shattered || cloneOnWall) return;
        int projected = canvas.Sim.Balls.Count + pendingSpawns.Count;
        if (projected >= TargetCount) return;

        spawnCooldown.TryGetValue(a, out double ta);
        spawnCooldown.TryGetValue(b, out double tb);
        if (simTime < ta || simTime < tb) return;

        int batchCap = projected < 24 ? 2 * spawnsPerHit : projected < 120 ? 5 * spawnsPerHit : 14 * spawnsPerHit;
        if (pendingSpawns.Count >= batchCap) return;

        spawnCooldown[a] = simTime + spawnCooldownDur;
        spawnCooldown[b] = simTime + spawnCooldownDur;
        var origin = spawnAtCenter ? new Vec2(center.X, center.Y - 40) : contactPt;
        for (int k = 0; k < spawnsPerHit && projected + k < TargetCount; k++)
        {
            pendingSpawns.Enqueue(new SpawnRequest
            {
                Origin = origin,
                ColorIdx = nextPaletteIdx++ % GameCanvas.SpectrumPalette24.Length,
            });
        }
    }

    private void OnWallBounced(SimBall b, Vec2 contactPt, Vec2 inwardNormal)
    {
        if (shattered || !cloneOnWall) return;
        int projected = canvas.Sim.Balls.Count + pendingSpawns.Count;
        if (projected >= TargetCount) return;

        spawnCooldown.TryGetValue(b, out double tb);
        if (simTime < tb) return;

        int batchCap = projected < 24 ? 2 * spawnsPerHit : projected < 120 ? 6 * spawnsPerHit : 16 * spawnsPerHit;
        if (pendingSpawns.Count >= batchCap) return;

        spawnCooldown[b] = simTime + Math.Min(spawnCooldownDur, 0.07);
        var origin = spawnAtCenter ? new Vec2(center.X, center.Y - 42) : contactPt + inwardNormal * (b.R * 2.1 + 4);
        for (int k = 0; k < spawnsPerHit && projected + k < TargetCount; k++)
        {
            pendingSpawns.Enqueue(new SpawnRequest
            {
                Origin = origin,
                ColorIdx = nextPaletteIdx++ % GameCanvas.SpectrumPalette24.Length,
            });
        }
    }

    private void OnSimulated(double sdt)
    {
        if (sdt <= 0) return;
        simTime += sdt;

        float fdt = (float)sdt;
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            var pr = pulses[i];
            pr.Age += fdt;
            if (pr.Age >= pr.Life) pulses.RemoveAt(i);
            else pulses[i] = pr;
        }

        double br = BallRadiusForTarget();
        var fallbackCenter = polySides == 3 ? center + new Vec2(0, 28) : new Vec2(center.X, center.Y - 36);

        while (pendingSpawns.Count > 0 && canvas.Sim.Balls.Count < TargetCount && !shattered)
        {
            var req = pendingSpawns.Dequeue();
            Vec2 pos;
            if (spawnAtCenter)
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                double offR = rng.NextDouble() * 44.0;
                pos = new Vec2(fallbackCenter.X + Math.Cos(ang) * offR, fallbackCenter.Y + Math.Sin(ang) * offR);
            }
            else
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                double offR = rng.NextDouble() * 14.0;
                pos = ClampInside(new Vec2(req.Origin.X + Math.Cos(ang) * offR, req.Origin.Y + Math.Sin(ang) * offR));
            }
            double vAng = rng.NextDouble() * Math.PI * 2.0;
            double vSp = 150.0 + rng.NextDouble() * 140.0;
            int argb = GameCanvas.SpectrumPalette24[req.ColorIdx];
            var nb = canvas.Sim.AddBall(pos, new Vec2(Math.Cos(vAng) * vSp, Math.Sin(vAng) * vSp), br, argb: argb);
            spawnCooldown[nb] = simTime + spawnCooldownDur * 1.35;

            if (pulseOn)
            {
                pulses.Add(new PulseRing
                {
                    X = (float)pos.X, Y = (float)pos.Y,
                    Age = 0f, Life = 0.42f,
                    Col = Color.FromArgb(255, Color.FromArgb(argb)),
                });
            }
        }
        pendingSpawns.Clear();

        // Слегка подпитываем шары, если при сверхплотной упаковке их скорость упала до 0
        if (!shattered)
        {
            foreach (var b in canvas.Sim.Balls)
            {
                if (b.Vel.Len2 < 900.0 && Math.Abs(b.Pos.Y - (center.Y + arenaR - b.R)) > 12.0)
                {
                    b.Vel.X += (rng.NextDouble() - 0.5) * 40.0;
                }
            }
        }

        if (!shattered && canvas.Sim.Balls.Count >= TargetCount)
            TriggerRingShatter();
    }

    private void TriggerRingShatter()
    {
        shattered = true;
        canvas.Sim.ClearCircles();
        canvas.Design.Circles.Clear();
        if (arenaPoly != null)
        {
            canvas.DissolveWall(arenaPoly);
            canvas.Design.Walls.Remove(arenaPoly);
        }

        // Рассыпаем синий контур окружности на множество вращающихся неоновых сегментов
        if (polySides == 0)
        {
            const int segs = 36;
            var ringCol = Color.FromArgb(128, 172, 255);
            for (int i = 0; i < segs; i++)
            {
                double a = (i + 0.5) * (Math.PI * 2.0 / segs);
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float mx = (float)center.X + ca * (float)arenaR;
                float my = (float)center.Y + sa * (float)arenaR;
                float sp = 160f + (float)rng.NextDouble() * 240f;
                float tan = (float)(rng.NextDouble() - 0.5) * 90f;
                canvas.EmitShard(
                    mx, my,
                    ca * sp - sa * tan, sa * sp + ca * tan,
                    (float)(a + Math.PI * 0.5),
                    (float)(rng.NextDouble() - 0.5) * 9f,
                    (float)(arenaR * Math.PI / segs * 0.85),
                    ringCol, 0.85);
            }
        }

        canvas.SpawnGoalConfetti(center);
        canvas.Invalidate();
    }

    private void OnRenderOverlay(Graphics g, LevelData lvl)
    {
        for (int i = 0; i < pulses.Count; i++)
        {
            var pr = pulses[i];
            float u = Math.Clamp(pr.Age / pr.Life, 0f, 1f);
            float rad = 6f + 26f * u;
            int alpha = Math.Clamp((int)((1f - u) * 210f), 0, 220);
            pulsePen.Color = Color.FromArgb(alpha, pr.Col);
            g.DrawEllipse(pulsePen, pr.X - rad, pr.Y - rad, rad * 2f, rad * 2f);
        }

        if (shattered) return;
        ringGlowPen.Color = Color.FromArgb(66, Theme.Mix(Theme.Accent, Color.FromArgb(115, 148, 255), 0.65f));
        ringCorePen.Color = Theme.Mix(Theme.Accent, Color.FromArgb(135, 168, 255), 0.72f);

        if (polySides >= 3 && arenaPts.Length >= 3)
        {
            g.DrawPolygon(ringGlowPen, arenaPts);
            g.DrawPolygon(ringCorePen, arenaPts);
        }
        else
        {
            float r = (float)arenaR;
            g.DrawEllipse(ringGlowPen, (float)center.X - r, (float)center.Y - r, r * 2f, r * 2f);
            g.DrawEllipse(ringCorePen, (float)center.X - r, (float)center.Y - r, r * 2f, r * 2f);
        }
    }

    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center };

    private void OnRenderScreenOverlay(Graphics g)
    {
        double bottomOffset = polySides >= 3 ? arenaR * Math.Cos(Math.PI / polySides) + (polySides == 3 ? 28 : 0) : arenaR;
        var anchor = canvas.WorldToScreen(new Vec2(center.X, center.Y + Math.Max(bottomOffset, 240) + 22));
        string main = shattered
            ? $"N = {canvas.Sim.Balls.Count}  —  ПРОРЫВ!"
            : $"N = {canvas.Sim.Balls.Count}";
        g.DrawString(main, counterFont, counterBrush, anchor.X, anchor.Y, CenterFmt);

        string sub = shattered
            ? "R — новый запуск"
            : $"цель: {TargetCount}  ·  клик — добавить шар";
        g.DrawString(sub, subHudFont, subHudBrush, anchor.X, anchor.Y + 24f * canvas.ViewScale, CenterFmt);
    }
}

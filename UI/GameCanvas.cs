using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

public enum Tool
{
    Select,      // выбор / перемещение / свойства объекта
    Eraser,      // ластик
    WallFree,    // стенка — рисование от руки
    WallLine,    // стенка — отрезок
    WallRect,    // стенка — прямоугольник
    WallEllipse, // стенка — эллипс
    WallCircle,  // стенка — точный аналитический круг (CircleData)
    WallPoly,    // стенка — правильный многоугольник
    WallStar,    // стенка — звезда
    ArcRing,     // аналитическое кольцо с вырезом («Ротор», ArcData)
    GoalFree,    // цель — рисование от руки
    GoalRect,    // цель — прямоугольник
    GoalEllipse, // цель — эллипс
    GoalPoly,    // цель — правильный многоугольник
    Ball,        // шар
    WaveSource,  // точечный источник волнового фронта
}

/// <summary>Маркер выделения точечного источника волны на холсте редактора.</summary>
public sealed class WaveSourceHandle
{
    public static readonly WaveSourceHandle Instance = new();
    private WaveSourceHandle() { }
}

/// <summary>
/// Игровое полотно. Мир 1280×720 рендерится в кадровый буфер того же размера,
/// затем поверх проходит программная пост-обработка (Fx: свечение, аберрации,
/// виньетка, зерно) и кадр вписывается в контрол. Режим уровней (Editor=false),
/// редактор (Editor=true, Testing — прогон) и «волновой фронт» (WaveMode).
/// Все перья/кисти созданы один раз: на каждый кадр ничего не аллоцируется.
/// </summary>
public class GameCanvas : Control
{
    public const double AimPower = 3.2; // сила броска = длина натяжения * коэффициент

    public LevelData Design = new();
    public Simulation Sim = new();
    public bool Editor;
    public bool Testing;
    public bool Paused;
    public bool ShowGrid = true;
    public bool SnapOn;
    public const double SnapStep = 20;

    /// <summary>Уровни: скорость шара неизменна — задаётся только направлением броска.</summary>
    public const double PlayFixedSpeed = 800;
    /// <summary>Редактор: скорость, назначаемая новым шарам и выбранному шару.</summary>
    public double EditorBallSpeed = 800;

    /// <summary>Скин «Ротора»: кольца и осколки розовые (как в первоисточнике),
    /// а не синие, как обычные стенки.</summary>
    public bool RotorSkin;

    /// <summary>Тёмный диск-счётчик в центре «Ротора»: сколько кругов осталось пройти.</summary>
    public Vec2? HubCenter;
    public double HubRadius;
    public Func<string> HubText;

    /// <summary>Пост-обработка кадра («шейдеры»): свечение, аберрация, виньетка, зерно.</summary>
    public bool FxOn = true;
    /// <summary>Множитель скорости симуляции (0.01–5×) — применяется на лету во всех режимах.</summary>
    public static double SimSpeed = 1.0;
    /// <summary>Угловая скорость вращения стенок и дуг вокруг их осей (рад/с; отрицательная — против часовой стрелки).</summary>
    public double SpinSpeed;

    // ------------------------------------------------- анимация рассыпания колец и стенок

    private struct Shard
    {
        public float X, Y, VX, VY;
        public float Angle, Spin, HalfLen; // HalfLen == 0 — искра-точка, > 0 — вращающийся осколок дуги
        public double Born, Life;
        public bool Pink;
    }

    private readonly List<Shard> shards = new(512);
    private int dissolveSeed = 1;
    private double fxTime; // монотонное время эффектов (не сбрасывается каждый кадр!)
    private double uiTime; // монотонное время пульсации целей/источника

    private static readonly Pen[] BlueShardPens = MakeShardPens(94, 197, 255);
    private static readonly Pen[] RotorShardPens = MakeShardPens(248, 120, 242);
    private static readonly SolidBrush[] SparkBrushes = MakeSparkBrushes();

    private static Pen[] MakeShardPens(byte r, byte g, byte b)
    {
        var pens = new Pen[16];
        for (int i = 0; i < pens.Length; i++)
            pens[i] = new Pen(Color.FromArgb(18 + i * 15, r, g, b), 3.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
        return pens;
    }

    private static SolidBrush[] MakeSparkBrushes()
    {
        var br = new SolidBrush[16];
        for (int i = 0; i < br.Length; i++)
            br[i] = new SolidBrush(Color.FromArgb(18 + i * 15, 255, 205, 252));
        return br;
    }

    private static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
            x ^= x >> 22;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Количество живых осколков анимации рассыпания (для самопроверки).</summary>
    public int ActiveShardCount => shards.Count;

    // ------------------------------------------------- замедление времени и неоновое конфетти при достижении цели

    /// <summary>Включить сильное замедление (slow-mo) и выброс неонового конфетти при касании зелёной цели.</summary>
    public bool GoalCelebrationOn;

    /// <summary>Текущий множитель замедления времени от достижения цели (1.0 — обычная скорость).</summary>
    public double GoalTimeScale { get; private set; } = 1.0;

    /// <summary>Количество активных частиц праздничного конфетти (для самопроверки).</summary>
    public int ActiveConfettiCount => confetti.Count;

    private struct ConfettiPiece
    {
        public float X, Y, VX, VY;
        public float Angle, Spin, TiltPhase, TiltSpeed;
        public float Size;
        public byte ShapeKind; // 0 = 3D-лента, 1 = неоновый ромб, 2 = треугольная призма, 3 = искра со шлейфом
        public byte R, G, B;
        public float Age, Life;
    }

    private struct GoalShockwave
    {
        public float X, Y;
        public float R0, R1;
        public byte R, G, B;
        public float Age, Life;
    }

    private readonly List<ConfettiPiece> confetti = new(192);
    private readonly List<GoalShockwave> goalWaves = new(8);
    private readonly PointF[] confettiPoly4 = new PointF[4];
    private readonly PointF[] confettiPoly3 = new PointF[3];
    private bool goalSlowMoActive;
    private double goalSlowMoElapsed;
    private int confettiSeed = 7;

    private void OnSimGoalReached(SimBall ball, Vec2 goalCenter)
    {
        if (!GoalCelebrationOn || !SimActive) return;
        goalSlowMoActive = true;
        goalSlowMoElapsed = 0.0;
        GoalTimeScale = 0.08; // сильное рапид-замедление (в 12.5 раз медленнее!)
        Vec2 origin = (ball.Pos + goalCenter) * 0.5;
        SpawnGoalConfetti(origin);
    }

    public void SpawnGoalConfetti(Vec2 origin)
    {
        int seed = confettiSeed++ * 1319;
        ReadOnlySpan<(byte R, byte G, byte B)> palette = stackalloc (byte, byte, byte)[]
        {
            (64, 225, 140),   // неоновый изумруд (цвет цели)
            (94, 197, 255),   // кибер-голубой
            (166, 128, 255),  // неоновый фиолет
            (255, 176, 88),   // тёплый янтарь
            (246, 118, 240),  // розовый неон
            (255, 228, 108),  // золотая искра
            (238, 246, 255),  // холодный белый
        };

        goalWaves.Add(new GoalShockwave
        {
            X = (float)origin.X, Y = (float)origin.Y,
            R0 = 14f, R1 = 215f,
            R = 64, G = 225, B = 140,
            Age = 0f, Life = 0.85f,
        });
        goalWaves.Add(new GoalShockwave
        {
            X = (float)origin.X, Y = (float)origin.Y,
            R0 = 8f, R1 = 145f,
            R = 94, G = 197, B = 255,
            Age = 0f, Life = 0.65f,
        });

        const int count = 92;
        for (int i = 0; i < count; i++)
        {
            float h1 = Hash01(seed + i * 19);
            float h2 = Hash01(seed + i * 43);
            float h3 = Hash01(seed + i * 71);
            float h4 = Hash01(seed + i * 107);

            double ang = (i + (h1 - 0.5f) * 0.85) * (Math.PI * 2.0 / count);
            byte shape = (byte)(i % 4 == 3 ? 3 : (i % 3));
            float speed = shape == 3
                ? 240f + 410f * h2
                : 115f + 380f * MathF.Pow(h2, 0.75f);

            float vx = (float)Math.Cos(ang) * speed;
            float vy = (float)Math.Sin(ang) * speed - (65f + 110f * h3); // лёгкий фонтан вверх
            var col = palette[(i + (int)(h4 * 7)) % palette.Length];

            confetti.Add(new ConfettiPiece
            {
                X = (float)origin.X + (h1 - 0.5f) * 10f,
                Y = (float)origin.Y + (h2 - 0.5f) * 10f,
                VX = vx,
                VY = vy,
                Angle = h3 * MathF.PI * 2f,
                Spin = (h4 - 0.5f) * 11f,
                TiltPhase = h1 * MathF.PI * 2f,
                TiltSpeed = 6.5f + h2 * 9.5f,
                Size = shape == 3 ? (3.2f + h3 * 2.4f) : (5.2f + h3 * 4.4f),
                ShapeKind = shape,
                R = col.R,
                G = col.G,
                B = col.B,
                Age = 0f,
                Life = shape == 3 ? (0.75f + 0.55f * h4) : (1.35f + 0.95f * h4),
            });
        }
    }

    private void StepGoalCelebration(double dt)
    {
        if (goalSlowMoActive)
        {
            goalSlowMoElapsed += dt;
            const double holdDur = 0.85;
            const double recoverDur = 0.80;
            if (goalSlowMoElapsed < holdDur)
            {
                GoalTimeScale = 0.08;
            }
            else if (goalSlowMoElapsed < holdDur + recoverDur)
            {
                double u = (goalSlowMoElapsed - holdDur) / recoverDur;
                double s = u * u * (3.0 - 2.0 * u);
                GoalTimeScale = 0.08 + (1.0 - 0.08) * s;
            }
            else
            {
                goalSlowMoActive = false;
                GoalTimeScale = 1.0;
            }
        }

        float fdt = (float)dt;
        for (int i = goalWaves.Count - 1; i >= 0; i--)
        {
            var w = goalWaves[i];
            w.Age += fdt;
            if (w.Age >= w.Life) goalWaves.RemoveAt(i);
            else goalWaves[i] = w;
        }

        if (confetti.Count > 0)
        {
            float drag = MathF.Exp(-2.35f * fdt);
            for (int i = confetti.Count - 1; i >= 0; i--)
            {
                var c = confetti[i];
                c.Age += fdt;
                if (c.Age >= c.Life) { confetti.RemoveAt(i); continue; }
                c.VX *= drag;
                c.VY = c.VY * drag + 275f * fdt;
                float flutter = MathF.Sin(c.TiltPhase + c.Age * 5.5f) * 32f * fdt;
                c.X += c.VX * fdt + flutter;
                c.Y += c.VY * fdt;
                c.Angle += c.Spin * fdt;
                c.TiltPhase += c.TiltSpeed * fdt;
                confetti[i] = c;
            }
        }
    }

    private void DrawGoalCelebration(Graphics g)
    {
        for (int i = 0; i < goalWaves.Count; i++)
        {
            var w = goalWaves[i];
            float u = Math.Clamp(w.Age / w.Life, 0f, 1f);
            float ease = 1f - (1f - u) * (1f - u) * (1f - u);
            float rad = w.R0 + (w.R1 - w.R0) * ease;
            int alpha = (int)((1f - u) * (1f - u) * 195f);
            if (alpha <= 2) continue;
            using var glowPen = new Pen(Color.FromArgb(alpha / 3, w.R, w.G, w.B), 7.5f);
            using var corePen = new Pen(Color.FromArgb(alpha, w.R, w.G, w.B), 2.2f);
            g.DrawEllipse(glowPen, w.X - rad, w.Y - rad, rad * 2f, rad * 2f);
            g.DrawEllipse(corePen, w.X - rad, w.Y - rad, rad * 2f, rad * 2f);
        }

        for (int i = 0; i < confetti.Count; i++)
        {
            var c = confetti[i];
            float u = Math.Clamp(c.Age / c.Life, 0f, 1f);
            float fade = u < 0.65f ? 1f : (1f - (u - 0.65f) / 0.35f);
            int alpha = Math.Clamp((int)(fade * fade * 245f), 0, 245);
            if (alpha <= 4) continue;

            if (c.ShapeKind == 3)
            {
                float sp = MathF.Sqrt(c.VX * c.VX + c.VY * c.VY);
                float inv = sp > 1f ? 1f / sp : 0f;
                float len = Math.Clamp(sp * 0.042f, 4f, 18f);
                using var tailPen = new Pen(Color.FromArgb(alpha * 3 / 4, c.R, c.G, c.B), 2.1f)
                {
                    StartCap = LineCap.Round, EndCap = LineCap.Round,
                };
                g.DrawLine(tailPen, c.X - c.VX * inv * len, c.Y - c.VY * inv * len, c.X, c.Y);
                using var headBr = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
                g.FillEllipse(headBr, c.X - 1.6f, c.Y - 1.6f, 3.2f, 3.2f);
                continue;
            }

            float ca = MathF.Cos(c.Angle), sa = MathF.Sin(c.Angle);
            float tilt = 0.24f + 0.76f * MathF.Abs(MathF.Cos(c.TiltPhase));
            float hw = c.Size, hh = c.Size * 0.56f * tilt;

            using var fill = new SolidBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            using var rim = new Pen(Color.FromArgb(Math.Min(255, alpha / 2 + 40), 255, 255, 255), 0.9f);

            if (c.ShapeKind == 0) // 3D-вращающаяся неоновая лента
            {
                float ux = ca * hw, uy = sa * hw;
                float vx = -sa * hh, vy = ca * hh;
                confettiPoly4[0] = new PointF(c.X - ux - vx, c.Y - uy - vy);
                confettiPoly4[1] = new PointF(c.X + ux - vx, c.Y + uy - vy);
                confettiPoly4[2] = new PointF(c.X + ux + vx, c.Y + uy + vy);
                confettiPoly4[3] = new PointF(c.X - ux + vx, c.Y - uy + vy);
                g.FillPolygon(fill, confettiPoly4);
                g.DrawPolygon(rim, confettiPoly4);
            }
            else if (c.ShapeKind == 1) // сияющий ромб-кристалл
            {
                float ux = ca * hw * 1.15f, uy = sa * hw * 1.15f;
                float vx = -sa * hh * 1.15f, vy = ca * hh * 1.15f;
                confettiPoly4[0] = new PointF(c.X + ux, c.Y + uy);
                confettiPoly4[1] = new PointF(c.X + vx, c.Y + vy);
                confettiPoly4[2] = new PointF(c.X - ux, c.Y - uy);
                confettiPoly4[3] = new PointF(c.X - vx, c.Y - vy);
                g.FillPolygon(fill, confettiPoly4);
                g.DrawPolygon(rim, confettiPoly4);
            }
            else // геометрический мини-треугольник
            {
                float r = c.Size * 0.95f;
                for (int k = 0; k < 3; k++)
                {
                    float a = c.Angle + k * (MathF.PI * 2f / 3f);
                    float dx = MathF.Cos(a) * r;
                    float dy = MathF.Sin(a) * r * tilt;
                    confettiPoly3[k] = new PointF(c.X + dx, c.Y + dy);
                }
                g.FillPolygon(fill, confettiPoly3);
                g.DrawPolygon(rim, confettiPoly3);
            }
        }
    }

    /// <summary>Продвинуть часы визуальных эффектов (используется и циклом окна, и тестами).</summary>
    public void StepVisuals(double dt)
    {
        if (dt <= 0) return;
        uiTime += dt;
        if (!Paused)
        {
            fxTime += dt;
            StepGoalCelebration(dt);
        }
    }

    /// <summary>Рассыпать аналитическую дугу («Ротор») на разлетающиеся осколки и искры, убрать из физики.</summary>
    public void DissolveArc(SimArc arc, bool inwardMix = false)
    {
        if (arc == null) return;
        int seed = dissolveSeed++ * 977;
        int count = inwardMix
            ? Math.Clamp((int)(arc.Radius * arc.SweepRad / 20.0), 18, 28)
            : Math.Clamp((int)(arc.Radius * arc.SweepRad / 13.0), 36, 60);
        double segSweep = arc.SweepRad / count;
        float halfLen = (float)Math.Max(2.2, arc.Radius * segSweep * 0.42);
        for (int i = 0; i < count; i++)
        {
            double a = arc.StartRad + segSweep * (i + 0.5);
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            float mx = (float)arc.Center.X + ca * (float)arc.Radius;
            float my = (float)arc.Center.Y + sa * (float)arc.Radius;
            float h1 = Hash01(seed + i * 17);
            float h2 = Hash01(seed + i * 31);
            float h3 = Hash01(seed + i * 53);

            float vRad = inwardMix ? (-55f + 115f * h1) : (60f + 145f * h1);
            float vTan = (float)(SpinSpeed * arc.Radius * 0.55) + (h2 - 0.5f) * 85f;
            shards.Add(new Shard
            {
                X = mx,
                Y = my,
                VX = ca * vRad - sa * vTan,
                VY = sa * vRad + ca * vTan,
                Angle = (float)(a + Math.PI * 0.5),
                Spin = (h3 - 0.5f) * 9.5f,
                HalfLen = halfLen,
                Born = fxTime,
                Life = inwardMix ? (0.28 + 0.16 * h2) : (0.68 + 0.34 * h2),
                Pink = RotorSkin,
            });

            if ((i & (inwardMix ? 3 : 1)) == 0)
            {
                shards.Add(new Shard
                {
                    X = mx,
                    Y = my,
                    VX = (ca * vRad - sa * vTan) * 1.35f,
                    VY = (sa * vRad + ca * vTan) * 1.35f,
                    HalfLen = 0,
                    Born = fxTime,
                    Life = inwardMix ? (0.22 + 0.14 * h3) : (0.36 + 0.24 * h3),
                    Pink = RotorSkin,
                });
            }
        }
        Sim.RemoveArc(arc);
    }

    /// <summary>Рассыпать стенку-полилинию на отдельные осколки-сегменты и убрать из физики.</summary>
    public void DissolveWall(PolyData src)
    {
        var w = Sim.FindWall(src);
        if (w == null) return;
        int seed = dissolveSeed++ * 613;
        int n = w.Pts.Count;
        int segs = w.Closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var p0 = w.Pts[i];
            var p1 = w.Pts[(i + 1) % n];
            var mid = (p0 + p1) * 0.5;
            var dir = p1 - p0;
            double len = dir.Len;
            if (len < 0.5) continue;
            var outDir = (mid - w.Pivot).Norm();
            float h1 = Hash01(seed + i * 19);
            float h2 = Hash01(seed + i * 37);
            float vRad = 55f + 130f * h1;
            float vTan = (h2 - 0.5f) * 90f;
            shards.Add(new Shard
            {
                X = (float)mid.X,
                Y = (float)mid.Y,
                VX = (float)(outDir.X * vRad - outDir.Y * vTan),
                VY = (float)(outDir.Y * vRad + outDir.X * vTan),
                Angle = (float)Math.Atan2(dir.Y, dir.X),
                Spin = (Hash01(seed + i * 61) - 0.5f) * 9f,
                HalfLen = (float)(len * 0.46),
                Born = fxTime,
                Life = 0.60 + 0.32 * h2,
                Pink = RotorSkin,
            });
        }
        Sim.RemoveWall(src);
        PtsDirty();
    }

    private void DrawDissolves(Graphics g)
    {
        for (int i = shards.Count - 1; i >= 0; i--)
        {
            var s = shards[i];
            double dt = fxTime - s.Born;
            if (dt < 0) dt = 0;
            if (dt >= s.Life) { shards.RemoveAt(i); continue; }
            double t = dt / s.Life;
            float move = (float)(dt * (1.0 - 0.35 * t));
            float px = s.X + s.VX * move;
            float py = s.Y + s.VY * move + (float)(42.0 * dt * dt);
            int ai = Math.Clamp((int)((1.0 - t * t) * 15.99), 0, 15);
            if (s.HalfLen <= 0.1f)
            {
                g.FillEllipse(SparkBrushes[ai], px - 1.7f, py - 1.7f, 3.4f, 3.4f);
            }
            else
            {
                float ang = s.Angle + s.Spin * (float)dt;
                float hl = s.HalfLen * (1f - 0.25f * (float)t);
                float dx = MathF.Cos(ang) * hl, dy = MathF.Sin(ang) * hl;
                var pen = (s.Pink ? RotorShardPens : BlueShardPens)[ai];
                g.DrawLine(pen, px - dx, py - dy, px + dx, py + dy);
            }
        }
    }
    /// <summary>Сглаживание программного рендера лучей (схема У); выключение ускоряет огромные плотности.</summary>
    public static bool SmoothOn = true;

    /// <summary>Режим «волновой фронт»: клик задаёт точку источника, шары не ставятся прицелом.</summary>
    public bool WaveMode;
    /// <summary>Реалистичная непрерывная волна с подстановкой лучей на растяжениях и дифракцией на вершинах.</summary>
    public bool UseWaveFront;
    public readonly WaveFrontSim WaveFront = new();
    private byte[] waveWake;
    private byte[] persistentWake;
    private Vec2[] persistentRayPrev = Array.Empty<Vec2>();
    private int persistentRayCount;
    private bool persistentTrace;

    /// <summary>Вечный след волнового фронта и лучей: оставляет неисчезающую полосу, наглядно проявляя теневые зоны.</summary>
    public bool PersistentTrace
    {
        get => persistentTrace;
        set
        {
            if (persistentTrace == value) return;
            persistentTrace = value;
            if (!value) ClearPersistentTrace();
            else persistentRayCount = 0;
            Invalidate();
        }
    }

    public void ClearPersistentTrace()
    {
        if (persistentWake != null) Array.Clear(persistentWake, 0, persistentWake.Length);
        persistentRayCount = 0;
    }

    /// <summary>Сумма R+G+B вечного следа в пикселе (x, y) — используется самопроверкой каустик и теней.</summary>
    public int GetPersistentTraceSum(int x, int y)
    {
        if (persistentWake == null || (uint)x >= (uint)LevelData.W || (uint)y >= (uint)LevelData.H) return 0;
        int idx = (y * (int)LevelData.W + x) * 4;
        return persistentWake[idx] + persistentWake[idx + 1] + persistentWake[idx + 2];
    }
    /// <summary>Постановка шара в один клик с нулевой скоростью и без выбора направления («Ротор»).</summary>
    public bool InstantDropNoAim;
    /// <summary>Ограничение точки постановки шара (например, внутри внутреннего круга «Ротора»).</summary>
    public Func<Vec2, Vec2> ClampDropPos;
    /// <summary>Скрыть баннер «ПАУЗА» (когда пауза техническая — ожидание постановки первого шара).</summary>
    public bool SuppressPauseBanner;
    /// <summary>Ненавязчивая плашка-подсказка в верхней части поля (например, «кликните, чтобы поставить шар»).</summary>
    public string PromptTitle;
    public string PromptSub;
    public event Action<SimBall> BallPlaced;
    private bool mouseInside;

    /// <summary>Показывать затухающие траектории шаров/лучей (во всех режимах).</summary>
    public bool Trails = true;
    public Vec2? WaveSource;
    /// <summary>Цвет новых шаров: 0 = белый; СПЕКТР — каждый следующий шар нового тона.</summary>
    public int NewBallArgb;
    public bool NewBallSpectrum;
    private int spectrumIdx;
    public event Action<Vec2> SourcePicked;

    /// <summary>Выбранный объект в редакторе (BallData, ArcData, CircleData, PolyData, GoalData или WaveSourceHandle).</summary>
    public object SelectedObject { get; private set; }
    /// <summary>Выбранный шар в редакторе (совместимость и быстрый доступ).</summary>
    public BallData SelectedBall
    {
        get => SelectedObject as BallData;
        set => SelectObject(value);
    }
    public event Action SelectionChanged;
    public event Action BallSpeedChanged;
    public event Action DesignChanged;

    // --- параметры создания новых примитивов в редакторе ---
    public int PolySides = 6;
    public double StarInnerRatio = 0.42;
    public double EditorArcGapDeg = 62.0;
    public double EditorArcSpinMul = 1.0;
    public double EditorArcHalfWidth = 2.25;
    public bool EditorFreehandClosed;
    public bool EditorWallLocalPivot;

    private readonly List<SimBall> placedStack = new(); // постановки шаров в режиме уровней — для Ctrl+Z

    private Tool curTool = Tool.WallFree;
    public Tool CurTool
    {
        get => curTool;
        set { curTool = value; Cursor = value == Tool.Select ? Cursors.Default : Cursors.Cross; Invalidate(); }
    }

    public double NewBallR = 12;
    public Vec2 LastMouseWorld { get; private set; }

    public event Action Won;
    public event Action BallRadiusChanged;
    /// <summary>Каждый тик после шага симуляции (симуляционное время, сек) — логика режимов.</summary>
    public event Action<double> Simulated;

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Stopwatch sw = Stopwatch.StartNew();
    private bool wasFinished;

    // --- состояние ввода ---
    private bool drawing;                       // рисование от руки
    private readonly List<Vec2> freePts = new();
    private bool shaping;                       // отрезок/прямоугольник/эллипс/круг/многоугольник/дуга
    private Vec2 shapeStart, shapeCur;
    private bool aiming;                        // прицел шара
    private Vec2 aimStart, aimCur;
    private object dragObj;                     // перетаскивание в режиме Select
    private object dragClickTarget;             // объект под курсором в начале клика
    private Vec2 dragLast;
    private bool dragMoved;
    private bool erasing;
    private bool gestureUndoPushed;

    private readonly List<LevelData> undoStack = new();
    private readonly List<LevelData> redoStack = new();

    private double viewScale = 1, viewOx, viewOy;

    // --- кадровый буфер: мир 1280×720 рендерится 1:1, затем вписывается в контрол ---
    private Bitmap frameBmp;
    private Graphics frameG;
    private int fxTick;

    // --- переиспользуемые ресурсы GDI (никогда не пересоздаются в кадре) ---
    private static readonly Pen WallPen = new(Color.FromArgb(94, 197, 255), 2.6f) { LineJoin = LineJoin.Round };
    private static readonly Pen WallGlowPen = new(Color.FromArgb(60, 94, 197, 255), 6f) { LineJoin = LineJoin.Round };
    private static readonly Pen RotorWallPen = new(Color.FromArgb(246, 118, 240), 3.6f) { LineJoin = LineJoin.Round };
    private static readonly Pen RotorGlowPen = new(Color.FromArgb(36, 246, 118, 240), 5.6f) { LineJoin = LineJoin.Round };
    private static readonly Pen RotorArcPen = new(Color.FromArgb(246, 118, 240), 4.5f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
    private static readonly Pen ArcDynPen = new(Color.FromArgb(246, 118, 240), 4.5f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
    private static readonly Pen BallVelPen = new(Color.FromArgb(195, 130, 215, 255), 2.2f) { EndCap = LineCap.ArrowAnchor };
    private static readonly Pen PivotPen = new(Color.FromArgb(165, 255, 176, 88), 1.6f);
    private static readonly SolidBrush RotorBgBrush = new(Color.FromArgb(3, 4, 7));
    private static readonly Pen GoalPen = new(Color.FromArgb(64, 225, 140), 2.6f);
    private static readonly Pen[] GoalGlowPens = MakeGoalGlowPens();
    private static readonly SolidBrush[] GoalFills = MakeGoalFills();
    private static readonly SolidBrush GoalDotBrush = new(Color.FromArgb(220, 124, 255, 176));
    private static readonly SolidBrush GridDotBrush = new(Color.FromArgb(34, 42, 60));
    private static readonly Rectangle[] GridDots = MakeGridDots();
    private static readonly Pen EdgePen = new(Color.FromArgb(30, 38, 54), 1f);
    private static readonly Pen BorderPen = new(Color.FromArgb(94, 197, 255), 2.2f);
    private static readonly Pen BorderGlowPen = new(Color.FromArgb(52, 94, 197, 255), 8f);
    /// <summary>Показывать ореол вокруг шаров (глобальная настройка из боковой панели).</summary>
    public static bool HaloOn;
    private static readonly SolidBrush BallBodyBrush = new(Color.FromArgb(238, 243, 255));
    private static readonly SolidBrush SpecBrush = new(Color.White);
    private static readonly Pen PreviewPen = new(Color.FromArgb(160, 166, 128, 255), 2.2f) { DashStyle = DashStyle.Dash };
    private static readonly Pen GhostPen = new(Color.FromArgb(150, 94, 197, 255), 2f) { DashStyle = DashStyle.Dash };
    private static readonly Pen AimPen = new(Color.FromArgb(255, 155, 232, 255), 3f) { EndCap = LineCap.ArrowAnchor };
    private static readonly byte[] HueRGB = BuildHueRGB();   // 64 тона → RGB для программного рендера
    private static readonly byte[] WaveFadeA = { 26, 43, 60, 77, 94, 111, 128, 145 }; // альфа ступеней затухания
    private static readonly SolidBrush BannerBgBrush = new(Color.FromArgb(205, 8, 10, 15));
    private static readonly SolidBrush BannerSubBrush = new(Color.FromArgb(215, 218, 224, 233));
    private static readonly SolidBrush ScreenBgBrush = new(Color.FromArgb(4, 6, 10));
    private static readonly Font BannerTitleFont = new("Segoe UI", 30, FontStyle.Bold);
    private static readonly Font BannerSubFont = new("Segoe UI", 11);
    private static readonly SolidBrush HubStarBrush = new(Color.FromArgb(58, 225, 232, 248));
    private static readonly PointF[] HubStarOffsets = MakeHubStars();
    private static readonly Font HubFont = new("Segoe UI", 16.5F, FontStyle.Bold);
    private static readonly SolidBrush HubTextBrush = new(Color.FromArgb(242, 246, 255));
    private static readonly Pen SourcePen = new(Color.FromArgb(215, 94, 197, 255), 2f);
    private static readonly Pen SourceRingPen = new(Color.FromArgb(120, 94, 197, 255), 1.6f);
    private static readonly SolidBrush SourceDotBrush = new(Color.FromArgb(230, 94, 197, 255));
    private static readonly Pen SelectionPen = new(Color.FromArgb(255, 155, 232, 255), 2f) { DashStyle = DashStyle.Dash };
    private static readonly Pen RotorGhostPen = new(Color.FromArgb(185, 246, 118, 240), 2f) { DashStyle = DashStyle.Dash };
    private static readonly Font PromptTitleFont = new("Segoe UI", 11F, FontStyle.Bold);
    private static readonly Font PromptSubFont = new("Segoe UI", 9F);
    private static readonly Pen PromptGlowPen = new(Color.FromArgb(46, 246, 118, 240), 4f);
    private static readonly Pen PromptBorderPen = new(Color.FromArgb(165, 246, 118, 240), 1.3f);
    private static readonly SolidBrush PromptTitleBrush = new(Color.FromArgb(250, 156, 246));
    private static readonly LinearGradientBrush FieldGrad = new(
        new Rectangle(0, 0, (int)LevelData.W, (int)LevelData.H),
        Color.FromArgb(13, 17, 26), Color.FromArgb(6, 8, 13), LinearGradientMode.Vertical);

    private static PointF[] MakeHubStars()
    {
        var pts = new PointF[22];
        for (int i = 0; i < pts.Length; i++)
        {
            double a = i * 2.399963; // золотой угол — равномерная россыпь в круге
            double r = 0.28 + 0.64 * Math.Sqrt((i + 0.5) / pts.Length);
            pts[i] = new PointF((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
        }
        return pts;
    }

    private static byte[] BuildHueRGB()
    {
        var arr = new byte[64 * 3];
        for (int i = 0; i < 64; i++)
        {
            var c = Hsv(i * 360.0 / 64, 0.72, 1.0);
            arr[i * 3] = c.R;
            arr[i * 3 + 1] = c.G;
            arr[i * 3 + 2] = c.B;
        }
        return arr;
    }

    private static int HueIdx(int hue)
    {
        int h = ((hue % 360) + 360) % 360;
        return h * 64 / 360;
    }

    private static Color Hsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    private static Pen[] MakeGoalGlowPens()
    {
        var pens = new Pen[16];
        for (int i = 0; i < pens.Length; i++)
            pens[i] = new Pen(Color.FromArgb(26 + i * 5, 64, 225, 140), 8f);
        return pens;
    }

    private static SolidBrush[] MakeGoalFills()
    {
        var fills = new SolidBrush[16];
        for (int i = 0; i < fills.Length; i++)
            fills[i] = new SolidBrush(Color.FromArgb(85 + i * 7, 52, 210, 130));
        return fills;
    }

    private static Rectangle[] MakeGridDots()
    {
        var list = new List<Rectangle>(600);
        for (double x = 40; x < LevelData.W; x += 40)
            for (double y = 40; y < LevelData.H; y += 40)
                list.Add(new Rectangle((int)x - 1, (int)y - 1, 2, 2));
        return list.ToArray();
    }

    public static void SyncTheme()
    {
        WallPen.Color = Theme.Accent;
        WallGlowPen.Color = Color.FromArgb(60, Theme.Accent);
        BorderPen.Color = Theme.Accent;
        BorderGlowPen.Color = Color.FromArgb(52, Theme.Accent);
        EdgePen.Color = Theme.PanelBorder;
        GridDotBrush.Color = Theme.Border;
        GoalPen.Color = Theme.Green;
        GoalDotBrush.Color = Color.FromArgb(220, Theme.Mix(Theme.Green, Color.White, 0.35f));
        for (int i = 0; i < GoalGlowPens.Length; i++)
            GoalGlowPens[i].Color = Color.FromArgb(26 + i * 5, Theme.Green);
        for (int i = 0; i < GoalFills.Length; i++)
            GoalFills[i].Color = Color.FromArgb(85 + i * 7, Theme.Green);
        PreviewPen.Color = Color.FromArgb(160, Theme.Violet);
        GhostPen.Color = Color.FromArgb(150, Theme.Accent);
        AimPen.Color = Theme.Mix(Theme.Accent, Color.White, 0.35f);
        BallVelPen.Color = Color.FromArgb(195, Theme.Mix(Theme.Accent, Color.White, 0.25f));
        PivotPen.Color = Color.FromArgb(175, Theme.Orange);
        SelectionPen.Color = Theme.Mix(Theme.Accent, Color.White, 0.35f);
        SourcePen.Color = Color.FromArgb(215, Theme.Accent);
        SourceRingPen.Color = Color.FromArgb(120, Theme.Accent);
        SourceDotBrush.Color = Color.FromArgb(230, Theme.Accent);
        BannerBgBrush.Color = Color.FromArgb(210, Theme.Bg);
        ScreenBgBrush.Color = Theme.Mix(Theme.Bg, Color.Black, 0.30f);
        FieldGrad.LinearColors = new[] { Theme.Panel, Theme.Mix(Theme.Bg, Color.Black, 0.25f) };
    }

    public GameCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Cross;
        Action onTheme = () => { SyncTheme(); Invalidate(); };
        Theme.Changed += onTheme;
        timer.Tick += (s, e) => Loop();
        timer.Start();
        Disposed += (s, e) =>
        {
            Theme.Changed -= onTheme;
            timer.Stop();
            frameG?.Dispose();
            frameBmp?.Dispose();
        };
        Sim.GoalReached += OnSimGoalReached;
        Sim.ResetFrom(Design);
    }

    public bool SimActive => !Editor || Testing;
    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    /// <summary>Уведомить холст о программной правке геометрии из инспектора.</summary>
    public void NotifyDesignMutated()
    {
        PtsDirty();
        DesignChanged?.Invoke();
        Invalidate();
    }

    /// <summary>Включить/выключить таймер игрового цикла (неактивные вкладки не тратят CPU в фоне).</summary>
    public void SetLoopActive(bool active)
    {
        if (active)
        {
            if (!timer.Enabled) { sw.Restart(); timer.Start(); Invalidate(); }
        }
        else if (timer.Enabled)
        {
            timer.Stop();
        }
    }

    // ------------------------------------------------- цикл

    private void Loop()
    {
        if (ClientSize.Width <= 2 || ClientSize.Height <= 2) { sw.Restart(); return; }

        // сырой кадр ограничен 50 мс (страховка от фризов), множитель скорости — сверху
        double dt = Math.Min(sw.Elapsed.TotalSeconds, 0.05);
        sw.Restart();
        StepVisuals(dt);

        if (!SimActive)
        {
            // в чистом редакторе без запущенного теста перерисовываем по таймеру пульсацию целей/осколков/источника волны
            if (Design.Goals.Count > 0 || shards.Count > 0 || Design.WaveEmitter) Invalidate();
            return;
        }

        if (!Paused)
        {
            double sdt = dt * SimSpeed * GoalTimeScale;
            if (WaveMode && UseWaveFront)
            {
                WaveFront.Step(sdt);
            }
            else
            {
                Sim.SpinSpeed = SpinSpeed;
                if (SpinSpeed != 0 && Sim.Walls.Count > 0) PtsDirty();
                Sim.Step(sdt);
            }
            Simulated?.Invoke(sdt);
        }
        else if (Design.Goals.Count == 0 && !WaveMode && shards.Count == 0 && confetti.Count == 0 && goalWaves.Count == 0)
        {
            return; // статичная пауза без пульсирующих целей/осколков — 0% CPU в простое
        }
        if (Sim.Finished && !wasFinished) { wasFinished = true; Won?.Invoke(); }
        Invalidate();
    }

    /// <summary>Детерминированный шаг цикла холста (физика + эффекты + события режимов) для автотестов.</summary>
    public void AdvanceFixedStep(double dt)
    {
        StepVisuals(dt);
        if (!SimActive || Paused) return;
        double sdt = dt * SimSpeed * GoalTimeScale;
        if (WaveMode && UseWaveFront)
        {
            WaveFront.Step(sdt);
        }
        else
        {
            Sim.SpinSpeed = SpinSpeed;
            if (SpinSpeed != 0 && Sim.Walls.Count > 0) PtsDirty();
            Sim.Step(sdt);
        }
        Simulated?.Invoke(sdt);
        if (Sim.Finished && !wasFinished) { wasFinished = true; Won?.Invoke(); }
    }

    public void RestartSim()
    {
        Sim.ResetFrom(Design);
        Sim.SpinSpeed = SpinSpeed;
        wasFinished = false;
        goalSlowMoActive = false;
        goalSlowMoElapsed = 0.0;
        GoalTimeScale = 1.0;
        confetti.Clear();
        goalWaves.Clear();
        placedStack.Clear();
        waveCount = 0;
        if (waveWake != null) Array.Clear(waveWake, 0, waveWake.Length);
        ClearPersistentTrace();
        ballPaths.Clear();
        shards.Clear();
    }

    public void LoadLevel(LevelData lvl)
    {
        Design = lvl;
        RestartSim();
        undoStack.Clear();
        redoStack.Clear();
        SelectedObject = null;
        SelectionChanged?.Invoke();
        PtsDirty();
        DesignChanged?.Invoke();
    }

    // ------------------------------------------------- отмена / повтор

    public void PushUndo()
    {
        undoStack.Add(Design.Clone());
        if (undoStack.Count > 80) undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    public bool DoUndo()
    {
        if (undoStack.Count == 0) return false;
        redoStack.Add(Design);
        Design = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        SelectedObject = null;
        SelectionChanged?.Invoke();
        PtsDirty();
        DesignChanged?.Invoke();
        if (Testing) RestartSim();
        Invalidate();
        return true;
    }

    public bool DoRedo()
    {
        if (redoStack.Count == 0) return false;
        undoStack.Add(Design);
        Design = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        SelectedObject = null;
        SelectionChanged?.Invoke();
        PtsDirty();
        DesignChanged?.Invoke();
        if (Testing) RestartSim();
        Invalidate();
        return true;
    }

    /// <summary>Ctrl+Z в режиме уровней: убрать последний поставленный шар.</summary>
    public void UndoBallPlacement()
    {
        while (placedStack.Count > 0)
        {
            var b = placedStack[^1];
            placedStack.RemoveAt(placedStack.Count - 1);
            if (Sim.Balls.Remove(b)) { Invalidate(); return; }
        }
    }

    public void SelectObject(object o)
    {
        if (ReferenceEquals(SelectedObject, o)) return;
        SelectedObject = o;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void SelectBall(BallData b) => SelectObject(b);

    /// <summary>СПЕКТР: очередной тон радуги для следующего шара.</summary>
    private int NextSpectrumColor()
    {
        spectrumIdx = (spectrumIdx + 1) & 7;
        return Hsv(spectrumIdx * 45.0, 0.72, 1.0).ToArgb();
    }

    /// <summary>Скорость выбранного шара (с сохранением направления) или скорость новых шаров.</summary>
    public void SetBallSpeed(double v)
    {
        v = Math.Clamp(Math.Round(v / 50.0) * 50, 0, Simulation.MaxSpeed);
        if (SelectedBall != null)
        {
            var dir = SelectedBall.HasVel && SelectedBall.Vel.Len > 1 ? SelectedBall.Vel.Norm() : new Vec2(1, 0);
            SelectedBall.Vel = dir * v;
            SelectedBall.HasVel = v > 1;
        }
        else EditorBallSpeed = v;
        BallSpeedChanged?.Invoke();
        Invalidate();
    }

    // ------------------------------------------------- система координат

    private void ComputeView()
    {
        double cw = Math.Max(1, ClientSize.Width), ch = Math.Max(1, ClientSize.Height);
        viewScale = Math.Min(cw / LevelData.W, ch / LevelData.H);
        viewOx = (cw - LevelData.W * viewScale) / 2;
        viewOy = (ch - LevelData.H * viewScale) / 2;
    }

    private Vec2 ToWorld(Point p) => new((p.X - viewOx) / viewScale, (p.Y - viewOy) / viewScale);

    private Vec2 Snap(Vec2 p) =>
        SnapOn ? new(Math.Round(p.X / SnapStep) * SnapStep, Math.Round(p.Y / SnapStep) * SnapStep) : p;

    // ------------------------------------------------- отрисовка

    private void EnsureFrame()
    {
        if (frameBmp == null)
        {
            frameBmp = new Bitmap((int)LevelData.W, (int)LevelData.H, PixelFormat.Format32bppArgb);
            frameG = Graphics.FromImage(frameBmp);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ComputeView();
        EnsureFrame();

        var g = frameG;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // поле: в «Роторе» (и при редактировании уровня Ротора) глубокий космос, иначе — тёмный градиент вниз
        bool spaceBg = RotorSkin || (!SimActive && Design.RotorRules);
        if (spaceBg)
            g.FillRectangle(RotorBgBrush, 0, 0, (int)LevelData.W, (int)LevelData.H);
        else
            g.FillRectangle(FieldGrad, 0, 0, (int)LevelData.W, (int)LevelData.H);

        if (ShowGrid && Editor && !Testing)
            g.FillRectangles(GridDotBrush, GridDots);

        // граница поля: неоновая, если рамка упругая (Border); иначе — едва заметный контур
        if (Design.Border)
        {
            g.DrawRectangle(BorderGlowPen, 0, 0, (float)LevelData.W, (float)LevelData.H);
            g.DrawRectangle(BorderPen, 0, 0, (float)LevelData.W, (float)LevelData.H);
        }
        else
            g.DrawRectangle(EdgePen, 0, 0, (float)LevelData.W, (float)LevelData.H);

        DrawGoals(g);
        if (HubCenter.HasValue) DrawHub(g);
        else if (!SimActive && Design.RotorRules && Design.Arcs.Count > 1)
            DrawEditorRotorHub(g);

        DrawWalls(g);
        DrawDissolves(g);
        if (WaveMode)
        {
            if (!UseWaveFront) UpdateWavePaths(); // треки и лучи рисуются программно, см. RenderWaveFrame
        }
        else
        {
            if (Trails && SimActive) DrawBallTrails(g);
            DrawBalls(g);
        }
        if (confetti.Count > 0 || goalWaves.Count > 0)
            DrawGoalCelebration(g);
        DrawPreview(g);
        DrawAim(g);
        DrawDropPreview(g);

        if (WaveMode && WaveSource.HasValue) DrawSource(g, WaveSource.Value, editorRays: false);
        else if (!SimActive && Design.WaveEmitter) DrawSource(g, Design.WaveSourcePos, editorRays: true);

        if (!SimActive && SelectedObject != null)
            DrawSelection(g, SelectedObject);

        // тысячи лучей или непрерывный волновой фронт рисуются прямо в пиксели кадра (аддитивно), минуя GDI
        if (WaveMode)
        {
            if (UseWaveFront) RenderWaveFrontFrame(frameBmp);
            else RenderWaveFrame(frameBmp);
        }

        // «шейдерный» проход пост-обработки кадра
        if (FxOn) Fx.Apply(frameBmp, fxTick++);

        // вывод кадра на экран (вписывание с сохранением пропорций)
        var sg = e.Graphics;
        sg.FillRectangle(ScreenBgBrush, 0, 0, ClientSize.Width, ClientSize.Height);
        sg.InterpolationMode = InterpolationMode.Bilinear;
        sg.PixelOffsetMode = PixelOffsetMode.Half;
        sg.DrawImage(frameBmp,
            new RectangleF((float)viewOx, (float)viewOy, (float)(LevelData.W * viewScale), (float)(LevelData.H * viewScale)),
            new RectangleF(0, 0, (float)LevelData.W, (float)LevelData.H), GraphicsUnit.Pixel);

        DrawOverlay(sg);
    }

    private void DrawEditorRotorHub(Graphics g)
    {
        double minR = double.MaxValue;
        Vec2 c = new(640, 360);
        for (int i = 0; i < Design.Arcs.Count; i++)
        {
            if (Design.Arcs[i].Radius < minR)
            {
                minR = Design.Arcs[i].Radius;
                c = Design.Arcs[i].Center;
            }
        }
        float r = (float)Math.Max(18.0, minR - 8.0);
        for (int i = 0; i < HubStarOffsets.Length; i++)
        {
            float sx = (float)c.X + HubStarOffsets[i].X * r;
            float sy = (float)c.Y + HubStarOffsets[i].Y * r;
            g.FillRectangle(HubStarBrush, sx, sy, 1.4f, 1.4f);
        }
        string text = Design.Arcs.Count.ToString();
        var s = g.MeasureString(text, HubFont);
        g.DrawString(text, HubFont, HubTextBrush, (float)(c.X - s.Width / 2), (float)(c.Y - s.Height / 2));
    }

    private void DrawSource(Graphics g, Vec2 s, bool editorRays)
    {
        float pr = 8 + (float)((Math.Sin(uiTime * 3) + 1) * 4); // пульсация источника
        if (editorRays)
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0 + uiTime * 0.4;
                float x1 = (float)(s.X + Math.Cos(a) * (pr + 3));
                float y1 = (float)(s.Y + Math.Sin(a) * (pr + 3));
                float x2 = (float)(s.X + Math.Cos(a) * (pr + 12));
                float y2 = (float)(s.Y + Math.Sin(a) * (pr + 12));
                g.DrawLine(SourceRingPen, x1, y1, x2, y2);
            }
        }
        g.DrawLine(SourcePen, (float)(s.X - 9), (float)s.Y, (float)(s.X + 9), (float)s.Y);
        g.DrawLine(SourcePen, (float)s.X, (float)(s.Y - 9), (float)s.X, (float)(s.Y + 9));
        g.FillEllipse(SourceDotBrush, (float)(s.X - 3.5), (float)(s.Y - 3.5), 7, 7);
        g.DrawEllipse(SourceRingPen, (float)(s.X - pr), (float)(s.Y - pr), pr * 2, pr * 2);
    }

    private void DrawSelection(Graphics g, object obj)
    {
        switch (obj)
        {
            case BallData b:
            {
                float rr = (float)(b.R + 6);
                g.DrawEllipse(SelectionPen, (float)(b.Pos.X - rr), (float)(b.Pos.Y - rr), rr * 2, rr * 2);
                break;
            }
            case CircleData c:
            {
                float rr = (float)(c.Radius + 5);
                g.DrawEllipse(SelectionPen, (float)(c.Center.X - rr), (float)(c.Center.Y - rr), rr * 2, rr * 2);
                g.DrawLine(SelectionPen, (float)c.Center.X - 6, (float)c.Center.Y, (float)c.Center.X + 6, (float)c.Center.Y);
                g.DrawLine(SelectionPen, (float)c.Center.X, (float)c.Center.Y - 6, (float)c.Center.X, (float)c.Center.Y + 6);
                break;
            }
            case ArcData a:
            {
                float rr = (float)(a.Radius + a.HalfWidth + 4);
                g.DrawArc(SelectionPen, (float)a.Center.X - rr, (float)a.Center.Y - rr, rr * 2, rr * 2,
                    (float)a.StartDeg, (float)a.SweepDeg);
                double gapMidRad = (a.StartDeg + a.SweepDeg + (360.0 - a.SweepDeg) * 0.5) * Math.PI / 180.0;
                float gx = (float)(a.Center.X + Math.Cos(gapMidRad) * a.Radius);
                float gy = (float)(a.Center.Y + Math.Sin(gapMidRad) * a.Radius);
                g.DrawLine(PivotPen, (float)a.Center.X, (float)a.Center.Y, gx, gy);
                break;
            }
            case PolyData p when p.Pts.Count >= 2:
            {
                var pts = ToPts(p.Pts);
                if (p.Closed && pts.Length >= 3) g.DrawPolygon(SelectionPen, pts);
                else g.DrawLines(SelectionPen, pts);
                if (p.Pivot.HasValue)
                {
                    var pv = p.Pivot.Value;
                    g.DrawEllipse(PivotPen, (float)pv.X - 5, (float)pv.Y - 5, 10, 10);
                    g.DrawLine(PivotPen, (float)pv.X - 8, (float)pv.Y, (float)pv.X + 8, (float)pv.Y);
                    g.DrawLine(PivotPen, (float)pv.X, (float)pv.Y - 8, (float)pv.X, (float)pv.Y + 8);
                }
                break;
            }
            case GoalData gl when gl.Pts.Count >= 3:
            {
                g.DrawPolygon(SelectionPen, ToPts(gl.Pts));
                break;
            }
            case WaveSourceHandle when Design.WaveEmitter:
            {
                var ws = Design.WaveSourcePos;
                g.DrawEllipse(SelectionPen, (float)ws.X - 18, (float)ws.Y - 18, 36, 36);
                break;
            }
        }
    }

    private static PointF[] ToPts(List<Vec2> pts) => pts.Select(p => new PointF((float)p.X, (float)p.Y)).ToArray();

    // кеш точек фигур: пересобирается только при изменении геометрии — кадр не аллоцирует
    private int designVersion = 1;
    private int cacheVersion;
    private PointF[][] wallCache = Array.Empty<PointF[]>();
    private bool[] wallClosedCache = Array.Empty<bool>();
    private PointF[][] goalCache = Array.Empty<PointF[]>();
    private Vec2[] goalCenterCache = Array.Empty<Vec2>();

    private void PtsDirty() => designVersion++;

    private void EnsurePtsCache()
    {
        if (cacheVersion == designVersion) return;
        cacheVersion = designVersion;
        var ws = Design.Walls.Where(w => w.Pts.Count >= 2).ToList();
        wallCache = ws.Select(w => ToPts(w.Pts)).ToArray();
        wallClosedCache = ws.Select(w => w.Closed).ToArray();
        var gs = Design.Goals.Where(g => g.Pts.Count >= 3).ToList();
        goalCache = gs.Select(g => ToPts(g.Pts)).ToArray();
        goalCenterCache = gs.Select(g => Centroid(g.Pts)).ToArray();
    }

    private void DrawGoals(Graphics g)
    {
        if (Design.Goals.Count == 0) return;
        EnsurePtsCache();
        double pulse = (Math.Sin(uiTime * 3) + 1) / 2;
        int pi = (int)(pulse * (GoalFills.Length - 1));
        var fill = GoalFills[pi];
        var glow = GoalGlowPens[pi];
        for (int i = 0; i < goalCache.Length; i++)
        {
            g.DrawPolygon(glow, goalCache[i]);
            g.FillPolygon(fill, goalCache[i]);
            g.DrawPolygon(GoalPen, goalCache[i]);
            var c = goalCenterCache[i];
            g.FillEllipse(GoalDotBrush, (float)(c.X - 4), (float)(c.Y - 4), 8, 8);
        }
    }

    /// <summary>Тёмный центр «Ротора» со счётчиком оставшихся кругов (как в первоисточнике).</summary>
    private void DrawHub(Graphics g)
    {
        var text = HubText?.Invoke();
        if (string.IsNullOrEmpty(text)) return;
        var c = HubCenter.Value;
        float r = (float)HubRadius;
        for (int i = 0; i < HubStarOffsets.Length; i++)
        {
            float sx = (float)c.X + HubStarOffsets[i].X * r;
            float sy = (float)c.Y + HubStarOffsets[i].Y * r;
            g.FillRectangle(HubStarBrush, sx, sy, 1.4f, 1.4f);
        }
        var s = g.MeasureString(text, HubFont);
        g.DrawString(text, HubFont, HubTextBrush,
            (float)(c.X - s.Width / 2), (float)(c.Y - s.Height / 2));
    }

    public static Vec2 Centroid(List<Vec2> pts)
    {
        if (pts == null || pts.Count == 0) return new Vec2(LevelData.W / 2, LevelData.H / 2);
        double x = 0, y = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; }
        return new Vec2(x / pts.Count, y / pts.Count);
    }

    private void DrawWalls(Graphics g)
    {
        bool pinkArcs = RotorSkin || Design.RotorRules || (!SimActive && Design.Arcs.Count > 0);
        ArcDynPen.Color = pinkArcs ? Color.FromArgb(246, 118, 240) : WallPen.Color;

        if (!SimActive)
        {
            for (int i = 0; i < Design.Arcs.Count; i++)
            {
                var a = Design.Arcs[i];
                if (a.Radius < 1 || a.Radius - a.HalfWidth > 755) continue;
                float r = (float)a.Radius;
                ArcDynPen.Width = (float)Math.Clamp(a.HalfWidth * 2.0, 2.0, 14.0);
                g.DrawArc(ArcDynPen, (float)a.Center.X - r, (float)a.Center.Y - r, r * 2f, r * 2f,
                    (float)a.StartDeg, (float)a.SweepDeg);
            }
        }
        else if (Sim.Arcs.Count > 0)
        {
            const float Rad2Deg = (float)(180.0 / Math.PI);
            for (int i = 0; i < Sim.Arcs.Count; i++)
            {
                var a = Sim.Arcs[i];
                if (a.Radius < 1 || a.Radius - a.HalfWidth > 755) continue;
                float r = (float)a.Radius;
                ArcDynPen.Width = (float)Math.Clamp(a.HalfWidth * 2.0, 2.0, 14.0);
                g.DrawArc(ArcDynPen, (float)a.Center.X - r, (float)a.Center.Y - r, r * 2f, r * 2f,
                    (float)a.StartRad * Rad2Deg, (float)a.SweepRad * Rad2Deg);
            }
        }

        var cGlow = RotorSkin ? RotorGlowPen : WallGlowPen;
        var cPen = RotorSkin ? RotorWallPen : WallPen;
        foreach (var c in Design.Circles)
        {
            var r = (float)(c.Radius * 2);
            g.DrawEllipse(cGlow, (float)(c.Center.X - c.Radius), (float)(c.Center.Y - c.Radius), r, r);
            g.DrawEllipse(cPen, (float)(c.Center.X - c.Radius), (float)(c.Center.Y - c.Radius), r, r);
        }

        if (SimActive && SpinSpeed != 0)
        {
            if (Sim.Walls.Count == 0) return;
            foreach (var w in Sim.Walls)
            {
                int n = w.Pts.Count;
                var pts = spinPtsPool.TryGetValue(n, out var arr) ? arr : (spinPtsPool[n] = new PointF[n]);
                for (int i = 0; i < n; i++) pts[i] = new PointF((float)w.Pts[i].X, (float)w.Pts[i].Y);
                if (w.Closed) { g.DrawPolygon(cGlow, pts); g.DrawPolygon(cPen, pts); }
                else { g.DrawLines(cGlow, pts); g.DrawLines(cPen, pts); }
            }
            return;
        }

        EnsurePtsCache();
        for (int i = 0; i < wallCache.Length; i++)
        {
            if (wallClosedCache[i])
            {
                g.DrawPolygon(cGlow, wallCache[i]);
                g.DrawPolygon(cPen, wallCache[i]);
            }
            else
            {
                g.DrawLines(cGlow, wallCache[i]);
                g.DrawLines(cPen, wallCache[i]);
            }
        }
    }

    // ------------------------------------------------- волновой фронт

    private readonly Dictionary<int, PointF[]> spinPtsPool = new();

    // плоский кольцевой буфер истории всех лучей (32 точки на луч, один массив без аллокаций и сдвигов)
    private const int WaveSlotShift = 5;
    private const int WaveSlotMask = 31;
    private Vec2[] wavePts = Array.Empty<Vec2>();
    private byte[] waveStart = Array.Empty<byte>();
    private byte[] waveLen = Array.Empty<byte>();
    private int waveCount;

    private void UpdateWavePaths()
    {
        var balls = Sim.Balls;
        int bc = balls.Count;
        if (waveCount != bc)
        {
            if (waveLen.Length < bc)
            {
                int alloc = Math.Max(bc, 256);
                wavePts = new Vec2[alloc << WaveSlotShift];
                waveStart = new byte[alloc];
                waveLen = new byte[alloc];
            }
            else
            {
                Array.Clear(waveStart, 0, bc);
                Array.Clear(waveLen, 0, bc);
            }
            waveCount = bc;
        }
        if (!Trails)
        {
            Array.Clear(waveLen, 0, waveCount);
            return;
        }
        // чем плотнее фронт, тем короче история — иначе кадр не влезает в бюджет
        int cap = bc <= 720 ? 22
                : bc <= 2000 ? 12
                : bc <= 6000 ? 7
                : bc <= 20000 ? 4
                : 0;
        if (cap == 0)
        {
            Array.Clear(waveLen, 0, waveCount);
            return;
        }
        for (int i = 0; i < bc; i++)
        {
            int bIdx = i << WaveSlotShift;
            int st = waveStart[i];
            int cnt = waveLen[i];
            if (cnt > cap)
            {
                st = (st + (cnt - cap)) & WaveSlotMask;
                cnt = cap;
                waveStart[i] = (byte)st;
                waveLen[i] = (byte)cnt;
            }
            var p = balls[i].Pos;
            if (cnt == 0 || (wavePts[bIdx | ((st + cnt - 1) & WaveSlotMask)] - p).Len2 > 6.25)
            {
                if (cnt == cap)
                {
                    wavePts[bIdx | ((st + cap) & WaveSlotMask)] = p;
                    waveStart[i] = (byte)((st + 1) & WaveSlotMask);
                }
                else
                {
                    wavePts[bIdx | ((st + cnt) & WaveSlotMask)] = p;
                    waveLen[i] = (byte)(cnt + 1);
                }
            }
        }
    }

    /// <summary>
    /// Программный рендер реалистичного непрерывного волнового фронта и дифракционных волн:
    /// мягкий затухающий шлейф (или вечный след без затухания) + узкополосный антиалиасинговый гребень.
    /// </summary>
    private unsafe void RenderWaveFrontFrame(Bitmap bmp)
    {
        int fw = (int)LevelData.W, fh = (int)LevelData.H;
        int wakeBytes = fw * fh * 4;
        waveWake ??= new byte[wakeBytes];
        bool keepTrace = persistentTrace;
        if (keepTrace) persistentWake ??= new byte[wakeBytes];

        var nodes = WaveFront.Nodes;
        int nCount = WaveFront.NodeCount;
        double crestR = Math.Clamp(NewBallR * 0.46, 1.15, 3.2);
        double crestR2 = crestR * crestR;
        int wakeStride = fw * 4;
        int decayMul = Paused ? 256 : (Trails ? 245 : 196);

        var bd = bmp.LockBits(new Rectangle(0, 0, fw, fh), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        byte* b0 = (byte*)bd.Scan0;
        int stride = bd.Stride;
        try
        {
            fixed (byte* w0 = waveWake)
            fixed (byte* pw0 = persistentWake)
            {
                // 1. Сначала прорисовываем свежие сегменты в вечный след и затухающий буфер
                if (!Paused)
                {
                    for (int i = 0; i < nCount; i++)
                    {
                        ref var a = ref nodes[i];
                        if (a.Alive == 0) continue;
                        int j = a.Next;
                        if (j < 0) continue;
                        ref var nb = ref nodes[j];
                        if (nb.Alive == 0) continue;

                        byte r, g, b;
                        if (a.Argb != 0) { var bc = Color.FromArgb(a.Argb); r = bc.R; g = bc.G; b = bc.B; }
                        else if (a.Hue >= 0) { int hi = HueIdx(a.Hue) * 3; r = HueRGB[hi]; g = HueRGB[hi + 1]; b = HueRGB[hi + 2]; }
                        else { r = 238; g = 243; b = 255; }

                        float amp = Math.Clamp((a.Intensity + nb.Intensity) * 0.5f, 0.04f, 1.0f);
                        byte wakeA = (byte)Math.Clamp((int)(amp * (Trails ? 44f : 56f)), 2, 90);
                        AddLineFast(w0, wakeStride, fw, fh, a.Pos, nb.Pos, r, g, b, wakeA);

                        if (keepTrace && pw0 != null)
                        {
                            StampPersistentLine(pw0, wakeStride, fw, fh, a.Pos, nb.Pos, r, g, b);
                            if ((a.Pos - a.FrameStartPos).Len2 < 196.0 && (nb.Pos - nb.FrameStartPos).Len2 < 196.0)
                            {
                                var m0 = (a.FrameStartPos + a.Pos) * 0.5;
                                var m1 = (nb.FrameStartPos + nb.Pos) * 0.5;
                                StampPersistentLine(pw0, wakeStride, fw, fh, m0, m1, r, g, b);
                                StampPersistentLine(pw0, wakeStride, fw, fh, a.FrameStartPos, nb.FrameStartPos, r, g, b);
                            }
                        }
                    }
                }

                // 2. Параллельный перенос затухающего буфера и вечного следа в кадр
                nint wPtr = (nint)w0;
                nint pwPtr = keepTrace && pw0 != null ? (nint)pw0 : 0;
                nint bPtr = (nint)b0;
                const int strips = 12;
                System.Threading.Tasks.Parallel.For(0, strips, s =>
                {
                    int y0 = s * fh / strips;
                    int y1 = (s + 1) * fh / strips;
                    CompositeWakeRows((byte*)bPtr, stride, (byte*)wPtr, (byte*)pwPtr, wakeStride, fw, y0, y1, decayMul);
                });

                // 3. Отрисовка яркого антиалиасингового гребня живого фронта поверх следа
                for (int i = 0; i < nCount; i++)
                {
                    ref var a = ref nodes[i];
                    if (a.Alive == 0) continue;
                    int j = a.Next;
                    if (j < 0) continue;
                    ref var nb = ref nodes[j];
                    if (nb.Alive == 0) continue;

                    byte r, g, b;
                    if (a.Argb != 0) { var bc = Color.FromArgb(a.Argb); r = bc.R; g = bc.G; b = bc.B; }
                    else if (a.Hue >= 0) { int hi = HueIdx(a.Hue) * 3; r = HueRGB[hi]; g = HueRGB[hi + 1]; b = HueRGB[hi + 2]; }
                    else { r = 238; g = 243; b = 255; }

                    float amp = Math.Clamp((a.Intensity + nb.Intensity) * 0.5f, 0.04f, 1.0f);
                    byte crestA = (byte)Math.Clamp((int)(amp * 238f), 8, 245);
                    if (SmoothOn)
                        AddCrestLine(b0, stride, fw, fh, a.Pos, nb.Pos, crestR, crestR2, r, g, b, crestA);
                    else
                        AddLineFast(b0, stride, fw, fh, a.Pos, nb.Pos, r, g, b, crestA);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static unsafe void CompositeWakeRows(
        byte* b0, int stride, byte* w0, byte* pw0, int wakeStride, int fw, int y0, int y1, int decayMul)
    {
        int rowBytes = fw * 4;
        for (int py = y0; py < y1; py++)
        {
            byte* wr = w0 + py * wakeStride;
            byte* pwr = pw0 != null ? pw0 + py * wakeStride : null;
            byte* br = b0 + py * stride;
            for (int idx = 0; idx < rowBytes; idx += 4)
            {
                int wb = wr[idx], wg = wr[idx + 1], wrr = wr[idx + 2];
                if (decayMul < 256)
                {
                    wb = (wb * decayMul) >> 8;
                    wg = (wg * decayMul) >> 8;
                    wrr = (wrr * decayMul) >> 8;
                    wr[idx] = (byte)wb;
                    wr[idx + 1] = (byte)wg;
                    wr[idx + 2] = (byte)wrr;
                }
                if (pwr != null)
                {
                    int pb = pwr[idx], pg = pwr[idx + 1], pr = pwr[idx + 2];
                    if (pb > wb) wb = pb;
                    if (pg > wg) wg = pg;
                    if (pr > wrr) wrr = pr;
                }
                if ((wb | wg | wrr) != 0)
                {
                    int vb = br[idx] + wb; br[idx] = (byte)(vb > 255 ? 255 : vb);
                    int vg = br[idx + 1] + wg; br[idx + 1] = (byte)(vg > 255 ? 255 : vg);
                    int vr = br[idx + 2] + wrr; br[idx + 2] = (byte)(vr > 255 ? 255 : vr);
                }
            }
        }
    }

    private static unsafe void StampPersistentLine(
        byte* pw0, int stride, int fw, int fh, Vec2 p0, Vec2 p1, byte r, byte g, byte b)
    {
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 1.25);
        if (steps < 1) steps = 1;
        double x = p0.X, y = p0.Y;
        double ix = dx / steps, iy = dy / steps;

        // Мягкий предел насыщения (~54% яркости): след никогда не исчезает со временем и никогда не засвечивает экран в белый,
        // чётко проявляя тёмные области тени, куда волновой фронт ни разу не проник!
        int capB = (b * 138) >> 8;
        int capG = (g * 138) >> 8;
        int capR = (r * 138) >> 8;
        int softB = (capB * 3) >> 2;
        int softG = (capG * 3) >> 2;
        int softR = (capR * 3) >> 2;

        for (int k = 0; k <= steps; k++)
        {
            int px = (int)x, py = (int)y;
            if ((uint)px < (uint)fw && (uint)py < (uint)fh)
            {
                RaiseChannel3(pw0 + py * stride + px * 4, capB, capG, capR);
                if (px + 1 < fw) RaiseChannel3Soft(pw0 + py * stride + (px + 1) * 4, softB, softG, softR);
                if (py + 1 < fh) RaiseChannel3Soft(pw0 + (py + 1) * stride + px * 4, softB, softG, softR);
            }
            x += ix;
            y += iy;
        }
    }

    private static unsafe void RaiseChannel3(byte* p, int capB, int capG, int capR)
    {
        if (p[0] < capB) { int v = p[0] + ((capB - p[0] + 1) >> 1); p[0] = (byte)(v > capB ? capB : v); }
        if (p[1] < capG) { int v = p[1] + ((capG - p[1] + 1) >> 1); p[1] = (byte)(v > capG ? capG : v); }
        if (p[2] < capR) { int v = p[2] + ((capR - p[2] + 1) >> 1); p[2] = (byte)(v > capR ? capR : v); }
    }

    private static unsafe void RaiseChannel3Soft(byte* p, int capB, int capG, int capR)
    {
        if (p[0] < capB) { int v = p[0] + ((capB - p[0] + 2) >> 2); p[0] = (byte)(v > capB ? capB : v); }
        if (p[1] < capG) { int v = p[1] + ((capG - p[1] + 2) >> 2); p[1] = (byte)(v > capG ? capG : v); }
        if (p[2] < capR) { int v = p[2] + ((capR - p[2] + 2) >> 2); p[2] = (byte)(v > capR ? capR : v); }
    }

    private static readonly byte[] CrestWeightLut = BuildCrestWeightLut();

    private static byte[] BuildCrestWeightLut()
    {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double t = 1.0 - i / 256.0;
            lut[i] = (byte)Math.Round(255.0 * t * t);
        }
        return lut;
    }

    /// <summary>
    /// Сверхбыстрая субпиксельная антиалиасинговая растеризация гребня O(L) на целочисленной арифметике Q16/Q8:
    /// строит гладкий гребень с точным разбиением единицы на стыках узлов без просадок FPS на плотных фронтах.
    /// </summary>
    private static unsafe void AddCrestLine(byte* b0, int stride, int fw, int fh,
        Vec2 p0, Vec2 p1, double reach, double reach2, byte r, byte g, byte b, byte a)
    {
        if ((p0.X < -4 && p1.X < -4) || (p0.Y < -4 && p1.Y < -4) ||
            (p0.X > fw + 4 && p1.X > fw + 4) || (p0.Y > fh + 4 && p1.Y > fh + 4))
            return;

        if (reach <= 1.75)
        {
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double adx = Math.Abs(dx), ady = Math.Abs(dy);
            if (adx < 0.08 && ady < 0.08)
            {
                AddCircleFast(b0, stride, fw, fh, p0, reach, r, g, b, a);
                return;
            }

            double len = Math.Sqrt(dx * dx + dy * dy);
            int ar = r * a, ag = g * a, ab = b * a;
            byte[] lut = CrestWeightLut;

            if (adx >= ady)
            {
                double xStart = p0.X, yStart = p0.Y, xEnd = p1.X, yEnd = p1.Y;
                if (xStart > xEnd)
                {
                    (xStart, xEnd) = (xEnd, xStart);
                    (yStart, yEnd) = (yEnd, yStart);
                }
                int ix0 = Math.Max(0, (int)Math.Floor(xStart));
                int ix1 = Math.Min(fw - 1, (int)Math.Floor(xEnd));
                if (ix0 > ix1) return;

                double slope = (yEnd - yStart) / (xEnd - xStart);
                int slopeQ16 = (int)Math.Round(slope * 65536.0);
                // В сдвинутых координатах (y - 0.5) центр пикселя py лежит ровно в целой точке py
                double yAtIx0 = (yStart - 0.5) + ((ix0 + 0.5) - xStart) * slope;
                int yQ16 = (int)Math.Round(yAtIx0 * 65536.0);
                int normMulQ8 = Math.Max(1, (int)Math.Round((xEnd - xStart) / (len * reach) * 256.0));
                int rawFirst = (int)Math.Floor(xStart);
                int rawLast = (int)Math.Floor(xEnd);

                for (int px = ix0; px <= ix1; px++, yQ16 += slopeQ16)
                {
                    int covXQ8 = 256;
                    if (px == rawFirst || px == rawLast)
                    {
                        double ov = Math.Min(xEnd, px + 1.0) - Math.Max(xStart, (double)px);
                        covXQ8 = Math.Clamp((int)Math.Round(ov * 256.0), 0, 256);
                        if (covXQ8 == 0) continue;
                    }

                    int pyCenter = (yQ16 + 32768) >> 16;
                    int offQ8 = (yQ16 - (pyCenter << 16)) >> 8;
                    int pxOff = px * 4;

                    for (int k = -2; k <= 2; k++)
                    {
                        int py = pyCenter + k;
                        if ((uint)py >= (uint)fh) continue;
                        int dv = (k << 8) - offQ8;
                        if (dv < 0) dv = -dv;
                        int li = (dv * normMulQ8) >> 8;
                        if (li >= 256) continue;
                        int w = (lut[li] * covXQ8) >> 8;
                        if (w == 0) continue;
                        byte* p = b0 + py * stride + pxOff;
                        int v = p[0] + ((ab * w) >> 16); p[0] = (byte)(v > 255 ? 255 : v);
                        v = p[1] + ((ag * w) >> 16); p[1] = (byte)(v > 255 ? 255 : v);
                        v = p[2] + ((ar * w) >> 16); p[2] = (byte)(v > 255 ? 255 : v);
                    }
                }
            }
            else
            {
                double xStart = p0.X, yStart = p0.Y, xEnd = p1.X, yEnd = p1.Y;
                if (yStart > yEnd)
                {
                    (xStart, xEnd) = (xEnd, xStart);
                    (yStart, yEnd) = (yEnd, yStart);
                }
                int iy0 = Math.Max(0, (int)Math.Floor(yStart));
                int iy1 = Math.Min(fh - 1, (int)Math.Floor(yEnd));
                if (iy0 > iy1) return;

                double invSlope = (xEnd - xStart) / (yEnd - yStart);
                int stepXQ16 = (int)Math.Round(invSlope * 65536.0);
                double xAtIy0 = (xStart - 0.5) + ((iy0 + 0.5) - yStart) * invSlope;
                int xQ16 = (int)Math.Round(xAtIy0 * 65536.0);
                int normMulQ8 = Math.Max(1, (int)Math.Round((yEnd - yStart) / (len * reach) * 256.0));
                int rawFirst = (int)Math.Floor(yStart);
                int rawLast = (int)Math.Floor(yEnd);

                for (int py = iy0; py <= iy1; py++, xQ16 += stepXQ16)
                {
                    int covYQ8 = 256;
                    if (py == rawFirst || py == rawLast)
                    {
                        double ov = Math.Min(yEnd, py + 1.0) - Math.Max(yStart, (double)py);
                        covYQ8 = Math.Clamp((int)Math.Round(ov * 256.0), 0, 256);
                        if (covYQ8 == 0) continue;
                    }

                    int pxCenter = (xQ16 + 32768) >> 16;
                    int offQ8 = (xQ16 - (pxCenter << 16)) >> 8;
                    byte* row = b0 + py * stride;

                    for (int k = -2; k <= 2; k++)
                    {
                        int px = pxCenter + k;
                        if ((uint)px >= (uint)fw) continue;
                        int dv = (k << 8) - offQ8;
                        if (dv < 0) dv = -dv;
                        int li = (dv * normMulQ8) >> 8;
                        if (li >= 256) continue;
                        int w = (lut[li] * covYQ8) >> 8;
                        if (w == 0) continue;
                        byte* p = row + px * 4;
                        int v = p[0] + ((ab * w) >> 16); p[0] = (byte)(v > 255 ? 255 : v);
                        v = p[1] + ((ag * w) >> 16); p[1] = (byte)(v > 255 ? 255 : v);
                        v = p[2] + ((ar * w) >> 16); p[2] = (byte)(v > 255 ? 255 : v);
                    }
                }
            }
            return;
        }

        // Широкий гребень (reach > 1.75): строго построчный (row-major) проход по памяти для попадания в L1-кеш
        int pad = (int)Math.Ceiling(reach);
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(p0.X, p1.X)) - pad);
        int x1 = Math.Min(fw - 1, (int)Math.Ceiling(Math.Max(p0.X, p1.X)) + pad);
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(p0.Y, p1.Y)) - pad);
        int y1 = Math.Min(fh - 1, (int)Math.Ceiling(Math.Max(p0.Y, p1.Y)) + pad);
        if (x0 > x1 || y0 > y1) return;

        double ax = p0.X, ay = p0.Y, abx = p1.X - ax, aby = p1.Y - ay;
        double len2 = abx * abx + aby * aby;
        double invLen2 = len2 < 1e-12 ? 0 : 1.0 / len2;
        int arWide = r * a, agWide = g * a, abWide = b * a;
        double invReach2 = 1.0 / reach2;
        bool steep = Math.Abs(aby) > 1e-3;
        double invAby = steep ? 1.0 / aby : 0.0;
        int rowBand = steep ? (int)Math.Ceiling((Math.Abs(abx * invAby) + 1.0) * reach) + 2 : (x1 - x0 + 1);

        for (int py = y0; py <= y1; py++)
        {
            byte* row = b0 + py * stride;
            double pyc = py + 0.5;
            int pxMin = x0, pxMax = x1;
            if (steep)
            {
                double tc = Math.Clamp((pyc - ay) * invAby, 0.0, 1.0);
                double xc = ax + abx * tc;
                pxMin = Math.Max(x0, (int)Math.Floor(xc) - rowBand);
                pxMax = Math.Min(x1, (int)Math.Ceiling(xc) + rowBand);
            }
            double dyTerm = (pyc - ay) * aby;
            for (int px = pxMin; px <= pxMax; px++)
            {
                double cx = px + 0.5;
                double t = ((cx - ax) * abx + dyTerm) * invLen2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double dxW = cx - (ax + abx * t), dyW = pyc - (ay + aby * t);
                double d2 = dxW * dxW + dyW * dyW;
                if (d2 >= reach2) continue;
                double cov = 1.0 - d2 * invReach2;
                cov *= cov;
                byte* p = row + px * 4;
                int v = p[0] + ((int)(abWide * cov) >> 8); p[0] = (byte)(v > 255 ? 255 : v);
                v = p[1] + ((int)(agWide * cov) >> 8); p[1] = (byte)(v > 255 ? 255 : v);
                v = p[2] + ((int)(arWide * cov) >> 8); p[2] = (byte)(v > 255 ? 255 : v);
            }
        }
    }

    /// <summary>
    /// Программный рендер волнового фронта: треки и лучи рисуются прямо в пиксели
    /// кадра с аддитивным смешиванием (плотность лучей = яркость). Тысячи лучей
    /// обходятся без GDI-вызовов — это на порядки быстрее DrawLine.
    /// </summary>
    private unsafe void RenderWaveFrame(Bitmap bmp)
    {
        var balls = Sim.Balls;
        int bcCount = balls.Count;
        int fw = (int)LevelData.W, fh = (int)LevelData.H;
        int wakeStride = fw * 4;
        bool keepTrace = persistentTrace;
        if (keepTrace)
        {
            persistentWake ??= new byte[fw * fh * 4];
            if (persistentRayPrev.Length < bcCount)
                persistentRayPrev = new Vec2[Math.Max(bcCount, 256)];
            if (persistentRayCount != bcCount)
            {
                Vec2 init = WaveSource ?? new Vec2(fw * 0.5, fh * 0.5);
                for (int i = 0; i < bcCount; i++)
                    persistentRayPrev[i] = (balls[i].Pos - init).Len2 < 900.0 ? init : balls[i].Pos;
                persistentRayCount = bcCount;
            }
        }

        var bd = bmp.LockBits(new Rectangle(0, 0, fw, fh), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        byte* b0 = (byte*)bd.Scan0;
        int stride = bd.Stride;
        try
        {
            if (keepTrace && persistentWake != null)
            {
                fixed (byte* pw0 = persistentWake)
                {
                    if (!Paused)
                    {
                        for (int i = 0; i < bcCount; i++)
                        {
                            var ball = balls[i];
                            byte r, g, b;
                            if (ball.Argb != 0) { var c = Color.FromArgb(ball.Argb); r = c.R; g = c.G; b = c.B; }
                            else if (ball.Hue >= 0) { int hi = HueIdx(ball.Hue) * 3; r = HueRGB[hi]; g = HueRGB[hi + 1]; b = HueRGB[hi + 2]; }
                            else { r = 238; g = 243; b = 255; }

                            var prev = persistentRayPrev[i];
                            if ((ball.Pos - prev).Len2 < 6400.0)
                                StampPersistentLine(pw0, wakeStride, fw, fh, prev, ball.Pos, r, g, b);
                            persistentRayPrev[i] = ball.Pos;
                        }
                    }

                    nint pwPtr = (nint)pw0;
                    nint bPtr = (nint)b0;
                    const int strips = 12;
                    System.Threading.Tasks.Parallel.For(0, strips, s =>
                    {
                        int y0 = s * fh / strips;
                        int y1 = (s + 1) * fh / strips;
                        int rowBytes = fw * 4;
                        byte* pBase = (byte*)pwPtr;
                        byte* bBase = (byte*)bPtr;
                        for (int py = y0; py < y1; py++)
                        {
                            byte* pwr = pBase + py * wakeStride;
                            byte* br = bBase + py * stride;
                            for (int idx = 0; idx < rowBytes; idx += 4)
                            {
                                int pb = pwr[idx], pg = pwr[idx + 1], pr = pwr[idx + 2];
                                if ((pb | pg | pr) != 0)
                                {
                                    int vb = br[idx] + pb; br[idx] = (byte)(vb > 255 ? 255 : vb);
                                    int vg = br[idx + 1] + pg; br[idx + 1] = (byte)(vg > 255 ? 255 : vg);
                                    int vr = br[idx + 2] + pr; br[idx + 2] = (byte)(vr > 255 ? 255 : vr);
                                }
                            }
                        }
                    });
                }
            }

            for (int i = 0; i < bcCount; i++)
            {
                var ball = balls[i];
                byte r, g, b;
                if (ball.Argb != 0)
                {
                    var bc = Color.FromArgb(ball.Argb);
                    r = bc.R; g = bc.G; b = bc.B;
                }
                else if (ball.Hue >= 0)
                {
                    int hi = HueIdx(ball.Hue) * 3;
                    r = HueRGB[hi]; g = HueRGB[hi + 1]; b = HueRGB[hi + 2];
                }
                else
                {
                    r = 238; g = 243; b = 255;
                }

                if (Trails && i < waveCount)
                {
                    int n = waveLen[i];
                    if (n >= 2)
                    {
                        int bIdx = i << WaveSlotShift;
                        int st = waveStart[i];
                        var pPrev = wavePts[bIdx | st];
                        int denom = n - 1;
                        for (int s = 1; s < n; s++)
                        {
                            var pCur = wavePts[bIdx | ((st + s) & WaveSlotMask)];
                            int f = (s - 1) * 8 / denom;
                            if (SmoothOn)
                                AddLine(b0, stride, fw, fh, pPrev, pCur, r, g, b, WaveFadeA[f]);
                            else
                                AddLineFast(b0, stride, fw, fh, pPrev, pCur, r, g, b, WaveFadeA[f]);
                            pPrev = pCur;
                        }
                    }
                }

                // тело луча и блик — аддитивные круги
                if (SmoothOn)
                {
                    AddCircle(b0, stride, fw, fh, ball.Pos, ball.R, r, g, b, 235);
                    if (ball.R >= 2)
                        AddCircle(b0, stride, fw, fh,
                            new Vec2(ball.Pos.X - ball.R * 0.3, ball.Pos.Y - ball.R * 0.35),
                            ball.R * 0.35, 255, 255, 255, 170);
                }
                else
                {
                    AddCircleFast(b0, stride, fw, fh, ball.Pos, ball.R, r, g, b, 235);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }

    private static unsafe void AddLine(byte* b0, int stride, int fw, int fh, Vec2 p0, Vec2 p1, byte r, byte g, byte b, byte a) =>
        AddCrestLine(b0, stride, fw, fh, p0, p1, 1.25, 1.5625, r, g, b, a);

    private static unsafe void AddCircle(byte* b0, int stride, int fw, int fh, Vec2 c, double rad, byte r, byte g, byte b, byte a)
    {
        int ar = r, ag = g, ab = b;
        if (rad < 0.7)
        {
            int px = (int)c.X, py = (int)c.Y;
            if ((uint)px < (uint)fw && (uint)py < (uint)fh) AddPx(b0 + py * stride + px * 4, ar * a >> 8, ag * a >> 8, ab * a >> 8);
            return;
        }
        int y0 = (int)Math.Floor(c.Y - rad), y1 = (int)Math.Ceiling(c.Y + rad);
        double r2 = rad * rad;
        for (int py = y0; py <= y1; py++)
        {
            if ((uint)py >= (uint)fh) continue;
            double dy = py + 0.5 - c.Y;
            double w = r2 - dy * dy;
            if (w <= 0) continue;
            double half = Math.Sqrt(w);
            double lx = c.X - half, rx = c.X + half;
            int x0 = (int)Math.Ceiling(lx), x1 = (int)Math.Floor(rx);
            byte* row = b0 + py * stride;
            for (int px = x0; px <= x1; px++)
            {
                if ((uint)px >= (uint)fw) continue;
                // мягкий край: пиксели у границы закрашиваются частично
                double cov = Math.Min(px + 1 - lx, rx - px);
                if (cov <= 0) continue;
                if (cov > 1) cov = 1;
                AddPx(row + px * 4, (int)(ar * a * cov) >> 8, (int)(ag * a * cov) >> 8, (int)(ab * a * cov) >> 8);
            }
        }
    }

    private static unsafe void AddPx(byte* p, int r, int g, int b)
    {
        int v = p[0] + b; p[0] = (byte)(v > 255 ? 255 : v);
        v = p[1] + g; p[1] = (byte)(v > 255 ? 255 : v);
        v = p[2] + r; p[2] = (byte)(v > 255 ? 255 : v);
    }

    // быстрые варианты без сглаживания — для гигантских плотностей (SmoothOn = false)

    private static unsafe void AddLineFast(byte* b0, int stride, int fw, int fh, Vec2 p0, Vec2 p1, byte r, byte g, byte b, byte a)
    {
        double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (steps < 1) steps = 1;
        double x = p0.X, y = p0.Y;
        double ix = dx / steps, iy = dy / steps;
        int ar = r * a >> 8, ag = g * a >> 8, ab = b * a >> 8;
        for (int k = 0; k <= steps; k++)
        {
            int px = (int)x, py = (int)y;
            if ((uint)px < (uint)fw && (uint)py < (uint)fh)
                AddPx(b0 + py * stride + px * 4, ar, ag, ab);
            x += ix;
            y += iy;
        }
    }

    private static unsafe void AddCircleFast(byte* b0, int stride, int fw, int fh, Vec2 c, double rad, byte r, byte g, byte b, byte a)
    {
        int ar = r * a >> 8, ag = g * a >> 8, ab = b * a >> 8;
        if (rad < 0.7)
        {
            int px = (int)c.X, py = (int)c.Y;
            if ((uint)px < (uint)fw && (uint)py < (uint)fh) AddPx(b0 + py * stride + px * 4, ar, ag, ab);
            return;
        }
        int y0 = (int)Math.Floor(c.Y - rad), y1 = (int)Math.Ceiling(c.Y + rad);
        double r2 = rad * rad;
        for (int py = y0; py <= y1; py++)
        {
            if ((uint)py >= (uint)fh) continue;
            double dy = py + 0.5 - c.Y;
            double w = r2 - dy * dy;
            if (w <= 0) continue;
            int half = (int)Math.Sqrt(w);
            int x0 = (int)Math.Ceiling(c.X - half), x1 = (int)Math.Floor(c.X + half);
            byte* row = b0 + py * stride;
            for (int px = x0; px <= x1; px++)
                if ((uint)px < (uint)fw)
                    AddPx(row + px * 4, ar, ag, ab);
        }
    }

    // ------------------------------------------------- шары

    private void DrawBalls(Graphics g)
    {
        if (WaveMode) return; // лучи волнового фронта рисуются в RenderWaveFrame
        var prevInterp = g.InterpolationMode;
        var prevOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        try
        {
            if (!SimActive)
            {
                foreach (var b in Design.Balls)
                {
                    if (b.HasVel && b.Vel.Len > 1)
                    {
                        double len = 34 + Math.Min(64, b.Vel.Len * 0.045);
                        var end = b.Pos + b.Vel.Norm() * len;
                        g.DrawLine(BallVelPen, (float)b.Pos.X, (float)b.Pos.Y, (float)end.X, (float)end.Y);
                    }
                    DrawBallBody(g, b.Pos, b.R, b.Argb);
                }
                return;
            }
            foreach (var b in Sim.Balls)
                DrawBallBody(g, b.Pos, b.R, b.Argb);
        }
        finally
        {
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = prevOffset;
        }
    }

    // затухающие треки обычных шаров (в игре и тесте редактора); кеш перьев и гладких радиальных ореолов по цвету
    private readonly List<List<Vec2>> ballPaths = new();
    private static readonly Dictionary<int, Pen[]> TrailFadePens = new();
    private static readonly Dictionary<int, SolidBrush> BodyBrushes = new();
    private static readonly Dictionary<(int Argb, bool Wide), Bitmap> BallGlowCache = new();
    private static readonly SolidBrush CoreHighlightBrush = new(Color.FromArgb(62, 255, 255, 255));

    private static Pen[] FadePens(int argb)
    {
        if (TrailFadePens.TryGetValue(argb, out var pens)) return pens;
        var c = argb != 0 ? Color.FromArgb(argb) : Color.FromArgb(205, 224, 255);
        pens = new Pen[8];
        for (int f = 0; f < 8; f++)
            pens[f] = new Pen(Color.FromArgb(26 + f * 17, c), 2.2f);
        TrailFadePens[argb] = pens;
        return pens;
    }

    private static SolidBrush BodyBrush(int argb) =>
        BodyBrushes.TryGetValue(argb, out var b) ? b
        : BodyBrushes[argb] = new SolidBrush(argb != 0 ? Color.FromArgb(argb) : Color.FromArgb(238, 243, 255));

    private static unsafe Bitmap GetBallGlowSprite(int argb, bool wide)
    {
        var key = (argb, wide);
        if (BallGlowCache.TryGetValue(key, out var cached)) return cached;

        const int S = 128;
        const double half = S * 0.5;
        var baseCol = argb != 0 ? Color.FromArgb(argb) : Color.FromArgb(196, 222, 255);
        double cr = baseCol.R, cg = baseCol.G, cb = baseCol.B;
        double peakAlpha = wide ? 110.0 : 56.0;
        double expK = wide ? 3.35 : 4.75;

        var bmp = new Bitmap(S, S, PixelFormat.Format32bppPArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, S, S), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte* b0 = (byte*)bd.Scan0;
            int stride = bd.Stride;
            for (int py = 0; py < S; py++)
            {
                byte* row = b0 + py * stride;
                double dy = (py + 0.5 - half) / half;
                for (int px = 0; px < S; px++)
                {
                    double dx = (px + 0.5 - half) / half;
                    double u2 = dx * dx + dy * dy;
                    byte* p = row + px * 4;
                    if (u2 >= 1.0)
                    {
                        p[0] = 0; p[1] = 0; p[2] = 0; p[3] = 0;
                        continue;
                    }

                    double env = 1.0 - u2;
                    double f = Math.Exp(-expK * u2) * env * env;
                    double alpha = peakAlpha * f;
                    // Мягкое тёпло-белое высветление у самой кромки сферы для бесшовного слияния шара и ореола
                    double coreMix = Math.Exp(-9.0 * u2) * 0.34;
                    double rLin = cr + (255.0 - cr) * coreMix;
                    double gLin = cg + (255.0 - cg) * coreMix;
                    double bLin = cb + (255.0 - cb) * coreMix;

                    // Треугольный суб-LSB дизеринг устраняет малейшую ступенчатость 8-битных альфа-колец
                    double dither = (Hash01(px * 73856093 ^ py * 19349663) +
                                     Hash01(px * 83492791 ^ py * 297121507) - 1.0) * 0.6;
                    int aByte = Math.Clamp((int)Math.Round(alpha + dither), 0, 255);
                    if (aByte == 0)
                    {
                        p[0] = 0; p[1] = 0; p[2] = 0; p[3] = 0;
                        continue;
                    }

                    double pm = aByte / 255.0;
                    p[0] = (byte)Math.Clamp((int)Math.Round(bLin * pm + dither * 0.35), 0, aByte);
                    p[1] = (byte)Math.Clamp((int)Math.Round(gLin * pm + dither * 0.35), 0, aByte);
                    p[2] = (byte)Math.Clamp((int)Math.Round(rLin * pm + dither * 0.35), 0, aByte);
                    p[3] = (byte)aByte;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        BallGlowCache[key] = bmp;
        return bmp;
    }

    private void DrawBallTrails(Graphics g)
    {
        var balls = Sim.Balls;
        // дорожки только добавляются/убираются с краёв — при постановке шара
        // существующие треки не сбрасываются (не было бы вспышки-«застывания»)
        while (ballPaths.Count < balls.Count) ballPaths.Add(new List<Vec2>());
        if (ballPaths.Count > balls.Count)
            ballPaths.RemoveRange(balls.Count, ballPaths.Count - balls.Count);
        for (int i = 0; i < balls.Count; i++)
        {
            var b = balls[i];
            var path = ballPaths[i];
            if (path.Count == 0 || Vec2.Dist(path[^1], b.Pos) > 2.5)
            {
                path.Add(b.Pos);
                if (path.Count > 26) path.RemoveAt(0);
            }
            int n = path.Count;
            if (n < 2) continue;
            var pens = FadePens(b.Argb);
            for (int s = 1; s < n; s++)
                g.DrawLine(pens[(s - 1) * 8 / (n - 1)],
                    (float)path[s - 1].X, (float)path[s - 1].Y, (float)path[s].X, (float)path[s].Y);
        }
    }

    // шар — плавная сфера с непрерывным гауссовым радиальным ореолом (без концентрических колец и цветовых ступеней)
    private static void DrawBallBody(Graphics g, Vec2 pos, double r, int argb)
    {
        float rr = (float)r;
        float glowR = HaloOn ? (rr * 2.45f + 13f) : (rr * 1.48f + 4.5f);
        var glow = GetBallGlowSprite(argb, HaloOn);
        g.DrawImage(glow, (float)pos.X - glowR, (float)pos.Y - glowR, glowR * 2f, glowR * 2f);

        g.FillEllipse(BodyBrush(argb), (float)pos.X - rr, (float)pos.Y - rr, rr * 2f, rr * 2f);
        float coreR = rr * 0.68f;
        g.FillEllipse(CoreHighlightBrush, (float)pos.X - coreR * 1.05f, (float)pos.Y - coreR * 1.08f, coreR * 2f, coreR * 2f);
        g.FillEllipse(SpecBrush, (float)(pos.X - r * 0.42), (float)(pos.Y - r * 0.46), (float)(r * 0.36), (float)(r * 0.36));
    }

    private void DrawPreview(Graphics g)
    {
        if (drawing && freePts.Count > 1)
        {
            var pts = ToPts(freePts);
            if (CurTool == Tool.GoalFree || (CurTool == Tool.WallFree && EditorFreehandClosed && pts.Length >= 3))
                g.DrawPolygon(PreviewPen, pts);
            else
                g.DrawLines(PreviewPen, pts);
        }
        else if (shaping)
        {
            double x1 = Math.Min(shapeStart.X, shapeCur.X), x2 = Math.Max(shapeStart.X, shapeCur.X);
            double y1 = Math.Min(shapeStart.Y, shapeCur.Y), y2 = Math.Max(shapeStart.Y, shapeCur.Y);
            double rad = Vec2.Dist(shapeStart, shapeCur);
            double ang = SnapAngleIfNeeded(Math.Atan2(shapeCur.Y - shapeStart.Y, shapeCur.X - shapeStart.X));

            switch (CurTool)
            {
                case Tool.WallLine:
                    g.DrawLine(PreviewPen, (float)shapeStart.X, (float)shapeStart.Y, (float)shapeCur.X, (float)shapeCur.Y);
                    break;
                case Tool.WallRect:
                case Tool.GoalRect:
                    g.DrawRectangle(PreviewPen, (float)x1, (float)y1, (float)(x2 - x1), (float)(y2 - y1));
                    break;
                case Tool.WallEllipse:
                case Tool.GoalEllipse:
                    g.DrawEllipse(PreviewPen, (float)x1, (float)y1, (float)(x2 - x1), (float)(y2 - y1));
                    break;
                case Tool.WallCircle:
                    if (rad >= 2)
                    {
                        float fr = (float)rad;
                        g.DrawEllipse(PreviewPen, (float)shapeStart.X - fr, (float)shapeStart.Y - fr, fr * 2, fr * 2);
                        g.DrawLine(PreviewPen, (float)shapeStart.X, (float)shapeStart.Y, (float)shapeCur.X, (float)shapeCur.Y);
                    }
                    break;
                case Tool.WallPoly:
                case Tool.GoalPoly:
                    if (rad >= 4)
                        g.DrawPolygon(PreviewPen, ToPts(LevelShapes.RegularPolygonPts(shapeStart, rad, PolySides, ang)));
                    break;
                case Tool.WallStar:
                    if (rad >= 6)
                        g.DrawPolygon(PreviewPen, ToPts(LevelShapes.StarPolygonPts(shapeStart, rad, PolySides, StarInnerRatio, ang)));
                    break;
                case Tool.ArcRing:
                    if (rad >= 6)
                    {
                        float fr = (float)rad;
                        double gap = Math.Clamp(EditorArcGapDeg, 12.0, 280.0);
                        double startDeg = NormalizeDeg(ang * 180.0 / Math.PI + gap * 0.5);
                        g.DrawArc(RotorGhostPen, (float)shapeStart.X - fr, (float)shapeStart.Y - fr, fr * 2, fr * 2,
                            (float)startDeg, (float)(360.0 - gap));
                        g.DrawLine(PivotPen, (float)shapeStart.X, (float)shapeStart.Y, (float)shapeCur.X, (float)shapeCur.Y);
                    }
                    break;
            }
        }
    }

    private double SnapAngleIfNeeded(double angRad)
    {
        if (!SnapOn) return angRad;
        const double step = Math.PI / 12.0; // шаг 15° при включённой привязке
        return Math.Round(angRad / step) * step;
    }

    private static double NormalizeDeg(double deg)
    {
        double m = deg % 360.0;
        return m < 0 ? m + 360.0 : m;
    }

    private void DrawAim(Graphics g)
    {
        if (!aiming) return;
        float r = (float)NewBallR;
        g.DrawEllipse(GhostPen, (float)(aimStart.X - r), (float)(aimStart.Y - r), r * 2, r * 2);
        var dir = aimCur - aimStart;
        if (dir.Len < 4) return;
        var end = aimStart + dir.Norm() * 110; // стрелка показывает только направление
        g.DrawLine(AimPen, (float)aimStart.X, (float)aimStart.Y, (float)end.X, (float)end.Y);
    }

    private void DrawDropPreview(Graphics g)
    {
        if (!InstantDropNoAim || !mouseInside) return;
        var p = ClampDropPos != null ? ClampDropPos(LastMouseWorld) : LastMouseWorld;
        float r = (float)NewBallR;
        g.DrawEllipse(RotorSkin ? RotorGhostPen : GhostPen, (float)(p.X - r), (float)(p.Y - r), r * 2, r * 2);
    }

    private void DrawOverlay(Graphics g)
    {
        // ненавязчивая плашка сверху — приглашение поставить шар в «Роторе»
        if (!string.IsNullOrEmpty(PromptTitle))
        {
            DrawPromptPill(g, PromptTitle, PromptSub ?? "");
            return;
        }
        // баннера «уровень пройден» нет ни в одном режиме — только пауза
        if (Paused && SimActive && !SuppressPauseBanner)
        {
            DrawBanner(g, "ПАУЗА", "пробел — продолжить", Theme.Accent);
        }
    }

    private static readonly StringFormat CenterTextFmt = new() { Alignment = StringAlignment.Center };

    private void DrawPromptPill(Graphics g, string title, string sub)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s1 = g.MeasureString(title, PromptTitleFont);
        var s2 = string.IsNullOrEmpty(sub) ? SizeF.Empty : g.MeasureString(sub, PromptSubFont);
        float bw = Math.Max(s1.Width, s2.Width) + 44;
        float bh = s1.Height + (s2.Height > 0 ? s2.Height + 4 : 0) + 18;
        float bx = (ClientSize.Width - bw) / 2f;
        float by = 14f;
        var rect = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
        using (var path = Theme.RoundRect(rect, 10))
        {
            g.DrawPath(PromptGlowPen, path);
            g.FillPath(BannerBgBrush, path);
            g.DrawPath(PromptBorderPen, path);
        }
        float cx = bx + bw / 2f;
        g.DrawString(title, PromptTitleFont, PromptTitleBrush, cx, by + 8f, CenterTextFmt);
        if (s2.Height > 0)
            g.DrawString(sub, PromptSubFont, BannerSubBrush, cx, by + 11f + s1.Height, CenterTextFmt);
    }

    private void DrawBanner(Graphics g, string title, string sub, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s1 = g.MeasureString(title, BannerTitleFont);
        var s2 = g.MeasureString(sub, BannerSubFont);
        float bw = Math.Max(s1.Width, s2.Width) + 72;
        float bh = s1.Height + s2.Height + 44;
        float bx = (ClientSize.Width - bw) / 2;
        float by = ClientSize.Height * 0.30f;
        var rect = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
        using (var path = Theme.RoundRect(rect, 14))
        {
            using (var glow = new Pen(Color.FromArgb(54, accent), 5f)) g.DrawPath(glow, path);
            using var p = new Pen(Color.FromArgb(170, accent), 1.6f);
            g.FillPath(BannerBgBrush, path);
            g.DrawPath(p, path);
        }
        using var tb = new SolidBrush(accent);
        float cx = bx + bw / 2f;
        g.DrawString(title, BannerTitleFont, tb, cx, by + 16, CenterTextFmt);
        g.DrawString(sub, BannerSubFont, BannerSubBrush, cx, by + 24 + s1.Height, CenterTextFmt);
    }

    // ------------------------------------------------- ввод

    private Vec2 EditorPoint(MouseEventArgs e)
    {
        var w = ToWorld(e.Location);
        return Editor && !Testing ? Snap(w) : w;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (WaveMode) // волновой фронт: клик = перенос источника лучей
        {
            if (e.Button == MouseButtons.Left)
            {
                WaveSource = ToWorld(e.Location);
                SourcePicked?.Invoke(WaveSource.Value);
                Invalidate();
            }
            return;
        }

        var w = EditorPoint(e);

        if (!SimActive) // чистое редактирование
        {
            if (e.Button == MouseButtons.Right) { gestureUndoPushed = false; EraseGesture(ToWorld(e.Location)); return; }
            switch (CurTool)
            {
                case Tool.WallFree:
                case Tool.GoalFree:
                    SelectObject(null);
                    drawing = true;
                    freePts.Clear();
                    freePts.Add(w);
                    break;
                case Tool.WallLine:
                case Tool.WallRect:
                case Tool.WallEllipse:
                case Tool.WallPoly:
                case Tool.WallStar:
                case Tool.GoalRect:
                case Tool.GoalEllipse:
                case Tool.GoalPoly:
                    SelectObject(null);
                    shaping = true;
                    shapeStart = w;
                    shapeCur = w;
                    break;
                case Tool.WallCircle:
                case Tool.ArcRing:
                    SelectObject(null);
                    shaping = true;
                    shapeStart = SnapRingCenter(w);
                    shapeCur = w;
                    break;
                case Tool.WaveSource:
                    PushUndo();
                    Design.WaveEmitter = true;
                    Design.WaveSourcePos = w;
                    SelectObject(WaveSourceHandle.Instance);
                    BeginDragObject(WaveSourceHandle.Instance, ToWorld(e.Location), pushUndo: false);
                    DesignChanged?.Invoke();
                    break;
                case Tool.Ball:
                    var hitBall = HitBall(w);
                    if (hitBall != null) { SelectObject(hitBall); break; }
                    SelectObject(null);
                    aiming = true;
                    aimStart = w;
                    aimCur = w;
                    break;
                case Tool.Eraser:
                    erasing = true;
                    gestureUndoPushed = false;
                    EraseGesture(ToWorld(e.Location));
                    break;
                case Tool.Select:
                    var hit = HitTest(ToWorld(e.Location));
                    SelectObject(hit);
                    if (hit != null) BeginDragObject(hit, ToWorld(e.Location), pushUndo: true);
                    break;
            }
        }
        else if (!Editor || Testing) // режим уровней, ротор и тест редактора
        {
            if (e.Button == MouseButtons.Left)
            {
                if (InstantDropNoAim)
                    PlaceInstantBall(w);
                else
                {
                    aiming = true;
                    aimStart = w;
                    aimCur = w;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                var p = ToWorld(e.Location);
                Sim.Balls.RemoveAll(b => Vec2.Dist(b.Pos, p) < b.R + 8);
                placedStack.RemoveAll(pb => !Sim.Balls.Contains(pb));
            }
        }
        Invalidate();
    }

    private Vec2 SnapRingCenter(Vec2 p)
    {
        var fieldC = new Vec2(LevelData.W / 2, LevelData.H / 2);
        if (Vec2.Dist(p, fieldC) <= 22.0) return fieldC;
        for (int i = Design.Arcs.Count - 1; i >= 0; i--)
            if (Vec2.Dist(p, Design.Arcs[i].Center) <= 22.0) return Design.Arcs[i].Center;
        for (int i = Design.Circles.Count - 1; i >= 0; i--)
            if (Vec2.Dist(p, Design.Circles[i].Center) <= 22.0) return Design.Circles[i].Center;
        return p;
    }

    /// <summary>Поставить шар с нулевой начальной скоростью (клик в «Роторе» и самопроверка).</summary>
    public SimBall PlaceInstantBall(Vec2 worldPos)
    {
        var dropPos = ClampDropPos != null ? ClampDropPos(worldPos) : worldPos;
        int argb = NewBallSpectrum ? NextSpectrumColor() : NewBallArgb;
        var placed = Sim.AddBall(dropPos, new Vec2(0, 0), NewBallR, -1, argb);
        placedStack.Add(placed);
        BallPlaced?.Invoke(placed);
        Invalidate();
        return placed;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        mouseInside = true;
        if (InstantDropNoAim) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        mouseInside = false;
        if (InstantDropNoAim) Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        LastMouseWorld = ToWorld(e.Location);
        var w = EditorPoint(e);

        // перерисовка нужна, только когда мышь реально меняет вид (прицел, рисование, сдвиг, превью дропа)
        bool visual = drawing || shaping || aiming || erasing || dragObj != null ||
                      (InstantDropNoAim && Paused);
        if (drawing && Vec2.Dist(freePts[^1], w) > 4) freePts.Add(w);
        if (shaping) shapeCur = w;
        if (aiming) aimCur = w;
        if (erasing) EraseGesture(ToWorld(e.Location));
        if (dragObj != null)
        {
            var d = ToWorld(e.Location) - dragLast;
            TranslateObj(dragObj, d);
            dragLast = ToWorld(e.Location);
            dragMoved = true;
            PtsDirty();
        }
        if (visual) Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (drawing)
        {
            drawing = false;
            if (freePts.Count >= 2)
            {
                PushUndo();
                if (CurTool == Tool.WallFree)
                {
                    var pts = new List<Vec2>(freePts);
                    var poly = new PolyData
                    {
                        Pts = pts,
                        Closed = EditorFreehandClosed && pts.Count >= 3,
                        Pivot = EditorWallLocalPivot ? Centroid(pts) : null,
                    };
                    Design.Walls.Add(poly);
                    SelectObject(poly);
                }
                else
                {
                    var pts = Simplify(freePts, 8);
                    if (pts.Count >= 3)
                    {
                        var goal = new GoalData { Pts = pts };
                        Design.Goals.Add(goal);
                        SelectObject(goal);
                    }
                }
                PtsDirty();
                DesignChanged?.Invoke();
            }
        }
        else if (shaping)
        {
            shaping = false;
            CommitShape();
        }
        else if (aiming)
        {
            aiming = false;
            var dir = aimCur - aimStart;
            bool moving = dir.Len >= 4 && EditorBallSpeed > 1;
            double speed = Editor ? EditorBallSpeed : PlayFixedSpeed;
            var vel = moving ? dir.Norm() * speed : new Vec2();
            int argb = NewBallSpectrum ? NextSpectrumColor() : NewBallArgb;
            if (!SimActive) // редактор: шар с заданным направлением остаётся в уровне
            {
                PushUndo();
                var bd = new BallData { Pos = aimStart, R = NewBallR, Vel = vel, HasVel = moving, Argb = argb };
                Design.Balls.Add(bd);
                SelectObject(bd);
                DesignChanged?.Invoke();
            }
            else // игра/тест: шар сразу летит
            {
                placedStack.Add(Sim.AddBall(aimStart, vel, NewBallR, -1, argb));
            }
        }
        else if (dragObj != null)
        {
            if (!dragMoved && CurTool == Tool.Select && undoStack.Count > 0)
                undoStack.RemoveAt(undoStack.Count - 1); // клик без движения
            if (dragMoved) DesignChanged?.Invoke();
            if (!dragMoved && dragClickTarget != null) SelectObject(dragClickTarget);
            dragObj = null;
            dragClickTarget = null;
        }
        erasing = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        NewBallR = Math.Clamp(NewBallR + Math.Sign(e.Delta) * 2, 2, 40);
        BallRadiusChanged?.Invoke();
        Invalidate();
    }

    // ------------------------------------------------- инструменты

    private void CommitShape()
    {
        double x1 = Math.Min(shapeStart.X, shapeCur.X), x2 = Math.Max(shapeStart.X, shapeCur.X);
        double y1 = Math.Min(shapeStart.Y, shapeCur.Y), y2 = Math.Max(shapeStart.Y, shapeCur.Y);
        double rad = Vec2.Dist(shapeStart, shapeCur);
        double ang = SnapAngleIfNeeded(Math.Atan2(shapeCur.Y - shapeStart.Y, shapeCur.X - shapeStart.X));

        if (CurTool == Tool.WallCircle)
        {
            if (rad < 10) return;
            PushUndo();
            var cd = new CircleData { Center = shapeStart, Radius = rad };
            Design.Circles.Add(cd);
            PtsDirty();
            SelectObject(cd);
            DesignChanged?.Invoke();
            return;
        }

        if (CurTool == Tool.ArcRing)
        {
            if (rad < 14) return;
            PushUndo();
            double gap = Math.Clamp(EditorArcGapDeg, 12.0, 280.0);
            var ad = new ArcData
            {
                Center = shapeStart,
                Radius = rad,
                StartDeg = NormalizeDeg(ang * 180.0 / Math.PI + gap * 0.5),
                SweepDeg = 360.0 - gap,
                HalfWidth = Math.Clamp(EditorArcHalfWidth, 1.0, 7.0),
                SpinMul = EditorArcSpinMul,
            };
            Design.Arcs.Add(ad);
            PtsDirty();
            SelectObject(ad);
            DesignChanged?.Invoke();
            return;
        }

        List<Vec2> pts = null;
        bool closed = false;
        bool isGoal = false;
        switch (CurTool)
        {
            case Tool.WallLine:
                if (rad < 8) return;
                pts = new List<Vec2> { shapeStart, shapeCur };
                break;
            case Tool.WallRect:
                if (x2 - x1 < 8 || y2 - y1 < 8) return;
                pts = new List<Vec2> { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
                closed = true;
                break;
            case Tool.WallEllipse:
                if (x2 - x1 < 10 || y2 - y1 < 10) return;
                pts = EllipsePts((x1 + x2) / 2, (y1 + y2) / 2, (x2 - x1) / 2, (y2 - y1) / 2, 64);
                closed = true;
                break;
            case Tool.WallPoly:
                if (rad < 10) return;
                pts = LevelShapes.RegularPolygonPts(shapeStart, rad, PolySides, ang);
                closed = true;
                break;
            case Tool.WallStar:
                if (rad < 12) return;
                pts = LevelShapes.StarPolygonPts(shapeStart, rad, PolySides, StarInnerRatio, ang);
                closed = true;
                break;
            case Tool.GoalRect:
                if (x2 - x1 < 8 || y2 - y1 < 8) return;
                pts = new List<Vec2> { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
                isGoal = true;
                break;
            case Tool.GoalEllipse:
                if (x2 - x1 < 10 || y2 - y1 < 10) return;
                pts = EllipsePts((x1 + x2) / 2, (y1 + y2) / 2, (x2 - x1) / 2, (y2 - y1) / 2, 48);
                isGoal = true;
                break;
            case Tool.GoalPoly:
                if (rad < 10) return;
                pts = LevelShapes.RegularPolygonPts(shapeStart, rad, PolySides, ang);
                isGoal = true;
                break;
        }
        if (pts == null) return;

        PushUndo();
        if (!isGoal)
        {
            var poly = new PolyData
            {
                Pts = pts,
                Closed = closed,
                Pivot = EditorWallLocalPivot ? Centroid(pts) : null,
            };
            Design.Walls.Add(poly);
            SelectObject(poly);
        }
        else
        {
            var goal = new GoalData { Pts = pts };
            Design.Goals.Add(goal);
            SelectObject(goal);
        }
        PtsDirty();
        DesignChanged?.Invoke();
    }

    private static List<Vec2> EllipsePts(double cx, double cy, double rx, double ry, int n = 48)
    {
        var pts = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            double a = i * 2 * Math.PI / n;
            pts.Add(new Vec2(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry));
        }
        return pts;
    }

    // прореживание нарисованных от руки точек (для целей-полигонов)
    private static List<Vec2> Simplify(List<Vec2> src, double minDist)
    {
        var res = new List<Vec2> { src[0] };
        for (int i = 1; i < src.Count; i++)
            if (Vec2.Dist(res[^1], src[i]) >= minDist) res.Add(src[i]);
        if (res.Count < 3)
        {
            res.Clear();
            res.AddRange(src);
        }
        return res;
    }

    private void BeginDragObject(object hit, Vec2 w, bool pushUndo)
    {
        if (hit == null) return;
        if (pushUndo) PushUndo();
        dragObj = hit;
        dragClickTarget = hit;
        dragLast = w;
        dragMoved = false;
    }

    private BallData HitBall(Vec2 w) =>
        Design.Balls.FirstOrDefault(b => Vec2.Dist(b.Pos, w) < b.R + 6);

    private static bool IsAngleInArcDeg(double angleRad, double startDeg, double sweepDeg)
    {
        double a = NormalizeDeg(angleRad * 180.0 / Math.PI);
        double s = NormalizeDeg(startDeg);
        double rel = a - s;
        if (rel < 0) rel += 360.0;
        return rel <= Math.Clamp(sweepDeg, 1.0, 360.0);
    }

    public object HitTest(Vec2 w)
    {
        if (Design.WaveEmitter && Vec2.Dist(w, Design.WaveSourcePos) < 15)
            return WaveSourceHandle.Instance;
        for (int i = Design.Balls.Count - 1; i >= 0; i--)
            if (Vec2.Dist(Design.Balls[i].Pos, w) < Design.Balls[i].R + 6) return Design.Balls[i];
        for (int i = Design.Arcs.Count - 1; i >= 0; i--)
        {
            var a = Design.Arcs[i];
            if (Math.Abs(Vec2.Dist(w, a.Center) - a.Radius) < Math.Max(8.0, a.HalfWidth + 6.0) &&
                IsAngleInArcDeg(Math.Atan2(w.Y - a.Center.Y, w.X - a.Center.X), a.StartDeg, a.SweepDeg))
                return a;
        }
        for (int i = Design.Goals.Count - 1; i >= 0; i--)
            if (Design.Goals[i].Pts.Count >= 3 && Simulation.PointInPoly(w, Design.Goals[i].Pts)) return Design.Goals[i];
        for (int i = Design.Circles.Count - 1; i >= 0; i--)
            if (Math.Abs(Vec2.Dist(w, Design.Circles[i].Center) - Design.Circles[i].Radius) < 8) return Design.Circles[i];
        for (int i = Design.Walls.Count - 1; i >= 0; i--)
        {
            var p = Design.Walls[i];
            int n = p.Pts.Count;
            if (n < 2) continue;
            int segs = p.Closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
                if (Simulation.DistPtSeg(w, p.Pts[k], p.Pts[(k + 1) % n]) < 8) return p;
        }
        return null;
    }

    private void EraseGesture(Vec2 w)
    {
        var hit = HitTest(w);
        if (hit == null) return;
        if (!gestureUndoPushed) { PushUndo(); gestureUndoPushed = true; }
        if (ReferenceEquals(SelectedObject, hit)) SelectObject(null);
        RemoveObj(hit);
        PtsDirty();
        DesignChanged?.Invoke();
        Invalidate();
    }

    private void RemoveObj(object o)
    {
        switch (o)
        {
            case WaveSourceHandle: Design.WaveEmitter = false; break;
            case BallData b: Design.Balls.Remove(b); break;
            case ArcData a: Design.Arcs.Remove(a); break;
            case GoalData g: Design.Goals.Remove(g); break;
            case CircleData c: Design.Circles.Remove(c); break;
            case PolyData p: Design.Walls.Remove(p); break;
        }
    }

    private void TranslateObj(object o, Vec2 d)
    {
        switch (o)
        {
            case WaveSourceHandle: Design.WaveSourcePos += d; break;
            case BallData b: b.Pos += d; break;
            case ArcData a: a.Center += d; break;
            case CircleData c: c.Center += d; break;
            case GoalData g:
                for (int i = 0; i < g.Pts.Count; i++) g.Pts[i] += d;
                break;
            case PolyData p:
                for (int i = 0; i < p.Pts.Count; i++) p.Pts[i] += d;
                if (p.Pivot.HasValue) p.Pivot = p.Pivot.Value + d;
                break;
        }
    }

    public bool DeleteSelected()
    {
        if (SelectedObject == null) return false;
        PushUndo();
        var target = SelectedObject;
        SelectObject(null);
        RemoveObj(target);
        PtsDirty();
        DesignChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool DuplicateSelected()
    {
        if (SelectedObject == null || SelectedObject is WaveSourceHandle) return false;
        PushUndo();
        var shift = new Vec2(24, 24);
        object copy = null;
        switch (SelectedObject)
        {
            case BallData b:
                var nb = new BallData { Pos = b.Pos + shift, R = b.R, Vel = b.Vel, HasVel = b.HasVel, Argb = b.Argb };
                Design.Balls.Add(nb);
                copy = nb;
                break;
            case CircleData c:
                var nc = new CircleData { Center = c.Center, Radius = Math.Min(600, c.Radius + 28) };
                Design.Circles.Add(nc);
                copy = nc;
                break;
            case ArcData a:
                var na = new ArcData
                {
                    Center = a.Center,
                    Radius = a.Radius + 22.0,
                    StartDeg = NormalizeDeg(a.StartDeg + 32.0),
                    SweepDeg = a.SweepDeg,
                    HalfWidth = a.HalfWidth,
                    SpinMul = a.SpinMul,
                };
                Design.Arcs.Add(na);
                copy = na;
                break;
            case PolyData p:
                var np = new PolyData
                {
                    Closed = p.Closed,
                    Pivot = p.Pivot.HasValue ? p.Pivot.Value + shift : null,
                    Pts = p.Pts.Select(pt => pt + shift).ToList(),
                };
                Design.Walls.Add(np);
                copy = np;
                break;
            case GoalData g:
                var ng = new GoalData { Pts = g.Pts.Select(pt => pt + shift).ToList() };
                Design.Goals.Add(ng);
                copy = ng;
                break;
        }
        if (copy == null) return false;
        PtsDirty();
        SelectObject(copy);
        DesignChanged?.Invoke();
        Invalidate();
        return true;
    }

    public bool CenterSelected()
    {
        if (SelectedObject == null) return false;
        var fc = new Vec2(LevelData.W / 2, LevelData.H / 2);
        PushUndo();
        switch (SelectedObject)
        {
            case WaveSourceHandle: Design.WaveSourcePos = fc; break;
            case BallData b: b.Pos = fc; break;
            case CircleData c: c.Center = fc; break;
            case ArcData a: a.Center = fc; break;
            case PolyData p when p.Pts.Count > 0:
                TranslateObj(p, fc - Centroid(p.Pts));
                break;
            case GoalData g when g.Pts.Count > 0:
                TranslateObj(g, fc - Centroid(g.Pts));
                break;
        }
        PtsDirty();
        DesignChanged?.Invoke();
        Invalidate();
        return true;
    }

    public void ClearAll()
    {
        if (Design.Walls.Count == 0 && Design.Circles.Count == 0 && Design.Arcs.Count == 0 &&
            Design.Goals.Count == 0 && Design.Balls.Count == 0 && !Design.WaveEmitter)
            return;
        PushUndo();
        Design.Walls.Clear();
        Design.Circles.Clear();
        Design.Arcs.Clear();
        Design.Goals.Clear();
        Design.Balls.Clear();
        Design.WaveEmitter = false;
        SelectObject(null);
        PtsDirty();
        DesignChanged?.Invoke();
        Invalidate();
    }
}

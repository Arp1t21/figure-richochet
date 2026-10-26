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
    Select,     // выбор/перемещение
    WallFree,   // стенка — рисование от руки
    WallLine,   // стенка — отрезок
    WallRect,   // стенка — прямоугольник
    WallEllipse,// стенка — эллипс
    GoalFree,   // цель — рисование от руки
    GoalRect,   // цель — прямоугольник
    GoalEllipse,// цель — эллипс
    Ball,       // шар
    Eraser      // ластик
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

    /// <summary>Продвинуть часы визуальных эффектов (используется и циклом окна, и тестами).</summary>
    public void StepVisuals(double dt)
    {
        if (dt <= 0) return;
        uiTime += dt;
        if (!Paused) fxTime += dt;
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

    /// <summary>Выбранный шар в редакторе (клик по шару в режиме правки) — для смены скорости.</summary>
    public BallData SelectedBall;
    public event Action SelectionChanged;
    public event Action BallSpeedChanged;

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
    private bool shaping;                       // отрезок/прямоугольник/эллипс
    private Vec2 shapeStart, shapeCur;
    private bool aiming;                        // прицел шара
    private Vec2 aimStart, aimCur;
    private object dragObj;                     // перетаскивание в режиме Select
    private BallData dragHitBall;               // шар под курсором в начале клика (для выбора без переноса)
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

    public GameCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Cross;
        timer.Tick += (s, e) => Loop();
        timer.Start();
        Disposed += (s, e) =>
        {
            timer.Stop();
            frameG?.Dispose();
            frameBmp?.Dispose();
        };
        Sim.ResetFrom(Design);
    }

    public bool SimActive => !Editor || Testing;
    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

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
            // в чистом редакторе без запущенного теста перерисовываем по таймеру только пульсацию целей/осколков
            if (Design.Goals.Count > 0 || shards.Count > 0) Invalidate();
            return;
        }

        if (!Paused)
        {
            double sdt = dt * SimSpeed;
            Sim.SpinSpeed = SpinSpeed;
            if (SpinSpeed != 0 && Sim.Walls.Count > 0) PtsDirty();
            Sim.Step(sdt);
            Simulated?.Invoke(sdt);
        }
        else if (Design.Goals.Count == 0 && !WaveMode && shards.Count == 0)
        {
            return; // статичная пауза без пульсирующих целей/осколков — 0% CPU в простое
        }
        if (Sim.Finished && !wasFinished) { wasFinished = true; Won?.Invoke(); }
        Invalidate();
    }

    public void RestartSim()
    {
        Sim.ResetFrom(Design);
        Sim.SpinSpeed = SpinSpeed;
        wasFinished = false;
        placedStack.Clear();
        waveCount = 0;
        ballPaths.Clear();
        shards.Clear();
    }

    public void LoadLevel(LevelData lvl)
    {
        Design = lvl;
        RestartSim();
        undoStack.Clear();
        redoStack.Clear();
        SelectedBall = null;
        SelectionChanged?.Invoke();
        PtsDirty();
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
        SelectedBall = null;
        SelectionChanged?.Invoke();
        PtsDirty();
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
        SelectedBall = null;
        SelectionChanged?.Invoke();
        PtsDirty();
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

    public void SelectBall(BallData b)
    {
        if (ReferenceEquals(SelectedBall, b)) return;
        SelectedBall = b;
        SelectionChanged?.Invoke();
        Invalidate();
    }

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

        // поле: в «Роторе» глубокий космос, иначе — тёмный градиент вниз (глубина сцены)
        if (RotorSkin)
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
        DrawWalls(g);
        DrawDissolves(g);
        if (WaveMode) UpdateWavePaths(); // треки и лучи рисуются программно, см. RenderWaveFrame
        else
        {
            if (Trails && SimActive) DrawBallTrails(g);
            DrawBalls(g);
        }
        DrawPreview(g);
        DrawAim(g);
        DrawDropPreview(g);

        if (WaveMode && WaveSource.HasValue) DrawSource(g);
        if (!SimActive && SelectedBall != null)
        {
            float rr = (float)(SelectedBall.R + 6);
            g.DrawEllipse(SelectionPen, (float)(SelectedBall.Pos.X - rr), (float)(SelectedBall.Pos.Y - rr), rr * 2, rr * 2);
        }

        // тысячи лучей рисуются прямо в пиксели кадра (аддитивно), минуя GDI
        if (WaveMode) RenderWaveFrame(frameBmp);

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

    private void DrawSource(Graphics g)
    {
        var s = WaveSource.Value;
        float pr = 8 + (float)((Math.Sin(uiTime * 3) + 1) * 4); // пульсация источника
        g.DrawLine(SourcePen, (float)(s.X - 9), (float)s.Y, (float)(s.X + 9), (float)s.Y);
        g.DrawLine(SourcePen, (float)s.X, (float)(s.Y - 9), (float)s.X, (float)(s.Y + 9));
        g.FillEllipse(SourceDotBrush, (float)(s.X - 3.5), (float)(s.Y - 3.5), 7, 7);
        g.DrawEllipse(SourceRingPen, (float)(s.X - pr), (float)(s.Y - pr), pr * 2, pr * 2);
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

    private static Vec2 Centroid(List<Vec2> pts)
    {
        double x = 0, y = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; }
        return new Vec2(x / pts.Count, y / pts.Count);
    }

    private void DrawWalls(Graphics g)
    {
        if (Sim.Arcs.Count > 0)
        {
            const float Rad2Deg = (float)(180.0 / Math.PI);
            for (int i = 0; i < Sim.Arcs.Count; i++)
            {
                var a = Sim.Arcs[i];
                if (a.Radius < 1 || a.Radius - a.HalfWidth > 755) continue;
                float r = (float)a.Radius;
                g.DrawArc(RotorArcPen, (float)a.Center.X - r, (float)a.Center.Y - r, r * 2f, r * 2f,
                    (float)a.StartRad * Rad2Deg, (float)a.SweepRad * Rad2Deg);
            }
        }
        if (SpinSpeed != 0)
        {
            if (Sim.Walls.Count == 0) return;
            var linePen = RotorSkin ? RotorWallPen : WallPen;
            var glowPen = RotorSkin ? RotorGlowPen : WallGlowPen;
            foreach (var w in Sim.Walls)
            {
                int n = w.Pts.Count;
                var pts = spinPtsPool.TryGetValue(n, out var arr) ? arr : (spinPtsPool[n] = new PointF[n]);
                for (int i = 0; i < n; i++) pts[i] = new PointF((float)w.Pts[i].X, (float)w.Pts[i].Y);
                if (w.Closed) { g.DrawPolygon(glowPen, pts); g.DrawPolygon(linePen, pts); }
                else { g.DrawLines(glowPen, pts); g.DrawLines(linePen, pts); }
            }
            return;
        }
        EnsurePtsCache();
        foreach (var c in Design.Circles)
        {
            var r = (float)(c.Radius * 2);
            g.DrawEllipse(WallGlowPen, (float)(c.Center.X - c.Radius), (float)(c.Center.Y - c.Radius), r, r);
            g.DrawEllipse(WallPen, (float)(c.Center.X - c.Radius), (float)(c.Center.Y - c.Radius), r, r);
        }
        for (int i = 0; i < wallCache.Length; i++)
        {
            if (wallClosedCache[i])
            {
                g.DrawPolygon(WallGlowPen, wallCache[i]);
                g.DrawPolygon(WallPen, wallCache[i]);
            }
            else
            {
                g.DrawLines(WallGlowPen, wallCache[i]);
                g.DrawLines(WallPen, wallCache[i]);
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
    /// Программный рендер волнового фронта: треки и лучи рисуются прямо в пиксели
    /// кадра с аддитивным смешиванием (плотность лучей = яркость). Тысячи лучей
    /// обходятся без GDI-вызовов — это на порядки быстрее DrawLine.
    /// </summary>
    private unsafe void RenderWaveFrame(Bitmap bmp)
    {
        var balls = Sim.Balls;
        int fw = (int)LevelData.W, fh = (int)LevelData.H;
        var bd = bmp.LockBits(new Rectangle(0, 0, fw, fh), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        byte* b0 = (byte*)bd.Scan0;
        int stride = bd.Stride;
        try
        {
            for (int i = 0; i < balls.Count; i++)
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

    private static unsafe void AddLine(byte* b0, int stride, int fw, int fh, Vec2 p0, Vec2 p1, byte r, byte g, byte b, byte a)
    {
        // «капсула» вокруг отрезка: покрытие пикселя = квадратичный спад по
        // квадрату расстояния до отрезка (без sqrt и деления во внутреннем цикле).
        // Линия непрерывна, стыки сегментов скруглены, края мягкие
        int x0 = (int)Math.Floor(Math.Min(p0.X, p1.X)) - 1, x1 = (int)Math.Ceiling(Math.Max(p0.X, p1.X)) + 1;
        int y0 = (int)Math.Floor(Math.Min(p0.Y, p1.Y)) - 1, y1 = (int)Math.Ceiling(Math.Max(p0.Y, p1.Y)) + 1;
        if (x1 < 0 || y1 < 0 || x0 >= fw || y0 >= fh) return;
        if (x0 < 0) x0 = 0;
        if (y0 < 0) y0 = 0;
        if (x1 >= fw) x1 = fw - 1;
        if (y1 >= fh) y1 = fh - 1;

        double ax = p0.X, ay = p0.Y, abx = p1.X - ax, aby = p1.Y - ay;
        double len2 = abx * abx + aby * aby;
        double invLen2 = len2 < 1e-12 ? 0 : 1.0 / len2;
        int ar = r * a, ag = g * a, ab = b * a; // покрытие 0..1 домножается ниже
        const double Reach2 = 1.5625;           // 1.25² — радиус влияния капсулы
        const double K = 1.0 / Reach2;
        for (int py = y0; py <= y1; py++)
        {
            byte* row = b0 + py * stride;
            double dy0 = (py + 0.5 - ay) * aby;
            double pyc = py + 0.5;
            for (int px = x0; px <= x1; px++)
            {
                double cx = px + 0.5;
                double t = ((cx - ax) * abx + dy0) * invLen2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double dx = cx - (ax + abx * t), dy = pyc - (ay + aby * t);
                double d2 = dx * dx + dy * dy;
                if (d2 >= Reach2) continue;
                double cov = 1 - d2 * K;
                byte* p = row + px * 4; // порядок байт: B, G, R, A
                int v = p[0] + ((int)(ab * cov) >> 8); p[0] = (byte)(v > 255 ? 255 : v);
                v = p[1] + ((int)(ag * cov) >> 8); p[1] = (byte)(v > 255 ? 255 : v);
                v = p[2] + ((int)(ar * cov) >> 8); p[2] = (byte)(v > 255 ? 255 : v);
            }
        }
    }

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
        if (!SimActive)
        {
            foreach (var b in Design.Balls)
                DrawBallBody(g, b.Pos, b.R, b.Argb);
            return;
        }
        foreach (var b in Sim.Balls)
            DrawBallBody(g, b.Pos, b.R, b.Argb);
    }

    // затухающие треки обычных шаров (в игре и тесте редактора); кеш перьев по цвету
    private readonly List<List<Vec2>> ballPaths = new();
    private static readonly Dictionary<int, Pen[]> TrailFadePens = new();
    private static readonly Dictionary<int, SolidBrush> BodyBrushes = new();
    private static readonly Dictionary<int, Pen> HaloPens = new();

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

    private static Pen HaloPen(int argb) =>
        HaloPens.TryGetValue(argb, out var p) ? p
        : HaloPens[argb] = new Pen(Color.FromArgb(64, argb != 0 ? Color.FromArgb(argb) : Color.FromArgb(205, 224, 255)), 7f);

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

    // шар — чистая сфера (заливка + блик); ореол добавляется настройкой HaloOn,
    // мягкое свечение ярким элементам — только пост-обработкой (Fx)
    private static void DrawBallBody(Graphics g, Vec2 pos, double r, int argb)
    {
        float rr = (float)r;
        if (HaloOn)
        {
            float gr = rr + 3.5f;
            g.DrawEllipse(HaloPen(argb), (float)(pos.X - gr), (float)(pos.Y - gr), gr * 2, gr * 2);
        }
        g.FillEllipse(BodyBrush(argb), (float)(pos.X - rr), (float)(pos.Y - rr), rr * 2, rr * 2);
        g.FillEllipse(SpecBrush, (float)(pos.X - r * 0.4), (float)(pos.Y - r * 0.45), (float)(r * 0.35), (float)(r * 0.35));
    }

    private void DrawPreview(Graphics g)
    {
        if (drawing && freePts.Count > 1)
            g.DrawLines(PreviewPen, ToPts(freePts));
        else if (shaping)
        {
            double x1 = Math.Min(shapeStart.X, shapeCur.X), x2 = Math.Max(shapeStart.X, shapeCur.X);
            double y1 = Math.Min(shapeStart.Y, shapeCur.Y), y2 = Math.Max(shapeStart.Y, shapeCur.Y);
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
            }
        }
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
                    SelectBall(null);
                    drawing = true;
                    freePts.Clear();
                    freePts.Add(w);
                    break;
                case Tool.WallLine:
                case Tool.WallRect:
                case Tool.WallEllipse:
                case Tool.GoalRect:
                case Tool.GoalEllipse:
                    SelectBall(null);
                    shaping = true;
                    shapeStart = w;
                    shapeCur = w;
                    break;
                case Tool.Ball:
                    var hitBall = HitBall(w);
                    if (hitBall != null) { SelectBall(hitBall); break; } // клик по шару = выбрать (смена скорости)
                    SelectBall(null);
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
                    if (hit == null) SelectBall(null);
                    BeginDrag(ToWorld(e.Location));
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
        var w = EditorPoint(e);

        if (drawing)
        {
            drawing = false;
            if (freePts.Count >= 2)
            {
                PushUndo();
                if (CurTool == Tool.WallFree) Design.Walls.Add(new PolyData { Pts = new List<Vec2>(freePts) });
            else
            {
                var pts = Simplify(freePts, 8);
                if (pts.Count >= 3) Design.Goals.Add(new GoalData { Pts = pts });
            }
            PtsDirty();
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
            bool moving = dir.Len >= 4;
            // скорость задана режимом: в уровнях — неизменная, в редакторе/тесте — настраиваемая
            double speed = Editor ? EditorBallSpeed : PlayFixedSpeed;
            var vel = moving ? dir.Norm() * speed : new Vec2();
            int argb = NewBallSpectrum ? NextSpectrumColor() : NewBallArgb;
            if (!SimActive) // редактор: шар с заданным направлением остаётся в уровне
            {
                PushUndo();
                Design.Balls.Add(new BallData { Pos = aimStart, R = NewBallR, Vel = vel, HasVel = moving, Argb = argb });
            }
            else // игра/тест: шар сразу летит
            {
                placedStack.Add(Sim.AddBall(aimStart, vel, NewBallR, -1, argb));
            }
        }
        else if (dragObj != null)
        {
            if (!dragMoved && undoStack.Count > 0) undoStack.RemoveAt(undoStack.Count - 1); // клик без движения
            if (!dragMoved && dragHitBall != null) SelectBall(dragHitBall); // клик по шару = выбрать
            dragObj = null;
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

        List<Vec2> pts = null;
        bool closed = false;
        switch (CurTool)
        {
            case Tool.WallLine:
                if (Vec2.Dist(shapeStart, shapeCur) < 8) return;
                pts = new List<Vec2> { shapeStart, shapeCur };
                break;
            case Tool.WallRect:
                if (x2 - x1 < 8 || y2 - y1 < 8) return;
                pts = new List<Vec2> { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
                closed = true;
                break;
            case Tool.WallEllipse:
                if (x2 - x1 < 10 || y2 - y1 < 10) return;
                pts = EllipsePts((x1 + x2) / 2, (y1 + y2) / 2, (x2 - x1) / 2, (y2 - y1) / 2);
                closed = true;
                break;
            case Tool.GoalRect:
                if (x2 - x1 < 8 || y2 - y1 < 8) return;
                pts = new List<Vec2> { new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2) };
                break;
            case Tool.GoalEllipse:
                if (x2 - x1 < 10 || y2 - y1 < 10) return;
                pts = EllipsePts((x1 + x2) / 2, (y1 + y2) / 2, (x2 - x1) / 2, (y2 - y1) / 2);
                break;
        }
        if (pts == null) return;

        PushUndo();
        if (CurTool == Tool.WallLine || CurTool == Tool.WallRect || CurTool == Tool.WallEllipse)
            Design.Walls.Add(new PolyData { Pts = pts, Closed = closed });
        else
            Design.Goals.Add(new GoalData { Pts = pts });
        PtsDirty();
    }

    private static List<Vec2> EllipsePts(double cx, double cy, double rx, double ry, int n = 36)
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

    private void BeginDrag(Vec2 w)
    {
        var hit = HitTest(w);
        if (hit == null) return;
        PushUndo();
        dragObj = hit;
        dragHitBall = hit as BallData;
        dragLast = w;
        dragMoved = false;
    }

    private BallData HitBall(Vec2 w) =>
        Design.Balls.FirstOrDefault(b => Vec2.Dist(b.Pos, w) < b.R + 6);

    private object HitTest(Vec2 w)
    {
        foreach (var b in Design.Balls)
            if (Vec2.Dist(b.Pos, w) < b.R + 6) return b;
        foreach (var g in Design.Goals)
            if (g.Pts.Count >= 3 && Simulation.PointInPoly(w, g.Pts)) return g;
        foreach (var c in Design.Circles)
            if (Math.Abs(Vec2.Dist(w, c.Center) - c.Radius) < 7) return c;
        foreach (var p in Design.Walls)
        {
            int n = p.Pts.Count;
            if (n < 2) continue;
            int segs = p.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                if (Simulation.DistPtSeg(w, p.Pts[i], p.Pts[(i + 1) % n]) < 7) return p;
        }
        return null;
    }

    private void EraseGesture(Vec2 w)
    {
        var hit = HitTest(w);
        if (hit == null) return;
        if (!gestureUndoPushed) { PushUndo(); gestureUndoPushed = true; }
        if (ReferenceEquals(SelectedBall, hit)) SelectBall(null);
        RemoveObj(hit);
        PtsDirty();
        Invalidate();
    }

    private void RemoveObj(object o)
    {
        switch (o)
        {
            case BallData b: Design.Balls.Remove(b); break;
            case GoalData g: Design.Goals.Remove(g); break;
            case CircleData c: Design.Circles.Remove(c); break;
            case PolyData p: Design.Walls.Remove(p); break;
        }
    }

    private void TranslateObj(object o, Vec2 d)
    {
        switch (o)
        {
            case BallData b: b.Pos += d; break;
            case CircleData c: c.Center += d; break;
            case GoalData g:
                for (int i = 0; i < g.Pts.Count; i++) g.Pts[i] += d;
                break;
            case PolyData p:
                for (int i = 0; i < p.Pts.Count; i++) p.Pts[i] += d;
                break;
        }
    }
}

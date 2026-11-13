using System;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Ротор»: шар внутри неполных вращающихся кругов с вырезом.
/// При запуске любого уровня круги стоят на паузе, а шар не создаётся сам —
/// игрок кликом ЛКМ ставит шар с нулевой начальной скоростью (без выбора направления),
/// что сразу запускает вращение кругов.
/// </summary>
public class RotorView : UserControl, IModeView
{
    public const int LevelCount = 22;
    public const int SpiralCount = 200;
    public const double SpiralR0 = 98.0;
    public const double SpiralStep = 8.5;
    public const double SpiralGapDeg = 60.0;
    public const double SpiralTwistDeg = 3.0;

    public static readonly string[] PresetBadges =
    {
        "1 КРУГ", "200 КОЛЕЦ", "РЕВЕРС", "ВСТРЕЧНЫЕ", "РАЗГОН",
        "ВИХРЬ", "50 · ПРОСТОР", "ШЛЮЗ", "МАЯТНИК", "ОРБИТА",
        "6→КРУГ", "3→12 ГРАНЕЙ", "ЗМЕЙКА ±180°", "УШКО 110→28°", "ШКВАЛ ×3", "ТУРБИНА",
        "СУПЕРНОВА ×5", "4→КРУГ", "ГАЛАКТИКА", "МЕДЛЕННЫЙ", "5→КРУГ", "300 КРУГОВ",
    };

    private sealed class PolyShell
    {
        public PolyData Src;
        public double Radius;
        public int Sides;
    }

    private readonly GameCanvas canvas = new()
    {
        Editor = false,
        Dock = DockStyle.Fill,
        ShowGrid = false,
        Trails = false,
        RotorSkin = true,
        InstantDropNoAim = true,
        NewBallR = 10,
        NewBallArgb = unchecked((int)0xFFF0F3FF),
    };

    public GameCanvas Canvas => canvas;

    private int index;
    private Vec2 center = new(640, 360);
    private double curBaseR0 = SpiralR0;
    private int curTotalRings = 1;
    private bool curReverseOnBreak;
    private double curAccelOnBreak = 1.0;
    private bool curContractRings = true;
    private int curDropBurst = 1;
    private bool waitingForBall;
    private readonly System.Collections.Generic.List<PolyShell> polyRings = new();

    private readonly LevelSelectButton lblName = new()
    {
        Size = new Size(248, 38), Role = AccentRole.Orange,
    };
    private readonly NeoButton btnEdit = new()
    {
        Text = "В РЕДАКТОР", Icon = IconPainter.Pencil, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(108, 38), Font = new Font("Segoe UI", 8.2F, FontStyle.Bold),
    };
    private readonly NeoButton btnSandbox = new()
    {
        Text = "ПЕСОЧНИЦА", Icon = IconPainter.Sliders, IconPx = 15, Role = AccentRole.Accent,
        Size = new Size(104, 38), Font = new Font("Segoe UI", 8.2F, FontStyle.Bold),
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Play, Size = new Size(40, 38), IconPx = 19 };
    private readonly NeoButton btnRestart = new() { Icon = IconPainter.Restart, Size = new Size(40, 38), IconPx = 19 };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(42, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF), Size = new Size(44, 38) };
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 26, Value = 10, Size = new Size(52, 38) };
    private readonly Label hintLbl;

    public RotorView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Toolbar { Dock = DockStyle.Top, Height = 56 };
        var hints = new Bar { Dock = DockStyle.Bottom, Height = 34, LineAtTop = true, BackColor = Theme.Panel };
        hintLbl = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Panel,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        hints.Controls.Add(hintLbl);

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(36, 38), IconPx = 19 };
        var btnNext = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(36, 38), IconPx = 19 };

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий уровень");
        tips.SetToolTip(btnNext, "Следующий уровень");
        tips.SetToolTip(lblName, "Открыть сетку уровней «Ротора» с мини-превью");
        tips.SetToolTip(btnEdit, "Перенести копию текущего уровня «Ротора» в «Редактор»");
        tips.SetToolTip(btnSandbox, "Тонкая настройка вращения, физики и правил (Песочница)");
        tips.SetToolTip(chipFx, "Шейдерные эффекты");
        tips.SetToolTip(colorBtn, "Цвет шара (СПЕКТР — каждый шар нового тона)");
        tips.SetToolTip(sizeBtn, "Размер шара — ползунок (или колесо мыши, +/−)");
        tips.SetToolTip(btnPause, "Пауза / продолжить (пробел)");
        tips.SetToolTip(btnRestart, "Заново (R)");

        lblName.ItemsProvider = () =>
        {
            var saved = (curBaseR0, curTotalRings, curReverseOnBreak, curAccelOnBreak, curContractRings, curDropBurst);
            var items = new System.Collections.Generic.List<LevelCardItem>(LevelCount);
            for (int i = 0; i < LevelCount; i++)
            {
                var l = BuildLevel(i, out _, out _, out _);
                items.Add(new LevelCardItem
                {
                    Title = $"{i + 1}. {l.Name}",
                    Badge = PresetBadges[i],
                    Preview = l,
                    Role = AccentRole.Orange,
                    RotorStyle = true,
                });
            }
            (curBaseR0, curTotalRings, curReverseOnBreak, curAccelOnBreak, curContractRings, curDropBurst) = saved;
            return (index, items);
        };
        lblName.LevelSelected += ShowLevel;

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + LevelCount) % LevelCount);
        btnNext.Click += (s, e) => ShowLevel((index + 1) % LevelCount);
        btnEdit.Click += (s, e) => MainForm.RequestOpenInEditor(ExportCurrentLevel());
        SandboxPopup.Attach(btnSandbox, "Песочница · Ротор", BuildSandboxEntries);
        btnPause.Click += (s, e) => TogglePause();
        btnRestart.Click += (s, e) => ShowLevel(index);
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;
        colorBtn.Changed += () =>
        {
            canvas.NewBallSpectrum = colorBtn.Selected == BallColors.Spectrum;
            canvas.NewBallArgb = colorBtn.Selected;
            int argb = canvas.NewBallSpectrum ? unchecked((int)0xFF5EC5FF) : colorBtn.Selected;
            foreach (var b in canvas.Sim.Balls) b.Argb = argb;
            canvas.Invalidate();
        };
        sizeBtn.Changed += v => SetBallRadius(v);
        canvas.BallRadiusChanged += () => SetBallRadius(canvas.NewBallR);
        canvas.ClampDropPos = ClampToInnerChamber;
        canvas.BallPlaced += _ => OnBallPlaced();
        canvas.Simulated += OnSimulated;

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName).AddLeft(btnEdit).AddLeft(btnSandbox);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn).AddRight(btnRestart).AddRight(btnPause);

        Controls.Add(canvas);
        Controls.Add(hints);
        Controls.Add(top);

        Action onTheme = () =>
        {
            BackColor = Theme.Bg;
            top.BackColor = Theme.Panel;
            hints.BackColor = Theme.Panel;
            hintLbl.BackColor = Theme.Panel;
            hintLbl.ForeColor = Theme.TextDim;
            Invalidate(true);
        };
        Theme.Changed += onTheme;
        Disposed += (s, e) => Theme.Changed -= onTheme;

        ShowLevel(0);
    }

    public void SetActive(bool active) => canvas.SetLoopActive(active);

    /// <summary>Ограничивает точку постановки шара внутренней камерой текущего кольца.</summary>
    private Vec2 ClampToInnerChamber(Vec2 p)
    {
        double? maxPolyR = polyRings.Count > 0
            ? polyRings[0].Radius * Math.Cos(Math.PI / Math.Max(3, polyRings[0].Sides)) - canvas.NewBallR - 4.0
            : null;
        double? maxArcR = canvas.Sim.Arcs.Count > 0
            ? canvas.Sim.Arcs[0].Radius - canvas.NewBallR - canvas.Sim.Arcs[0].HalfWidth - 3.5
            : null;
        double? best = (maxPolyR.HasValue && maxArcR.HasValue) ? Math.Min(maxPolyR.Value, maxArcR.Value) : (maxPolyR ?? maxArcR);
        if (best.HasValue)
        {
            double maxDist = Math.Max(4.0, best.Value);
            var d = p - center;
            double len = d.Len;
            if (len > maxDist && len > 1e-6)
                return center + d * (maxDist / len);
            return p;
        }
        return new Vec2(
            Math.Clamp(p.X, canvas.NewBallR + 6, LevelData.W - canvas.NewBallR - 6),
            Math.Clamp(p.Y, canvas.NewBallR + 6, LevelData.H - canvas.NewBallR - 6));
    }

    private void OnBallPlaced()
    {
        bool wasWait = waitingForBall;
        if (waitingForBall || canvas.Paused)
        {
            waitingForBall = false;
            canvas.Paused = false;
            canvas.SuppressPauseBanner = false;
            canvas.PromptTitle = null;
            canvas.PromptSub = null;
            btnPause.Icon = IconPainter.Pause;
            btnPause.Invalidate();
        }
        if (wasWait && curDropBurst > 1 && canvas.Sim.Balls.Count == 1)
        {
            var b0 = canvas.Sim.Balls[0];
            double spread = Math.Max(14.0, canvas.NewBallR * 2.2);
            for (int k = 1; k < curDropBurst; k++)
            {
                double ang = k * (Math.PI * 2.0 / curDropBurst) + 0.4;
                var pos = ClampToInnerChamber(b0.Pos + new Vec2(Math.Cos(ang) * spread, Math.Sin(ang) * spread));
                var vel = new Vec2(Math.Cos(ang) * 95, Math.Sin(ang) * 40 - 35);
                int argb = GameCanvas.SpectrumPalette24[(k * 7 + 3) % GameCanvas.SpectrumPalette24.Length];
                canvas.Sim.AddBall(pos, vel, canvas.NewBallR, argb: argb);
            }
        }
        UpdateProgress();
    }

    private void SetBallRadius(double r)
    {
        double clamped = Math.Clamp(Math.Round(r), sizeBtn.Min, sizeBtn.Max);
        canvas.NewBallR = clamped;
        sizeBtn.Value = (int)clamped;
        foreach (var b in canvas.Sim.Balls) b.R = clamped;
        canvas.Invalidate();
    }

    /// <summary>Экспортировать текущий уровень «Ротора» со всеми его правилами для «Редактора».</summary>
    public LevelData ExportCurrentLevel() => CreatePresetLevel(index, out _, out _, out _, out _, out _, out _, out _, out _);

    /// <summary>Все 16 эталонных уровней «Ротора» со всеми правилами для галереи шаблонов редактора.</summary>
    public static System.Collections.Generic.List<LevelData> BuildPresetLevels()
    {
        var list = new System.Collections.Generic.List<LevelData>(LevelCount);
        for (int i = 0; i < LevelCount; i++)
            list.Add(CreatePresetLevel(i, out _, out _, out _, out _, out _, out _, out _, out _));
        return list;
    }

    /// <summary>Свежая геометрия уровня без предсозданных шаров (шар ставит сам игрок).</summary>
    private LevelData BuildLevel(int i, out Vec2 c, out double spinSpeed, out string startHint)
    {
        var lvl = CreatePresetLevel(i, out c, out spinSpeed, out curBaseR0, out curTotalRings, out curReverseOnBreak, out curAccelOnBreak, out curContractRings, out curDropBurst);
        startHint = "  " + lvl.Hint;
        return lvl;
    }

    private static int LerpArgb(int a, int b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int a0 = (a >> 24) & 255, r0 = (a >> 16) & 255, g0 = (a >> 8) & 255, b0 = a & 255;
        int a1 = (b >> 24) & 255, r1 = (b >> 16) & 255, g1 = (b >> 8) & 255, b1 = b & 255;
        int aa = (int)Math.Round(a0 + (a1 - a0) * t);
        int rr = (int)Math.Round(r0 + (r1 - r0) * t);
        int gg = (int)Math.Round(g0 + (g1 - g0) * t);
        int bb = (int)Math.Round(b0 + (b1 - b0) * t);
        return (aa << 24) | (rr << 16) | (gg << 8) | bb;
    }

    private static PolyData MakeOpenPolygon(Vec2 c, double r, int sides, double startAngleDeg, double spinMul, int argb)
    {
        double a0 = startAngleDeg * Math.PI / 180.0;
        double da = Math.PI * 2.0 / sides;
        int vCount = sides >= 9 ? sides - 1 : sides;
        var pts = new System.Collections.Generic.List<Vec2>(vCount);
        for (int i = 0; i < vCount; i++)
            pts.Add(new Vec2(c.X + Math.Cos(a0 + i * da) * r, c.Y + Math.Sin(a0 + i * da) * r));
        return new PolyData { Closed = false, Pts = pts, Pivot = c, SpinMul = spinMul, Argb = argb };
    }

    private static LevelData CreatePresetLevel(
        int i, out Vec2 c, out double spinSpeed,
        out double baseR0, out int totalRings, out bool reverseOnBreak, out double accelOnBreak,
        out bool contractRings, out int dropBurst)
    {
        c = new Vec2(640, 360);
        reverseOnBreak = false;
        accelOnBreak = 1.0;
        contractRings = true;
        dropBurst = 1;

        switch (i)
        {
            case 0: // 1. «ВЫХОД» — один большой вращающийся круг
            {
                baseR0 = 250.0;
                totalRings = 1;
                spinSpeed = -0.72;
                var lvl = new LevelData
                {
                    Name = "ВЫХОД",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "ЛКМ внутри круга — поставить шар (скорость 0) и запустить вращение  ·  R — заново",
                };
                lvl.Arcs.Add(new ArcData
                {
                    Center = c,
                    Radius = baseR0,
                    StartDeg = 35,
                    SweepDeg = 360 - 62,
                    HalfWidth = 2.25,
                });
                return lvl;
            }

            case 1: // 2. «200 КРУГОВ» — классическая плотная розовая спираль со счётчиком
            {
                baseR0 = SpiralR0;
                totalRings = SpiralCount;
                spinSpeed = 1.45;
                var lvl = new LevelData
                {
                    Name = "200 КРУГОВ",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = $"ЛКМ в центре — поставить шар (скорость 0) и запустить спираль из {SpiralCount} кругов  ·  R — заново",
                };
                for (int k = 0; k < SpiralCount; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = SpiralR0 + k * SpiralStep,
                        StartDeg = 158.0 + k * SpiralTwistDeg,
                        SweepDeg = 360.0 - SpiralGapDeg,
                        HalfWidth = 2.25,
                    });
                }
                return lvl;
            }

            case 2: // 3. «РЕВЕРС» — каждый пройденный круг меняет направление вращения всех остальных на противоположное
            {
                baseR0 = 128.0;
                totalRings = 30;
                reverseOnBreak = true;
                spinSpeed = 1.15;
                var lvl = new LevelData
                {
                    Name = "РЕВЕРС",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    ReverseOnBreak = true,
                    RotorBaseR0 = baseR0,
                    Hint = "каждый пройденный круг меняет направление вращения всех кругов на противоположное  ·  ЛКМ — старт",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 22.0,
                        StartDeg = 135.0 + k * 52.0,
                        SweepDeg = 360.0 - 68.0,
                        HalfWidth = 2.25,
                    });
                }
                return lvl;
            }

            case 3: // 4. «ВСТРЕЧНЫЕ» — чётные и нечётные круги вращаются навстречу друг другу
            {
                baseR0 = 128.0;
                totalRings = 30;
                spinSpeed = 0.98;
                var lvl = new LevelData
                {
                    Name = "ВСТРЕЧНЫЕ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "соседние круги вращаются в противоположные стороны  ·  ЛКМ в центре — поставить шар (скорость 0)",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 22.0,
                        StartDeg = 90.0 + k * 43.0,
                        SweepDeg = 360.0 - 70.0,
                        HalfWidth = 2.25,
                        SpinMul = (k % 2 == 0) ? 1.0 : -1.0,
                    });
                }
                return lvl;
            }

            case 4: // 5. «РАЗГОН» — с каждым разрушенным кругом вращение ускоряется
            {
                baseR0 = 128.0;
                totalRings = 35;
                accelOnBreak = 1.045;
                spinSpeed = 0.48;
                var lvl = new LevelData
                {
                    Name = "РАЗГОН",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    AccelOnBreak = accelOnBreak,
                    RotorBaseR0 = baseR0,
                    Hint = "каждый пройденный круг ускоряет вращение остальных  ·  ЛКМ в центре — поставить шар (скорость 0)",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 20.0,
                        StartDeg = 120.0 + k * 47.0,
                        SweepDeg = 360.0 - 70.0,
                        HalfWidth = 2.25,
                    });
                }
                return lvl;
            }

            case 5: // 6. «ВИХРЬ» — дифференциальные скорости и встречные потоки колец
            {
                baseR0 = 128.0;
                totalRings = 36;
                spinSpeed = 1.05;
                var lvl = new LevelData
                {
                    Name = "ВИХРЬ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "каждое кольцо крутится со своей скоростью и направлением  ·  ЛКМ в центре — поставить шар",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    double dir = (k % 2 == 0) ? 1.0 : -1.0;
                    double speedMul = 0.65 + (k % 5) * 0.22;
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 20.0,
                        StartDeg = 150.0 + k * 37.0,
                        SweepDeg = 360.0 - 72.0,
                        HalfWidth = 2.25,
                        SpinMul = dir * speedMul,
                    });
                }
                return lvl;
            }

            case 6: // 7. «50 КРУГОВ · ПРОСТОР» — 50 колец вокруг огромной свободной камеры, как в первом уровне
            {
                baseR0 = 248.0;
                totalRings = 50;
                spinSpeed = 1.18;
                var lvl = new LevelData
                {
                    Name = "50 КРУГОВ · ПРОСТОР",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "50 кругов и огромное открытое пространство внутри, как в первом уровне  ·  ЛКМ — поставить шар",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 8.5,
                        StartDeg = 289.0 + k * 5.0,
                        SweepDeg = 360.0 - 64.0,
                        HalfWidth = 2.25,
                    });
                }
                return lvl;
            }

            case 7: // 8. «УЗКИЙ ШЛЮЗ» — массивные кольца с узкими прорезями
            {
                baseR0 = 142.0;
                totalRings = 22;
                spinSpeed = 1.12;
                var lvl = new LevelData
                {
                    Name = "УЗКИЙ ШЛЮЗ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "толстые кольца с узким шлюзом 50° — дождитесь точного выравнивания прорези  ·  ЛКМ — старт",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 18.0,
                        StartDeg = 340.0 + k * 41.0,
                        SweepDeg = 360.0 - 50.0,
                        HalfWidth = 3.2,
                    });
                }
                return lvl;
            }

            case 8: // 9. «МАЯТНИК» — встречные слои + реверс и плавный разгон при каждом пробое
            {
                baseR0 = 150.0;
                totalRings = 28;
                reverseOnBreak = true;
                accelOnBreak = 1.025;
                spinSpeed = 1.08;
                var lvl = new LevelData
                {
                    Name = "МАЯТНИК",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    ReverseOnBreak = true,
                    AccelOnBreak = accelOnBreak,
                    RotorBaseR0 = baseR0,
                    Hint = "встречные слои меняют ход и ускоряются при каждом пройденном кольце  ·  ЛКМ — старт",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 16.0,
                        StartDeg = 348.0 + k * 38.0,
                        SweepDeg = 360.0 - 66.0,
                        HalfWidth = 2.25,
                        SpinMul = (k % 2 == 0) ? 1.0 : -0.75,
                    });
                }
                return lvl;
            }

            case 9: // 10. «ОРБИТА» — просторная камера с тремя вращающимися спутниками-отражателями
            {
                baseR0 = 226.0;
                totalRings = 24;
                spinSpeed = 1.06;
                var lvl = new LevelData
                {
                    Name = "ОРБИТА",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "три спутника вращаются внутри просторной камеры и подбрасывают шар к прорезям  ·  ЛКМ — старт",
                };
                for (int m = 0; m < 3; m++)
                {
                    double orbitAng = (30.0 + m * 120.0) * Math.PI / 180.0;
                    double mx = c.X + Math.Cos(orbitAng) * 112.0;
                    double my = c.Y + Math.Sin(orbitAng) * 112.0;
                    var sat = new PolyData { Closed = true, Pivot = c };
                    for (int v = 0; v < 6; v++)
                    {
                        double va = orbitAng + v * Math.PI / 3.0;
                        sat.Pts.Add(new Vec2(mx + Math.Cos(va) * 18.0, my + Math.Sin(va) * 18.0));
                    }
                    lvl.Walls.Add(sat);
                }
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 11.0,
                        StartDeg = 317.0 + k * 29.0,
                        SweepDeg = 360.0 - 68.0,
                        HalfWidth = 2.25,
                    });
                }
                return lvl;
            }

            case 10: // 11. «ПОЛИГОНЫ 6→КРУГ» — внутри вращаются открытые персиковые шестиугольники, снаружи — изумрудные круги (как в видео)
            {
                const int polyCount = 6;
                const int arcCount = 18;
                baseR0 = 88.0;
                totalRings = polyCount + arcCount;
                spinSpeed = 1.05;
                var lvl = new LevelData
                {
                    Name = "ПОЛИГОНЫ 6→КРУГ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "внутри открытые персиковые шестиугольники, снаружи — градиент изумрудных колец (как в видео)  ·  ЛКМ — старт",
                };
                int peach0 = unchecked((int)0xFFFF7A9E);
                int peach1 = unchecked((int)0xFFFFBF6B);
                double rPoly = baseR0;
                double cosHex = Math.Cos(Math.PI / 6.0);
                for (int k = 0; k < polyCount; k++)
                {
                    int col = LerpArgb(peach0, peach1, k / (double)(polyCount - 1));
                    lvl.Walls.Add(MakeOpenPolygon(c, rPoly, 6, -90.0 + k * 14.0, 1.0, col));
                    if (k + 1 < polyCount)
                        rPoly = Math.Max(rPoly + 16.0, (rPoly + 5.0) / cosHex);
                }
                int green0 = unchecked((int)0xFF8CFF7A);
                int teal1 = unchecked((int)0xFF39E8D6);
                for (int k = 0; k < arcCount; k++)
                {
                    double r = rPoly + 14.0 + k * 11.0;
                    int col = LerpArgb(green0, teal1, k / (double)(arcCount - 1));
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = r,
                        StartDeg = 120.0 + k * 12.0,
                        SweepDeg = 360.0 - 62.0,
                        HalfWidth = 2.3,
                        SpinMul = 0.92,
                        Argb = col,
                    });
                }
                return lvl;
            }

            case 11: // 12. «КАЛЕЙДОСКОП ГРАНЕЙ» — многоугольники от треугольника (3) до двенадцатиугольника (12) + кольца
            {
                int[] sidesSeq = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
                const int arcCount = 12;
                baseR0 = 76.0;
                totalRings = sidesSeq.Length + arcCount;
                spinSpeed = 0.98;
                var lvl = new LevelData
                {
                    Name = "КАЛЕЙДОСКОП ГРАНЕЙ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "каждый слой усложняется от 3 до 12 граней и чередует направление вращения  ·  ЛКМ — старт",
                };
                double r = baseR0;
                for (int k = 0; k < sidesSeq.Length; k++)
                {
                    if (k > 0)
                        r = Math.Max(r + 12.0, (r + 6.0) / Math.Cos(Math.PI / sidesSeq[k]));
                    int col = GameCanvas.SpectrumPalette24[(k * 2) % GameCanvas.SpectrumPalette24.Length];
                    double mul = (k % 2 == 0) ? 1.05 : -0.85;
                    lvl.Walls.Add(MakeOpenPolygon(c, r, sidesSeq[k], -80.0 + k * 23.0, mul, col));
                }
                for (int k = 0; k < arcCount; k++)
                {
                    double ar = r + 14.0 + k * 11.0;
                    int col = GameCanvas.SpectrumPalette24[((sidesSeq.Length + k) * 2) % GameCanvas.SpectrumPalette24.Length];
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = ar,
                        StartDeg = 40.0 + k * 21.0,
                        SweepDeg = 360.0 - 64.0,
                        HalfWidth = 2.3,
                        SpinMul = ((sidesSeq.Length + k) % 2 == 0) ? 1.0 : -0.85,
                        Argb = col,
                    });
                }
                return lvl;
            }

            case 12: // 13. «ЗМЕЙКА ВЫРЕЗОВ ±180°» — прорези прыгают на противоположную сторону на каждом шаге
            {
                baseR0 = 114.0;
                totalRings = 26;
                spinSpeed = 1.08;
                var lvl = new LevelData
                {
                    Name = "ЗМЕЙКА ВЫРЕЗОВ ±180°",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "соседние выходы смещены на 180° — шар мечется от стенки к стенке сквозь 26 слоёв  ·  ЛКМ — старт",
                };
                int cyan0 = unchecked((int)0xFF56F0FF);
                int mag1 = unchecked((int)0xFFE26CFF);
                for (int k = 0; k < totalRings; k++)
                {
                    int col = LerpArgb(cyan0, mag1, k / (double)(totalRings - 1));
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 16.5,
                        StartDeg = (k % 2 == 0 ? 18.0 : 198.0) + k * 7.0,
                        SweepDeg = 360.0 - 62.0,
                        HalfWidth = 2.25,
                        Argb = col,
                    });
                }
                return lvl;
            }

            case 13: // 14. «ИГОЛЬНОЕ УШКО» — прорезь постепенно сужается с 110° до узких 28°
            {
                baseR0 = 118.0;
                totalRings = 36;
                spinSpeed = 1.12;
                var lvl = new LevelData
                {
                    Name = "ИГОЛЬНОЕ УШКО",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "вырез сужается от широких 110° на старте до узких 28° на внешнем ободе  ·  ЛКМ — старт",
                };
                int c0 = unchecked((int)0xFF52FFA6);
                int c1 = unchecked((int)0xFFFF4B72);
                for (int k = 0; k < totalRings; k++)
                {
                    double t = k / (double)(totalRings - 1);
                    double gap = 110.0 - 82.0 * t;
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 11.0,
                        StartDeg = 135.0 + k * 19.0,
                        SweepDeg = 360.0 - gap,
                        HalfWidth = 2.4,
                        Argb = LerpArgb(c0, c1, t),
                    });
                }
                return lvl;
            }

            case 14: // 15. «ШКВАЛ ×3» — старт сразу тремя сталкивающимися шарами в 60 радужных слоях
            {
                baseR0 = 122.0;
                totalRings = 60;
                spinSpeed = 1.15;
                dropBurst = 3;
                var lvl = new LevelData
                {
                    Name = "ШКВАЛ ×3",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    NoBallCollisions = false,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "при старте вылетают сразу 3 сталкивающихся шара сквозь 60 радужных колец  ·  ЛКМ — старт",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 9.5,
                        StartDeg = 200.0 + k * 9.5,
                        SweepDeg = 360.0 - 64.0,
                        HalfWidth = 2.25,
                        Argb = GameCanvas.SpectrumPalette24[k % GameCanvas.SpectrumPalette24.Length],
                    });
                }
                return lvl;
            }

            case 15: // 16. «ТУРБИНА ХАОСА» — вращающиеся лопасти внутри 38 радужных колец
            {
                baseR0 = 136.0;
                totalRings = 38;
                spinSpeed = 1.05;
                var lvl = new LevelData
                {
                    Name = "ТУРБИНА ХАОСА",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "три вращающиеся лопасти турбины постоянно подбрасывают шары к прорезям  ·  ЛКМ — старт",
                };
                for (int b = 0; b < 3; b++)
                {
                    double ba = b * (Math.PI * 2.0 / 3.0);
                    var blade = new PolyData
                    {
                        Closed = false,
                        Pivot = c,
                        SpinMul = -1.65,
                        Argb = unchecked((int)0xFF5CE8FF),
                        Pts = new System.Collections.Generic.List<Vec2>
                        {
                            c + new Vec2(Math.Cos(ba) * 20.0, Math.Sin(ba) * 20.0),
                            c + new Vec2(Math.Cos(ba) * 102.0, Math.Sin(ba) * 102.0),
                        },
                    };
                    lvl.Walls.Add(blade);
                }
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 10.0,
                        StartDeg = 40.0 + k * 17.0,
                        SweepDeg = 360.0 - 64.0,
                        HalfWidth = 2.25,
                        Argb = GameCanvas.SpectrumPalette24[(k * 2) % GameCanvas.SpectrumPalette24.Length],
                    });
                }
                return lvl;
            }

            case 16: // 17. «СУПЕРНОВА ×5» — 5 сталкивающихся шаров разрывают 72 огненных кольца
            {
                baseR0 = 132.0;
                totalRings = 72;
                spinSpeed = 1.18;
                dropBurst = 5;
                var lvl = new LevelData
                {
                    Name = "СУПЕРНОВА ×5",
                    Gravity = true,
                    GravityScale = 0.56,
                    ElasticBounce = true,
                    NoBallCollisions = false,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "выстрел сразу 5 шарами: рикошеты друг от друга разрывают 72 огненных кольца  ·  ЛКМ — старт",
                };
                int fire0 = unchecked((int)0xFFFF3D6E);
                int fire1 = unchecked((int)0xFFFFE45E);
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 9.0,
                        StartDeg = 90.0 + k * 11.5,
                        SweepDeg = 360.0 - 64.0,
                        HalfWidth = 2.2,
                        Argb = LerpArgb(fire0, fire1, k / (double)(totalRings - 1)),
                    });
                }
                return lvl;
            }

            case 17: // 18. «СПИРАЛЬ КВАДРАТОВ» — 8 соосных открытых квадратов переходят в 16 колец
            {
                const int polyCount = 8;
                const int arcCount = 16;
                baseR0 = 84.0;
                totalRings = polyCount + arcCount;
                spinSpeed = 1.02;
                var lvl = new LevelData
                {
                    Name = "СПИРАЛЬ КВАДРАТОВ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "восемь открытых квадратов со смещёнными выходами рассыпаются перед градиентом колец  ·  ЛКМ — старт",
                };
                int sq0 = unchecked((int)0xFFFF68D8);
                int sq1 = unchecked((int)0xFF56EEFF);
                for (int k = 0; k < polyCount; k++)
                {
                    double r = baseR0 + k * 24.0;
                    int col = LerpArgb(sq0, sq1, k / (double)(polyCount - 1));
                    lvl.Walls.Add(MakeOpenPolygon(c, r, 4, -45.0 + (k % 4) * 90.0, 1.0, col));
                }
                for (int k = 0; k < arcCount; k++)
                {
                    double r = baseR0 + (polyCount - 1) * 24.0 + 18.0 + k * 11.0;
                    int col = LerpArgb(sq1, unchecked((int)0xFF6EFF9E), k / (double)(arcCount - 1));
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = r,
                        StartDeg = 45.0 + k * 15.0,
                        SweepDeg = 360.0 - 62.0,
                        HalfWidth = 2.3,
                        SpinMul = 0.90,
                        Argb = col,
                    });
                }
                return lvl;
            }

            case 18: // 19. «ГАЛАКТИКА · ОРБИТА» — встречные внутренние клинья + разгон 32 фиолетово-мятных колец
            {
                baseR0 = 92.0;
                totalRings = 32;
                spinSpeed = 1.18;
                accelOnBreak = 1.022;
                var lvl = new LevelData
                {
                    Name = "ДВОЙНАЯ ОРБИТА",
                    Gravity = true,
                    GravityScale = 0.64,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "встречные орбитальные клинья и ускорение с каждым из 32 колец галактики  ·  ЛКМ — старт",
                };
                for (int s = 0; s < 4; s++)
                {
                    double a = 0.25 + s * Math.PI * 0.5;
                    double rIn = (s % 2 == 0) ? 28.0 : 38.0;
                    double rOut = (s % 2 == 0) ? 54.0 : 66.0;
                    var sat = new PolyData
                    {
                        Closed = false,
                        Pivot = c,
                        SpinMul = (s % 2 == 0) ? 1.45 : -1.65,
                        Argb = (s % 2 == 0) ? unchecked((int)0xFFFF7ED4) : unchecked((int)0xFF56F0FF),
                        Pts = new System.Collections.Generic.List<Vec2>
                        {
                            c + new Vec2(Math.Cos(a) * rIn, Math.Sin(a) * rIn),
                            c + new Vec2(Math.Cos(a + 0.42) * rOut, Math.Sin(a + 0.42) * rOut),
                        },
                    };
                    lvl.Walls.Add(sat);
                }
                int g0 = unchecked((int)0xFF9E68FF);
                int g1 = unchecked((int)0xFF45FFC8);
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 15.5,
                        StartDeg = 115.0 + k * 14.0,
                        SweepDeg = 360.0 - 74.0,
                        HalfWidth = 2.3,
                        SpinMul = (k % 2 == 0) ? 1.0 : -0.92,
                        Argb = LerpArgb(g0, g1, k / (double)(totalRings - 1)),
                    });
                }
                return lvl;
            }

            case 19: // 20. «ЗАМЕДЛЕНИЕ» — гипнотический тяжёлый шар в медленных золотых кольцах с фиксацией в прорези
            {
                baseR0 = 112.0;
                totalRings = 28;
                spinSpeed = 0.56;
                var lvl = new LevelData
                {
                    Name = "ГИПНОЗ · МЕДЛЕННЫЙ",
                    Gravity = true,
                    GravityScale = 0.86,
                    ElasticBounce = true,
                    FreezeSpinInGap = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "тяжёлая гравитация и неспешное вращение с фиксацией прорези при пролёте  ·  ЛКМ — старт",
                };
                int gold0 = unchecked((int)0xFFFFCE52);
                int ruby1 = unchecked((int)0xFFFF4672);
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 12.0,
                        StartDeg = 75.0 + k * 18.0,
                        SweepDeg = 360.0 - 66.0,
                        HalfWidth = 2.65,
                        Argb = LerpArgb(gold0, ruby1, k / (double)(totalRings - 1)),
                    });
                }
                return lvl;
            }

            case 20: // 21. «ПЯТИУГОЛЬНИКИ 5→КРУГ» — 8 вращающихся открытых пентагонов + 18 фиолетово-золотых колец
            {
                const int polyCount = 8;
                const int arcCount = 18;
                baseR0 = 80.0;
                totalRings = polyCount + arcCount;
                spinSpeed = 1.04;
                var lvl = new LevelData
                {
                    Name = "ПЯТИУГОЛЬНИКИ 5→КРУГ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "внутри 8 открытых пятиугольников, снаружи — 18 фиолетово-золотых колец  ·  ЛКМ — старт",
                };
                int p0 = unchecked((int)0xFF56F0FF);
                int p1 = unchecked((int)0xFF9C7AFF);
                for (int k = 0; k < polyCount; k++)
                {
                    double r = baseR0 + k * 21.0;
                    int col = LerpArgb(p0, p1, k / (double)(polyCount - 1));
                    lvl.Walls.Add(MakeOpenPolygon(c, r, 5, -90.0 + (k * 144.0), 1.0, col));
                }
                for (int k = 0; k < arcCount; k++)
                {
                    double r = baseR0 + (polyCount - 1) * 21.0 + 18.0 + k * 11.0;
                    int col = LerpArgb(p1, unchecked((int)0xFFFFD45A), k / (double)(arcCount - 1));
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = r,
                        StartDeg = 110.0 + k * 14.0,
                        SweepDeg = 360.0 - 62.0,
                        HalfWidth = 2.3,
                        SpinMul = 0.94,
                        Argb = col,
                    });
                }
                return lvl;
            }

            default: // 22. «МАРАФОН 300» — гигантская радужная спираль из 300 колец с двумя шарами
            {
                baseR0 = 108.0;
                totalRings = 300;
                spinSpeed = 1.12;
                accelOnBreak = 1.003;
                dropBurst = 2;
                var lvl = new LevelData
                {
                    Name = "МАРАФОН 300",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                    NoBallCollisions = false,
                    SpinSpeed = spinSpeed,
                    RotorRules = true,
                    InstantDrop = true,
                    ContractRings = true,
                    RotorBaseR0 = baseR0,
                    Hint = "300 радужных колец, 2 шара на старте и постепенный разгон до самого финала  ·  ЛКМ — старт",
                };
                for (int k = 0; k < totalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = baseR0 + k * 7.8,
                        StartDeg = 220.0 + k * 6.5,
                        SweepDeg = 360.0 - 66.0,
                        HalfWidth = 2.15,
                        Argb = GameCanvas.SpectrumPalette24[(k / 3) % GameCanvas.SpectrumPalette24.Length],
                    });
                }
                return lvl;
            }
        }
    }

    /// <summary>Неполный круг в виде полилинии (сохранён для совместимости и тестов стенок).</summary>
    public static PolyData ArcPoly(double cx, double cy, double r, double gapCenterDeg, double gapDeg = 60, int n = 56)
    {
        var p = new PolyData { Pivot = new Vec2(cx, cy) };
        double a0 = (gapCenterDeg + gapDeg / 2) * Math.PI / 180;
        double sweep = (360 - gapDeg) * Math.PI / 180;
        for (int i = 0; i <= n; i++)
        {
            double a = a0 + sweep * i / n;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return p;
    }

    /// <summary>Переключить уровень «Ротора» (0..15): круги на паузе, ожидание постановки шара игроком.</summary>
    public void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, LevelCount - 1);
        var lvl = BuildLevel(index, out var c, out double spinSpeed, out var startHint);
        center = c;
        canvas.SpinSpeed = spinSpeed;

        polyRings.Clear();
        for (int wi = 0; wi < lvl.Walls.Count; wi++)
        {
            var w = lvl.Walls[wi];
            if (!w.Closed && w.Pts.Count >= 3)
            {
                double r = (w.Pts[0] - c).Len;
                double chord = (w.Pts[1] - w.Pts[0]).Len;
                int sides = r > 1e-3 ? Math.Max(3, (int)Math.Round(Math.PI / Math.Asin(Math.Clamp(chord / (2.0 * r), 0.1, 0.999)))) : w.Pts.Count;
                polyRings.Add(new PolyShell { Src = w, Radius = r, Sides = sides });
            }
        }
        polyRings.Sort(static (a, b) => a.Radius.CompareTo(b.Radius));

        if (curTotalRings > 1)
        {
            double hubR = curBaseR0 - 8;
            if (polyRings.Count > 0)
            {
                double ap0 = polyRings[0].Radius * Math.Cos(Math.PI / Math.Max(3, polyRings[0].Sides));
                hubR = Math.Clamp(ap0 - 12.0, 20.0, curBaseR0 - 8);
            }
            canvas.HubCenter = center;
            canvas.HubRadius = hubR;
            canvas.HubText = () =>
            {
                int left = polyRings.Count + canvas.Sim.Arcs.Count;
                return left > 0 ? left.ToString() : "";
            };
        }
        else
        {
            canvas.HubCenter = null;
            canvas.HubText = null;
        }

        canvas.LoadLevel(lvl);

        // На старте шара нет, а все круги стоят на паузе до первого клика игрока
        waitingForBall = true;
        canvas.Paused = true;
        canvas.SuppressPauseBanner = true;
        canvas.PromptTitle = "КЛИКНИТЕ ЛКМ, ЧТОБЫ ПОСТАВИТЬ ШАР";
        canvas.PromptSub = curDropBurst > 1
            ? $"появятся сразу {curDropBurst} шара и запустят вращение слоёв"
            : "шар появится с нулевой скоростью и запустит вращение кругов";
        btnPause.Icon = IconPainter.Play;
        btnPause.Invalidate();

        lblName.Text = $"  {index + 1}/{LevelCount} · {lvl.Name}";
        hintLbl.Text = startHint;
        canvas.Invalidate();
    }

    /// <summary>Рассыпание пройденных многоугольников и кругов, модификаторы уровня и плавное сжатие спирали к центру.</summary>
    private void OnSimulated(double dt)
    {
        var arcs = canvas.Sim.Arcs;
        var balls = canvas.Sim.Balls;
        if (arcs.Count == 0 && polyRings.Count == 0) return;

        int broken = 0;
        while (polyRings.Count > 0 && balls.Count > 0)
        {
            var sh0 = polyRings[0];
            bool lastShell = polyRings.Count == 1 && arcs.Count == 0;
            var liveWall = canvas.Sim.FindWall(sh0.Src);
            double innerAp = sh0.Radius * Math.Cos(Math.PI / Math.Max(3, sh0.Sides));
            bool escaped = false;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (b.MinCenterDist >= innerAp - 1.0) continue; // шар должен побывать внутри многоугольника
                double dist = Vec2.Dist(b.Pos, center);
                double exitDist = lastShell ? (sh0.Radius + b.R + 2.5) : (sh0.Radius + 1.5);
                if (dist > exitDist)
                {
                    escaped = true;
                    break;
                }
                if (liveWall != null && liveWall.Pts.Count >= 3)
                {
                    var pA = liveWall.Pts[liveWall.Pts.Count - 1];
                    var pB = liveWall.Pts[0];
                    var mid = (pA + pB) * 0.5;
                    var outVec = mid - center;
                    double outLen = outVec.Len;
                    var outN = outLen > 1e-6 ? outVec * (1.0 / outLen) : new Vec2(0, -1);
                    var chord = pB - pA;
                    double chordLen = chord.Len;
                    var chordDir = chordLen > 1e-6 ? chord * (1.0 / chordLen) : new Vec2(1, 0);
                    double sideDist = Vec2.Dot(b.Pos - mid, outN);
                    double along = Math.Abs(Vec2.Dot(b.Pos - mid, chordDir));
                    double gapExit = lastShell ? (b.R + 2.0) : 1.5;
                    if (sideDist > gapExit && along <= chordLen * 0.5 + b.R)
                    {
                        escaped = true;
                        break;
                    }
                }
            }
            if (!escaped) break;

            canvas.DissolveWall(sh0.Src);
            canvas.Design.Walls.Remove(sh0.Src);
            polyRings.RemoveAt(0);
            broken++;
        }

        while (arcs.Count > 0 && balls.Count > 0)
        {
            var arc0 = arcs[0];
            bool lastRing = arcs.Count == 1;
            bool escaped = false;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (b.MinCenterDist >= arc0.Radius - 2.0) continue; // шар должен побывать внутри кольца
                double dist = Vec2.Dist(b.Pos, center);
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
            // «РЕВЕРС»: каждый пройденный круг меняет направление вращения всех остальных колец
            if (curReverseOnBreak)
            {
                canvas.SpinSpeed = -canvas.SpinSpeed;
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }

            // «РАЗГОН»: плавное ускорение вращения с каждым разрушенным кольцом
            if (curAccelOnBreak > 1.0)
            {
                double sign = Math.Sign(canvas.SpinSpeed);
                double mag = Math.Abs(canvas.SpinSpeed) * Math.Pow(curAccelOnBreak, broken);
                canvas.SpinSpeed = (sign == 0 ? 1.0 : sign) * Math.Min(3.2, mag);
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }
        }

        // В многокольцевых уровнях оставшиеся слои плавно смыкаются к радиусу curBaseR0
        double curInnerR = polyRings.Count > 0 ? polyRings[0].Radius : (arcs.Count > 0 ? arcs[0].Radius : 0);
        if (curContractRings && curTotalRings > 1 && (polyRings.Count > 0 || arcs.Count > 0) && curInnerR > curBaseR0)
        {
            double minAllowedR0 = curBaseR0;
            if (polyRings.Count > 0)
            {
                var sh0 = polyRings[0];
                double cosAp = Math.Cos(Math.PI / Math.Max(3, sh0.Sides));
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    double dist = Vec2.Dist(b.Pos, center);
                    if (dist < sh0.Radius * 0.92)
                        minAllowedR0 = Math.Max(minAllowedR0, (dist + b.R + 2.0) / Math.Max(0.4, cosAp));
                }
            }
            else if (arcs.Count > 0)
            {
                var arc0 = arcs[0];
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= arc0.Radius + 1.2 && !arc0.IsInSolid(Math.Atan2(dy, dx)))
                        minAllowedR0 = Math.Max(minAllowedR0, dist + 2.5);
                }
            }

            double diff = curInnerR - minAllowedR0;
            if (diff > 0.01)
            {
                double speed = Math.Max(240.0, diff * 8.0);
                double dr = Math.Min(diff, speed * dt);

                if (polyRings.Count > 0)
                {
                    for (int k = 0; k < polyRings.Count; k++)
                    {
                        var sh = polyRings[k];
                        double oldR = sh.Radius;
                        double newR = Math.Max(8.0, oldR - dr);
                        sh.Radius = newR;
                        var w = canvas.Sim.FindWall(sh.Src);
                        if (w != null && oldR > 1e-6)
                        {
                            double scale = newR / oldR;
                            for (int pi = 0; pi < w.Pts.Count; pi++)
                                w.Pts[pi] = center + (w.Pts[pi] - center) * scale;
                            w.UpdateBounds();
                        }
                    }
                    canvas.Sim.RebuildGrid();
                }

                double oldArcR0 = arcs.Count > 0 ? arcs[0].Radius : 0;
                for (int k = 0; k < arcs.Count; k++)
                    arcs[k].Radius -= dr;

                if (polyRings.Count == 0 && arcs.Count > 0)
                {
                    var arc0 = arcs[0];
                    double wallInwardV = dt > 1e-5 ? Math.Min(90.0, (dr / dt) * 0.30) : 0.0;
                    for (int bi = 0; bi < balls.Count; bi++)
                    {
                        var b = balls[bi];
                        double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        double lim = arc0.Radius - b.R - arc0.HalfWidth - 0.02;
                        if (dist > lim && dist <= oldArcR0 + 1.2 && dist > 1e-6 && arc0.IsInSolid(Math.Atan2(dy, dx)))
                        {
                            double rx = dx / dist, ry = dy / dist;
                            b.Pos = new Vec2(center.X + rx * lim, center.Y + ry * lim);
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
        }

        // Ограничиваем только экстремальный пинбольный разгон выше естественной скорости камеры,
        // не затрагивая рабочие отскоки шара
        if (curTotalRings > 1 && (arcs.Count > 0 || polyRings.Count > 0))
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

        if (broken > 0) UpdateProgress();
    }

    private void UpdateProgress()
    {
        int left = polyRings.Count + canvas.Sim.Arcs.Count;
        if (curTotalRings == 1)
        {
            hintLbl.Text = left == 0
                ? "  круг рассыпался — шар улетает в пустоту  ·  R — сыграть ещё  ·  ЛКМ — добавить шар"
                : "  вылетите через разрыв вращающегося круга  ·  ЛКМ — добавить шар (скорость 0)  ·  ПКМ — убрать  ·  R — заново";
            return;
        }

        if (left == 0)
        {
            hintLbl.Text = $"  все слои ({curTotalRings}) рассыпались — шар улетает в пустоту  ·  R — сыграть ещё";
            return;
        }

        string extra = curReverseOnBreak
            ? (canvas.SpinSpeed >= 0 ? "  ·  вращение: ↻ по часовой" : "  ·  вращение: ↺ против часовой")
            : curAccelOnBreak > 1.0
                ? $"  ·  скорость слоёв: {Math.Abs(canvas.SpinSpeed):0.00} рад/с"
                : "";
        hintLbl.Text = $"  слоёв осталось: {left} из {curTotalRings}{extra}  ·  ЛКМ — шар (скорость 0)  ·  R — заново";
    }

    private System.Collections.Generic.List<SandboxEntry> BuildSandboxEntries()
    {
        return new System.Collections.Generic.List<SandboxEntry>
        {
            new SandboxSlider { Label = "Скорость вращения", Min = -3.5, Max = 3.5, Step = 0.05, Format = v => $"{v:0.00}", Get = () => canvas.SpinSpeed, Set = v => { canvas.SpinSpeed = v; canvas.Sim.SpinSpeed = v; canvas.Invalidate(); } },
            new SandboxSlider { Label = "Гравитация", Min = 0.0, Max = 2.5, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => canvas.Sim.GravityScale, Set = v => { canvas.Sim.GravityScale = v; canvas.Design.GravityScale = v; canvas.Invalidate(); } },
            new SandboxSlider { Label = "Упругость стен", Min = 0.2, Max = 1.35, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => canvas.Sim.WallRestitution, Set = v => { canvas.Sim.WallRestitution = v; canvas.Design.WallRestitution = v; } },
            new SandboxSlider { Label = "Упругость шаров", Min = 0.2, Max = 1.35, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => canvas.Sim.BallRestitution, Set = v => { canvas.Sim.BallRestitution = v; canvas.Design.BallRestitution = v; } },
            new SandboxSlider { Label = "Разгон за пробой", Min = 1.00, Max = 1.15, Step = 0.005, Format = v => $"×{v:0.000}", Get = () => curAccelOnBreak, Set = v => curAccelOnBreak = v },
            new SandboxSlider { Label = "Радиус шара", Min = 2, Max = 26, Step = 1, Get = () => canvas.NewBallR, Set = SetBallRadius },
            new SandboxSlider { Label = "Скорость симуляции", Min = 0.1, Max = 4.0, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => GameCanvas.SimSpeed, Set = v => { GameCanvas.SimSpeed = v; canvas.Invalidate(); } },
            new SandboxToggle { Label = "Пауза вращения в прорези", Get = () => canvas.Sim.FreezeSpinInGap, Set = v => { canvas.Sim.FreezeSpinInGap = v; canvas.Design.FreezeSpinInGap = v; } },
            new SandboxToggle { Label = "Реверс при каждом пробое", Get = () => curReverseOnBreak, Set = v => { curReverseOnBreak = v; UpdateProgress(); } },
            new SandboxToggle { Label = "Сжатие слоёв к центру", Get = () => curContractRings, Set = v => { curContractRings = v; } },
            new SandboxToggle { Label = "100% упругий отскок", Get = () => canvas.Sim.ElasticBounce, Set = v => { canvas.Sim.ElasticBounce = v; canvas.Design.ElasticBounce = v; } },
            new SandboxToggle { Label = "Столкновения шаров", Get = () => canvas.Sim.BallCollisions, Set = v => { canvas.Sim.BallCollisions = v; canvas.Design.NoBallCollisions = !v; } },
            new SandboxAction
            {
                Label = "+ Добавить шар", Role = AccentRole.Green, RefreshAllAfter = false,
                Run = () =>
                {
                    var p = ClampToInnerChamber(new Vec2(center.X + (canvas.Sim.Balls.Count % 3 - 1) * 14, center.Y - 18));
                    int argb = canvas.NewBallSpectrum ? GameCanvas.SpectrumPalette24[canvas.Sim.Balls.Count * 5 % 24] : canvas.NewBallArgb;
                    canvas.Sim.AddBall(p, new Vec2(35, -20), canvas.NewBallR, argb: argb);
                    OnBallPlaced();
                }
            },
            new SandboxAction { Label = "Сбросить уровень", Role = AccentRole.Orange, Run = () => { GameCanvas.SimSpeed = 1.0; ShowLevel(index); } },
        };
    }

    /// <summary>Гравитация режима «Ротор».</summary>
    public bool GravityOn
    {
        get => canvas.Sim.GravityOn;
        set { canvas.Sim.GravityOn = value; canvas.Design.Gravity = value; }
    }

    public bool TrailsOn
    {
        get => canvas.Trails;
        set => canvas.Trails = value;
    }

    private void TogglePause()
    {
        canvas.Paused = !canvas.Paused;
        if (!waitingForBall)
            canvas.SuppressPauseBanner = false;
        btnPause.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
        btnPause.Invalidate();
        canvas.Invalidate();
    }

    public bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.R:
                ShowLevel(index);
                return true;
            case Keys.G:
                GravityOn = !GravityOn;
                return true;
            case Keys.Space:
                TogglePause();
                return true;
            case Keys.Control | Keys.Z:
                canvas.UndoBallPlacement();
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                SetBallRadius(canvas.NewBallR - 2);
                return true;
            case Keys.Oemplus:
            case Keys.Add:
                SetBallRadius(canvas.NewBallR + 2);
                return true;
        }
        return false;
    }
}

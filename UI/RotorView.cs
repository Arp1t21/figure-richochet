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
    public const int LevelCount = 10;
    public const int SpiralCount = 200;
    public const double SpiralR0 = 98.0;
    public const double SpiralStep = 8.5;
    public const double SpiralGapDeg = 60.0;
    public const double SpiralTwistDeg = 3.0;

    public static readonly string[] PresetBadges =
    {
        "1 КРУГ", "200 КОЛЕЦ", "РЕВЕРС", "ВСТРЕЧНЫЕ", "РАЗГОН",
        "ВИХРЬ", "50 · ПРОСТОР", "ШЛЮЗ", "МАЯТНИК", "ОРБИТА",
    };

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

    private int index;
    private Vec2 center = new(640, 360);
    private double curBaseR0 = SpiralR0;
    private int curTotalRings = 1;
    private bool curReverseOnBreak;
    private double curAccelOnBreak = 1.0;
    private bool waitingForBall;

    private readonly LevelSelectButton lblName = new()
    {
        Size = new Size(276, 38), Role = AccentRole.Orange,
    };
    private readonly NeoButton btnEdit = new()
    {
        Text = "В РЕДАКТОР", Icon = IconPainter.Pencil, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(118, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Play, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton btnRestart = new() { Icon = IconPainter.Restart, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(46, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF) };
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 26, Value = 10 };
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

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(38, 38), IconPx = 20 };
        var btnNext = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(38, 38), IconPx = 20 };

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий уровень");
        tips.SetToolTip(btnNext, "Следующий уровень");
        tips.SetToolTip(lblName, "Открыть сетку уровней «Ротора» с мини-превью");
        tips.SetToolTip(btnEdit, "Перенести копию текущего уровня «Ротора» в «Редактор»");
        tips.SetToolTip(chipFx, "Шейдерные эффекты");
        tips.SetToolTip(colorBtn, "Цвет шара (СПЕКТР — каждый шар нового тона)");
        tips.SetToolTip(sizeBtn, "Размер шара — ползунок (или колесо мыши, +/−)");
        tips.SetToolTip(btnPause, "Пауза / продолжить (пробел)");
        tips.SetToolTip(btnRestart, "Заново (R)");

        lblName.ItemsProvider = () =>
        {
            var saved = (curBaseR0, curTotalRings, curReverseOnBreak, curAccelOnBreak);
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
            (curBaseR0, curTotalRings, curReverseOnBreak, curAccelOnBreak) = saved;
            return (index, items);
        };
        lblName.LevelSelected += ShowLevel;

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + LevelCount) % LevelCount);
        btnNext.Click += (s, e) => ShowLevel((index + 1) % LevelCount);
        btnEdit.Click += (s, e) => MainForm.RequestOpenInEditor(ExportCurrentLevel());
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

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName).AddLeft(btnEdit);
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
        if (canvas.Sim.Arcs.Count > 0)
        {
            var arc0 = canvas.Sim.Arcs[0];
            double maxDist = Math.Max(4.0, arc0.Radius - canvas.NewBallR - arc0.HalfWidth - 3.5);
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
    public LevelData ExportCurrentLevel() => CreatePresetLevel(index, out _, out _, out _, out _, out _, out _);

    /// <summary>Все 10 эталонных уровней «Ротора» со всеми правилами для галереи шаблонов редактора.</summary>
    public static System.Collections.Generic.List<LevelData> BuildPresetLevels()
    {
        var list = new System.Collections.Generic.List<LevelData>(LevelCount);
        for (int i = 0; i < LevelCount; i++)
            list.Add(CreatePresetLevel(i, out _, out _, out _, out _, out _, out _));
        return list;
    }

    /// <summary>Свежая геометрия уровня без предсозданных шаров (шар ставит сам игрок).</summary>
    private LevelData BuildLevel(int i, out Vec2 c, out double spinSpeed, out string startHint)
    {
        var lvl = CreatePresetLevel(i, out c, out spinSpeed, out curBaseR0, out curTotalRings, out curReverseOnBreak, out curAccelOnBreak);
        startHint = "  " + lvl.Hint;
        return lvl;
    }

    private static LevelData CreatePresetLevel(
        int i, out Vec2 c, out double spinSpeed,
        out double baseR0, out int totalRings, out bool reverseOnBreak, out double accelOnBreak)
    {
        c = new Vec2(640, 360);
        reverseOnBreak = false;
        accelOnBreak = 1.0;

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

            default: // 10. «ОРБИТА» — просторная камера с тремя вращающимися спутниками-отражателями
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

    /// <summary>Переключить уровень «Ротора» (0..5): круги на паузе, ожидание постановки шара игроком.</summary>
    public void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, LevelCount - 1);
        var lvl = BuildLevel(index, out var c, out double spinSpeed, out var startHint);
        center = c;
        canvas.SpinSpeed = spinSpeed;

        if (curTotalRings > 1)
        {
            canvas.HubCenter = center;
            canvas.HubRadius = curBaseR0 - 8;
            canvas.HubText = () => canvas.Sim.Arcs.Count > 0 ? canvas.Sim.Arcs.Count.ToString() : "";
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
        canvas.PromptSub = "шар появится с нулевой скоростью и запустит вращение кругов";
        btnPause.Icon = IconPainter.Play;
        btnPause.Invalidate();

        lblName.Text = $"  {index + 1}/{LevelCount} · {lvl.Name}";
        hintLbl.Text = startHint;
        canvas.Invalidate();
    }

    /// <summary>Рассыпание пройденных кругов, модификаторы уровня и плавное сжатие спирали к центру.</summary>
    private void OnSimulated(double dt)
    {
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
                if ((broken & 1) == 1)
                    canvas.SpinSpeed = -canvas.SpinSpeed;
                else
                    canvas.SpinSpeed = -canvas.SpinSpeed; // даже при редком двойном пробое даём явный реверс!
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }

            // «РАЗГОН»: плавное ускорение вращения с каждым разрушенным кольцом
            if (curAccelOnBreak > 1.0)
            {
                double sign = Math.Sign(canvas.SpinSpeed);
                double mag = Math.Abs(canvas.SpinSpeed) * Math.Pow(curAccelOnBreak, broken);
                canvas.SpinSpeed = (sign == 0 ? 1.0 : sign) * Math.Min(2.45, mag);
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }
        }

        // В многокольцевых уровнях оставшиеся круги плавно смыкаются к радиусу curBaseR0,
        // сохраняя шаг между кольцами, чтобы вся серия по очереди входила в кадр.
        if (curTotalRings > 1 && arcs.Count > 0 && arcs[0].Radius > curBaseR0)
        {
            var arc0 = arcs[0];
            double minAllowedR0 = curBaseR0;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
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

                // Сжимающееся внутреннее кольцо упруго передаёт импульс шару (без гашения 1.8·vr),
                // компенсируя уменьшение высоты падения при сужении камеры.
                double wallInwardV = dt > 1e-5 ? Math.Min(90.0, (dr / dt) * 0.30) : 0.0;
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double lim = arc0.Radius - b.R - arc0.HalfWidth - 0.02;
                    if (dist > lim && dist <= oldR0 + 1.2 && dist > 1e-6 && arc0.IsInSolid(Math.Atan2(dy, dx)))
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

        // Ограничиваем только экстремальный пинбольный разгон выше естественной скорости камеры,
        // не затрагивая рабочие отскоки шара
        if (curTotalRings > 1 && arcs.Count > 0)
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
        int left = canvas.Sim.Arcs.Count;
        if (curTotalRings == 1)
        {
            hintLbl.Text = left == 0
                ? "  круг рассыпался — шар улетает в пустоту  ·  R — сыграть ещё  ·  ЛКМ — добавить шар"
                : "  вылетите через разрыв вращающегося круга  ·  ЛКМ — добавить шар (скорость 0)  ·  ПКМ — убрать  ·  R — заново";
            return;
        }

        if (left == 0)
        {
            hintLbl.Text = $"  все круги ({curTotalRings}) рассыпались — шар улетает в пустоту  ·  R — сыграть ещё";
            return;
        }

        string extra = curReverseOnBreak
            ? (canvas.SpinSpeed >= 0 ? "  ·  вращение: ↻ по часовой" : "  ·  вращение: ↺ против часовой")
            : curAccelOnBreak > 1.0
                ? $"  ·  скорость кругов: {Math.Abs(canvas.SpinSpeed):0.00} рад/с"
                : "";
        hintLbl.Text = $"  кругов осталось: {left} из {curTotalRings}{extra}  ·  ЛКМ — шар (скорость 0)  ·  R — заново";
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

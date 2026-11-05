using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Волновой фронт в математических бильярдах»: из точки-источника вылетает
/// веер лучей-шаров или непрерывный адаптивный волновой фронт с дифракцией на вершинах.
/// 32 фигуры: от интегрируемого круга и эллипса до гриба Бунимовича, бильярда Синая,
/// двухщелевой оптики Юнга и лабиринта тени.
/// </summary>
public class WaveView : UserControl, IModeView
{
    private const double WaveSpeed = 320;
    private double waveR = 3; // радиус лучей-шаров (меняется на лету)

    private readonly GameCanvas canvas = new()
    {
        Editor = false, WaveMode = true, UseWaveFront = true, Dock = DockStyle.Fill, ShowGrid = false, NewBallR = 3,
    };
    private readonly List<(LevelData Level, Vec2 Source)> figures = new();
    private readonly LevelSelectButton lblName = new()
    {
        Size = new Size(210, 38), Role = AccentRole.Green,
    };
    private readonly NeoButton btnEdit = new()
    {
        Text = "В РЕДАКТОР", Icon = IconPainter.Pencil, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(116, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton chipWave = new()
    {
        Text = "ВОЛНА", IsToggle = true, IsOn = true, Role = AccentRole.Green, Size = new Size(74, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly NeoButton chipTrace = new()
    {
        Text = "СЛЕД", Icon = IconPainter.TrailLine, IconPx = 15, IsToggle = true, Role = AccentRole.Accent,
        Size = new Size(78, 38), Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
    };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(46, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = BallColors.Spectrum }; // радуга
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 16, Value = 3 };
    private readonly SliderButton raysBtn = new()
    {
        Size = new Size(100, 38), Min = 10, Max = 100000, Log = true, Value = 120, Prefix = "ЛУЧИ ",
        Format = static v => v >= 1000 ? (Math.Round(v / 100.0) / 10.0).ToString("0.#") + "K" : Math.Round(v).ToString(),
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Pause, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton btnEmit = new()
    {
        Icon = IconPainter.Play, Role = AccentRole.Green, Size = new Size(50, 38), IconPx = 20,
    };

    private int figIndex;
    private int rays = 120;
    private Vec2 source = new(640, 360);
    private int ballArgb;      // 0 = СПЕКТР
    private bool spectrum = true;
    private readonly ToolTip tips = new();

    public GameCanvas Canvas => canvas;
    public NeoButton WaveToggleButton => chipWave;
    public SliderButton RaysSliderButton => raysBtn;
    public NeoButton TraceToggleButton => chipTrace;

    /// <summary>Экспортировать текущую фигуру и настройки волны для переноса в «Редактор».</summary>
    public LevelData ExportCurrentLevel()
    {
        var wl = figures[figIndex].Level.Clone();
        wl.WaveEmitter = true;
        wl.WaveSourcePos = source;
        wl.WaveContinuous = chipWave.IsOn;
        wl.WaveRays = rays;
        wl.WaveSpeed = WaveSpeed;
        wl.WaveRayRadius = waveR;
        wl.WaveArgb = spectrum ? 0 : ballArgb;
        wl.Gravity = GravityOn;
        wl.NoBallCollisions = true;
        wl.Hint = "Математический бильярд: клик по полю переносит источник волны";
        return wl;
    }

    public WaveView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        figures.AddRange(BuildFigures());

        var top = new Toolbar { Dock = DockStyle.Top, Height = 56 };
        var hints = new Bar { Dock = DockStyle.Bottom, Height = 34, LineAtTop = true, BackColor = Theme.Panel };
        var hintLbl = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  клик — источник  ·  ВОЛНА — непрерывный фронт + дифракция  ·  СЛЕД (T) — вечная полоса покрытия  ·  G — гравитация  ·  R — пуск",
            BackColor = Theme.Panel,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        hints.Controls.Add(hintLbl);

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(38, 38), IconPx = 20 };
        var btnNext = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(38, 38), IconPx = 20 };

        raysBtn.Enabled = !chipWave.IsOn;

        tips.SetToolTip(btnPrev, "Предыдущая фигура");
        tips.SetToolTip(btnNext, "Следующая фигура");
        tips.SetToolTip(lblName, "Открыть сетку из 32 фигур бильярдов с мини-превью");
        tips.SetToolTip(btnEdit, "Перенести текущую фигуру бильярда в «Редактор»");
        tips.SetToolTip(chipWave, "Реалистичная волна: добавление лучей на растяжениях фронта и дифракционные волны в вершинах (выкл — классические частицы-лучи)");
        tips.SetToolTip(chipTrace, "Вечный след (T): волны или частицы оставляют неисчезающую полосу, наглядно показывая зоны тени");
        tips.SetToolTip(chipFx, "Шейдерные эффекты: свечение, аберрация, виньетка");
        tips.SetToolTip(colorBtn, "Цвет лучей (СПЕКТР — радуга)");
        tips.SetToolTip(sizeBtn, "Размер лучей — ползунок (или колесо мыши, +/−)");
        tips.SetToolTip(raysBtn, "Количество лучей-частиц 10…100K (работает в режиме частиц, когда «ВОЛНА» выключена)");
        tips.SetToolTip(btnPause, "Пауза (пробел)");
        tips.SetToolTip(btnEmit, "Испустить фронт заново (R)");

        lblName.ItemsProvider = () =>
        {
            var items = new List<LevelCardItem>(figures.Count);
            for (int i = 0; i < figures.Count; i++)
            {
                var (lvl, src) = figures[i];
                items.Add(new LevelCardItem
                {
                    Title = $"{i + 1}. {lvl.Name}",
                    Badge = "БИЛЬЯРД",
                    Preview = lvl,
                    Source = src,
                    Role = AccentRole.Green,
                });
            }
            return (figIndex, items);
        };
        lblName.LevelSelected += idx =>
        {
            figIndex = Math.Clamp(idx, 0, figures.Count - 1);
            source = figures[figIndex].Source;
            Emit();
        };

        btnPrev.Click += (s, e) => { figIndex = (figIndex - 1 + figures.Count) % figures.Count; source = figures[figIndex].Source; Emit(); };
        btnNext.Click += (s, e) => { figIndex = (figIndex + 1) % figures.Count; source = figures[figIndex].Source; Emit(); };
        btnEdit.Click += (s, e) => MainForm.RequestOpenInEditor(ExportCurrentLevel());
        btnEmit.Click += (s, e) => Emit();
        btnPause.Click += (s, e) => TogglePause();
        chipWave.Click += (s, e) =>
        {
            canvas.UseWaveFront = chipWave.IsOn;
            raysBtn.Enabled = !chipWave.IsOn;
            Emit();
        };
        chipTrace.Click += (s, e) =>
        {
            canvas.PersistentTrace = chipTrace.IsOn;
            if (!chipTrace.IsOn) canvas.ClearPersistentTrace();
            canvas.Invalidate();
        };
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;
        colorBtn.Changed += () =>
        {
            spectrum = colorBtn.Selected == BallColors.Spectrum;
            ballArgb = colorBtn.Selected;
            Emit(); // перекрасить фронт сразу
        };
        sizeBtn.Changed += v => SetRadius(v);
        canvas.BallRadiusChanged += () => SetRadius(canvas.NewBallR); // колесо над полем

        raysBtn.Changed += v =>
        {
            rays = SnapRays(v);
            raysBtn.Value = rays;
            Emit();
        };

        canvas.SourcePicked += p => { source = p; Emit(); }; // клик по полю — перенести источник

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName).AddLeft(btnEdit);
        top.AddCenter(chipWave).AddCenter(raysBtn).AddCenter(chipTrace);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn).AddRight(btnPause).AddRight(btnEmit);

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

        Emit();
    }

    // ------------------------------------------------- фигуры

    /// <summary>Все фигуры с их источниками по умолчанию.</summary>
    public static List<(LevelData Level, Vec2 Source)> BuildFigures()
    {
        var figures = new List<(LevelData, Vec2)>();
        void Add(string name, Vec2 src, Action<LevelData> fill)
        {
            var l = new LevelData { Name = name };
            fill(l);
            figures.Add((l, src));
        }
        var c = new Vec2(640, 360);

        Add("КРУГ", c, l => l.Circles.Add(new CircleData { Center = c, Radius = 330 }));
        Add("ЭЛЛИПС", c, l => l.Walls.Add(Ellipse(640, 360, 430, 270)));
        Add("СТАДИОН", c, l => l.Walls.Add(Stadium()));
        Add("КВАДРАТ", c, l => l.Walls.Add(Rect(310, 80, 660, 560)));
        Add("ПРЯМОУГОЛЬНИК", c, l => l.Walls.Add(Rect(160, 160, 960, 400)));
        Add("ТРЕУГОЛЬНИК", new Vec2(640, 430), l => l.Walls.Add(Regular(640, 430, 320, 3)));
        Add("5-УГОЛЬНИК", c, l => l.Walls.Add(Regular(640, 370, 310, 5)));
        Add("6-УГОЛЬНИК", c, l => l.Walls.Add(Regular(640, 360, 310, 6)));
        Add("8-УГОЛЬНИК", c, l => l.Walls.Add(Regular(640, 360, 310, 8)));
        Add("12-УГОЛЬНИК", c, l => l.Walls.Add(Regular(640, 360, 320, 12)));
        Add("РОМБ", c, l => l.Walls.Add(Closed(new Vec2(640, 70), new Vec2(1090, 360), new Vec2(640, 650), new Vec2(190, 360))));
        Add("ТРАПЕЦИЯ", c, l => l.Walls.Add(Closed(new Vec2(330, 120), new Vec2(950, 120), new Vec2(1150, 600), new Vec2(130, 600))));
        Add("ПАРАЛЛЕЛОГРАММ", c, l => l.Walls.Add(Closed(new Vec2(260, 150), new Vec2(860, 150), new Vec2(1020, 570), new Vec2(420, 570))));
        Add("ЗВЕЗДА", c, l => l.Walls.Add(Star(640, 360, 330, 135, 5)));
        Add("ПОЛУКРУГ", new Vec2(640, 470), l => l.Walls.Add(DShape(640, 560, 330)));
        Add("ПОЛУМЕСЯЦ", new Vec2(420, 360), l => l.Walls.Add(Crescent()));
        Add("ГАНТЕЛЬ", c, l => l.Walls.Add(Dumbbell()));
        Add("КРЕСТ", c, l => l.Walls.Add(Cross()));
        Add("СЕРДЦЕ", c, l => l.Walls.Add(Heart()));
        Add("ЛЕСТНИЦА", c, l => l.Walls.Add(Staircase()));
        Add("СУПЕРЭЛЛИПС", c, l => l.Walls.Add(Superellipse(640, 360, 450, 300, 2.5)));
        Add("РАМКА", new Vec2(450, 360), l =>
        {
            l.Walls.Add(Rect(310, 80, 660, 560));
            l.Walls.Add(Rect(540, 260, 200, 200));
        });
        Add("КРУГ+ПАЛКА", c, l =>
        {
            l.Circles.Add(new CircleData { Center = c, Radius = 330 });
            l.Walls.Add(new PolyData { Pts = { new Vec2(340, 300), new Vec2(940, 300) } });
        });
        Add("ПЕРЕГОРОДКА", c, l =>
        {
            l.Walls.Add(Ellipse(640, 360, 430, 270));
            l.Walls.Add(new PolyData { Pts = { new Vec2(640, 150), new Vec2(640, 330) } });
        });
        Add("ОПТИКА ЮНГА", new Vec2(330, 360), l =>
        {
            l.Walls.Add(Rect(160, 110, 960, 500));
            l.Walls.Add(new PolyData { Pts = { new Vec2(570, 110), new Vec2(570, 258) } });
            l.Walls.Add(new PolyData { Pts = { new Vec2(570, 298), new Vec2(570, 422) } });
            l.Walls.Add(new PolyData { Pts = { new Vec2(570, 462), new Vec2(570, 610) } });
        });
        Add("ГРИБ БУНИМОВИЧА", new Vec2(640, 240), l => l.Walls.Add(Mushroom()));
        Add("БИЛЬЯРД СИНАЯ", new Vec2(415, 360), l =>
        {
            l.Walls.Add(Rect(290, 70, 700, 580));
            l.Walls.Add(Ellipse(640, 360, 118, 118, 72));
        });
        Add("КАРДИОИДА", c, l => l.Walls.Add(Cardioid()));
        Add("ЗЕРКАЛА ФОКУСА", new Vec2(455, 360), l =>
        {
            l.Circles.Add(new CircleData { Center = c, Radius = 330 });
            l.Walls.Add(ArcBarrier(640, 360, 195, -52, 52, 28));
            l.Walls.Add(ArcBarrier(640, 360, 130, 128, 232, 24));
        });
        Add("ТРИЛИСТНИК", c, l => l.Walls.Add(PolarFlower(640, 365, 242, 68, 3, -Math.PI / 2, 144)));
        Add("ЛАБИРИНТ ТЕНИ", new Vec2(265, 360), l =>
        {
            l.Walls.Add(Rect(160, 110, 960, 500));
            l.Walls.Add(new PolyData { Pts = { new Vec2(430, 110), new Vec2(430, 455) } });
            l.Walls.Add(new PolyData { Pts = { new Vec2(670, 265), new Vec2(670, 610) } });
            l.Walls.Add(new PolyData { Pts = { new Vec2(910, 110), new Vec2(910, 455) } });
        });
        Add("ШЕСТЕРНЯ", c, l => l.Walls.Add(PolarFlower(640, 360, 262, 44, 8, 0, 160)));
        return figures;
    }

    private static PolyData Rect(double x, double y, double w, double h)
    {
        var p = new PolyData { Closed = true };
        p.Pts.Add(new Vec2(x, y));
        p.Pts.Add(new Vec2(x + w, y));
        p.Pts.Add(new Vec2(x + w, y + h));
        p.Pts.Add(new Vec2(x, y + h));
        return p;
    }

    private static PolyData Closed(params Vec2[] pts)
    {
        var p = new PolyData { Closed = true };
        p.Pts.AddRange(pts);
        return p;
    }

    private static PolyData Ellipse(double cx, double cy, double rx, double ry, int n = 96)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < n; i++)
        {
            double a = i * 2 * Math.PI / n;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry));
        }
        return p;
    }

    private static PolyData Stadium()
    {
        double cx1 = 490, cx2 = 790, cy = 360, r = 260;
        var p = new PolyData { Closed = true };
        p.Pts.Add(new Vec2(cx1, cy - r));
        p.Pts.Add(new Vec2(cx2, cy - r));
        for (int a = -84; a <= 84; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(cx2 + r * Math.Cos(rad), cy + r * Math.Sin(rad)));
        }
        p.Pts.Add(new Vec2(cx1, cy + r));
        for (int a = 96; a <= 264; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(cx1 + r * Math.Cos(rad), cy + r * Math.Sin(rad)));
        }
        return p;
    }

    private static PolyData Regular(double cx, double cy, double r, int n, double rot = -Math.PI / 2)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < n; i++)
        {
            double a = rot + i * 2 * Math.PI / n;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return p;
    }

    private static PolyData Star(double cx, double cy, double R, double r, int points)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < points * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / points;
            double rr = i % 2 == 0 ? R : r;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr));
        }
        return p;
    }

    private static PolyData DShape(double cx, double baseY, double r)
    {
        // плоское основание + полукруг сверху
        var p = new PolyData { Closed = true };
        p.Pts.Add(new Vec2(cx - r, baseY));
        for (int a = 180; a <= 360; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(cx + Math.Cos(rad) * r, baseY + Math.Sin(rad) * r));
        }
        p.Pts.Add(new Vec2(cx + r, baseY));
        return p;
    }

    private static PolyData Crescent()
    {
        // полумесяц: большой круг минус смещённый меньший ( источник — в «теле» серпа )
        var O = new Vec2(640, 360);
        var I = new Vec2(720, 360);
        const double R = 330, r = 260;
        var p = new PolyData { Closed = true };
        for (double a = 25.3; a <= 334.7; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(O.X + Math.Cos(rad) * R, O.Y + Math.Sin(rad) * R));
        }
        for (double a = 327.1; a >= 32.9; a -= 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(I.X + Math.Cos(rad) * r, I.Y + Math.Sin(rad) * r));
        }
        return p;
    }

    private static PolyData Dumbbell()
    {
        // две круговые «камеры», соединённые коридором
        var c1 = new Vec2(380, 360);
        var c2 = new Vec2(900, 360);
        const double r = 210, top = 270, bot = 450;
        double ax = Math.Sqrt(r * r - 90 * 90); // 189.6
        var p = new PolyData { Closed = true };
        p.Pts.Add(new Vec2(c1.X + ax, top));
        p.Pts.Add(new Vec2(c2.X - ax, top));
        for (double a = -154.6; a <= 154.6; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(c2.X + Math.Cos(rad) * r, c2.Y + Math.Sin(rad) * r));
        }
        p.Pts.Add(new Vec2(c2.X - ax, bot));
        p.Pts.Add(new Vec2(c1.X + ax, bot));
        for (double a = 25.4; a <= 334.6; a += 6)
        {
            double rad = a * Math.PI / 180;
            p.Pts.Add(new Vec2(c1.X + Math.Cos(rad) * r, c1.Y + Math.Sin(rad) * r));
        }
        return p;
    }

    private static PolyData Cross()
    {
        const int cx = 640, cy = 360, w = 110, L = 340;
        return Closed(
            new Vec2(cx - w, cy - L), new Vec2(cx + w, cy - L), new Vec2(cx + w, cy - w),
            new Vec2(cx + L, cy - w), new Vec2(cx + L, cy + w), new Vec2(cx + w, cy + w),
            new Vec2(cx + w, cy + L), new Vec2(cx - w, cy + L), new Vec2(cx - w, cy + w),
            new Vec2(cx - L, cy + w), new Vec2(cx - L, cy - w), new Vec2(cx - w, cy - w));
    }

    private static PolyData Heart()
    {
        var p = new PolyData { Closed = true };
        const double s = 17;
        for (double t = 0; t <= 2 * Math.PI + 0.001; t += Math.PI / 48)
        {
            double x = 16 * Math.Pow(Math.Sin(t), 3);
            double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
            p.Pts.Add(new Vec2(640 + s * x, 360 - s * (y + 6)));
        }
        return p;
    }

    private static PolyData Staircase()
    {
        return Closed(
            new Vec2(180, 620), new Vec2(180, 500), new Vec2(360, 500), new Vec2(360, 380),
            new Vec2(540, 380), new Vec2(540, 260), new Vec2(720, 260), new Vec2(720, 140),
            new Vec2(1100, 140), new Vec2(1100, 620));
    }

    private static PolyData Superellipse(double cx, double cy, double a, double b, double n)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < 120; i++)
        {
            double t = i * 2 * Math.PI / 120;
            double ct = Math.Cos(t), st = Math.Sin(t);
            double x = a * Math.Sign(ct) * Math.Pow(Math.Abs(ct), 2 / n);
            double y = b * Math.Sign(st) * Math.Pow(Math.Abs(st), 2 / n);
            p.Pts.Add(new Vec2(cx + x, cy + y));
        }
        return p;
    }

    private static PolyData Mushroom()
    {
        const double cx = 640, capY = 340, R = 250, stemHalf = 105, stemBot = 630;
        var p = new PolyData { Closed = true };
        for (int deg = 180; deg <= 360; deg += 4)
        {
            double rad = deg * Math.PI / 180.0;
            p.Pts.Add(new Vec2(cx + Math.Cos(rad) * R, capY + Math.Sin(rad) * R));
        }
        p.Pts.Add(new Vec2(cx + stemHalf, capY));
        p.Pts.Add(new Vec2(cx + stemHalf, stemBot));
        p.Pts.Add(new Vec2(cx - stemHalf, stemBot));
        p.Pts.Add(new Vec2(cx - stemHalf, capY));
        return p;
    }

    private static PolyData Cardioid()
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < 128; i++)
        {
            double t = i * 2.0 * Math.PI / 128.0;
            double r = 195.0 * (1.0 + 0.45 * Math.Cos(t));
            p.Pts.Add(new Vec2(595.0 + r * Math.Cos(t), 360.0 + r * Math.Sin(t)));
        }
        return p;
    }

    private static PolyData PolarFlower(double cx, double cy, double baseR, double amp, int lobes, double phase, int steps)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < steps; i++)
        {
            double t = i * 2.0 * Math.PI / steps;
            double r = baseR + amp * Math.Cos(lobes * t + phase);
            p.Pts.Add(new Vec2(cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return p;
    }

    private static PolyData ArcBarrier(double cx, double cy, double r, double deg0, double deg1, int segs)
    {
        var p = new PolyData { Closed = false };
        for (int i = 0; i <= segs; i++)
        {
            double deg = deg0 + (deg1 - deg0) * i / segs;
            double rad = deg * Math.PI / 180.0;
            p.Pts.Add(new Vec2(cx + Math.Cos(rad) * r, cy + Math.Sin(rad) * r));
        }
        return p;
    }

    // ------------------------------------------------- пуск и управление

    /// <summary>Размер лучей: меняет и уже летящие лучи, и все последующие пуски.</summary>
    private void SetRadius(double r)
    {
        waveR = Math.Clamp(Math.Round(r), 2, 16);
        canvas.NewBallR = waveR;
        sizeBtn.Value = (int)waveR;
        foreach (var b in canvas.Sim.Balls) b.R = waveR;
        canvas.Invalidate();
    }

    private void Emit()
    {
        var lvl = figures[figIndex].Level;
        canvas.LoadLevel(lvl);
        canvas.NewBallR = waveR;
        canvas.WaveSource = source;
        canvas.Sim.GravityOn = gravityCache;
        canvas.Sim.BallCollisions = false; // лучи не сталкиваются между собой

        if (canvas.UseWaveFront)
        {
            canvas.WaveFront.ResetFrom(lvl, source);
            canvas.WaveFront.GravityOn = gravityCache;
            canvas.WaveFront.EmitPointSource(source, rays, WaveSpeed, spectrum, ballArgb);
        }
        else
        {
            for (int i = 0; i < rays; i++)
            {
                double a = i * 2 * Math.PI / rays;
                if (spectrum)
                    canvas.Sim.AddBall(source, new Vec2(Math.Cos(a), Math.Sin(a)) * WaveSpeed, waveR,
                        hue: (int)(i * 360.0 / rays));
                else
                    canvas.Sim.AddBall(source, new Vec2(Math.Cos(a), Math.Sin(a)) * WaveSpeed, waveR,
                        argb: ballArgb);
            }
        }
        lblName.Text = $"  {figIndex + 1}/{figures.Count} · {lvl.Name}";
        raysBtn.Value = rays;
        canvas.Paused = false;
        btnPause.Icon = IconPainter.Pause;
    }

    /// <summary>Округление плотности к «красивым» значениям: 40, 120, 360, 1440, 5760, …</summary>
    private static int SnapRays(double v)
    {
        if (v < 100) return Math.Max(10, (int)Math.Round(v / 5) * 5);
        if (v < 1000) return (int)Math.Round(v / 25) * 25;
        if (v < 10000) return (int)Math.Round(v / 250) * 250;
        return (int)Math.Round(v / 2500) * 2500;
    }

    private bool gravityCache;

    /// <summary>Гравитация волнового фронта (фронт «стекает» вниз).</summary>
    public bool GravityOn
    {
        get => canvas.UseWaveFront ? canvas.WaveFront.GravityOn : canvas.Sim.GravityOn;
        set
        {
            gravityCache = value;
            canvas.Sim.GravityOn = value;
            canvas.WaveFront.GravityOn = value;
        }
    }

    public bool TrailsOn
    {
        get => canvas.Trails;
        set => canvas.Trails = value;
    }

    public void SetActive(bool active) => canvas.SetLoopActive(active);

    private void TogglePause()
    {
        canvas.Paused = !canvas.Paused;
        btnPause.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
        btnPause.Invalidate();
    }

    public bool HandleKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.R:
                Emit();
                return true;
            case Keys.Space:
                TogglePause();
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                SetRadius(waveR - 1);
                return true;
            case Keys.Oemplus:
            case Keys.Add:
                SetRadius(waveR + 1);
                return true;
            case Keys.G:
                GravityOn = !GravityOn; // чип в боковой панели синхронизируется сам
                return true;
            case Keys.T:
                chipTrace.IsOn = !chipTrace.IsOn;
                chipTrace.Invalidate();
                canvas.PersistentTrace = chipTrace.IsOn;
                if (!chipTrace.IsOn) canvas.ClearPersistentTrace();
                canvas.Invalidate();
                return true;
        }
        return false;
    }
}

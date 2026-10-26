using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Волновой фронт в математических бильярдах»: из точки-источника вылетает
/// веер лучей-шаров одинаковой скорости; упругие отражения внутри фигуры рисуют
/// волновой фронт и каустики. 24 фигуры: от интегрируемого круга и эллипса
/// до хаотического стадиона Бунимовича, звёзд, серпа и рамки-коридора.
/// </summary>
public class WaveView : UserControl, IModeView
{
    private const double WaveSpeed = 320;
    private double waveR = 3; // радиус лучей-шаров (меняется на лету)

    private readonly GameCanvas canvas = new() { Editor = false, WaveMode = true, Dock = DockStyle.Fill, ShowGrid = false };
    private readonly List<(LevelData Level, Vec2 Source)> figures = new();
    private readonly Label lblName = new()
    {
        AutoSize = false, Size = new Size(170, 34), ForeColor = Theme.Accent,
        Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
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
        Icon = IconPainter.Play, Accent = Theme.Green, Size = new Size(50, 38), IconPx = 20,
    };

    private int figIndex;
    private int rays = 120;
    private Vec2 source = new(640, 360);
    private int ballArgb;      // 0 = СПЕКТР
    private bool spectrum = true;
    private readonly ToolTip tips = new();

    public WaveView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        figures.AddRange(BuildFigures());

        var top = new Toolbar { Dock = DockStyle.Top, Height = 56 };
        var hints = new Bar { Dock = DockStyle.Bottom, Height = 30, LineAtTop = true, BackColor = Theme.Panel };
        hints.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "  клик — перенести источник  ·  ЛУЧИ — плотность веера  ·  колесо или R-кнопка — размер лучей  ·  G — гравитация  ·  R — пуск  ·  пробел — пауза  ·  Esc — к уровням",
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(38, 38), IconPx = 20 };
        var btnNext = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(38, 38), IconPx = 20 };

        tips.SetToolTip(btnPrev, "Предыдущая фигура");
        tips.SetToolTip(btnNext, "Следующая фигура");
        tips.SetToolTip(chipFx, "Шейдерные эффекты: свечение, аберрация, виньетка");
        tips.SetToolTip(colorBtn, "Цвет лучей (СПЕКТР — радуга)");
        tips.SetToolTip(sizeBtn, "Размер лучей — ползунок (или колесо мыши, +/−)");
        tips.SetToolTip(raysBtn, "Количество лучей фронта — ползунок 10…100K");
        tips.SetToolTip(btnPause, "Пауза (пробел)");
        tips.SetToolTip(btnEmit, "Испустить фронт заново (R)");

        btnPrev.Click += (s, e) => { figIndex = (figIndex - 1 + figures.Count) % figures.Count; source = figures[figIndex].Source; Emit(); };
        btnNext.Click += (s, e) => { figIndex = (figIndex + 1) % figures.Count; source = figures[figIndex].Source; Emit(); };
        btnEmit.Click += (s, e) => Emit();
        btnPause.Click += (s, e) => TogglePause();
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

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName);
        top.AddCenter(raysBtn);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn).AddRight(btnPause).AddRight(btnEmit);

        Controls.Add(canvas);
        Controls.Add(hints);
        Controls.Add(top);

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

    // ------------------------------------------------- пуск и управление

    /// <summary>Размер лучей: меняет и уже летящие лучи, и все последующие пуски.</summary>
    private void SetRadius(double r)
    {
        waveR = Math.Clamp(Math.Round(r), 2, 16);
        canvas.NewBallR = waveR;
        sizeBtn.Value = (int)waveR;
        foreach (var b in canvas.Sim.Balls) b.R = waveR;
    }

    private void Emit()
    {
        canvas.LoadLevel(figures[figIndex].Level);
        canvas.WaveSource = source;
        canvas.Sim.GravityOn = gravityCache;
        canvas.Sim.BallCollisions = false; // лучи не сталкиваются между собой
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
        lblName.Text = $"  {figures[figIndex].Level.Name}";
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
        get => canvas.Sim.GravityOn;
        set { gravityCache = value; canvas.Sim.GravityOn = value; }
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
        }
        return false;
    }
}

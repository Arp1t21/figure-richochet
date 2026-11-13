using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Аттракторы»: орбитальная физика гравитационных колодцев (притягивающих «чёрных дыр»,
/// отталкивающих «белых дыр» и вихревых закруток) вместе со связанными варп-порталами.
/// </summary>
public class GravityWellsView : UserControl, IModeView
{
    public const int PresetCount = 8;

    private static readonly string[] PresetNames =
    {
        "Бинарная орбита",
        "Сингулярность + порталы",
        "Отталкиватель в ромбе",
        "Кометный коридор",
        "Встречные вихри ×2",
        "Портальный пинбол",
        "Тройная звезда",
        "Реактор частиц (16 шаров)",
    };

    private static readonly string[] PresetBadges =
    {
        "БИНАРНАЯ", "ПОРТАЛЫ", "АНТИГРАВ", "КОРИДОР",
        "ВИХРЬ ×2", "ПИНБОЛ", "3 ЗВЕЗДЫ", "16 ШАРОВ",
    };

    private sealed class GravityWell
    {
        public Vec2 Pos;
        public Vec2 OrbitCenter;
        public double OrbitRadius;
        public double OrbitPhase;
        public double OrbitSpeed;
        public double Strength; // > 0 притяжение, < 0 отталкивание
        public double Swirl;    // тангенциальный вихревой момент
        public double VisualR;
    }

    private sealed class PortalPair
    {
        public Vec2 A, B;
        public double R;
        public Color ColA, ColB;
    }

    private struct PulseWave
    {
        public float X, Y, R0, R1, Age, Life;
        public Color Col;
    }

    private readonly Toolbar bar = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnPrev = new() { Icon = IconPainter.ChevronLeft, Size = new Size(34, 34), IconPx = 18 };
    private readonly LevelSelectButton lblPreset = new() { Size = new Size(230, 34), Role = AccentRole.Accent };
    private readonly NeoButton btnNext = new() { Icon = IconPainter.ChevronRight, Size = new Size(34, 34), IconPx = 18 };
    private readonly NeoButton btnInvert = new() { Text = "ИНВЕРС.", IsToggle = true, Role = AccentRole.Orange, Size = new Size(82, 34), Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
    private readonly NeoButton btnAddBall = new() { Text = "ШАР", Icon = IconPainter.Plus, Role = AccentRole.Green, Size = new Size(74, 34), IconPx = 15, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly NeoButton btnSandbox = new() { Text = "ПЕСОЧНИЦА", Icon = IconPainter.Sliders, Role = AccentRole.Accent, Size = new Size(104, 34), IconPx = 15, Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
    private readonly NeoButton btnRestart = new() { Text = "ЗАНОВО", Icon = IconPainter.Restart, Size = new Size(92, 34), IconPx = 16 };
    private readonly NeoButton btnPause = new() { Text = "ПАУЗА", Icon = IconPainter.Pause, IsToggle = true, Size = new Size(86, 34), IconPx = 16 };

    private readonly GameCanvas canvas = new()
    {
        Dock = DockStyle.Fill,
        RotorSkin = true,
        InstantDropNoAim = true,
        SuppressWallDraw = true,
    };

    public GameCanvas Canvas => canvas;

    private int presetIndex;
    private double gravPower = 1.0;
    private double swirlPower = 1.0;
    private double wallRestitution = 0.98;
    private bool portalsEnabled = true;
    private bool inverted;
    private int maxBalls = 64;
    private double simTime;
    private bool wasPaused;
    private int emitIdx;

    private readonly Vec2 center = new(640, 360);
    private double arenaR = 296;
    private readonly List<GravityWell> wells = new();
    private readonly List<PortalPair> portals = new();
    private readonly List<PulseWave> waves = new(32);
    private readonly List<(PointF[] Pts, bool Closed)> wallPtCache = new();
    private readonly Dictionary<SimBall, double> portalCooldowns = new();
    private readonly List<SimBall> tmpCdKeys = new();

    // Кэш GDI без аллокаций в кадре
    private readonly Font hudFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly SolidBrush hudBgBrush = new(Color.FromArgb(182, 10, 13, 22));
    private readonly SolidBrush hudTextBrush = new(Color.FromArgb(236, 244, 255));
    private readonly Pen hudBorderPen = new(Color.FromArgb(95, 94, 197, 255), 1.1f);
    private readonly Pen arenaGlowPen = new(Color.FromArgb(42, 94, 197, 255), 7.5f) { LineJoin = LineJoin.Round };
    private readonly Pen arenaCorePen = new(Color.FromArgb(228, 128, 220, 255), 2.6f) { LineJoin = LineJoin.Round };
    private readonly Pen fieldRingPen = new(Color.FromArgb(42, 94, 197, 255), 1.2f);
    private readonly SolidBrush coreFillBrush = new(Color.FromArgb(235, 10, 14, 24));
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    public int PresetIndex => presetIndex;
    public int WellCount => wells.Count;
    public int PortalPairCount => portals.Count;

    public GravityWellsView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        bar.AddLeft(btnPrev);
        bar.AddLeft(lblPreset);
        bar.AddLeft(btnNext);
        bar.AddLeft(btnInvert);
        bar.AddLeft(btnAddBall);
        bar.AddLeft(btnSandbox);
        bar.AddRight(btnPause);
        bar.AddRight(btnRestart);

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущая орбитальная система");
        tips.SetToolTip(lblPreset, "Выбрать систему аттракторов и порталов");
        tips.SetToolTip(btnNext, "Следующая орбитальная система");
        tips.SetToolTip(btnInvert, "Инвертировать полярность колодцев: притяжение <-> отталкивание (I)");
        tips.SetToolTip(btnAddBall, "Запустить ещё один орбитальный шар (+)");
        tips.SetToolTip(btnSandbox, "Песочница: сила колодцев, закрутка, порталы и упругость");
        tips.SetToolTip(btnRestart, "Перезапустить систему (R)");
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
                    Role = AccentRole.Accent,
                    RotorStyle = true,
                });
            }
            return (presetIndex, items);
        };
        lblPreset.LevelSelected += ShowPreset;

        btnPrev.Click += (_, _) => ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
        btnNext.Click += (_, _) => ShowPreset((presetIndex + 1) % PresetCount);
        btnInvert.Click += (_, _) => { inverted = btnInvert.IsOn; canvas.Invalidate(); };
        btnAddBall.Click += (_, _) => SpawnOrbitalBall();
        SandboxPopup.Attach(btnSandbox, "Песочница · Аттракторы", BuildSandboxEntries);
        btnRestart.Click += (_, _) => ShowPreset(presetIndex);
        btnPause.Click += (_, _) => SetPaused(btnPause.IsOn);

        canvas.Trails = true;
        canvas.ClampDropPos = ClampInside;
        canvas.BallPlaced += OnBallPlaced;
        canvas.Simulated += OnSimulated;
        canvas.RenderUnderlay += OnRenderUnderlay;
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
            canvas.BallPlaced -= OnBallPlaced;
            canvas.Simulated -= OnSimulated;
            canvas.RenderUnderlay -= OnRenderUnderlay;
            canvas.RenderOverlay -= OnRenderOverlay;
            canvas.RenderScreenOverlay -= OnRenderScreenOverlay;
            hudFont.Dispose();
            hudBgBrush.Dispose();
            hudTextBrush.Dispose();
            hudBorderPen.Dispose();
            arenaGlowPen.Dispose();
            arenaCorePen.Dispose();
            fieldRingPen.Dispose();
            coreFillBrush.Dispose();
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
            case Keys.Space:
                SetPaused(!canvas.Paused);
                return true;
            case Keys.R:
                ShowPreset(presetIndex);
                return true;
            case Keys.I:
                inverted = !inverted;
                btnInvert.IsOn = inverted;
                btnInvert.Invalidate();
                canvas.Invalidate();
                return true;
            case Keys.Oemplus:
            case Keys.Add:
                SpawnOrbitalBall();
                return true;
            case Keys.Left:
                ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
                return true;
            case Keys.Right:
                ShowPreset((presetIndex + 1) % PresetCount);
                return true;
        }
        return false;
    }

    private void SetPaused(bool paused)
    {
        canvas.Paused = paused;
        wasPaused = paused;
        btnPause.IsOn = paused;
        btnPause.Text = paused ? "ПУСК" : "ПАУЗА";
        btnPause.Icon = paused ? IconPainter.Play : IconPainter.Pause;
        btnPause.Invalidate();
        canvas.Invalidate();
    }

    private List<SandboxEntry> BuildSandboxEntries() => new()
    {
        new SandboxSlider { Label = "Сила притяжения", Min = 0.20, Max = 3.00, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => gravPower, Set = v => gravPower = v },
        new SandboxSlider { Label = "Вихревая закрутка", Min = 0.00, Max = 2.50, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => swirlPower, Set = v => swirlPower = v },
        new SandboxSlider { Label = "Упругость арены", Min = 0.50, Max = 1.15, Step = 0.02, Format = v => $"×{v:0.00}", Get = () => wallRestitution, Set = v => { wallRestitution = v; canvas.Sim.WallRestitution = v; } },
        new SandboxSlider { Label = "Лимит шаров", Min = 10, Max = 120, Step = 2, Format = v => ((int)Math.Round(v)).ToString(), Get = () => maxBalls, Set = v => maxBalls = (int)Math.Round(v) },
        new SandboxToggle { Label = "Инвертировать колодцы", Get = () => inverted, Set = v => { inverted = v; btnInvert.IsOn = v; btnInvert.Invalidate(); canvas.Invalidate(); } },
        new SandboxToggle { Label = "Порталы активны", Get = () => portalsEnabled, Set = v => { portalsEnabled = v; canvas.Invalidate(); } },
        new SandboxToggle { Label = "Гравитация вниз (G)", Get = () => canvas.Sim.GravityOn, Set = v => { canvas.Sim.GravityOn = v; canvas.Design.Gravity = v; } },
        new SandboxToggle { Label = "Следы орбит", Get = () => canvas.Trails, Set = v => canvas.Trails = v },
    };

    private static LevelData BuildArenaPolygon(int sides, Vec2 center, double r, double rotDeg = 0)
    {
        var lvl = new LevelData { Name = "Аттракторы", Border = false, Gravity = false, RotorRules = false, WallRestitution = 0.98 };
        if (sides <= 0)
        {
            lvl.Circles.Add(new CircleData { Center = center, Radius = r });
            return lvl;
        }
        var pts = new List<Vec2>(sides);
        double rot = rotDeg * Math.PI / 180.0;
        for (int i = 0; i < sides; i++)
        {
            double a = rot + i * Math.PI * 2.0 / sides;
            pts.Add(new Vec2(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r));
        }
        lvl.Walls.Add(new PolyData { Closed = true, Pts = pts });
        return lvl;
    }

    private LevelData CreatePreviewLevel(int idx)
    {
        return idx switch
        {
            0 => BuildArenaPolygon(0, center, 294),
            1 => BuildArenaPolygon(6, center, 300, 30),
            2 => BuildArenaPolygon(4, center, 296, 45),
            3 => BuildBoxCorridor(),
            4 => BuildArenaPolygon(0, center, 294),
            5 => BuildArenaPolygon(5, center, 296, -90),
            6 => BuildArenaPolygon(8, center, 298, 22.5),
            _ => BuildArenaPolygon(0, center, 300),
        };
    }

    private LevelData BuildBoxCorridor()
    {
        var lvl = new LevelData { Name = "Кометный коридор", Border = false, Gravity = false };
        lvl.Walls.Add(new PolyData
        {
            Closed = true,
            Pts = new List<Vec2>
            {
                new(160, 130), new(1120, 130), new(1120, 590), new(160, 590),
            },
        });
        return lvl;
    }

    public void ShowPreset(int index)
    {
        presetIndex = ((index % PresetCount) + PresetCount) % PresetCount;
        lblPreset.Text = $"{presetIndex + 1}. {PresetNames[presetIndex]}";

        simTime = 0;
        emitIdx = 0;
        inverted = false;
        btnInvert.IsOn = false;
        btnInvert.Invalidate();
        wells.Clear();
        portals.Clear();
        waves.Clear();
        portalCooldowns.Clear();

        var lvl = CreatePreviewLevel(presetIndex);
        lvl.Gravity = false;
        lvl.WallRestitution = wallRestitution;
        lvl.BallRestitution = 0.96;
        lvl.AirDrag = 0.006;

        arenaR = lvl.Circles.Count > 0 ? lvl.Circles[0].Radius : 292;
        canvas.SuppressCircleDraw = lvl.Circles.Count == 0;

        int ballCount = 6;
        switch (presetIndex)
        {
            case 0: // Бинарная орбита
                wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 105, OrbitPhase = 0, OrbitSpeed = 0.65, Strength = 1850000, Swirl = 0.14, VisualR = 16 });
                wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 105, OrbitPhase = Math.PI, OrbitSpeed = 0.65, Strength = 1850000, Swirl = 0.14, VisualR = 16 });
                ballCount = 8;
                break;

            case 1: // Сингулярность + 2 портала
                wells.Add(new GravityWell { Pos = center, OrbitCenter = center, Strength = 3100000, Swirl = 0.25, VisualR = 20 });
                portals.Add(new PortalPair
                {
                    A = new Vec2(center.X - 185, center.Y - 90),
                    B = new Vec2(center.X + 185, center.Y + 90),
                    R = 24,
                    ColA = Color.FromArgb(94, 197, 255),
                    ColB = Color.FromArgb(255, 176, 88),
                });
                ballCount = 7;
                break;

            case 2: // Отталкиватель в ромбе
                wells.Add(new GravityWell { Pos = center, OrbitCenter = center, Strength = -2400000, Swirl = 0.18, VisualR = 20 });
                wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 150, OrbitPhase = 0.4, OrbitSpeed = -0.55, Strength = 1250000, Swirl = 0, VisualR = 13 });
                wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 150, OrbitPhase = 0.4 + Math.PI, OrbitSpeed = -0.55, Strength = 1250000, Swirl = 0, VisualR = 13 });
                ballCount = 8;
                break;

            case 3: // Кометный коридор
                wells.Add(new GravityWell { Pos = new Vec2(410, 360), OrbitCenter = new Vec2(410, 360), Strength = 2400000, Swirl = 0.30, VisualR = 17 });
                wells.Add(new GravityWell { Pos = new Vec2(640, 360), OrbitCenter = new Vec2(640, 360), Strength = -1900000, Swirl = 0, VisualR = 16 });
                wells.Add(new GravityWell { Pos = new Vec2(870, 360), OrbitCenter = new Vec2(870, 360), Strength = 2400000, Swirl = -0.30, VisualR = 17 });
                portals.Add(new PortalPair
                {
                    A = new Vec2(230, 210),
                    B = new Vec2(1050, 510),
                    R = 22,
                    ColA = Color.FromArgb(64, 225, 140),
                    ColB = Color.FromArgb(166, 128, 255),
                });
                ballCount = 10;
                break;

            case 4: // Встречные вихри x2
                wells.Add(new GravityWell { Pos = new Vec2(center.X - 125, center.Y), OrbitCenter = new Vec2(center.X - 125, center.Y), Strength = 2000000, Swirl = 0.55, VisualR = 17 });
                wells.Add(new GravityWell { Pos = new Vec2(center.X + 125, center.Y), OrbitCenter = new Vec2(center.X + 125, center.Y), Strength = 2000000, Swirl = -0.55, VisualR = 17 });
                ballCount = 10;
                break;

            case 5: // Портальный пинбол
                wells.Add(new GravityWell { Pos = new Vec2(center.X, center.Y - 40), OrbitCenter = new Vec2(center.X, center.Y - 40), Strength = -1750000, Swirl = 0.15, VisualR = 17 });
                portals.Add(new PortalPair
                {
                    A = new Vec2(center.X - 150, center.Y - 70),
                    B = new Vec2(center.X + 150, center.Y + 95),
                    R = 22,
                    ColA = Color.FromArgb(94, 197, 255),
                    ColB = Color.FromArgb(246, 118, 240),
                });
                portals.Add(new PortalPair
                {
                    A = new Vec2(center.X + 150, center.Y - 70),
                    B = new Vec2(center.X - 150, center.Y + 95),
                    R = 22,
                    ColA = Color.FromArgb(64, 225, 140),
                    ColB = Color.FromArgb(255, 176, 88),
                });
                ballCount = 8;
                break;

            case 6: // Тройная звезда
                for (int k = 0; k < 3; k++)
                {
                    double a = k * Math.PI * 2.0 / 3.0;
                    wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 115, OrbitPhase = a, OrbitSpeed = 0.42, Strength = 1650000, Swirl = 0.12, VisualR = 15 });
                }
                ballCount = 9;
                break;

            default: // Реактор частиц (16 шаров)
                wells.Add(new GravityWell { Pos = center, OrbitCenter = center, Strength = 2800000, Swirl = 0.35, VisualR = 19 });
                wells.Add(new GravityWell { OrbitCenter = center, OrbitRadius = 165, OrbitPhase = 0, OrbitSpeed = 0.80, Strength = -1200000, Swirl = 0.25, VisualR = 13 });
                portals.Add(new PortalPair
                {
                    A = new Vec2(center.X, center.Y - 210),
                    B = new Vec2(center.X, center.Y + 210),
                    R = 22,
                    ColA = Color.FromArgb(94, 197, 255),
                    ColB = Color.FromArgb(255, 176, 88),
                });
                ballCount = 16;
                break;
        }

        UpdateWellPositions();

        for (int i = 0; i < ballCount; i++)
        {
            double ang = i * (Math.PI * 2.0 / ballCount) + 0.35;
            double rr = presetIndex == 3 ? 90 + (i % 3) * 45 : 155 + (i % 3) * 34;
            Vec2 pos = presetIndex == 3
                ? new Vec2(290 + i * 70, 220 + ((i % 2) == 0 ? 0 : 260))
                : new Vec2(center.X + Math.Cos(ang) * rr, center.Y + Math.Sin(ang) * rr);
            Vec2 tang = presetIndex == 3
                ? new Vec2(190, (i % 2 == 0 ? 1 : -1) * 165)
                : new Vec2(-Math.Sin(ang), Math.Cos(ang)) * (250 + (i % 3) * 28);
            int argb = GameCanvas.SpectrumPalette24[(i * 3) % 24];
            lvl.Balls.Add(new BallData { Pos = pos, Vel = tang, HasVel = true, R = 9.5, Argb = argb });
        }

        canvas.LoadLevel(lvl);
        wallPtCache.Clear();
        foreach (var w in lvl.Walls)
        {
            if (w.Pts.Count < 2) continue;
            var pts = new PointF[w.Pts.Count];
            for (int i = 0; i < w.Pts.Count; i++) pts[i] = new PointF((float)w.Pts[i].X, (float)w.Pts[i].Y);
            wallPtCache.Add((pts, w.Closed));
        }
        SetPaused(false);
    }

    private void UpdateWellPositions()
    {
        for (int i = 0; i < wells.Count; i++)
        {
            var w = wells[i];
            if (w.OrbitRadius > 0.5)
            {
                double a = w.OrbitPhase + simTime * w.OrbitSpeed;
                w.Pos = new Vec2(w.OrbitCenter.X + Math.Cos(a) * w.OrbitRadius, w.OrbitCenter.Y + Math.Sin(a) * w.OrbitRadius);
            }
        }
    }

    private Vec2 ClampInside(Vec2 p) => ClampInside(p, canvas.NewBallR);

    private Vec2 ClampInside(Vec2 p, double r)
    {
        if (presetIndex == 3)
            return new Vec2(Math.Clamp(p.X, 175 + r, 1105 - r), Math.Clamp(p.Y, 145 + r, 575 - r));
        var d = p - center;
        double lim = arenaR - r - 8;
        return d.Len > lim ? center + d.Norm() * lim : p;
    }

    private void SpawnOrbitalBall()
    {
        if (canvas.Sim.Balls.Count >= maxBalls)
            canvas.Sim.Balls.RemoveAt(0);
        int idx = emitIdx++;
        double ang = idx * 1.17 + simTime;
        var pos = ClampInside(new Vec2(center.X + Math.Cos(ang) * 155, center.Y + Math.Sin(ang) * 155), 9.5);
        var radial = (pos - center).Len > 1 ? (pos - center).Norm() : new Vec2(1, 0);
        var tang = new Vec2(-radial.Y, radial.X) * 290.0;
        int argb = GameCanvas.SpectrumPalette24[(idx * 2) % 24];
        canvas.Sim.AddBall(pos, tang, 9.5, hue: -1, argb: argb);
        canvas.Invalidate();
    }

    private void OnBallPlaced(SimBall b)
    {
        var away = b.Pos - center;
        var tang = away.Len > 5 ? new Vec2(-away.Y, away.X).Norm() : new Vec2(1, 0);
        b.Vel = tang * 265.0;
        b.R = 9.5;
        b.Argb = GameCanvas.SpectrumPalette24[(emitIdx++ * 2) % 24];
        while (canvas.Sim.Balls.Count > maxBalls)
            canvas.Sim.Balls.RemoveAt(0);
    }

    private void OnSimulated(double dt)
    {
        simTime += dt;
        UpdateWellPositions();

        double pol = inverted ? -1.0 : 1.0;
        var balls = canvas.Sim.Balls;

        // Ускорение от гравитационных колодцев + вихревой момент
        for (int bi = 0; bi < balls.Count; bi++)
        {
            var b = balls[bi];
            double ax = 0, ay = 0;
            for (int wi = 0; wi < wells.Count; wi++)
            {
                var w = wells[wi];
                double dx = w.Pos.X - b.Pos.X;
                double dy = w.Pos.Y - b.Pos.Y;
                double d2 = dx * dx + dy * dy;
                const double soft2 = 34.0 * 34.0;
                double invDist = 1.0 / Math.Sqrt(d2 + 120.0);
                double f = (w.Strength * gravPower * pol) / (d2 + soft2);
                double ux = dx * invDist, uy = dy * invDist;
                ax += ux * f;
                ay += uy * f;

                if (Math.Abs(w.Swirl * swirlPower) > 1e-4)
                {
                    double swirlF = (Math.Abs(w.Strength) * gravPower * w.Swirl * swirlPower) / (d2 + soft2);
                    ax += -uy * swirlF;
                    ay += ux * swirlF;
                }
            }
            b.Vel.X += ax * dt;
            b.Vel.Y += ay * dt;
            double spd2 = b.Vel.X * b.Vel.X + b.Vel.Y * b.Vel.Y;
            if (spd2 > 1350.0 * 1350.0)
            {
                double s = 1350.0 / Math.Sqrt(spd2);
                b.Vel.X *= s;
                b.Vel.Y *= s;
            }
        }

        // Охлаждение телепортов + проверка входа в портал
        if (portalCooldowns.Count > 0)
        {
            tmpCdKeys.Clear();
            foreach (var k in portalCooldowns.Keys) tmpCdKeys.Add(k);
            for (int i = 0; i < tmpCdKeys.Count; i++)
            {
                var k = tmpCdKeys[i];
                double rem = portalCooldowns[k] - dt;
                if (rem <= 0) portalCooldowns.Remove(k);
                else portalCooldowns[k] = rem;
            }
        }

        if (portalsEnabled && portals.Count > 0)
        {
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (portalCooldowns.ContainsKey(b)) continue;
                for (int pi = 0; pi < portals.Count; pi++)
                {
                    var p = portals[pi];
                    double rHit = p.R + b.R * 0.4;
                    double rHit2 = rHit * rHit;
                    if ((b.Pos - p.A).Len2 <= rHit2)
                    {
                        TeleportBall(b, p.A, p.B, p.R, p.ColA, p.ColB);
                        break;
                    }
                    if ((b.Pos - p.B).Len2 <= rHit2)
                    {
                        TeleportBall(b, p.B, p.A, p.R, p.ColB, p.ColA);
                        break;
                    }
                }
            }
        }

        for (int i = waves.Count - 1; i >= 0; i--)
        {
            var w = waves[i];
            w.Age += (float)dt;
            if (w.Age >= w.Life) waves.RemoveAt(i);
            else waves[i] = w;
        }
    }

    private void TeleportBall(SimBall b, Vec2 src, Vec2 dst, double r, Color cSrc, Color cDst)
    {
        var dir = b.Vel.Len > 1e-3 ? b.Vel.Norm() : new Vec2(1, 0);
        b.Pos = dst + dir * (r + b.R + 4.0);
        portalCooldowns[b] = 0.28;
        waves.Add(new PulseWave { X = (float)src.X, Y = (float)src.Y, R0 = (float)r, R1 = (float)r + 36f, Age = 0f, Life = 0.42f, Col = cSrc });
        waves.Add(new PulseWave { X = (float)dst.X, Y = (float)dst.Y, R0 = (float)r * 0.6f, R1 = (float)r + 44f, Age = 0f, Life = 0.48f, Col = cDst });
    }

    private void OnRenderUnderlay(Graphics g, LevelData lvl)
    {
        // Стенки арены рисуются мягким неоновым двойным контуром
        for (int wi = 0; wi < wallPtCache.Count; wi++)
        {
            var (pts, closed) = wallPtCache[wi];
            if (closed)
            {
                g.DrawPolygon(arenaGlowPen, pts);
                g.DrawPolygon(arenaCorePen, pts);
            }
            else
            {
                g.DrawLines(arenaGlowPen, pts);
                g.DrawLines(arenaCorePen, pts);
            }
        }

        double pol = inverted ? -1.0 : 1.0;
        for (int i = 0; i < wells.Count; i++)
        {
            var w = wells[i];
            bool attract = w.Strength * pol >= 0;
            Color col = attract ? Theme.Accent : Theme.Orange;
            float pulse = (float)(0.5 + 0.5 * Math.Sin(simTime * 4.0 + i * 1.4));
            float r1 = (float)w.VisualR + 22f + pulse * 6f;
            float r2 = (float)w.VisualR + 46f - pulse * 5f;
            fieldRingPen.Color = Color.FromArgb(46, col);
            g.DrawEllipse(fieldRingPen, (float)w.Pos.X - r1, (float)w.Pos.Y - r1, r1 * 2f, r1 * 2f);
            fieldRingPen.Color = Color.FromArgb(24, col);
            g.DrawEllipse(fieldRingPen, (float)w.Pos.X - r2, (float)w.Pos.Y - r2, r2 * 2f, r2 * 2f);
        }
    }

    private void OnRenderOverlay(Graphics g, LevelData lvl)
    {
        double pol = inverted ? -1.0 : 1.0;
        for (int i = 0; i < wells.Count; i++)
        {
            var w = wells[i];
            bool attract = w.Strength * pol >= 0;
            Color col = attract ? Theme.Accent : Theme.Orange;
            float r = (float)w.VisualR;
            float x = (float)w.Pos.X, y = (float)w.Pos.Y;

            g.FillEllipse(Theme.BrushOf(Color.FromArgb(36, col)), x - r * 1.55f, y - r * 1.55f, r * 3.1f, r * 3.1f);
            g.FillEllipse(coreFillBrush, x - r, y - r, r * 2f, r * 2f);
            g.DrawEllipse(Theme.PenOf(col, 2.2f), x - r, y - r, r * 2f, r * 2f);
            g.DrawEllipse(Theme.PenOf(Color.FromArgb(215, Color.White), 1.0f), x - r * 0.55f, y - r * 0.55f, r * 1.1f, r * 1.1f);

            // Знак + (притяжение) или - (отталкивание) в ядре
            var sp = Theme.PenOf(col, 2.0f, roundCap: true);
            g.DrawLine(sp, x - 5f, y, x + 5f, y);
            if (attract) g.DrawLine(sp, x, y - 5f, x, y + 5f);
        }

        if (portalsEnabled)
        {
            for (int i = 0; i < portals.Count; i++)
            {
                var p = portals[i];
                DrawPortal(g, p.A, p.R, p.ColA);
                DrawPortal(g, p.B, p.R, p.ColB);
            }
        }

        for (int i = 0; i < waves.Count; i++)
        {
            var w = waves[i];
            float u = Math.Clamp(w.Age / w.Life, 0f, 1f);
            float r = w.R0 + (w.R1 - w.R0) * u;
            int a = Math.Clamp((int)((1f - u) * 210f), 0, 220);
            g.DrawEllipse(Theme.PenOf(Color.FromArgb(a, w.Col), 2.2f), w.X - r, w.Y - r, r * 2f, r * 2f);
        }
    }

    private void DrawPortal(Graphics g, Vec2 pos, double rad, Color col)
    {
        float x = (float)pos.X, y = (float)pos.Y, r = (float)rad;
        float spin = (float)(simTime * 110.0);
        g.FillEllipse(Theme.BrushOf(Color.FromArgb(30, col)), x - r * 1.25f, y - r * 1.25f, r * 2.5f, r * 2.5f);
        g.DrawArc(Theme.PenOf(Color.FromArgb(215, col), 3.0f, roundCap: true), x - r, y - r, r * 2f, r * 2f, spin, 230f);
        g.DrawArc(Theme.PenOf(Color.FromArgb(180, Color.White), 1.6f, roundCap: true), x - r * 0.65f, y - r * 0.65f, r * 1.3f, r * 1.3f, -spin * 1.4f, 180f);
    }

    private void OnRenderScreenOverlay(Graphics g)
    {
        string polStr = inverted ? "ОТТАЛКИВ." : "ПРИТЯЖ.";
        string status = $"Аттракторы: {wells.Count} ({polStr})   ·   Шаров: {canvas.Sim.Balls.Count}   ·   ЛКМ — шар   ·   I — инверсия";
        var sz = g.MeasureString(status, hudFont);
        float bw = sz.Width + 28f, bh = sz.Height + 10f;
        float bx = (canvas.ClientSize.Width - bw) * 0.5f;
        float by = canvas.ClientSize.Height - bh - 12f;
        var rect = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
        Theme.FillDrawRoundRect(g, rect, 9, hudBgBrush, hudBorderPen);
        g.DrawString(status, hudFont, hudTextBrush, bx + bw * 0.5f, by + bh * 0.5f, CenterFmt);
    }
}

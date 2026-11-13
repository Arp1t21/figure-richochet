using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Битва цветов»: 2–4 команды неоновых шаров соревнуются за захват территории арены.
/// Сетка территории (320×180) обновляется инкрементально без перебора всего поля и выводится через unsafe LockBits.
/// </summary>
public class ColorClashView : UserControl, IModeView
{
    public const int PresetCount = 8;

    private const int GridW = 320;
    private const int GridH = 180;
    private const int CellPx = 4; // 320*4 = 1280, 180*4 = 720

    private static readonly string[] PresetNames =
    {
        "Дуэль 2 команды · Квадрат",
        "Трио · Шестиугольник",
        "Квадро-арена · 4 команды",
        "Лабиринт-дуэль с проходом",
        "Колизей с ×2 и краска-бомбами",
        "Грави-котёл 4 команды",
        "Восьмиугольник · рой ×12",
        "Захват нейтральной арены",
    };

    private static readonly string[] PresetBadges =
    {
        "2 КОМ.", "3 КОМ.", "4 КОМ.", "ЛАБИРИНТ",
        "ЭКШН ×2", "ГРАВИ 4×", "РОЙ 12", "С НУЛЯ",
    };

    private static readonly (string Name, int BallArgb, int TileArgb)[] Teams =
    {
        ("ЦИАН",     unchecked((int)0xFF38D4FF), unchecked((int)0x8C1878A8)),
        ("КОРАЛЛ",   unchecked((int)0xFFFF5E7E), unchecked((int)0x8CA82042)),
        ("ИЗУМРУД",  unchecked((int)0xFF3AE896), unchecked((int)0x8C168854)),
        ("ЗОЛОТО",   unchecked((int)0xFFFFBE4A), unchecked((int)0x8CA87414)),
    };

    private sealed class PaintOrb
    {
        public Vec2 Pos;
        public double R;
        public bool IsBomb; // true = радиальный взрыв краски, false = x2 клон шара
        public double Cooldown;
        public double Flash;
    }

    private struct SplashWave
    {
        public float X, Y, R0, R1, Age, Life;
        public Color Col;
    }

    private readonly Toolbar bar = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnPrev = new() { Icon = IconPainter.ChevronLeft, Size = new Size(34, 34), IconPx = 18 };
    private readonly LevelSelectButton lblPreset = new() { Size = new Size(225, 34), Role = AccentRole.Red };
    private readonly NeoButton btnNext = new() { Icon = IconPainter.ChevronRight, Size = new Size(34, 34), IconPx = 18 };
    private readonly NeoButton btnWaveAll = new() { Text = "+ЗАЛП", Icon = IconPainter.Plus, Role = AccentRole.Green, Size = new Size(82, 34), IconPx = 15, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly NeoButton btnOrbs = new() { Text = "БОНУСЫ", IsToggle = true, IsOn = true, Role = AccentRole.Orange, Size = new Size(86, 34), Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
    private readonly NeoButton btnSandbox = new() { Text = "ПЕСОЧНИЦА", Icon = IconPainter.Sliders, Role = AccentRole.Accent, Size = new Size(104, 34), IconPx = 15, Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
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
    private int activeTeamCount = 2;
    private double paintBrushBonus = 3.0;
    private double boostOnPaint = 1.0;
    private double orbCooldownDur = 4.0;
    private double gravityScale = 0.78;
    private double wallRestitution = 1.02;
    private bool orbsEnabled = true;
    private int maxBalls = 40;
    private int dropTeamCursor;
    private double simTime;
    private int winnerTeam = -1;
    private bool wasPaused;

    private readonly Vec2 center = new(640, 350);
    private double arenaR = 290;

    // 0 = вне арены, 1..4 = команда 0..3, 5 = нейтральная незакрашенная клетка внутри арены
    private readonly byte[] owner = new byte[GridW * GridH];
    private readonly int[] teamCells = new int[4];
    private int totalArenaCells;
    private bool dirtyBmp = true;
    private Bitmap territoryBmp;

    private readonly List<PaintOrb> orbs = new();
    private readonly List<SplashWave> waves = new(32);
    private readonly List<(PointF[] Pts, bool Closed)> wallPtCache = new();
    private readonly Dictionary<SimBall, int> ballTeam = new();

    // Кэш GDI
    private readonly Font hudFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly Font orbFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly SolidBrush hudBgBrush = new(Color.FromArgb(188, 10, 13, 22));
    private readonly SolidBrush hudTextBrush = new(Color.FromArgb(238, 244, 255));
    private readonly Pen hudBorderPen = new(Color.FromArgb(95, 255, 110, 135), 1.1f);
    private readonly Pen arenaGlowPen = new(Color.FromArgb(46, 255, 110, 135), 7.5f) { LineJoin = LineJoin.Round };
    private readonly Pen arenaCorePen = new(Color.FromArgb(232, 240, 246, 255), 2.5f) { LineJoin = LineJoin.Round };
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    public int PresetIndex => presetIndex;
    public int ActiveTeamCount => activeTeamCount;
    public int WinnerTeam => winnerTeam;
    public double GetTeamShare(int teamIdx) => totalArenaCells > 0 && teamIdx >= 0 && teamIdx < 4
        ? (double)teamCells[teamIdx] / totalArenaCells
        : 0.0;

    public ColorClashView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        bar.AddLeft(btnPrev);
        bar.AddLeft(lblPreset);
        bar.AddLeft(btnNext);
        bar.AddLeft(btnWaveAll);
        bar.AddLeft(btnOrbs);
        bar.AddLeft(btnSandbox);
        bar.AddRight(btnPause);
        bar.AddRight(btnRestart);

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущая арена битвы цветов");
        tips.SetToolTip(lblPreset, "Выбрать арену и число команд");
        tips.SetToolTip(btnNext, "Следующая арена битвы цветов");
        tips.SetToolTip(btnWaveAll, "Выпустить дополнительный залп (+1 шар каждой команде) (+)");
        tips.SetToolTip(btnOrbs, "Бонусные сферы на поле: ×2 клон и краска-бомба");
        tips.SetToolTip(btnSandbox, "Песочница: радиус кисти, упругость, лимит шаров");
        tips.SetToolTip(btnRestart, "Начать битву заново (R)");
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
                    Role = AccentRole.Red,
                    RotorStyle = true,
                });
            }
            return (presetIndex, items);
        };
        lblPreset.LevelSelected += ShowPreset;

        btnPrev.Click += (_, _) => ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
        btnNext.Click += (_, _) => ShowPreset((presetIndex + 1) % PresetCount);
        btnWaveAll.Click += (_, _) => SpawnTeamVolley();
        btnOrbs.Click += (_, _) => { orbsEnabled = btnOrbs.IsOn; canvas.Invalidate(); };
        SandboxPopup.Attach(btnSandbox, "Песочница · Битва цветов", BuildSandboxEntries);
        btnRestart.Click += (_, _) => ShowPreset(presetIndex);
        btnPause.Click += (_, _) => SetPaused(btnPause.IsOn);

        canvas.ClampDropPos = ClampInside;
        canvas.BallPlaced += OnBallPlaced;
        canvas.Sim.BallsCollided += OnBallsCollided;
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
            canvas.Sim.BallsCollided -= OnBallsCollided;
            canvas.Simulated -= OnSimulated;
            canvas.RenderUnderlay -= OnRenderUnderlay;
            canvas.RenderOverlay -= OnRenderOverlay;
            canvas.RenderScreenOverlay -= OnRenderScreenOverlay;
            territoryBmp?.Dispose();
            hudFont.Dispose();
            orbFont.Dispose();
            hudBgBrush.Dispose();
            hudTextBrush.Dispose();
            hudBorderPen.Dispose();
            arenaGlowPen.Dispose();
            arenaCorePen.Dispose();
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
            case Keys.Oemplus:
            case Keys.Add:
                SpawnTeamVolley();
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
        new SandboxSlider { Label = "Ширина кисти", Min = 0.0, Max = 14.0, Step = 1.0, Format = v => $"+{v:0}px", Get = () => paintBrushBonus, Set = v => paintBrushBonus = v },
        new SandboxSlider { Label = "Ускорение захвата", Min = 0.5, Max = 2.5, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => boostOnPaint, Set = v => boostOnPaint = v },
        new SandboxSlider { Label = "Упругость стен", Min = 0.70, Max = 1.15, Step = 0.02, Format = v => $"×{v:0.00}", Get = () => wallRestitution, Set = v => { wallRestitution = v; canvas.Sim.WallRestitution = v; } },
        new SandboxSlider { Label = "Лимит шаров", Min = 8, Max = 96, Step = 4, Format = v => ((int)Math.Round(v)).ToString(), Get = () => maxBalls, Set = v => maxBalls = (int)Math.Round(v) },
        new SandboxSlider { Label = "КД бонусов", Min = 1.5, Max = 10.0, Step = 0.5, Format = v => $"{v:0.0}с", Get = () => orbCooldownDur, Set = v => orbCooldownDur = v },
        new SandboxToggle { Label = "Бонусные сферы", Get = () => orbsEnabled, Set = v => { orbsEnabled = v; btnOrbs.IsOn = v; btnOrbs.Invalidate(); canvas.Invalidate(); } },
        new SandboxToggle { Label = "Гравитация вниз (G)", Get = () => canvas.Sim.GravityOn, Set = v => { canvas.Sim.GravityOn = v; canvas.Design.Gravity = v; } },
        new SandboxToggle { Label = "Трейлы шаров", Get = () => canvas.Trails, Set = v => canvas.Trails = v },
    };

    private static PolyData PolyCircle(Vec2 c, double r, int segs = 56)
    {
        var pts = new List<Vec2>(segs);
        for (int i = 0; i < segs; i++)
        {
            double a = i * Math.PI * 2.0 / segs;
            pts.Add(new Vec2(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
        }
        return new PolyData { Closed = true, Pts = pts };
    }

    private static PolyData RegularPoly(Vec2 c, double r, int sides, double rotDeg)
    {
        var pts = new List<Vec2>(sides);
        double rot = rotDeg * Math.PI / 180.0;
        for (int i = 0; i < sides; i++)
        {
            double a = rot + i * Math.PI * 2.0 / sides;
            pts.Add(new Vec2(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r));
        }
        return new PolyData { Closed = true, Pts = pts };
    }

    private LevelData CreatePreviewLevel(int idx)
    {
        var lvl = new LevelData { Name = "Битва цветов", Border = false, Gravity = idx == 5, RotorRules = false };
        switch (idx)
        {
            case 0: // Квадрат
                lvl.Walls.Add(RegularPoly(center, 360, 4, 45));
                break;
            case 1: // Шестиугольник
                lvl.Walls.Add(RegularPoly(center, 296, 6, 30));
                break;
            case 2: // Квадро квадрат
                lvl.Walls.Add(RegularPoly(center, 372, 4, 45));
                break;
            case 3: // Лабиринт
                lvl.Walls.Add(new PolyData
                {
                    Closed = true,
                    Pts = new List<Vec2> { new(290, 96), new(990, 96), new(990, 604), new(290, 604) },
                });
                lvl.Walls.Add(new PolyData { Closed = false, Pts = new List<Vec2> { new(640, 170), new(640, 300) } });
                lvl.Walls.Add(new PolyData { Closed = false, Pts = new List<Vec2> { new(640, 400), new(640, 530) } });
                break;
            case 6: // Восьмиугольник
                lvl.Walls.Add(RegularPoly(center, 298, 8, 22.5));
                break;
            default: // Круг
                lvl.Walls.Add(PolyCircle(center, 290, 56));
                break;
        }
        return lvl;
    }

    public void ShowPreset(int index)
    {
        presetIndex = ((index % PresetCount) + PresetCount) % PresetCount;
        lblPreset.Text = $"{presetIndex + 1}. {PresetNames[presetIndex]}";

        simTime = 0;
        winnerTeam = -1;
        dropTeamCursor = 0;
        orbs.Clear();
        waves.Clear();
        ballTeam.Clear();

        activeTeamCount = presetIndex switch
        {
            0 or 3 => 2,
            1 or 4 => 3,
            _ => 4,
        };

        var lvl = CreatePreviewLevel(presetIndex);
        lvl.GravityScale = gravityScale;
        lvl.WallRestitution = wallRestitution;
        lvl.BallRestitution = 1.02;
        lvl.AirDrag = 0.0;

        // Инициализируем маску арены и стартовые владения
        var arenaPoly = lvl.Walls[0].Pts;
        InitTerritoryGrid(arenaPoly, startNeutral: presetIndex == 7);

        // Бонусы на арене (краска-бомбы и x2 клоны)
        if (presetIndex == 4 || presetIndex == 7 || presetIndex == 2)
        {
            orbs.Add(new PaintOrb { Pos = center, R = 17, IsBomb = true });
            orbs.Add(new PaintOrb { Pos = new Vec2(center.X - 135, center.Y), R = 15, IsBomb = false });
            orbs.Add(new PaintOrb { Pos = new Vec2(center.X + 135, center.Y), R = 15, IsBomb = false });
        }
        else if (presetIndex != 0)
        {
            orbs.Add(new PaintOrb { Pos = center, R = 16, IsBomb = presetIndex % 2 == 1 });
        }

        // Стартовые шары команд
        int ballsPerTeam = presetIndex switch
        {
            0 => 2,
            6 => 3,
            _ => presetIndex == 4 ? 2 : 1,
        };
        if (presetIndex == 6) ballsPerTeam = 3; // 4 * 3 = 12 шаров
        else if (presetIndex == 1 || presetIndex == 2 || presetIndex == 3 || presetIndex == 5 || presetIndex == 7)
            ballsPerTeam = 2;

        var seeds = new List<(Vec2 Pos, Vec2 Vel, int Team)>(activeTeamCount * ballsPerTeam);
        for (int t = 0; t < activeTeamCount; t++)
        {
            double baseAng = activeTeamCount == 2 ? (t == 0 ? Math.PI : 0.0) : (t * Math.PI * 2.0 / activeTeamCount + Math.PI * 0.25);
            for (int k = 0; k < ballsPerTeam; k++)
            {
                double da = (k - (ballsPerTeam - 1) * 0.5) * 0.34;
                double dist = presetIndex == 3 ? 190 : 145;
                Vec2 p = ClampInside(new Vec2(center.X + Math.Cos(baseAng + da) * dist, center.Y + Math.Sin(baseAng + da) * dist), 11.5);
                double va = baseAng + da + Math.PI * 0.68 + k * 0.45;
                double spd = 340 + k * 35;
                Vec2 v = new Vec2(Math.Cos(va) * spd, Math.Sin(va) * spd);
                seeds.Add((p, v, t));
            }
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

        foreach (var (p, v, t) in seeds)
        {
            var sb = canvas.Sim.AddBall(p, v, 11.5, hue: -1, argb: Teams[t].BallArgb);
            ballTeam[sb] = t;
            PaintDisk(p, 11.5 + paintBrushBonus, t);
        }
        SetPaused(false);
    }

    private void InitTerritoryGrid(List<Vec2> outerPoly, bool startNeutral)
    {
        Array.Clear(owner);
        Array.Clear(teamCells);
        totalArenaCells = 0;

        for (int gy = 0; gy < GridH; gy++)
        {
            double wy = gy * CellPx + CellPx * 0.5;
            for (int gx = 0; gx < GridW; gx++)
            {
                double wx = gx * CellPx + CellPx * 0.5;
                var wp = new Vec2(wx, wy);
                if (!Simulation.PointInPoly(wp, outerPoly))
                {
                    owner[gy * GridW + gx] = 0;
                    continue;
                }
                totalArenaCells++;
                if (startNeutral)
                {
                    owner[gy * GridW + gx] = 5;
                }
                else
                {
                    int team;
                    if (activeTeamCount == 2)
                    {
                        team = wx < center.X ? 0 : 1;
                    }
                    else
                    {
                        double ang = Math.Atan2(wy - center.Y, wx - center.X);
                        if (ang < 0) ang += Math.PI * 2.0;
                        team = (int)Math.Floor(ang / (Math.PI * 2.0 / activeTeamCount)) % activeTeamCount;
                    }
                    owner[gy * GridW + gx] = (byte)(team + 1);
                    teamCells[team]++;
                }
            }
        }
        dirtyBmp = true;
    }

    private Vec2 ClampInside(Vec2 p) => ClampInside(p, canvas.NewBallR);

    private Vec2 ClampInside(Vec2 p, double r)
    {
        if (presetIndex == 0 || presetIndex == 2)
            return new Vec2(Math.Clamp(p.X, center.X - 242 + r, center.X + 242 - r), Math.Clamp(p.Y, center.Y - 242 + r, center.Y + 242 - r));
        if (presetIndex == 3)
            return new Vec2(Math.Clamp(p.X, 305 + r, 975 - r), Math.Clamp(p.Y, 112 + r, 588 - r));
        var d = p - center;
        double lim = arenaR - r - 18;
        return d.Len > lim ? center + d.Norm() * lim : p;
    }

    private void SpawnTeamVolley()
    {
        if (winnerTeam >= 0) return;
        for (int t = 0; t < activeTeamCount; t++)
        {
            if (canvas.Sim.Balls.Count >= maxBalls) break;
            double ang = simTime * 1.4 + t * (Math.PI * 2.0 / activeTeamCount);
            var pos = ClampInside(new Vec2(center.X + Math.Cos(ang) * 95, center.Y + Math.Sin(ang) * 95), 11.5);
            var vel = new Vec2(Math.Cos(ang + 1.1) * 360, Math.Sin(ang + 1.1) * 360);
            var sb = canvas.Sim.AddBall(pos, vel, 11.5, hue: -1, argb: Teams[t].BallArgb);
            ballTeam[sb] = t;
        }
        canvas.Invalidate();
    }

    private void OnBallPlaced(SimBall b)
    {
        int t = dropTeamCursor++ % activeTeamCount;
        b.R = 11.5;
        b.Argb = Teams[t].BallArgb;
        double a = simTime * 2.3 + t * 1.7;
        b.Vel = new Vec2(Math.Cos(a) * 350, Math.Sin(a) * 350);
        ballTeam[b] = t;
        PaintDisk(b.Pos, b.R + paintBrushBonus, t);
        while (canvas.Sim.Balls.Count > maxBalls)
        {
            var removed = canvas.Sim.Balls[0];
            ballTeam.Remove(removed);
            canvas.Sim.Balls.RemoveAt(0);
        }
    }

    private void OnBallsCollided(SimBall a, SimBall b, Vec2 pt)
    {
        if (!ballTeam.TryGetValue(a, out int ta) || !ballTeam.TryGetValue(b, out int tb)) return;
        if (ta == tb) return;
        waves.Add(new SplashWave
        {
            X = (float)pt.X, Y = (float)pt.Y,
            R0 = 8f, R1 = 44f, Age = 0f, Life = 0.34f,
            Col = Color.FromArgb(Teams[ta].BallArgb),
        });
    }

    private int PaintDisk(Vec2 pos, double radiusPx, int team)
    {
        int gx0 = Math.Max(0, (int)Math.Floor((pos.X - radiusPx) / CellPx));
        int gx1 = Math.Min(GridW - 1, (int)Math.Ceiling((pos.X + radiusPx) / CellPx));
        int gy0 = Math.Max(0, (int)Math.Floor((pos.Y - radiusPx) / CellPx));
        int gy1 = Math.Min(GridH - 1, (int)Math.Ceiling((pos.Y + radiusPx) / CellPx));
        double r2 = radiusPx * radiusPx;
        byte target = (byte)(team + 1);
        int painted = 0;

        for (int gy = gy0; gy <= gy1; gy++)
        {
            double dy = gy * CellPx + CellPx * 0.5 - pos.Y;
            double dy2 = dy * dy;
            int row = gy * GridW;
            for (int gx = gx0; gx <= gx1; gx++)
            {
                byte cur = owner[row + gx];
                if (cur == 0 || cur == target) continue;
                double dx = gx * CellPx + CellPx * 0.5 - pos.X;
                if (dx * dx + dy2 > r2) continue;

                if (cur >= 1 && cur <= 4) teamCells[cur - 1]--;
                owner[row + gx] = target;
                teamCells[team]++;
                painted++;
            }
        }
        if (painted > 0) dirtyBmp = true;
        return painted;
    }

    private void OnSimulated(double dt)
    {
        simTime += dt;
        var balls = canvas.Sim.Balls;

        // Закрашивание клеток под шарами и поддержание темпа игры
        for (int bi = 0; bi < balls.Count; bi++)
        {
            var b = balls[bi];
            if (!ballTeam.TryGetValue(b, out int team)) continue;
            int painted = PaintDisk(b.Pos, b.R + paintBrushBonus, team);
            double spd = b.Vel.Len;
            if (painted > 0 && boostOnPaint > 1.01 && spd < 580)
            {
                double mul = Math.Min(1.12, 1.0 + (boostOnPaint - 1.0) * 0.08);
                b.Vel *= mul;
            }
            if (spd < 230)
                b.Vel = spd > 1e-3 ? b.Vel * (255.0 / spd) : new Vec2(255, 110);
            else if (spd > 640 && !canvas.Sim.GravityOn)
                b.Vel *= (640.0 / spd);
        }

        // Активация бонусов
        if (orbsEnabled)
        {
            for (int oi = 0; oi < orbs.Count; oi++)
            {
                var orb = orbs[oi];
                if (orb.Cooldown > 0) { orb.Cooldown = Math.Max(0, orb.Cooldown - dt); continue; }
                orb.Flash = Math.Max(0, orb.Flash - dt * 3.0);
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    if ((b.Pos - orb.Pos).Len2 > (b.R + orb.R) * (b.R + orb.R)) continue;
                    if (!ballTeam.TryGetValue(b, out int team)) continue;

                    orb.Cooldown = orbCooldownDur;
                    orb.Flash = 1.0;
                    Color tCol = Color.FromArgb(Teams[team].BallArgb);
                    if (orb.IsBomb)
                    {
                        PaintDisk(orb.Pos, 66.0 + paintBrushBonus, team);
                        waves.Add(new SplashWave { X = (float)orb.Pos.X, Y = (float)orb.Pos.Y, R0 = 14f, R1 = 92f, Age = 0f, Life = 0.52f, Col = tCol });
                    }
                    else if (balls.Count < maxBalls)
                    {
                        var perp = new Vec2(-b.Vel.Y, b.Vel.X);
                        var cloneVel = perp.Len > 1 ? perp.Norm() * Math.Max(280, b.Vel.Len) : new Vec2(300, -180);
                        var clone = canvas.Sim.AddBall(b.Pos, cloneVel, b.R, hue: -1, argb: Teams[team].BallArgb);
                        ballTeam[clone] = team;
                        waves.Add(new SplashWave { X = (float)orb.Pos.X, Y = (float)orb.Pos.Y, R0 = 12f, R1 = 56f, Age = 0f, Life = 0.42f, Col = tCol });
                    }
                    break;
                }
            }
        }

        // Проверка победы
        if (winnerTeam < 0 && totalArenaCells > 0)
        {
            for (int t = 0; t < activeTeamCount; t++)
            {
                if (teamCells[t] >= totalArenaCells * 0.965)
                {
                    winnerTeam = t;
                    canvas.SpawnGoalConfetti(center);
                    break;
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

    private void EnsureTerritoryBmp()
    {
        if (territoryBmp == null)
        {
            territoryBmp = new Bitmap(GridW, GridH, PixelFormat.Format32bppArgb);
            dirtyBmp = true;
        }
        if (!dirtyBmp) return;
        dirtyBmp = false;

        int t0 = Teams[0].TileArgb;
        int t1 = Teams[1].TileArgb;
        int t2 = Teams[2].TileArgb;
        int t3 = Teams[3].TileArgb;
        const int neutralArgb = unchecked((int)0x281C2438);

        var rect = new Rectangle(0, 0, GridW, GridH);
        var bd = territoryBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* basePtr = (byte*)bd.Scan0;
                int stride = bd.Stride;
                for (int gy = 0; gy < GridH; gy++)
                {
                    int* dst = (int*)(basePtr + gy * stride);
                    int row = gy * GridW;
                    for (int gx = 0; gx < GridW; gx++)
                    {
                        byte st = owner[row + gx];
                        dst[gx] = st switch
                        {
                            1 => t0,
                            2 => t1,
                            3 => t2,
                            4 => t3,
                            5 => neutralArgb,
                            _ => 0,
                        };
                    }
                }
            }
        }
        finally
        {
            territoryBmp.UnlockBits(bd);
        }
    }

    private void OnRenderUnderlay(Graphics g, LevelData lvl)
    {
        EnsureTerritoryBmp();
        var prevMode = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(territoryBmp, 0, 0, (float)LevelData.W, (float)LevelData.H);
        g.InterpolationMode = prevMode;

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
    }

    private void OnRenderOverlay(Graphics g, LevelData lvl)
    {
        if (orbsEnabled)
        {
            for (int i = 0; i < orbs.Count; i++)
            {
                var orb = orbs[i];
                if (orb.Cooldown > 0) continue;
                Color col = orb.IsBomb ? Theme.Orange : Theme.Violet;
                float r = (float)(orb.R + Math.Sin(simTime * 5.0 + i) * 1.4);
                float x = (float)orb.Pos.X, y = (float)orb.Pos.Y;
                g.FillEllipse(Theme.BrushOf(Color.FromArgb(205, col)), x - r, y - r, r * 2f, r * 2f);
                g.DrawEllipse(Theme.PenOf(Color.White, 1.8f), x - r, y - r, r * 2f, r * 2f);
            }
        }

        for (int i = 0; i < waves.Count; i++)
        {
            var w = waves[i];
            float u = Math.Clamp(w.Age / w.Life, 0f, 1f);
            float r = w.R0 + (w.R1 - w.R0) * u;
            int a = Math.Clamp((int)((1f - u) * 210f), 0, 220);
            g.DrawEllipse(Theme.PenOf(Color.FromArgb(a, w.Col), 2.4f), w.X - r, w.Y - r, r * 2f, r * 2f);
        }
    }

    private void OnRenderScreenOverlay(Graphics g)
    {
        if (orbsEnabled)
        {
            for (int i = 0; i < orbs.Count; i++)
            {
                var orb = orbs[i];
                if (orb.Cooldown > 0) continue;
                var sp = canvas.WorldToScreen(orb.Pos);
                g.DrawString(orb.IsBomb ? "Б" : "×2", orbFont, Theme.BrushOf(Color.FromArgb(14, 16, 26)), sp.X, sp.Y, CenterFmt);
            }
        }

        // Полоса захвата территории + процентное соотношение команд
        float barW = Math.Min(560f, Math.Max(260f, canvas.ClientSize.Width * 0.48f));
        float barH = 32f;
        float bx = (canvas.ClientSize.Width - barW) * 0.5f;
        float by = canvas.ClientSize.Height - barH - 12f;
        var pillRect = new Rectangle((int)bx, (int)by, (int)barW, (int)barH);
        Theme.FillDrawRoundRect(g, pillRect, 9, hudBgBrush, hudBorderPen);

        // Цветная шкала долей сверху капсулы
        float meterX = bx + 10f, meterY = by + 5f, meterW = barW - 20f, meterH = 6f;
        float cx = meterX;
        double denom = Math.Max(1, totalArenaCells);
        for (int t = 0; t < activeTeamCount; t++)
        {
            float frac = (float)(teamCells[t] / denom);
            float segW = t == activeTeamCount - 1 ? Math.Max(0f, meterX + meterW - cx) : meterW * frac;
            if (segW >= 1f)
                g.FillRectangle(Theme.BrushOf(Color.FromArgb(Teams[t].BallArgb)), cx, meterY, segW, meterH);
            cx += segW;
        }

        // Текстовый счёт под шкалой
        string score = winnerTeam >= 0
            ? $"ПОБЕДА: {Teams[winnerTeam].Name}!   ·   R — реванш"
            : BuildScoreText();
        g.DrawString(score, hudFont, hudTextBrush, bx + barW * 0.5f, by + 21f, CenterFmt);
    }

    private string BuildScoreText()
    {
        double denom = Math.Max(1, totalArenaCells);
        var sb = new System.Text.StringBuilder(64);
        for (int t = 0; t < activeTeamCount; t++)
        {
            if (t > 0) sb.Append("  ·  ");
            sb.Append(Teams[t].Name).Append(' ').Append((teamCells[t] * 100.0 / denom).ToString("0")).Append('%');
        }
        return sb.ToString();
    }
}

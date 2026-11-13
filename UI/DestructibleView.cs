using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Разрушение» (Clips 4A, 4B и 5):
/// — Спиральные полярные блоки с центральным порталом (Clip 4A);
/// — Спираль-бур с разрушаемой радужной массой и сферами xN (Clip 4B);
/// — Полукруг-бур с концентрическими U-каналами и множителями x2..x6 (Clip 5).
/// </summary>
public class DestructibleView : UserControl, IModeView
{
    public const int PresetCount = 10;

    private static readonly string[] PresetNames =
    {
        "Полукруг-бур + xN",
        "Спираль-бур + xN",
        "Спиральные блоки",
        "Спиральные блоки ×2",
        "Радужный монолит + xN",
        "Сердце из стекла + xN",
        "Спиральные блоки · 8 витков",
        "Слоёный пинбол + xN",
        "Звезда-лабиринт + xN",
        "Кольца сатурна + xN",
    };

    private static readonly string[] PresetBadges =
    {
        "БУР · xN", "СПИРАЛЬ", "БЛОКИ ×1", "БЛОКИ ×2", "МОНОЛИТ",
        "СЕРДЦЕ", "8 ВИТКОВ", "ПИНБОЛ", "ЗВЕЗДА", "КОЛЬЦА",
    };

    private sealed class MultiplierOrb
    {
        public Vec2 Pos;
        public double R;
        public int Value;
        public bool IsRepeatable;
        public bool Collected;
        public double Cooldown;
        public double Flash;
        public int PaletteIdx;
    }

    private struct OrbWave
    {
        public float X, Y;
        public float R0, R1, Age, Life;
        public Color Col;
    }

    private struct PendingOrbSpawn
    {
        public double Delay;
        public Vec2 Pos;
        public Vec2 Vel;
        public double Radius;
        public int Argb;
        public MultiplierOrb SourceOrb;
    }

    private struct BallOrbLock
    {
        public SimBall Ball;
        public MultiplierOrb Orb;
        public double Until;
    }

    private readonly Toolbar bar = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnPrev = new() { Icon = IconPainter.ChevronLeft, Size = new Size(34, 34), IconPx = 18 };
    private readonly LevelSelectButton lblPreset = new() { Size = new Size(240, 34), Role = AccentRole.Orange };
    private readonly NeoButton btnNext = new() { Icon = IconPainter.ChevronRight, Size = new Size(34, 34), IconPx = 18 };
    private readonly NeoButton btnAddBall = new() { Text = "ШАР", Icon = IconPainter.Plus, Role = AccentRole.Green, Size = new Size(74, 34), IconPx = 15, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly NeoButton btnOrbsToggle = new() { Text = "МНОЖИТЕЛИ", IsToggle = true, IsOn = true, Role = AccentRole.Orange, Size = new Size(98, 34), Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
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
        GoalCelebrationOn = true,
    };

    public GameCanvas Canvas => canvas;

    private int presetIndex;
    private bool orbsEnabled = true;
    private bool glassVanishOn = true;
    private bool autoSpawnOn = true;
    private double spawnInterval = 1.5;
    private double autoSpawnAccum;
    private int maxBalls = 48;
    private double carveRadius = 8.5;
    private double wallRestitution = 0.38;
    private double terrainRestitution = 0.42;
    private double ballRestitution = 0.72;
    private double gravityScale = 0.78;
    private double airDrag = 0.04;
    private double minSpeedFloor;
    private Vec2 emitterPos = new(640, 200);
    private Vec2 emitterVel = new(45, 190);
    private bool wasPaused;
    private double simTime;
    private bool won;
    private int ballColorIdx;
    private int emitSeq;
    private Random rng = new(77);

    public Vec2 EmitterPos => emitterPos;
    public bool GlassVanishOn { get => glassVanishOn; set => glassVanishOn = value; }
    public bool AutoSpawnOn { get => autoSpawnOn; set => autoSpawnOn = value; }
    public double SpawnInterval { get => spawnInterval; set { spawnInterval = Math.Clamp(value, 0.1, 5.0); autoSpawnAccum = Math.Min(autoSpawnAccum, spawnInterval); } }
    public double WallRestitution { get => wallRestitution; set { wallRestitution = Math.Clamp(value, 0.05, 1.35); terrainRestitution = Math.Min(1.2, wallRestitution + 0.04); ApplySandboxPhysics(); } }

    private readonly Vec2 center = new(640, 360);
    private double arenaR = 308;

    private DestructibleBitmapTerrain terrain;
    private Bitmap terrainBmp;
    private PolarSpiralGrid spiralGrid;
    private readonly List<MultiplierOrb> orbs = new();
    private readonly List<OrbWave> orbWaves = new();
    private readonly List<PendingOrbSpawn> pendingOrbSpawns = new();
    private readonly List<BallOrbLock> ballOrbLocks = new();
    private readonly List<(PointF[] Pts, bool Closed)> wallPtCache = new();
    private readonly Dictionary<SimBall, double> terrainHitCooldown = new();

    // Кеш перьев и кистей — 0 аллокаций на кадр
    private readonly SolidBrush tileFillBrush = new(Color.FromArgb(145, 22, 92, 132));
    private readonly Pen tileBorderPen = new(Color.FromArgb(210, 82, 215, 255), 1.15f);
    private readonly Pen rainbowGlowPen = new(Color.FromArgb(72, Color.Cyan), 8.0f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly Pen rainbowCorePen = new(Color.Cyan, 2.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly Pen laneGlowPen = new(Color.FromArgb(62, 110, 235, 255), 7.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly Pen laneCorePen = new(Color.FromArgb(240, 128, 240, 255), 2.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly SolidBrush orbFillBrush = new(Color.Cyan);
    private readonly Pen orbBorderPen = new(Color.White, 2.0f);
    private readonly Font orbFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly SolidBrush orbTextBrush = new(Color.FromArgb(250, 10, 14, 24));
    private readonly SolidBrush wedgeBrush = new(Color.FromArgb(245, 245, 250, 255));
    private readonly Pen vortexOuterPen = new(Color.FromArgb(180, 246, 100, 220), 2.4f);
    private readonly SolidBrush vortexCoreBrush = new(Color.FromArgb(240, 14, 6, 22));
    private readonly Pen wavePen = new(Color.White, 2.2f);
    private readonly Font hudFont = new("Segoe UI", 10F, FontStyle.Bold);
    private readonly SolidBrush hudBgBrush = new(Color.FromArgb(180, 10, 13, 22));
    private readonly SolidBrush hudTextBrush = new(Color.FromArgb(236, 242, 255));
    private readonly Pen hudBorderPen = new(Color.FromArgb(95, 110, 165, 255), 1.1f);
    private readonly PointF[] wedgePts = new PointF[3];
    private static readonly StringFormat CenterFmt = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    public int PresetIndex => presetIndex;
    public int RemainingTilesOrCells => spiralGrid != null ? spiralGrid.AliveCount : (terrain?.SolidCount ?? 0);
    public double ClearedRatio => spiralGrid != null
        ? (spiralGrid.TotalCount > 0 ? 1.0 - (double)spiralGrid.AliveCount / spiralGrid.TotalCount : 1.0)
        : (terrain?.ClearedRatio ?? 1.0);
    public bool IsCompleted => won;

    public DestructibleView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        bar.AddLeft(btnPrev);
        bar.AddLeft(lblPreset);
        bar.AddLeft(btnNext);
        bar.AddLeft(btnAddBall);
        bar.AddLeft(btnOrbsToggle);
        bar.AddLeft(btnSandbox);
        bar.AddRight(btnPause);
        bar.AddRight(btnRestart);

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий режим разрушения");
        tips.SetToolTip(lblPreset, "Выбрать сцену разрушения из сетки с мини-превью");
        tips.SetToolTip(btnNext, "Следующий режим разрушения");
        tips.SetToolTip(btnAddBall, "Выпустить шар из точки спавна (+)");
        tips.SetToolTip(btnOrbsToggle, "Включить/выключить сферы-множители (x2, x3, ...)");
        tips.SetToolTip(btnSandbox, "Настройка физики, спавна и хрупкости стекла (Песочница)");
        tips.SetToolTip(btnRestart, "Восстановить уровень заново (R)");
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
                    Role = AccentRole.Orange,
                    RotorStyle = true,
                });
            }
            return (presetIndex, items);
        };
        lblPreset.LevelSelected += ShowPreset;

        btnPrev.Click += (_, _) => ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
        btnNext.Click += (_, _) => ShowPreset((presetIndex + 1) % PresetCount);
        btnAddBall.Click += (_, _) => SpawnExtraBall();
        btnOrbsToggle.Click += (_, _) => { orbsEnabled = btnOrbsToggle.IsOn; ShowPreset(presetIndex); };
        SandboxPopup.Attach(btnSandbox, "Песочница · Разрушение", BuildSandboxEntries);
        btnRestart.Click += (_, _) => ShowPreset(presetIndex);
        btnPause.Click += (_, _) => SetPaused(btnPause.IsOn);

        canvas.ClampDropPos = ClampDropInside;
        canvas.Simulated += OnSimulated;
        canvas.BallPlaced += OnBallPlaced;
        canvas.Sim.GoalReached += (_, c) =>
        {
            if (!won)
            {
                won = true;
                canvas.SpawnGoalConfetti(c);
            }
        };
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
            canvas.Simulated -= OnSimulated;
            canvas.BallPlaced -= OnBallPlaced;
            canvas.RenderUnderlay -= OnRenderUnderlay;
            canvas.RenderOverlay -= OnRenderOverlay;
            canvas.RenderScreenOverlay -= OnRenderScreenOverlay;
            terrainBmp?.Dispose();
            tileFillBrush.Dispose();
            tileBorderPen.Dispose();
            rainbowGlowPen.Dispose();
            rainbowCorePen.Dispose();
            laneGlowPen.Dispose();
            laneCorePen.Dispose();
            orbFillBrush.Dispose();
            orbBorderPen.Dispose();
            orbFont.Dispose();
            orbTextBrush.Dispose();
            wedgeBrush.Dispose();
            vortexOuterPen.Dispose();
            vortexCoreBrush.Dispose();
            wavePen.Dispose();
            hudFont.Dispose();
            hudBgBrush.Dispose();
            hudTextBrush.Dispose();
            hudBorderPen.Dispose();
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
            case Keys.Oemplus:
            case Keys.Add:
                SpawnExtraBall();
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

    private void EnsureTerrainBmp()
    {
        terrainBmp ??= new Bitmap(DestructibleBitmapTerrain.GridW, DestructibleBitmapTerrain.GridH, PixelFormat.Format32bppPArgb);
    }

    private bool defaultsLoaded;

    private void LoadPresetDefaults(int p)
    {
        wallRestitution = 0.38;
        terrainRestitution = 0.42;
        ballRestitution = 0.72;
        airDrag = 0.04;
        gravityScale = (p == 2 || p == 3 || p == 6) ? 0.36 : 0.78;
        minSpeedFloor = (p == 0 || p == 1) ? 190.0 : 0.0;
        glassVanishOn = true;
        autoSpawnOn = true;
        spawnInterval = 1.5;
        maxBalls = 48;
        carveRadius = 8.5;
    }

    private void ApplySandboxPhysics()
    {
        canvas.Sim.GravityScale = gravityScale;
        canvas.Sim.WallRestitution = wallRestitution;
        canvas.Sim.BallRestitution = ballRestitution;
        canvas.Sim.AirDrag = airDrag;
        canvas.Design.GravityScale = gravityScale;
        canvas.Design.WallRestitution = wallRestitution;
        canvas.Design.BallRestitution = ballRestitution;
        canvas.Design.AirDrag = airDrag;
        canvas.Invalidate();
    }

    internal List<SandboxEntry> BuildSandboxEntries()
    {
        return new List<SandboxEntry>
        {
            new SandboxToggle { Label = "Шар исчезает о «стекло»", Get = () => glassVanishOn, Set = v => { glassVanishOn = v; } },
            new SandboxToggle { Label = "Автоспавн из одной точки", Get = () => autoSpawnOn, Set = v => { autoSpawnOn = v; autoSpawnAccum = 0; } },
            new SandboxToggle { Label = "Сферы-множители (xN)", Get = () => orbsEnabled, Set = v => { orbsEnabled = v; btnOrbsToggle.IsOn = v; btnOrbsToggle.Invalidate(); ShowPreset(presetIndex); } },
            new SandboxSlider { Label = "Интервал спавна (с)", Min = 0.1, Max = 5.0, Step = 0.05, Format = v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Get = () => spawnInterval, Set = v => { spawnInterval = Math.Clamp(v, 0.1, 5.0); autoSpawnAccum = Math.Min(autoSpawnAccum, spawnInterval); } },
            new SandboxSlider { Label = "Лимит шаров", Min = 4, Max = 120, Step = 1, Get = () => maxBalls, Set = v => maxBalls = (int)Math.Round(v) },
            new SandboxSlider { Label = "Сила пролома (px)", Min = 2.0, Max = 28.0, Step = 0.5, Get = () => carveRadius, Set = v => carveRadius = v },
            new SandboxSlider { Label = "Упругость стен", Min = 0.05, Max = 1.25, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => wallRestitution, Set = v => { wallRestitution = v; terrainRestitution = Math.Min(1.2, v + 0.04); ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Упругость шаров", Min = 0.10, Max = 1.25, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => ballRestitution, Set = v => { ballRestitution = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Гравитация", Min = 0.0, Max = 2.5, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => gravityScale, Set = v => { gravityScale = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Сопротивление воздуха", Min = 0.0, Max = 0.5, Step = 0.01, Get = () => airDrag, Set = v => { airDrag = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Мин. скорость шара", Min = 0.0, Max = 650.0, Step = 10.0, Get = () => minSpeedFloor, Set = v => minSpeedFloor = v },
            new SandboxSlider { Label = "Радиус шара", Min = 4.0, Max = 26.0, Step = 0.5, Format = v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), Get = () => canvas.NewBallR, Set = v => { canvas.NewBallR = v; foreach (var b in canvas.Sim.Balls) b.R = v; canvas.Invalidate(); } },
            new SandboxSlider { Label = "Скорость симуляции", Min = 0.1, Max = 4.0, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => GameCanvas.SimSpeed, Set = v => { GameCanvas.SimSpeed = v; canvas.Invalidate(); } },
            new SandboxAction { Label = "+ Шар из спавна", Role = AccentRole.Green, RefreshAllAfter = false, Run = SpawnExtraBall },
            new SandboxAction { Label = "Убрать все шары", Role = AccentRole.Violet, RefreshAllAfter = false, Run = () => { canvas.Sim.Balls.Clear(); pendingOrbSpawns.Clear(); ballOrbLocks.Clear(); terrainHitCooldown.Clear(); canvas.Invalidate(); } },
            new SandboxAction { Label = "Сбросить режим", Role = AccentRole.Orange, Run = () => { GameCanvas.SimSpeed = 1.0; LoadPresetDefaults(presetIndex); ShowPreset(presetIndex); } },
        };
    }

    private LevelData CreatePreviewLevel(int p)
    {
        var lvl = new LevelData { Name = PresetNames[Math.Clamp(p, 0, PresetCount - 1)] };
        const double r = 308;
        lvl.Circles.Add(new CircleData { Center = center, Radius = r });
        if (p == 0)
        {
            var pts = new List<Vec2>();
            for (int i = 0; i <= 24; i++)
            {
                double a = Math.PI - Math.PI * i / 24.0;
                pts.Add(new Vec2(center.X + Math.Cos(a) * 190, center.Y + Math.Sin(a) * 190));
            }
            lvl.Walls.Add(new PolyData { Closed = false, Pts = pts });
        }
        else if (p == 1 || p == 2 || p == 3 || p == 6)
        {
            int t = p == 6 ? 8 : (p == 1 ? 4 : 6);
            lvl.Walls.AddRange(PolarSpiralGrid.BuildSpiralWalls(center, 52, r, t));
            lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(center, 27, 16, 0) });
        }
        else if (p == 5)
        {
            var heart = new PolyData { Closed = true };
            for (int i = 0; i < 48; i++)
            {
                double t = i * Math.PI * 2.0 / 48.0;
                double hx = 16 * Math.Pow(Math.Sin(t), 3) * 12.0;
                double hy = -(13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t)) * 12.0;
                heart.Pts.Add(new Vec2(center.X + hx, center.Y + hy + 18));
            }
            lvl.Walls.Add(heart);
        }
        else if (p == 7)
        {
            lvl.Walls.Add(new PolyData { Closed = false, Pts = new() { new(center.X - 260, center.Y - 60), new(center.X + 260, center.Y - 60) } });
            lvl.Walls.Add(new PolyData { Closed = false, Pts = new() { new(center.X - 240, center.Y + 40), new(center.X + 240, center.Y + 40) } });
            lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(new Vec2(center.X, center.Y + 195), 28, 16, 0) });
        }
        else if (p == 8)
        {
            var star = new PolyData { Closed = true };
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2.0 + i * Math.PI / 5.0;
                double rr = (i % 2 == 0) ? 235.0 : 112.0;
                star.Pts.Add(center + new Vec2(Math.Cos(a) * rr, Math.Sin(a) * rr));
            }
            lvl.Walls.Add(star);
        }
        else if (p == 9)
        {
            lvl.Arcs.Add(new ArcData { Center = center, Radius = 215, StartDeg = 0, SweepDeg = 359, HalfWidth = 4 });
            lvl.Arcs.Add(new ArcData { Center = center, Radius = 140, StartDeg = 0, SweepDeg = 359, HalfWidth = 4 });
            lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(center, 28, 16, 0) });
        }
        return lvl;
    }

    public void ShowPreset(int index)
    {
        int clamped = Math.Clamp(index, 0, PresetCount - 1);
        if (!defaultsLoaded || clamped != presetIndex)
        {
            LoadPresetDefaults(clamped);
            defaultsLoaded = true;
        }
        presetIndex = clamped;
        simTime = 0;
        autoSpawnAccum = 0;
        emitSeq = 0;
        won = false;
        ballColorIdx = 0;
        rng = new Random(509 + presetIndex * 97);
        orbs.Clear();
        orbWaves.Clear();
        pendingOrbSpawns.Clear();
        ballOrbLocks.Clear();
        wallPtCache.Clear();
        terrainHitCooldown.Clear();

        var lvl = new LevelData
        {
            Border = false,
            Gravity = true,
            GravityScale = gravityScale,
            WallRestitution = wallRestitution,
            BallRestitution = ballRestitution,
            AirDrag = airDrag,
            NoBallCollisions = presetIndex == 2 || presetIndex == 3 || presetIndex == 6,
        };
        arenaR = 308;

        switch (presetIndex)
        {
            case 0: // Полукруг-бур + xN (Clip 5)
                BuildHalfCirclePreset(lvl);
                emitterPos = new Vec2(center.X - 278, center.Y - 58);
                emitterVel = new Vec2(-64, 340);
                break;
            case 1: // Спираль-бур + xN (Clip 4B)
                BuildSpiralTerrainPreset(lvl);
                emitterPos = new Vec2(center.X + 270, center.Y - 36);
                emitterVel = new Vec2(-135, -245);
                break;
            case 2: // Спиральные блоки (Clip 4A)
                BuildSpiralGridPreset(lvl, turns: 6, twoBalls: false);
                emitterPos = new Vec2(center.X + 272, center.Y - 62);
                emitterVel = new Vec2(-140, -310);
                break;
            case 3: // Спиральные блоки x2
                BuildSpiralGridPreset(lvl, turns: 6, twoBalls: true);
                emitterPos = new Vec2(center.X + 272, center.Y - 62);
                emitterVel = new Vec2(-165, -325);
                break;
            case 4: // Радужный монолит + xN
                BuildMonolithPreset(lvl);
                emitterPos = new Vec2(center.X, center.Y - 252);
                emitterVel = new Vec2(28, 175);
                break;
            case 5: // Сердце из стекла + xN
                BuildHeartPreset(lvl);
                emitterPos = new Vec2(center.X, center.Y - 266);
                emitterVel = new Vec2(32, 165);
                break;
            case 6: // Спиральные блоки · 8 витков
                BuildSpiralGridPreset(lvl, turns: 8, twoBalls: true);
                emitterPos = new Vec2(center.X + 285, center.Y - 72);
                emitterVel = new Vec2(-145, -365);
                break;
            case 7: // Слоёный пинбол + xN
                BuildLayeredPinballPreset(lvl);
                emitterPos = new Vec2(center.X, center.Y - 264);
                emitterVel = new Vec2(45, 160);
                break;
            case 8: // Звезда-лабиринт + xN
                BuildStarMazePreset(lvl);
                emitterPos = new Vec2(center.X, center.Y - 262);
                emitterVel = new Vec2(-36, 170);
                break;
            default: // Кольца сатурна + xN
                BuildRingsTargetPreset(lvl);
                emitterPos = new Vec2(center.X, center.Y - 274);
                emitterVel = new Vec2(38, 165);
                break;
        }

        lvl.Name = $"{presetIndex + 1}/{PresetCount} · {PresetNames[presetIndex]}";
        lblPreset.Text = lvl.Name;

        // Кеш точек полилиний стенок для быстрого неонового рендера без аллокаций
        foreach (var w in lvl.Walls)
        {
            var pts = new PointF[w.Pts.Count];
            for (int i = 0; i < w.Pts.Count; i++)
                pts[i] = new PointF((float)w.Pts[i].X, (float)w.Pts[i].Y);
            wallPtCache.Add((pts, w.Closed));
        }

        canvas.Trails = false;
        canvas.LoadLevel(lvl);
        SpawnInitialBalls();
        SetPaused(false);
        bar.Relayout();
        canvas.Invalidate();
    }

    private void BuildHalfCirclePreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });

        // 5 концентрических U-каналов в нижней полусфере (радиусы от 256 внутрь до 76)
        double[] radii = { 256, 211, 166, 121, 76 };
        double[] endAngles = { -0.18, -0.15, -0.15, -0.10, -0.10 };
        for (int k = 0; k < radii.Length; k++)
        {
            double r = radii[k];
            double aStart = Math.PI;
            double aEnd = endAngles[k];
            int steps = 42;
            var pts = new List<Vec2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double u = (double)i / steps;
                double a = aStart + (aEnd - aStart) * u;
                pts.Add(new Vec2(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r));
            }
            lvl.Walls.Add(new PolyData { Closed = false, Pts = pts, Pivot = center });
        }

        // Закругляем правые U-развороты между парными рельсами (1-2 и 3-4)
        void AddRightCap(double rOut, double rIn, double ang)
        {
            double rMid = (rOut + rIn) * 0.5;
            double rCap = (rOut - rIn) * 0.5;
            var capCenter = new Vec2(center.X + Math.Cos(ang) * rMid, center.Y + Math.Sin(ang) * rMid);
            var capPts = new List<Vec2>(13);
            for (int i = 0; i <= 12; i++)
            {
                double ta = ang - (i / 12.0) * Math.PI; // от внешнего рельса через верхнюю дугу к внутреннему
                capPts.Add(capCenter + new Vec2(Math.Cos(ta), Math.Sin(ta)) * rCap);
            }
            lvl.Walls.Add(new PolyData { Closed = false, Pts = capPts, Pivot = capCenter });
        }
        AddRightCap(radii[1], radii[2], endAngles[1]);
        AddRightCap(radii[3], radii[4], endAngles[3]);

        // Правая половина заполнена разрушаемой радужной массой (x >= center.X)
        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            if (dx < 0) return false;
            return dx * dx + dy * dy <= (arenaR - 5) * (arenaR - 5);
        });

        // Расчищаем нижне-левый вход к x2 и небольшие ячейки вокруг x2..x6
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 38, center.Y + 281), R = 18, Value = 2, IsRepeatable = true, PaletteIdx = 4 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 24, center.Y + 188), R = 16, Value = 3, IsRepeatable = false, PaletteIdx = 20 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 18, center.Y + 143), R = 15, Value = 4, IsRepeatable = false, PaletteIdx = 14 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 12, center.Y + 98), R = 14, Value = 5, IsRepeatable = false, PaletteIdx = 8 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 6, center.Y + 48), R = 13, Value = 6, IsRepeatable = false, PaletteIdx = 2 });

        // Слегка расчищаем стартовый путь до x2 и отсекаем узкие кармашки для самой границы
        terrain.CarveCircle(center.X - 20, center.Y + 281, 34, out _);
        canvas.NewBallR = 12;
    }

    private void BuildSpiralTerrainPreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        const int turns = 4;
        const double rMin = 62;
        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        lvl.Walls.AddRange(PolarSpiralGrid.BuildSpiralWalls(center, rMin, arenaR - 3, turns));

        // Центральная цель-воронка
        lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(center, 28, 24, 0) });

        // Радужная масса заполняет каналы спирали, оставляя свободным внешнее устье (верхне-правый карман) и ядро
        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= rMin + 4 || d >= arenaR - 7) return false;
            // Оставляем чистым стартовое окно в верхнем/нижнем внешнем витке
            if (d > arenaR - 64 && wx > center.X + 40 && wy > center.Y - 120 && wy < center.Y + 180)
                return false;
            return true;
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 35, center.Y + 268), R = 17, Value = 2, IsRepeatable = true, PaletteIdx = 6 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 150, center.Y + 92), R = 16, Value = 3, IsRepeatable = false, PaletteIdx = 20 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 20, center.Y + 132), R = 15, Value = 4, IsRepeatable = false, PaletteIdx = 10 });
        terrain.CarveCircle(orbs[0].Pos.X, orbs[0].Pos.Y, 30, out _);
        canvas.NewBallR = 11;
    }

    private void BuildSpiralGridPreset(LevelData lvl, int turns, bool twoBalls)
    {
        terrain = null;
        double rMin = turns >= 8 ? 46 : 52;
        arenaR = 312;

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR + 4 });
        lvl.Walls.AddRange(PolarSpiralGrid.BuildSpiralWalls(center, rMin, arenaR, turns));
        lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(center, 27, 24, 0) });

        double skip = turns >= 8 ? 0.56 : 0.55;
        spiralGrid = new PolarSpiralGrid(center, rMin, arenaR, turns, segsPerTurn: 48, startSkipRad: skip);
        canvas.NewBallR = turns >= 8 ? 9 : 12;
    }

    private void BuildMonolithPreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });

        // Центральный массивный радужный диск + пояс с зазорами наверху/снизу
        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            return d <= arenaR - 38 && wy >= center.Y - 110;
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X, center.Y - 195), R = 18, Value = 2, IsRepeatable = true, PaletteIdx = 4 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 115, center.Y + 60), R = 17, Value = 3, IsRepeatable = false, PaletteIdx = 18 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 115, center.Y + 60), R = 17, Value = 4, IsRepeatable = false, PaletteIdx = 12 });
        canvas.NewBallR = 13;
    }

    private void BuildHeartPreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        terrain.Build(center, (wx, wy) =>
        {
            double hx = (wx - center.X) / 145.0;
            double hy = -(wy - (center.Y + 12.0)) / 145.0;
            double a = hx * hx + hy * hy - 1.0;
            return a * a * a - hx * hx * (hy * hy * hy) <= 0.0;
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X, center.Y - 192), R = 17, Value = 2, IsRepeatable = true, PaletteIdx = 2 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 96, center.Y - 38), R = 15, Value = 3, IsRepeatable = false, PaletteIdx = 18 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 96, center.Y - 38), R = 15, Value = 4, IsRepeatable = false, PaletteIdx = 10 });
        terrain.CarveCircle(center.X - 96, center.Y - 38, 24, out _);
        terrain.CarveCircle(center.X + 96, center.Y - 38, 24, out _);
        canvas.NewBallR = 12;
    }

    private void BuildLayeredPinballPreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        lvl.Walls.Add(new PolyData { Closed = false, Pts = new List<Vec2> { new(center.X - 245, center.Y - 140), new(center.X - 90, center.Y - 110) } });
        lvl.Walls.Add(new PolyData { Closed = false, Pts = new List<Vec2> { new(center.X + 245, center.Y - 140), new(center.X + 90, center.Y - 110) } });
        lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(new Vec2(center.X, center.Y + 235), 28, 24, 0) });

        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            if (dx * dx + dy * dy > (arenaR - 14) * (arenaR - 14)) return false;
            return Math.Abs(dy + 62) <= 23 || Math.Abs(dy - 36) <= 23 || Math.Abs(dy - 134) <= 23;
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X, center.Y - 170), R = 17, Value = 2, IsRepeatable = true, PaletteIdx = 5 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 95, center.Y - 12), R = 15, Value = 3, IsRepeatable = false, PaletteIdx = 16 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 95, center.Y + 86), R = 15, Value = 4, IsRepeatable = false, PaletteIdx = 22 });
        canvas.NewBallR = 11;
    }

    private void BuildStarMazePreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 246) return false;
            double a = Math.Atan2(dy, dx) - Math.PI / 2.0;
            double wave = 0.5 + 0.5 * Math.Cos(5.0 * a);
            double edgeR = 118.0 + 124.0 * Math.Pow(wave, 1.45);
            return r <= edgeR;
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X, center.Y - 192), R = 17, Value = 2, IsRepeatable = true, PaletteIdx = 4 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 110, center.Y + 42), R = 15, Value = 3, IsRepeatable = false, PaletteIdx = 14 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 110, center.Y + 42), R = 15, Value = 4, IsRepeatable = false, PaletteIdx = 20 });
        terrain.CarveCircle(center.X - 110, center.Y + 42, 24, out _);
        terrain.CarveCircle(center.X + 110, center.Y + 42, 24, out _);
        canvas.NewBallR = 12;
    }

    private void BuildRingsTargetPreset(LevelData lvl)
    {
        spiralGrid = null;
        terrain = new DestructibleBitmapTerrain();
        EnsureTerrainBmp();

        lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
        lvl.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(center, 27, 24, 0) });

        terrain.Build(center, (wx, wy) =>
        {
            double dx = wx - center.X, dy = wy - center.Y;
            double r = Math.Sqrt(dx * dx + dy * dy);
            return (r >= 76 && r <= 110) || (r >= 138 && r <= 174) || (r >= 202 && r <= 238);
        });

        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X, center.Y - 258), R = 15, Value = 2, IsRepeatable = true, PaletteIdx = 6 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X - 188, center.Y), R = 14, Value = 3, IsRepeatable = false, PaletteIdx = 18 });
        orbs.Add(new MultiplierOrb { Pos = new Vec2(center.X + 124, center.Y), R = 14, Value = 4, IsRepeatable = false, PaletteIdx = 11 });
        canvas.NewBallR = 11;
    }

    private readonly List<PolarSpiralGrid.Tile> splashCache = new();

    private void SpawnInitialBalls()
    {
        if (presetIndex == 3 || presetIndex == 6)
        {
            SpawnFromEmitter(canvas.NewBallR);
            SpawnFromEmitter(canvas.NewBallR);
        }
        else
        {
            SpawnFromEmitter(canvas.NewBallR);
        }
    }

    private Vec2 NextEmitterVel()
    {
        int seq = emitSeq++;
        if (seq == 0) return emitterVel;
        var dir = emitterVel.Len > 1e-6 ? emitterVel.Norm() : new Vec2(0, 1);
        var perp = Vec2.Perp(dir);
        double spread = Math.Sin(seq * 1.6180339) * 18.0;
        return emitterVel + perp * spread;
    }

    private void SpawnFromEmitter(double radius)
    {
        var vel = NextEmitterVel();
        int argb = GameCanvas.SpectrumPalette24[(ballColorIdx++) % GameCanvas.SpectrumPalette24.Length];
        var b = canvas.Sim.AddBall(emitterPos, vel, radius, argb: argb);
        b.GhostTimer = 0.22;
    }

    private void SpawnExtraBall()
    {
        if (canvas.Sim.Balls.Count >= maxBalls + 12) return;
        SpawnFromEmitter(canvas.NewBallR);
    }

    private Vec2 ClampDropInside(Vec2 p)
    {
        double maxD = Math.Max(20, arenaR - canvas.NewBallR - 8);
        var d = p - center;
        if (d.Len > maxD) return center + d.Norm() * maxD;
        return p;
    }

    private void OnBallPlaced(SimBall b)
    {
        if (canvas.Sim.Balls.Count > maxBalls + 12)
        {
            canvas.Sim.Balls.Remove(b);
            return;
        }
        b.Pos = emitterPos;
        b.Vel = NextEmitterVel();
        b.Argb = GameCanvas.SpectrumPalette24[(ballColorIdx++) % GameCanvas.SpectrumPalette24.Length];
        b.GhostTimer = 0.22;
    }

    private void EmitShatterBurst(Vec2 pos, Color col, int count)
    {
        for (int s = 0; s < count; s++)
        {
            double sa = rng.NextDouble() * Math.PI * 2.0;
            double ssp = 95 + rng.NextDouble() * 210;
            canvas.EmitShard(
                (float)pos.X + (float)(rng.NextDouble() - 0.5) * 6f,
                (float)pos.Y + (float)(rng.NextDouble() - 0.5) * 6f,
                (float)(Math.Cos(sa) * ssp),
                (float)(Math.Sin(sa) * ssp),
                (float)(rng.NextDouble() * Math.PI),
                (float)((rng.NextDouble() - 0.5) * 12.0),
                (float)(3.2 + rng.NextDouble() * 4.2),
                col, 0.54);
        }
    }

    private bool IsOrbLockedForBall(SimBall b, MultiplierOrb orb)
    {
        double reachPlus = b.R + orb.R + 5.0;
        bool locked = false;
        for (int i = ballOrbLocks.Count - 1; i >= 0; i--)
        {
            var entry = ballOrbLocks[i];
            double d = (entry.Ball.Pos - entry.Orb.Pos).Len;
            if (simTime >= entry.Until && d >= entry.Ball.R + entry.Orb.R + 6.0)
            {
                ballOrbLocks.RemoveAt(i);
                continue;
            }
            if (ReferenceEquals(entry.Ball, b) && ReferenceEquals(entry.Orb, orb))
            {
                if (simTime < entry.Until || d < reachPlus)
                    locked = true;
            }
        }
        return locked;
    }

    private void LockBallForOrb(SimBall b, MultiplierOrb orb, double until)
    {
        for (int i = 0; i < ballOrbLocks.Count; i++)
        {
            var entry = ballOrbLocks[i];
            if (ReferenceEquals(entry.Ball, b) && ReferenceEquals(entry.Orb, orb))
            {
                entry.Until = Math.Max(entry.Until, until);
                ballOrbLocks[i] = entry;
                return;
            }
        }
        ballOrbLocks.Add(new BallOrbLock { Ball = b, Orb = orb, Until = until });
    }

    private void ReleaseBallLocks(SimBall b)
    {
        for (int i = ballOrbLocks.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(ballOrbLocks[i].Ball, b))
                ballOrbLocks.RemoveAt(i);
        }
    }

    private void QueueOrbClones(MultiplierOrb orb, Vec2 dir, double baseSp, int requestedCount)
    {
        int room = Math.Max(0, maxBalls - (canvas.Sim.Balls.Count + pendingOrbSpawns.Count));
        int spawnN = Math.Min(requestedCount, room);
        if (spawnN <= 0) return;

        double r = canvas.NewBallR;
        double sp = Math.Max(baseSp, 225.0);
        // Пошаговый выпуск вдоль траектории родительского шара: минимум 2.25*R дистанции между клонами
        double gapSec = Math.Clamp((r * 2.25) / sp, 0.065, 0.12);
        double maxSpread = wallPtCache.Count > 0 ? 0.14 : 0.30;
        double fanStep = maxSpread / Math.Max(1, (spawnN + 1) / 2);

        for (int k = 0; k < spawnN; k++)
        {
            double angOffset = spawnN == 1
                ? ((emitSeq++) % 2 == 0 ? 1.0 : -1.0) * 0.05
                : ((k % 2 == 0 ? 1.0 : -1.0) * ((k / 2) + 1) * fanStep);

            double cs = Math.Cos(angOffset), sn = Math.Sin(angOffset);
            var cloneDir = new Vec2(dir.X * cs - dir.Y * sn, dir.X * sn + dir.Y * cs);
            double cloneSp = Math.Max(220.0, sp * (0.98 - Math.Min(k, 4) * 0.015));

            pendingOrbSpawns.Add(new PendingOrbSpawn
            {
                Delay = gapSec * (k + 1),
                Pos = orb.Pos + cloneDir * 2.0,
                Vel = cloneDir * cloneSp,
                Radius = r,
                Argb = GameCanvas.SpectrumPalette24[(ballColorIdx++) % GameCanvas.SpectrumPalette24.Length],
                SourceOrb = orb,
            });
        }
    }

    private void FlushPendingOrbSpawns(double sdt)
    {
        for (int i = pendingOrbSpawns.Count - 1; i >= 0; i--)
        {
            var p = pendingOrbSpawns[i];
            p.Delay -= sdt;
            if (p.Delay > 0)
            {
                pendingOrbSpawns[i] = p;
                continue;
            }
            pendingOrbSpawns.RemoveAt(i);
            if (won || canvas.Sim.Balls.Count >= maxBalls) continue;

            var clone = canvas.Sim.AddBall(p.Pos, p.Vel, p.Radius, argb: p.Argb);
            clone.GhostTimer = 0.48;
            LockBallForOrb(clone, p.SourceOrb, double.MaxValue);
        }
    }

    private void OnSimulated(double sdt)
    {
        if (sdt <= 0) return;
        simTime += sdt;

        if (autoSpawnOn && !won && canvas.Sim.Balls.Count < maxBalls)
        {
            autoSpawnAccum += sdt;
            while (autoSpawnAccum >= spawnInterval && canvas.Sim.Balls.Count < maxBalls)
            {
                autoSpawnAccum -= spawnInterval;
                SpawnFromEmitter(canvas.NewBallR);
            }
        }

        float fdt = (float)sdt;
        for (int i = orbWaves.Count - 1; i >= 0; i--)
        {
            var w = orbWaves[i];
            w.Age += fdt;
            if (w.Age >= w.Life) orbWaves.RemoveAt(i);
            else orbWaves[i] = w;
        }

        if (orbsEnabled)
        {
            for (int i = 0; i < orbs.Count; i++)
            {
                if (orbs[i].Cooldown > 0) orbs[i].Cooldown = Math.Max(0, orbs[i].Cooldown - sdt);
                if (orbs[i].Flash > 0) orbs[i].Flash = Math.Max(0, orbs[i].Flash - sdt * 3.5);
            }
        }

        if (pendingOrbSpawns.Count > 0)
            FlushPendingOrbSpawns(sdt);

        // Идём с конца снимка шаров: безопасно для удаления разбившихся о «стекло» шаров
        for (int bi = canvas.Sim.Balls.Count - 1; bi >= 0; bi--)
        {
            var b = canvas.Sim.Balls[bi];
            if (minSpeedFloor > 1.0 && b.Vel.Len < minSpeedFloor)
            {
                var dir = b.Vel.Len > 1e-4 ? b.Vel.Norm() : new Vec2(0.7, -0.7);
                b.Vel = dir * minSpeedFloor;
            }

            if (orbsEnabled)
            {
                for (int oi = 0; oi < orbs.Count; oi++)
                {
                    var orb = orbs[oi];
                    if (orb.Collected || orb.Cooldown > 0) continue;
                    var d = b.Pos - orb.Pos;
                    double reach = b.R + orb.R;
                    if (d.Len >= reach) continue;
                    if (IsOrbLockedForBall(b, orb)) continue;

                    // Шар проходит через сферу-множитель насквозь без обратного отскока, сохраняя ход по каналу
                    double curSp = b.Vel.Len;
                    var dir = curSp > 1e-4
                        ? b.Vel / curSp
                        : (presetIndex == 0 ? new Vec2(1.0, 0.0) : new Vec2(0.0, 1.0));
                    double exitSp = Math.Max(curSp, 225.0);
                    b.Vel = dir * exitSp;
                    b.GhostTimer = Math.Max(b.GhostTimer, 0.50);
                    LockBallForOrb(b, orb, double.MaxValue);

                    orb.Flash = 1.0;
                    if (terrain != null) terrain.CarveCircle(orb.Pos.X, orb.Pos.Y, orb.R + 6.0, out _);
                    var oCol = Color.FromArgb(255, Color.FromArgb(GameCanvas.SpectrumPalette24[orb.PaletteIdx % 24]));
                    orbWaves.Add(new OrbWave
                    {
                        X = (float)orb.Pos.X, Y = (float)orb.Pos.Y,
                        R0 = (float)orb.R, R1 = (float)(orb.R + 46),
                        Age = 0f, Life = 0.42f, Col = oCol,
                    });

                    if (orb.IsRepeatable)
                    {
                        orb.Cooldown = 0.08;
                        orb.Value = Math.Min(99, orb.Value + 1);
                        QueueOrbClones(orb, dir, exitSp, 1);
                    }
                    else
                    {
                        orb.Collected = true;
                        QueueOrbClones(orb, dir, exitSp, Math.Max(0, orb.Value - 1));
                        EmitShatterBurst(orb.Pos, oCol, 10);
                    }
                }
            }

            if (terrain != null && terrain.CollideBall(b, carveRadius, out int carved, out Vec2 hitPos, out Color avgCol, restitution: terrainRestitution))
            {
                if (!glassVanishOn && presetIndex == 0)
                {
                    terrainHitCooldown.TryGetValue(b, out double cd);
                    if (simTime >= cd)
                    {
                        b.Argb = GameCanvas.SpectrumPalette24[(ballColorIdx++) % GameCanvas.SpectrumPalette24.Length];
                        terrainHitCooldown[b] = simTime + 0.08;
                    }
                }

                if (!glassVanishOn)
                {
                    double jAng = (rng.NextDouble() - 0.5) * 0.09;
                    double cs = Math.Cos(jAng), sn = Math.Sin(jAng);
                    b.Vel = new Vec2(b.Vel.X * cs - b.Vel.Y * sn, b.Vel.X * sn + b.Vel.Y * cs);
                }

                EmitShatterBurst(hitPos, avgCol, Math.Clamp(carved / 5, 3, 9));

                if (glassVanishOn)
                {
                    EmitShatterBurst(b.Pos, Color.FromArgb(255, Color.FromArgb(b.Argb)), 6);
                    terrainHitCooldown.Remove(b);
                    ReleaseBallLocks(b);
                    canvas.Sim.Balls.RemoveAt(bi);
                    continue;
                }
            }

            if (spiralGrid != null)
            {
                splashCache.Clear();
                if (spiralGrid.CollideBallEx(b, terrainRestitution, glassVanishOn ? b.R * 2.15 : 0.0, out var broken, splashCache))
                {
                    var tileCol = Color.FromArgb(88, 224, 255);
                    EmitShatterBurst(broken.Center, tileCol, glassVanishOn ? 6 : 4);
                    for (int si = 0; si < splashCache.Count; si++)
                        EmitShatterBurst(splashCache[si].Center, tileCol, 3);

                    if (glassVanishOn)
                    {
                        EmitShatterBurst(b.Pos, Color.FromArgb(255, Color.FromArgb(b.Argb)), 6);
                        ReleaseBallLocks(b);
                        canvas.Sim.Balls.RemoveAt(bi);
                        continue;
                    }
                }
            }
        }

        if (!won)
        {
            if (terrain != null && terrain.ClearedRatio >= 0.90)
            {
                won = true;
                canvas.SpawnGoalConfetti(center);
            }
            else if (spiralGrid != null && spiralGrid.AliveCount == 0)
            {
                won = true;
                canvas.SpawnGoalConfetti(center);
            }
        }
    }

    private void OnRenderUnderlay(Graphics g, LevelData lvl)
    {
        if (terrain != null && terrainBmp != null)
        {
            terrain.RenderToBitmap(terrainBmp, simTime * 0.14);
            var prevInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(terrainBmp, new Rectangle(0, 0, (int)LevelData.W, (int)LevelData.H));
            g.InterpolationMode = prevInterp;
        }

        if (spiralGrid != null)
        {
            for (int i = 0; i < spiralGrid.Tiles.Length; i++)
            {
                ref readonly var t = ref spiralGrid.Tiles[i];
                if (!t.Alive) continue;
                g.FillPolygon(tileFillBrush, t.Poly);
                g.DrawPolygon(tileBorderPen, t.Poly);
            }
        }
    }

    private void OnRenderOverlay(Graphics g, LevelData lvl)
    {
        double hueDeg = (simTime * 36.0) % 360.0;
        var colOuter = DestructibleBitmapTerrain.HsvColor(hueDeg, 0.85, 1.0);
        rainbowGlowPen.Color = Color.FromArgb(62, colOuter);
        rainbowCorePen.Color = colOuter;

        // Внешний контур арены
        if (lvl.Circles.Count > 0)
        {
            var c = lvl.Circles[0];
            float r = (float)c.Radius;
            g.DrawEllipse(rainbowGlowPen, (float)c.Center.X - r, (float)c.Center.Y - r, r * 2f, r * 2f);
            g.DrawEllipse(rainbowCorePen, (float)c.Center.X - r, (float)c.Center.Y - r, r * 2f, r * 2f);
        }

        // Внутренние стенки (спираль / U-каналы / направляющие)
        for (int i = 0; i < wallPtCache.Count; i++)
        {
            var (pts, closed) = wallPtCache[i];
            if (pts.Length < 2) continue;
            var gp = spiralGrid != null ? rainbowGlowPen : laneGlowPen;
            var cp = spiralGrid != null ? rainbowCorePen : laneCorePen;
            if (closed && pts.Length >= 3)
            {
                g.DrawPolygon(gp, pts);
                g.DrawPolygon(cp, pts);
            }
            else
            {
                g.DrawLines(gp, pts);
                g.DrawLines(cp, pts);
            }
        }

        // Верхний указатель-треугольник на 12 часах (как в Clip 5)
        if (presetIndex == 0)
        {
            float cx = (float)center.X;
            float ty = (float)(center.Y - arenaR + 1.0);
            wedgePts[0] = new PointF(cx - 7f, ty);
            wedgePts[1] = new PointF(cx + 7f, ty);
            wedgePts[2] = new PointF(cx, ty + 11f);
            g.FillPolygon(wedgeBrush, wedgePts);
        }

        // Центральный / целевой тёмно-неоновый вихрь для уровней с целью
        if (lvl.Goals.Count > 0)
        {
            var gPts = lvl.Goals[0].Pts;
            float vx = (float)center.X, vy = (float)center.Y;
            if (gPts.Count > 0)
            {
                double sx = 0, sy = 0;
                for (int gi = 0; gi < gPts.Count; gi++) { sx += gPts[gi].X; sy += gPts[gi].Y; }
                vx = (float)(sx / gPts.Count);
                vy = (float)(sy / gPts.Count);
            }
            float vr = 22f + MathF.Sin((float)(simTime * 4.0)) * 2.5f;
            g.FillEllipse(vortexCoreBrush, vx - vr, vy - vr, vr * 2f, vr * 2f);
            g.DrawEllipse(vortexOuterPen, vx - vr, vy - vr, vr * 2f, vr * 2f);
        }

        // Волны от активации сфер
        for (int i = 0; i < orbWaves.Count; i++)
        {
            var w = orbWaves[i];
            float u = Math.Clamp(w.Age / w.Life, 0f, 1f);
            float r = w.R0 + (w.R1 - w.R0) * u;
            int a = Math.Clamp((int)((1f - u) * 210f), 0, 220);
            wavePen.Color = Color.FromArgb(a, w.Col);
            g.DrawEllipse(wavePen, w.X - r, w.Y - r, r * 2f, r * 2f);
        }

        // Точка единого спавна (сопло-эмиттер) + направление запуска
        {
            float pulseR = (float)(canvas.NewBallR + 4.5 + Math.Sin(simTime * 5.0) * 1.6);
            orbFillBrush.Color = Color.FromArgb(42, 92, 240, 255);
            g.FillEllipse(orbFillBrush, (float)emitterPos.X - pulseR, (float)emitterPos.Y - pulseR, pulseR * 2f, pulseR * 2f);
            orbBorderPen.Color = Color.FromArgb(220, 120, 245, 255);
            orbBorderPen.Width = 1.8f;
            g.DrawEllipse(orbBorderPen, (float)emitterPos.X - pulseR, (float)emitterPos.Y - pulseR, pulseR * 2f, pulseR * 2f);
            if (emitterVel.Len > 1e-3)
            {
                var dir = emitterVel.Norm();
                float x0 = (float)(emitterPos.X + dir.X * (pulseR + 2));
                float y0 = (float)(emitterPos.Y + dir.Y * (pulseR + 2));
                float x1 = (float)(emitterPos.X + dir.X * (pulseR + 14));
                float y1 = (float)(emitterPos.Y + dir.Y * (pulseR + 14));
                g.DrawLine(orbBorderPen, x0, y0, x1, y1);
            }
            orbBorderPen.Color = Color.White;
            orbBorderPen.Width = 2.0f;
        }

        // Множители xN (рисуем в мировых координатах до Fx, подпись добавим в экранных для чёткости)
        if (orbsEnabled)
        {
            for (int i = 0; i < orbs.Count; i++)
            {
                var orb = orbs[i];
                if (orb.Collected) continue;
                var oCol = Color.FromArgb(GameCanvas.SpectrumPalette24[orb.PaletteIdx % 24]);
                float rr = (float)(orb.R + orb.Flash * 4.5);
                orbFillBrush.Color = Color.FromArgb(228, oCol);
                g.FillEllipse(orbFillBrush, (float)orb.Pos.X - rr, (float)orb.Pos.Y - rr, rr * 2f, rr * 2f);
                g.DrawEllipse(orbBorderPen, (float)orb.Pos.X - rr, (float)orb.Pos.Y - rr, rr * 2f, rr * 2f);
            }
        }
    }

    private void OnRenderScreenOverlay(Graphics g)
    {
        if (orbsEnabled)
        {
            for (int i = 0; i < orbs.Count; i++)
            {
                var orb = orbs[i];
                if (orb.Collected) continue;
                var p = canvas.WorldToScreen(orb.Pos);
                g.DrawString("x" + orb.Value, orbFont, orbTextBrush, p.X, p.Y, CenterFmt);
            }
        }

        string status = won
            ? $"ОЧИЩЕНО!  ·  Шаров: {canvas.Sim.Balls.Count}  ·  R — заново"
            : spiralGrid != null
                ? $"Блоков: {spiralGrid.AliveCount} / {spiralGrid.TotalCount}   ·   Шаров: {canvas.Sim.Balls.Count}"
                : $"Разрушено: {ClearedRatio * 100.0:0}%   ·   Шаров: {canvas.Sim.Balls.Count}";

        var sz = g.MeasureString(status, hudFont);
        float bw = sz.Width + 28, bh = sz.Height + 10;
        float bx = (canvas.ClientSize.Width - bw) * 0.5f;
        float by = canvas.ClientSize.Height - bh - 12f;
        var rect = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
        using var path = Theme.RoundRect(rect, 9);
        g.FillPath(hudBgBrush, path);
        g.DrawPath(hudBorderPen, path);
        g.DrawString(status, hudFont, hudTextBrush, bx + sz.Width * 0.5f + 14f, by + bh * 0.5f, CenterFmt);
    }
}

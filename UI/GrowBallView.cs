using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Рост шара» (Clip 2): шар растёт с каждым отскоком, меняет цвет
/// по спектру и оставляет 3D-объёмный стековый контурный след внутри арены.
/// </summary>
public class GrowBallView : UserControl, IModeView
{
    public const int PresetCount = 10;

    private static readonly string[] PresetNames =
    {
        "Радужный круг · 3D",
        "Турбо-гигант",
        "Два растущих шара",
        "Шестиугольник · 3D",
        "Чистый неон",
        "Треугольник хаоса",
        "Квадратная клетка",
        "Три шара · столкновение",
        "Невесомость · октагон",
        "Супер-отскок ×1.12",
    };

    private static readonly string[] PresetBadges =
    {
        "3D-СЛЕД", "ТУРБО", "×2 ШАРА", "6 ГРАНЕЙ", "ЧИСТЫЙ",
        "3 ГРАНИ", "4 ГРАНИ", "×3 ШАРА", "0G · 8 ГРАНЕЙ", "УПРУГ ×1.12",
    };

    private readonly Toolbar bar = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnPrev = new() { Icon = IconPainter.ChevronLeft, Size = new Size(34, 34), IconPx = 18 };
    private readonly LevelSelectButton lblPreset = new() { Size = new Size(240, 34), Role = AccentRole.Green };
    private readonly NeoButton btnNext = new() { Icon = IconPainter.ChevronRight, Size = new Size(34, 34), IconPx = 18 };
    private readonly NeoButton btnTube = new() { Text = "3D-СЛЕД", IsToggle = true, IsOn = true, Role = AccentRole.Green, Size = new Size(84, 34), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly NeoButton btnRainbow = new() { Text = "РАДУГА", IsToggle = true, IsOn = true, Role = AccentRole.Orange, Size = new Size(78, 34), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
    private readonly SliderButton growStepBtn = new()
    {
        Min = 0.2,
        Max = 12.0,
        Step = 0.1,
        Prefix = "РОСТ: +",
        Format = static v => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
        Size = new Size(100, 34),
        Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnSandbox = new() { Text = "ПЕСОЧНИЦА", Icon = IconPainter.Sliders, Role = AccentRole.Accent, Size = new Size(104, 34), IconPx = 15, Font = new Font("Segoe UI", 8.2F, FontStyle.Bold) };
    private readonly NeoButton btnRestart = new() { Text = "ЗАНОВО", Icon = IconPainter.Restart, Size = new Size(92, 34), IconPx = 16 };
    private readonly NeoButton btnPause = new() { Text = "ПАУЗА", Icon = IconPainter.Pause, IsToggle = true, Size = new Size(88, 34), IconPx = 16 };
    private readonly NeoButton btnClearTrail = new() { Text = "СБРОС СЛЕДА", Icon = IconPainter.Eraser, Size = new Size(114, 34), IconPx = 15 };

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
    private double growStep = 2.0;
    private double startRadius = 14.0;
    private double minBounceSpeed = 480.0;
    private double gravityScale = 0.85;
    private double wallRestitution = 1.0;
    private double ballRestitution = 1.0;
    private double maxCapRadius = 273.0;
    private bool ballCollisionsOn;
    private bool rainbowOn = true;
    private bool tubeTrailOn = true;
    private bool wasPaused;

    public double GrowStep
    {
        get => growStep;
        set
        {
            growStep = Math.Clamp(value, 0.2, 12.0);
            growStepBtn.Value = growStep;
        }
    }

    private readonly Vec2 center = new(640, 360);
    private double arenaR = 300;
    private int polygonSides; // 0 = идеальный круг, >=3 = правильный многоугольник
    private double winRadius = 264;
    private int bounces;
    private bool won;
    private double simTime;
    private int huePaletteIdx = 2;
    private double continuousHueDeg = 32;
    private double rimFlash;

    private readonly Dictionary<SimBall, (Vec2 LastStamp, double LastR, double LastBounceT, int PaletteIdx)> ballState = new();

    private Bitmap tubeBmp;
    private Graphics tubeG;
    private readonly SolidBrush tubeInnerBrush = new(Color.FromArgb(255, 8, 10, 18));
    private readonly Pen tubeRimPen = new(Color.Orange, 1.8f);
    private readonly Pen arenaGlowPen = new(Color.FromArgb(65, Color.Orange), 9.0f) { LineJoin = LineJoin.Round };
    private readonly Pen arenaCorePen = new(Color.Orange, 2.8f) { LineJoin = LineJoin.Round };
    private readonly Font hudFont = new("Segoe UI", 10.5F, FontStyle.Bold);
    private readonly SolidBrush hudBgBrush = new(Color.FromArgb(180, 10, 13, 22));
    private readonly SolidBrush hudTextBrush = new(Color.FromArgb(236, 242, 255));
    private readonly Pen hudBorderPen = new(Color.FromArgb(95, 110, 165, 255), 1.1f);
    private PointF[] polyCache = Array.Empty<PointF>();

    public int PresetIndex => presetIndex;
    public int BounceCount => bounces;
    public bool IsCompleted => won;

    public GrowBallView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        bar.AddLeft(btnPrev);
        bar.AddLeft(lblPreset);
        bar.AddLeft(btnNext);
        bar.AddLeft(btnTube);
        bar.AddLeft(btnRainbow);
        bar.AddLeft(growStepBtn);
        bar.AddLeft(btnSandbox);
        bar.AddRight(btnClearTrail);
        bar.AddRight(btnPause);
        bar.AddRight(btnRestart);

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий сценарий роста");
        tips.SetToolTip(lblPreset, "Выбрать сценарий роста из сетки с мини-превью");
        tips.SetToolTip(btnNext, "Следующий сценарий роста");
        tips.SetToolTip(btnTube, "3D-объёмный контурный след как в видео");
        tips.SetToolTip(btnRainbow, "Смена цвета арены и шара по спектру");
        tips.SetToolTip(growStepBtn, "Прирост радиуса шара за один отскок (клик — ползунок и ввод)");
        tips.SetToolTip(btnSandbox, "Тонкая настройка физики и параметров режима");
        tips.SetToolTip(btnRestart, "Начать заново (R)");
        tips.SetToolTip(btnPause, "Пауза / продолжить (Пробел)");
        tips.SetToolTip(btnClearTrail, "Стереть накопленный 3D-след");

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
                    Role = AccentRole.Green,
                });
            }
            return (presetIndex, items);
        };
        lblPreset.LevelSelected += ShowPreset;

        btnPrev.Click += (_, _) => ShowPreset((presetIndex + PresetCount - 1) % PresetCount);
        btnNext.Click += (_, _) => ShowPreset((presetIndex + 1) % PresetCount);
        btnTube.Click += (_, _) => { tubeTrailOn = btnTube.IsOn; if (!tubeTrailOn) ClearTube(); canvas.Invalidate(); };
        btnRainbow.Click += (_, _) => { rainbowOn = btnRainbow.IsOn; canvas.Invalidate(); };
        growStepBtn.Changed += v => { growStep = v; canvas.Invalidate(); };
        SandboxPopup.Attach(btnSandbox, "Песочница · Рост шара", BuildSandboxEntries);
        btnRestart.Click += (_, _) => ShowPreset(presetIndex);
        btnPause.Click += (_, _) => SetPaused(btnPause.IsOn);
        btnClearTrail.Click += (_, _) => { ClearTube(); canvas.Invalidate(); };

        canvas.ClampDropPos = ClampDropInside;
        canvas.Sim.WallBounced += OnWallBounced;
        canvas.Simulated += OnSimulated;
        canvas.BallPlaced += OnBallPlaced;
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
            canvas.Sim.WallBounced -= OnWallBounced;
            canvas.Simulated -= OnSimulated;
            canvas.BallPlaced -= OnBallPlaced;
            canvas.RenderUnderlay -= OnRenderUnderlay;
            canvas.RenderOverlay -= OnRenderOverlay;
            canvas.RenderScreenOverlay -= OnRenderScreenOverlay;
            tubeG?.Dispose();
            tubeBmp?.Dispose();
            tubeInnerBrush.Dispose();
            tubeRimPen.Dispose();
            arenaGlowPen.Dispose();
            arenaCorePen.Dispose();
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
        get => tubeTrailOn;
        set
        {
            tubeTrailOn = value;
            btnTube.IsOn = value;
            btnTube.Invalidate();
            if (!value) ClearTube();
            canvas.Invalidate();
        }
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
                GrowStep += 0.5;
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                GrowStep -= 0.5;
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

    private void ApplySandboxPhysics()
    {
        canvas.Sim.GravityScale = gravityScale;
        canvas.Sim.WallRestitution = wallRestitution;
        canvas.Sim.BallRestitution = ballRestitution;
        canvas.Sim.BallCollisions = ballCollisionsOn;
        canvas.Design.GravityScale = gravityScale;
        canvas.Design.WallRestitution = wallRestitution;
        canvas.Design.BallRestitution = ballRestitution;
        canvas.Design.NoBallCollisions = !ballCollisionsOn;
        canvas.Invalidate();
    }

    private void SpawnExtraBall()
    {
        if (canvas.Sim.Balls.Count >= 12) return;
        double ang = (bounces + canvas.Sim.Balls.Count * 2.1);
        var ac = polygonSides == 3 ? new Vec2(center.X, center.Y + 32) : center;
        var pos = ClampDropInside(new Vec2(ac.X + Math.Cos(ang) * 36, ac.Y - 50 + Math.Sin(ang) * 24));
        var b = canvas.Sim.AddBall(
            pos,
            new Vec2(Math.Cos(ang) * 210, -60),
            startRadius,
            argb: GameCanvas.SpectrumPalette24[(huePaletteIdx + canvas.Sim.Balls.Count * 4) % GameCanvas.SpectrumPalette24.Length]);
        ballState[b] = (b.Pos, b.R, simTime, (huePaletteIdx + canvas.Sim.Balls.Count * 4) % GameCanvas.SpectrumPalette24.Length);
        canvas.Invalidate();
    }

    private List<SandboxEntry> BuildSandboxEntries()
    {
        double inR = polygonSides >= 3 ? arenaR * Math.Cos(Math.PI / polygonSides) : arenaR;
        double maxAllow = Math.Round(inR * 0.96);
        return new List<SandboxEntry>
        {
            new SandboxSlider { Label = "Рост за отскок", Min = 0.2, Max = 12.0, Step = 0.1, Format = v => $"+{v:0.0}", Get = () => growStep, Set = v => { GrowStep = v; canvas.Invalidate(); } },
            new SandboxSlider { Label = "Начальный радиус", Min = 4, Max = 48, Step = 1, Get = () => startRadius, Set = v => { startRadius = v; canvas.NewBallR = v; } },
            new SandboxSlider { Label = "Макс. радиус", Min = 50, Max = maxAllow, Step = 2, Get = () => maxCapRadius, Set = v => { maxCapRadius = v; winRadius = Math.Min(winRadius, maxCapRadius * 0.96); canvas.Invalidate(); } },
            new SandboxSlider { Label = "Мин. скорость отскока", Min = 0, Max = 950, Step = 10, Get = () => minBounceSpeed, Set = v => minBounceSpeed = v },
            new SandboxSlider { Label = "Гравитация", Min = 0, Max = 3.0, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => gravityScale, Set = v => { gravityScale = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Упругость стен", Min = 0.15, Max = 1.35, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => wallRestitution, Set = v => { wallRestitution = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Упругость шаров", Min = 0.20, Max = 1.30, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => ballRestitution, Set = v => { ballRestitution = v; ApplySandboxPhysics(); } },
            new SandboxSlider { Label = "Скорость симуляции", Min = 0.10, Max = 4.0, Step = 0.05, Format = v => $"×{v:0.00}", Get = () => GameCanvas.SimSpeed, Set = v => { GameCanvas.SimSpeed = v; canvas.Invalidate(); } },
            new SandboxToggle { Label = "Столкновения шаров", Get = () => ballCollisionsOn, Set = v => { ballCollisionsOn = v; ApplySandboxPhysics(); } },
            new SandboxToggle { Label = "3D-след контуров", Get = () => tubeTrailOn, Set = v => { TrailsOn = v; } },
            new SandboxToggle { Label = "Радужный цвет", Get = () => rainbowOn, Set = v => { rainbowOn = v; btnRainbow.IsOn = v; btnRainbow.Invalidate(); canvas.Invalidate(); } },
            new SandboxAction { Label = "+ Добавить шар", Role = AccentRole.Green, RefreshAllAfter = false, Run = SpawnExtraBall },
            new SandboxAction { Label = "Сбросить режим", Role = AccentRole.Orange, Run = () => { GameCanvas.SimSpeed = 1.0; ShowPreset(presetIndex); } },
        };
    }

    private void EnsureTubeBitmap()
    {
        if (tubeBmp != null) return;
        tubeBmp = new Bitmap((int)LevelData.W, (int)LevelData.H, PixelFormat.Format32bppPArgb);
        tubeG = Graphics.FromImage(tubeBmp);
        tubeG.SmoothingMode = SmoothingMode.AntiAlias;
        tubeG.Clear(Color.Transparent);
    }

    private void ClearTube()
    {
        EnsureTubeBitmap();
        tubeG.Clear(Color.Transparent);
    }

    private static LevelData CreatePreviewLevel(int p)
    {
        int sides = p switch { 3 => 6, 5 => 3, 6 => 4, 8 => 8, _ => 0 };
        double r = p switch { 1 => 308, 2 => 306, 3 => 316, 5 => 335, 6 => 332, 7 => 312, 8 => 314, 9 => 306, _ => 300 };
        var c = new Vec2(640, p == 5 ? 392 : 360);
        var lvl = new LevelData { Name = PresetNames[Math.Clamp(p, 0, PresetCount - 1)] };
        if (sides >= 3)
        {
            double rot = sides == 4 ? Math.PI / 4.0 : -Math.PI / 2.0;
            lvl.Walls.Add(new PolyData { Closed = true, Pts = LevelShapes.RegularPolygonPts(c, r, sides, rot), Pivot = c });
        }
        else
        {
            lvl.Circles.Add(new CircleData { Center = c, Radius = r });
        }
        return lvl;
    }

    public void ShowPreset(int index)
    {
        presetIndex = Math.Clamp(index, 0, PresetCount - 1);
        polygonSides = presetIndex switch { 3 => 6, 5 => 3, 6 => 4, 8 => 8, _ => 0 };
        arenaR = presetIndex switch
        {
            1 => 308,
            2 => 306,
            3 => 316,
            5 => 335,
            6 => 332,
            7 => 312,
            8 => 314,
            9 => 306,
            _ => 300,
        };
        var arenaCenter = polygonSides == 3 ? new Vec2(center.X, center.Y + 32) : center;

        growStep = presetIndex switch
        {
            1 => 3.2,
            5 => 1.6,
            6 => 2.2,
            7 => 1.5,
            8 => 2.4,
            9 => 1.8,
            _ => 2.0,
        };
        growStepBtn.Value = growStep;
        startRadius = presetIndex switch
        {
            1 => 16,
            2 or 3 => 13,
            5 => 11,
            6 or 9 => 12,
            7 => 11,
            8 => 10,
            _ => 14,
        };
        minBounceSpeed = presetIndex switch
        {
            1 => 590,
            8 => 420,
            9 => 520,
            _ => 480,
        };
        gravityScale = presetIndex == 8 ? 0.0 : 0.85;
        wallRestitution = presetIndex == 9 ? 1.12 : 1.0;
        ballRestitution = 1.0;
        ballCollisionsOn = presetIndex == 2 || presetIndex == 7;
        double inRadius = polygonSides >= 3 ? arenaR * Math.Cos(Math.PI / polygonSides) : arenaR;
        maxCapRadius = inRadius * 0.90;

        var lvl = new LevelData
        {
            Border = false,
            Gravity = true,
            GravityScale = gravityScale,
            WallRestitution = wallRestitution,
            BallRestitution = ballRestitution,
            NoBallCollisions = !ballCollisionsOn,
        };

        bool useTube = presetIndex != 4;
        tubeTrailOn = useTube;
        btnTube.IsOn = useTube;
        btnTube.Invalidate();

        if (polygonSides >= 3)
        {
            double rot = polygonSides == 4 ? Math.PI / 4.0 : -Math.PI / 2.0;
            var pts = LevelShapes.RegularPolygonPts(arenaCenter, arenaR, polygonSides, rot);
            lvl.Walls.Add(new PolyData { Closed = true, Pts = pts, Pivot = arenaCenter });
            polyCache = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++) polyCache[i] = new PointF((float)pts[i].X, (float)pts[i].Y);
            winRadius = inRadius * 0.82;
        }
        else
        {
            lvl.Circles.Add(new CircleData { Center = center, Radius = arenaR });
            polyCache = Array.Empty<PointF>();
            winRadius = presetIndex switch
            {
                2 => arenaR * 0.54,
                7 => arenaR * 0.45,
                _ => arenaR * 0.88,
            };
        }

        lvl.Name = $"{presetIndex + 1}/{PresetCount} · {PresetNames[presetIndex]}";
        lblPreset.Text = lvl.Name;

        canvas.Trails = false; // отключаем стандартный хвост-линию, используем стековый контурный 3D-след
        canvas.NewBallR = startRadius;
        canvas.LoadLevel(lvl);

        bounces = 0;
        won = false;
        simTime = 0;
        rimFlash = 0;
        huePaletteIdx = 2;
        continuousHueDeg = 32;
        ballState.Clear();
        ClearTube();

        if (presetIndex == 2)
        {
            var b1 = canvas.Sim.AddBall(new Vec2(center.X - 68, center.Y - 95), new Vec2(165, -40), startRadius, argb: GameCanvas.SpectrumPalette24[2]);
            var b2 = canvas.Sim.AddBall(new Vec2(center.X + 74, center.Y - 55), new Vec2(-150, -90), startRadius, argb: GameCanvas.SpectrumPalette24[14]);
            ballState[b1] = (b1.Pos, b1.R, -1.0, 2);
            ballState[b2] = (b2.Pos, b2.R, -1.0, 14);
        }
        else if (presetIndex == 7)
        {
            for (int k = 0; k < 3; k++)
            {
                double a = -Math.PI / 2.0 + k * Math.PI * 2.0 / 3.0;
                int pIdx = (2 + k * 7) % GameCanvas.SpectrumPalette24.Length;
                var b = canvas.Sim.AddBall(
                    center + new Vec2(Math.Cos(a) * 95, Math.Sin(a) * 95),
                    new Vec2(Math.Cos(a + 1.1) * 200, Math.Sin(a + 1.1) * 200),
                    startRadius, argb: GameCanvas.SpectrumPalette24[pIdx]);
                ballState[b] = (b.Pos, b.R, -1.0, pIdx);
            }
        }
        else
        {
            var vx = presetIndex switch { 1 => 260.0, 8 => 235.0, 9 => 240.0, _ => 195.0 };
            var vy = presetIndex == 8 ? 165.0 : -55.0;
            var dropY = polygonSides == 3 ? arenaCenter.Y - 40 : center.Y - 125;
            var b = canvas.Sim.AddBall(new Vec2(center.X - 38, dropY), new Vec2(vx, vy), startRadius, argb: GameCanvas.SpectrumPalette24[huePaletteIdx]);
            ballState[b] = (b.Pos, b.R, -1.0, huePaletteIdx);
        }

        SetPaused(false);
        bar.Relayout();
        canvas.Invalidate();
    }

    private Vec2 ClampDropInside(Vec2 p)
    {
        var ac = polygonSides == 3 ? new Vec2(center.X, center.Y + 32) : center;
        double inR = polygonSides >= 3 ? arenaR * Math.Cos(Math.PI / polygonSides) : arenaR;
        double maxD = Math.Max(20, inR - canvas.NewBallR - 8);
        var d = p - ac;
        if (d.Len > maxD) return ac + d.Norm() * maxD;
        return p;
    }

    private void OnBallPlaced(SimBall b)
    {
        if (canvas.Sim.Balls.Count > 12)
        {
            canvas.Sim.Balls.Remove(b);
            return;
        }
        int pIdx = (huePaletteIdx + canvas.Sim.Balls.Count * 5) % GameCanvas.SpectrumPalette24.Length;
        b.Argb = GameCanvas.SpectrumPalette24[pIdx];
        b.Vel = new Vec2(((canvas.Sim.Balls.Count & 1) == 0 ? 1 : -1) * 180, -80);
        ballState[b] = (b.Pos, b.R, simTime, pIdx);
    }

    private void OnWallBounced(SimBall b, Vec2 contactPt, Vec2 inwardNormal)
    {
        if (!ballState.TryGetValue(b, out var st))
            st = (b.Pos, b.R, -1.0, huePaletteIdx);

        if (simTime - st.LastBounceT < 0.025) return;

        bounces++;
        rimFlash = 1.0;

        double maxCap = maxCapRadius;
        double step = growStep;
        if (!won && b.R < maxCap)
        {
            double nextR = Math.Min(maxCap, b.R + step);
            double dr = nextR - b.R;
            b.R = nextR;
            b.Pos += inwardNormal * (dr + 0.6);
        }

        // Если центр почти упёрся — удерживаем внутри круговой арены
        if (polygonSides == 0)
        {
            var dc = b.Pos - center;
            double lim = Math.Max(4, arenaR - b.R - 0.5);
            if (dc.Len > lim)
                b.Pos = center + dc.Norm() * lim;
        }

        // Лёгкий касательный сдвиг и нижний порог энергии, чтобы гигантский шар не застревал по вертикали
        double minSp = minBounceSpeed;
        if (b.Vel.Len < minSp)
        {
            var dir = b.Vel.Len > 1e-4 ? b.Vel.Norm() : inwardNormal;
            b.Vel = dir * minSp;
        }
        if (Math.Abs(Vec2.Dot(b.Vel.Norm(), inwardNormal)) > 0.96)
        {
            double tilt = ((bounces & 1) == 0 ? 0.045 : -0.045);
            double cs = Math.Cos(tilt), sn = Math.Sin(tilt);
            b.Vel = new Vec2(b.Vel.X * cs - b.Vel.Y * sn, b.Vel.X * sn + b.Vel.Y * cs);
        }

        int pIdx = st.PaletteIdx;
        if (rainbowOn)
        {
            huePaletteIdx = (huePaletteIdx + 1) % GameCanvas.SpectrumPalette24.Length;
            pIdx = (st.PaletteIdx + 1) % GameCanvas.SpectrumPalette24.Length;
            b.Argb = GameCanvas.SpectrumPalette24[pIdx];
            continuousHueDeg = (continuousHueDeg + 15.0) % 360.0;
        }

        if (tubeTrailOn)
            StampBallOutline(b.Pos, b.R, pIdx);

        // Мелкие искры от кромки
        var spCol = Color.FromArgb(255, Color.FromArgb(b.Argb));
        var tan = Vec2.Perp(inwardNormal);
        for (int k = -1; k <= 1; k += 2)
        {
            canvas.EmitShard(
                (float)contactPt.X, (float)contactPt.Y,
                (float)(inwardNormal.X * 95 + tan.X * k * 65),
                (float)(inwardNormal.Y * 95 + tan.Y * k * 65),
                0f, 0f, 0f, spCol, 0.34);
        }

        ballState[b] = (b.Pos, b.R, simTime, pIdx);

        if (!won && b.R >= winRadius)
        {
            won = true;
            canvas.SpawnGoalConfetti(center);
        }
    }

    private void OnSimulated(double sdt)
    {
        if (sdt <= 0) return;
        simTime += sdt;
        rimFlash = Math.Max(0, rimFlash - sdt * 4.5);

        if (rainbowOn)
            continuousHueDeg = (continuousHueDeg + sdt * 28.0) % 360.0;

        if (!tubeTrailOn) return;
        EnsureTubeBitmap();

        foreach (var b in canvas.Sim.Balls)
        {
            if (!ballState.TryGetValue(b, out var st))
            {
                st = (b.Pos, b.R, simTime, huePaletteIdx);
                ballState[b] = st;
                StampBallOutline(b.Pos, b.R, st.PaletteIdx);
                continue;
            }
            if ((b.Pos - st.LastStamp).Len >= 2.2)
            {
                StampBallOutline(b.Pos, b.R, st.PaletteIdx);
                ballState[b] = (b.Pos, b.R, st.LastBounceT, st.PaletteIdx);
            }
        }
    }

    private void StampBallOutline(Vec2 pos, double r, int pIdx)
    {
        EnsureTubeBitmap();
        float rr = (float)Math.Max(2.0, r);
        var col = Color.FromArgb(GameCanvas.SpectrumPalette24[pIdx % GameCanvas.SpectrumPalette24.Length]);
        tubeRimPen.Color = Color.FromArgb(228, col);
        tubeG.FillEllipse(tubeInnerBrush, (float)pos.X - rr, (float)pos.Y - rr, rr * 2f, rr * 2f);
        tubeG.DrawEllipse(tubeRimPen, (float)pos.X - rr, (float)pos.Y - rr, rr * 2f, rr * 2f);
    }

    private void OnRenderUnderlay(Graphics g, LevelData lvl)
    {
        if (tubeTrailOn && tubeBmp != null)
            g.DrawImageUnscaled(tubeBmp, 0, 0);
    }

    private void OnRenderOverlay(Graphics g, LevelData lvl)
    {
        var ringCol = rainbowOn
            ? DestructibleBitmapTerrain.HsvColor(continuousHueDeg, 0.85, 1.0)
            : Theme.Accent;
        int glowA = (int)(58 + rimFlash * 95);
        arenaGlowPen.Color = Color.FromArgb(Math.Clamp(glowA, 30, 185), ringCol);
        arenaCorePen.Color = ringCol;
        arenaCorePen.Width = (float)(2.8 + rimFlash * 1.4);

        if (polygonSides >= 3 && polyCache.Length >= 3)
        {
            g.DrawPolygon(arenaGlowPen, polyCache);
            g.DrawPolygon(arenaCorePen, polyCache);
        }
        else
        {
            float r = (float)arenaR;
            g.DrawEllipse(arenaGlowPen, (float)center.X - r, (float)center.Y - r, r * 2f, r * 2f);
            g.DrawEllipse(arenaCorePen, (float)center.X - r, (float)center.Y - r, r * 2f, r * 2f);
        }
    }

    private void OnRenderScreenOverlay(Graphics g)
    {
        double maxR = 0;
        foreach (var b in canvas.Sim.Balls)
            if (b.R > maxR) maxR = b.R;

        string msg = won
            ? $"ЗАПОЛНЕНО!  ·  R: {maxR:0} px  ·  Отскоков: {bounces}  ·  R — заново"
            : $"R: {maxR:0} / {winRadius:0} px   ·   Отскоков: {bounces}";
        var sz = g.MeasureString(msg, hudFont);
        float bw = sz.Width + 28, bh = sz.Height + 10;
        float bx = (canvas.ClientSize.Width - bw) * 0.5f;
        float by = canvas.ClientSize.Height - bh - 12f;
        var rect = new Rectangle((int)bx, (int)by, (int)bw, (int)bh);
        using var path = Theme.RoundRect(rect, 9);
        g.FillPath(hudBgBrush, path);
        g.DrawPath(hudBorderPen, path);
        g.DrawString(msg, hudFont, hudTextBrush, bx + 14f, by + 5f);
    }
}

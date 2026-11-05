using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Универсальный редактор уровней: позволяет создавать, настраивать, тестировать и сохранять
/// все типы арен и механик игры — классические уровни с целями, свободные песочницы, аналитические
/// круги и многоугольники, вращающиеся кольца «Ротора» (спирали, реверс, встречные кольца, разгон,
/// клик-старт с нулевой скоростью) и математические бильярды «Волнового фронта» с дифракцией.
/// </summary>
public class EditorView : UserControl, IModeView
{
    private const int BasePaletteWidth = 124;
    private const int BaseInspectorWidth = 238;

    private readonly GameCanvas canvas = new() { Editor = true, Dock = DockStyle.Fill, ShowGrid = true, GoalCelebrationOn = true };
    private readonly LevelRuntime runtime;

    // --- верхняя парящая капсула (Toolbar) ---
    private readonly Toolbar topPanel = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly NeoButton btnSaves = new()
    {
        Text = "СОХРАНЕНИЯ", Icon = IconPainter.Folder, IconPx = 15, Role = AccentRole.Accent,
        Size = new Size(122, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoTextBox nameBox = new(9.5F) { Size = new Size(136, 36) };
    private readonly NeoButton btnTemplates = new()
    {
        Text = "ШАБЛОНЫ", Icon = IconPainter.GridCards, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(110, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnUndo = new() { Icon = IconPainter.Undo, Size = new Size(38, 38), IconPx = 19 };
    private readonly NeoButton btnRedo = new() { Icon = IconPainter.Redo, Size = new Size(38, 38), IconPx = 19 };
    private readonly NeoButton btnClear = new() { Icon = IconPainter.Trash, Size = new Size(38, 38), IconPx = 18 };

    private readonly NeoButton chipGrid = MakeTopChip("СЕТКА", 62);
    private readonly NeoButton chipSnap = MakeTopChip("ПРИВЯЗКА", 82);
    private readonly NeoButton chipFx = MakeTopChip("FX", 44);

    private readonly NeoButton btnRestartTest = new() { Icon = IconPainter.Restart, Size = new Size(38, 38), IconPx = 19, Enabled = false };
    private readonly NeoButton btnPauseTest = new() { Icon = IconPainter.Pause, Size = new Size(38, 38), IconPx = 18, Enabled = false };
    private readonly NeoButton btnInspector = new()
    {
        Text = "СВОЙСТВА", Icon = IconPainter.Sliders, IconPx = 15, IsToggle = true, IsOn = true,
        Size = new Size(104, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnTest = new()
    {
        Text = "ТЕСТ", Role = AccentRole.Green, Icon = IconPainter.Play, IconPx = 18,
        Size = new Size(98, 38), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
    };

    // --- левая палитра инструментов ---
    private readonly DockPanel palettePanel = new(rightBorder: true) { Dock = DockStyle.Left, Width = BasePaletteWidth };
    private readonly List<NeoButton> toolBtns = new();
    private readonly NeoButton btnQuickSpiral = new()
    {
        Text = "СПИРАЛЬ", Icon = IconPainter.Spiral, IconPx = 16, Role = AccentRole.Orange,
        Size = new Size(102, 34), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly BallColorButton quickColorBtn = new()
    {
        Selected = unchecked((int)0xFFF0F3FF), ShowColorName = true, Size = new Size(102, 34),
    };

    // --- правый контекстный инспектор (СЦЕНА / ОБЪЕКТ) ---
    private readonly DockPanel inspectorPanel = new(rightBorder: false) { Dock = DockStyle.Right, Width = BaseInspectorWidth };
    private readonly NeoButton tabSceneBtn = new()
    {
        Text = "СЦЕНА", IsToggle = true, IsOn = true, Role = AccentRole.Accent,
        Size = new Size(104, 30), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton tabObjBtn = new()
    {
        Text = "ОБЪЕКТ", IsToggle = true, Role = AccentRole.Violet,
        Size = new Size(104, 30), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private int inspectorTab; // 0 = СЦЕНА, 1 = ОБЪЕКТ

    // Вкладка «СЦЕНА»
    private readonly NeoButton presetClassicBtn = MakeSceneChip("КЛАССИКА", 68, AccentRole.Accent);
    private readonly NeoButton presetRotorBtn = MakeSceneChip("РОТОР", 68, AccentRole.Orange);
    private readonly NeoButton presetWaveBtn = MakeSceneChip("ВОЛНА", 68, AccentRole.Green);

    private readonly NeoButton scGravity = MakeSceneChip("ГРАВИТАЦИЯ", 104);
    private readonly NeoButton scBorder = MakeSceneChip("РАМКА ПОЛЯ", 104);
    private readonly NeoButton scCollide = MakeSceneChip("СОУДАРЕНИЯ", 104);
    private readonly NeoButton scElastic = MakeSceneChip("УПРУГОСТЬ", 104);
    private readonly SliderButton scGravScale = new()
    {
        Size = new Size(214, 28), Min = 0.10, Max = 3.00, Value = 1.00,
        Prefix = "СИЛА G: ", Format = static v => v.ToString("0.00") + "×",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };

    private readonly SliderButton scSpinSpeed = new()
    {
        Size = new Size(214, 28), Min = -4.00, Max = 4.00, Value = 0.00,
        Prefix = "ВРАЩЕНИЕ: ", Format = static v => (Math.Abs(v) < 0.03 ? 0 : v).ToString("+0.00;-0.00;0.00") + " рад/с",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton scRotorRules = MakeSceneChip("РОТОР-РАСПАД", 104, AccentRole.Orange);
    private readonly NeoButton scInstantDrop = MakeSceneChip("КЛИК v=0", 104, AccentRole.Orange);
    private readonly NeoButton scContract = MakeSceneChip("СЖАТИЕ", 104);
    private readonly NeoButton scFreezeGap = MakeSceneChip("СТОП В ЩЕЛИ", 104);
    private readonly NeoButton scReverseBreak = MakeSceneChip("РЕВЕРС", 104);
    private readonly SliderButton scAccelBreak = new()
    {
        Size = new Size(104, 28), Min = 1.00, Max = 1.15, Value = 1.00,
        Prefix = "РАЗГОН ", Format = static v => v.ToString("0.00") + "×",
        Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };
    private readonly NeoButton scOpenSpiralGen = new()
    {
        Text = "ГЕНЕРАТОР КОЛЕЦ...", Icon = IconPainter.Spiral, IconPx = 16, Role = AccentRole.Orange,
        Size = new Size(214, 30), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };

    private readonly NeoButton scWaveMode = MakeSceneChip("ИЗЛУЧАТЕЛЬ", 104, AccentRole.Green);
    private readonly NeoButton scWaveCont = MakeSceneChip("ДИФРАКЦИЯ", 104, AccentRole.Green);
    private readonly SliderButton scWaveRays = new()
    {
        Size = new Size(104, 28), Min = 12, Max = 20000, Log = true, Value = 180,
        Prefix = "ЛУЧИ ", Format = FormatCount,
        Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };
    private readonly SliderButton scWaveSpeed = new()
    {
        Size = new Size(104, 28), Min = 60, Max = 1000, Value = 320,
        Prefix = "СКОР. ", Format = static v => Math.Round(v).ToString(),
        Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };
    private readonly NeoTextBox scHintBox = new(8.75F) { Size = new Size(214, 30) };

    // Вкладка «ОБЪЕКТ»
    private string objHeaderTitle = "ПАРАМЕТРЫ ИНСТРУМЕНТА";
    private readonly BallColorButton objBallColor = new() { Text = "ШАР", ShowColorName = true, Selected = unchecked((int)0xFFF0F3FF), Size = new Size(214, 30) };
    private readonly SliderButton objBallR = new()
    {
        Size = new Size(214, 28), Min = 2, Max = 40, Value = 12,
        Prefix = "РАДИУС ШАРА: R ", Format = static v => Math.Round(v).ToString(),
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objBallV = new()
    {
        Size = new Size(214, 28), Min = 0, Max = 2600, Value = 800,
        Prefix = "СКОРОСТЬ: V ", Format = static v => (Math.Round(v / 25.0) * 25).ToString(),
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objBallAng = new()
    {
        Size = new Size(214, 28), Min = 0, Max = 360, Value = 0,
        Prefix = "НАПРАВЛЕНИЕ: ", Format = static v => Math.Round(v) + "°",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton objBallZeroV = MakeSceneChip("БЕЗ НАЧАЛЬНОЙ СКОРОСТИ (v = 0)", 214);

    private readonly SliderButton objCircleR = new()
    {
        Size = new Size(214, 28), Min = 12, Max = 620, Value = 240,
        Prefix = "РАДИУС КРУГА: ", Format = static v => Math.Round(v) + " px",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };

    private readonly SliderButton objArcR = new()
    {
        Size = new Size(214, 28), Min = 20, Max = 650, Value = 220,
        Prefix = "РАДИУС КОЛЬЦА: ", Format = static v => Math.Round(v) + " px",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objArcGap = new()
    {
        Size = new Size(214, 28), Min = 12, Max = 260, Value = 62,
        Prefix = "ПУСТОЙ ВЫРЕЗ: ", Format = static v => Math.Round(v) + "°",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objArcStart = new()
    {
        Size = new Size(214, 28), Min = 0, Max = 360, Value = 35,
        Prefix = "УГОЛ ПОВОРОТА: ", Format = static v => Math.Round(v) + "°",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objArcSpinMul = new()
    {
        Size = new Size(214, 28), Min = -3.0, Max = 3.0, Value = 1.0,
        Prefix = "МНОЖ. ВРАЩЕНИЯ: ", Format = static v => v.ToString("+0.00;-0.00;0.00") + "×",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objArcThick = new()
    {
        Size = new Size(214, 28), Min = 2.0, Max = 14.0, Value = 4.5,
        Prefix = "ТОЛЩИНА КОЛЬЦА: ", Format = static v => v.ToString("0.0") + " px",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton objArcFlipSpin = new()
    {
        Text = "ИНВЕРТИРОВАТЬ ВРАЩЕНИЕ (−×)", Role = AccentRole.Orange,
        Size = new Size(214, 28), Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };

    private readonly SliderButton objPolySides = new()
    {
        Size = new Size(214, 28), Min = 3, Max = 24, Value = 6,
        Prefix = "ГРАНЕЙ / ЛУЧЕЙ: ", Format = static v => Math.Round(v).ToString(),
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly SliderButton objStarRatio = new()
    {
        Size = new Size(214, 28), Min = 18, Max = 85, Value = 42,
        Prefix = "ВНУТР. РАДИУС ЗВЕЗДЫ: ", Format = static v => Math.Round(v) + "%",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton objPolyClosed = MakeSceneChip("ЗАМКНУТЫЙ КОНТУР", 214);
    private readonly NeoButton objPolyPivot = MakeSceneChip("ВРАЩАТЬ ВОКРУГ СВОЕГО ЦЕНТРА", 214);
    private readonly NeoButton objConvertWallGoal = new()
    {
        Text = "ПРЕВРАТИТЬ СТЕНА ↔ ЦЕЛЬ", Role = AccentRole.Green,
        Size = new Size(214, 28), Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };

    private readonly BallColorButton objWaveColor = new() { Text = "ВОЛНА", ShowColorName = true, Selected = BallColors.Spectrum, Size = new Size(214, 30) };
    private readonly SliderButton objWaveRadius = new()
    {
        Size = new Size(214, 28), Min = 1, Max = 12, Value = 3,
        Prefix = "ТОЛЩИНА ГРЕБНЯ: R ", Format = static v => Math.Round(v).ToString(),
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };

    private readonly NeoButton objCenterBtn = new()
    {
        Text = "В ЦЕНТР ПОЛЯ (640, 360)", Size = new Size(214, 28), Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };
    private readonly NeoButton objDupBtn = new()
    {
        Text = "КОПИЯ", Icon = IconPainter.Duplicate, IconPx = 15,
        Size = new Size(104, 30), Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };
    private readonly NeoButton objDelBtn = new()
    {
        Text = "УДАЛИТЬ", Icon = IconPainter.Trash, IconPx = 15, Role = AccentRole.Red,
        Size = new Size(104, 30), Font = new Font("Segoe UI", 8.0F, FontStyle.Bold),
    };

    // --- строка состояния ---
    private readonly Bar statusBar = new() { LineAtTop = true };
    private readonly Label statusLbl = new();
    private readonly Label coordLbl = new();

    private string fileName;
    private DateTime statusHoldUntil;
    private bool syncingUi;
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 220 };

    private readonly List<(int Y, string Text)> paletteHeaders = new();
    private readonly List<(int Y, string Text)> inspectorHeaders = new();
    private Control[] sceneControls = Array.Empty<Control>();
    private Control[] allObjectControls = Array.Empty<Control>();

    private static readonly Dictionary<Tool, string> ToolHints = new()
    {
        { Tool.Select,      "ВЫБОР (V): клик — свойства объекта справа, перетаскивание — сдвиг, Ctrl+D — дублировать, Del / ПКМ — удалить." },
        { Tool.Eraser,      "ЛАСТИК (E): зажмите и ведите по объектам для удаления (ПКМ удаляет в любом инструменте)." },
        { Tool.WallFree,    "КИСТЬ СТЕНЫ (W): рисуйте произвольную дорожку или замкнутый контур (переключатель в «Свойствах»)." },
        { Tool.WallLine,    "ОТРЕЗОК (L): перегородка или наклонная рельса (в бильярдах концы дают дифракцию волны)." },
        { Tool.WallRect,    "ПРЯМОУГОЛЬНИК (R): потяните из угла в угол — замкнутая прямоугольная стена или преграда." },
        { Tool.WallEllipse, "ЭЛЛИПС (O): потяните рамку — гладкая эллиптическая арена (фокусирует каустики волн)." },
        { Tool.WallCircle,  "ТОЧНЫЙ КРУГ (C): зажмите в центре и потяните радиус — аналитическая круглая арена без погрешностей." },
        { Tool.WallPoly,    "МНОГОУГОЛЬНИК (P): из центра к вершине — правильный N-угольник (число граней 3…24 в панели справа)." },
        { Tool.WallStar,    "ЗВЕЗДА (S): потяните из центра — N-лучевая звезда (число лучей и внутренний радиус настраиваются справа)." },
        { Tool.ArcRing,     "КОЛЬЦО РОТОРА (A): потяните из центра к месту выреза — аналитическая дуга с пустым сектором." },
        { Tool.GoalFree,    "ЦЕЛЬ-КИСТЬ (F): обведите произвольную зелёную зону финиша." },
        { Tool.GoalRect,    "ЦЕЛЬ-ПРЯМОУГОЛЬНИК (T): прямоугольная зона финиша." },
        { Tool.GoalEllipse, "ЦЕЛЬ-ЭЛЛИПС (Y): круглая или овальная зона финиша." },
        { Tool.GoalPoly,    "ЦЕЛЬ-МНОГОУГОЛЬНИК (G): правильная многоугольная зона финиша." },
        { Tool.Ball,        "ШАР (B): клик — шар с v=0, зажать и потянуть — задать вектор броска. Клик по шару — изменить его скорость/радиус." },
        { Tool.WaveSource,  "ИСТОЧНИК ВОЛНЫ (U): кликните внутри фигуры, чтобы поставить излучатель непрерывного волнового фронта." },
    };

    public GameCanvas Canvas => canvas;
    public string CurrentSavePath => fileName;
    public SliderButton WaveRaysInspectorSlider => scWaveRays;

    public EditorView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        runtime = new LevelRuntime(canvas);
        runtime.StateChanged += () =>
        {
            btnPauseTest.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
            btnPauseTest.Invalidate();
        };

        BuildTopBar();
        BuildPalette();
        BuildInspector();
        BuildStatusBar();

        // Порядок добавления в WinForms: Fill первым, затем Bottom, Left/Right, Top последним
        Controls.Add(canvas);
        Controls.Add(inspectorPanel);
        Controls.Add(palettePanel);
        Controls.Add(statusBar);
        Controls.Add(topPanel);

        chipGrid.IsOn = true;
        chipFx.IsOn = true;
        nameBox.Text = canvas.Design.Name;

        canvas.SelectionChanged += OnCanvasSelectionChanged;
        canvas.BallRadiusChanged += SyncInspector;
        canvas.BallSpeedChanged += SyncInspector;
        canvas.DesignChanged += () => { palettePanel.Invalidate(); SyncInspector(); };
        canvas.Won += () =>
        {
            if (canvas.Testing)
                FlashStatus("  Цель достигнута! Продолжайте тест или нажмите «СТОП» (Esc) для возврата к правкам.");
        };

        uiTimer.Tick += (s, e) => RefreshUiState();
        uiTimer.Start();

        Action onTheme = () =>
        {
            BackColor = Theme.Bg;
            topPanel.BackColor = Theme.Panel;
            palettePanel.ApplyTheme();
            inspectorPanel.ApplyTheme();
            statusBar.BackColor = Theme.Panel;
            statusLbl.BackColor = Theme.Panel;
            statusLbl.ForeColor = Theme.TextDim;
            coordLbl.BackColor = Theme.Panel;
            coordLbl.ForeColor = Theme.TextDim;
            nameBox.ApplyTheme();
            scHintBox.ApplyTheme();
            Invalidate(true);
        };
        Theme.Changed += onTheme;
        Disposed += (s, e) => { Theme.Changed -= onTheme; uiTimer.Stop(); };

        SelectTool(Tool.WallFree);
        SyncInspector();
        RefreshUiState();
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        LayoutPalette();
        LayoutInspector();
    }

    private static NeoButton MakeTopChip(string text, int width) =>
        new() { Text = text, IsToggle = true, Size = new Size(width, 34), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };

    private static NeoButton MakeSceneChip(string text, int width, AccentRole role = AccentRole.None) =>
        new() { Text = text, IsToggle = true, Role = role, Size = new Size(width, 28), Font = new Font("Segoe UI", 7.8F, FontStyle.Bold) };

    private static string FormatCount(double v) =>
        v >= 1000 ? (Math.Round(v / 100.0) / 10.0).ToString("0.#") + "K" : Math.Round(v).ToString();

    private static int SnapWaveRays(double v)
    {
        if (v < 100) return Math.Max(12, (int)Math.Round(v / 6.0) * 6);
        if (v < 1000) return (int)Math.Round(v / 20.0) * 20;
        if (v < 10000) return (int)Math.Round(v / 200.0) * 200;
        return (int)Math.Round(v / 1000.0) * 1000;
    }

    // ------------------------------------------------- верхняя капсула

    private void BuildTopBar()
    {
        var btnNew = TopIconBtn(IconPainter.Doc, "Новый пустой уровень");
        var btnSave = TopIconBtn(IconPainter.Floppy, "Быстро сохранить уровень прямо в игре (Ctrl+S)");

        tips.SetToolTip(btnSaves, "Менеджер сохранений внутри игры (Ctrl+O): неограниченные слоты, загрузка, удаление, импорт и экспорт .json");

        btnNew.Click += (s, e) => NewLevel();
        btnSaves.Click += (s, e) => OpenSavesManager();
        btnSave.Click += (s, e) => SaveLevel();

        tips.SetToolTip(nameBox.Inner, "Название уровня (отображается в меню «Уровни»)");
        nameBox.InputChanged += (s, e) =>
        {
            if (!syncingUi) canvas.Design.Name = nameBox.Text;
        };

        tips.SetToolTip(btnTemplates, "Галерея готовых уровней, бильярдов и арен «Ротора» для изучения и переделки");
        btnTemplates.Click += (s, e) => OpenTemplatesGallery();

        tips.SetToolTip(btnUndo, "Отменить действие (Ctrl+Z)");
        tips.SetToolTip(btnRedo, "Повторить действие (Ctrl+Y)");
        tips.SetToolTip(btnClear, "Очистить сцену (с поддержкой отмены Ctrl+Z)");
        btnUndo.Click += (s, e) => { canvas.DoUndo(); SyncInspector(); RefreshUiState(); };
        btnRedo.Click += (s, e) => { canvas.DoRedo(); SyncInspector(); RefreshUiState(); };
        btnClear.Click += (s, e) =>
        {
            if (canvas.Testing) ToggleTest();
            canvas.ClearAll();
            SyncInspector();
            FlashStatus("  Сцена очищена (Ctrl+Z — вернуть).");
        };

        tips.SetToolTip(chipGrid, "Точечная сетка координат (40 px)");
        tips.SetToolTip(chipSnap, "Привязка вершин к сетке 20 px и углов к 15°");
        tips.SetToolTip(chipFx, "Шейдерная пост-обработка (свечение, аберрация, виньетка)");
        chipGrid.Click += (s, e) => { canvas.ShowGrid = chipGrid.IsOn; canvas.Invalidate(); };
        chipSnap.Click += (s, e) => canvas.SnapOn = chipSnap.IsOn;
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;

        tips.SetToolTip(btnRestartTest, "Перезапустить тест / переиспустить волну (R в режиме теста)");
        tips.SetToolTip(btnPauseTest, "Пауза / продолжение теста");
        tips.SetToolTip(btnInspector, "Показать или скрыть правую панель свойств сцены и объектов");
        tips.SetToolTip(btnTest, "Запустить или остановить живой тест уровня (Пробел / Esc)");

        btnRestartTest.Click += (s, e) =>
        {
            if (!canvas.Testing) return;
            runtime.RestartCurrent();
            btnPauseTest.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
            btnPauseTest.Invalidate();
        };
        btnPauseTest.Click += (s, e) =>
        {
            if (!canvas.Testing) return;
            canvas.Paused = !canvas.Paused;
            if (!runtime.IsWaitingForFirstDrop)
                canvas.SuppressPauseBanner = false;
            btnPauseTest.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
            btnPauseTest.Invalidate();
            canvas.Invalidate();
        };
        btnInspector.Click += (s, e) =>
        {
            inspectorPanel.Visible = btnInspector.IsOn;
            canvas.Invalidate();
        };
        btnTest.Click += (s, e) => ToggleTest();

        topPanel.AddLeft(btnNew).AddLeft(btnSaves).AddLeft(btnSave)
                .AddLeft(nameBox).AddLeft(btnTemplates)
                .AddLeft(btnUndo).AddLeft(btnRedo).AddLeft(btnClear);
        topPanel.AddCenter(chipGrid).AddCenter(chipSnap).AddCenter(chipFx);
        topPanel.AddRight(btnRestartTest).AddRight(btnPauseTest).AddRight(btnInspector).AddRight(btnTest);
    }

    private NeoButton TopIconBtn(Action<Graphics, Rectangle, Color> icon, string tip)
    {
        var b = new NeoButton { Icon = icon, Size = new Size(38, 38), IconPx = 19 };
        tips.SetToolTip(b, tip);
        return b;
    }

    // ------------------------------------------------- левая палитра инструментов

    private void BuildPalette()
    {
        AddToolBtn(Tool.Select,      IconPainter.CursorArrow, "V", "Выбор и перемещение объектов (V)", AccentRole.None);
        AddToolBtn(Tool.Eraser,      IconPainter.Eraser,      "E", "Ластик — удаление объектов (E)", AccentRole.Red);

        AddToolBtn(Tool.WallFree,    IconPainter.Pencil,      "W", "Стена: рисование от руки (W)", AccentRole.Accent);
        AddToolBtn(Tool.WallLine,    IconPainter.LineSeg,     "L", "Стена: прямой отрезок / перегородка (L)", AccentRole.Accent);
        AddToolBtn(Tool.WallRect,    IconPainter.Rect,        "R", "Стена: прямоугольник (R)", AccentRole.Accent);
        AddToolBtn(Tool.WallEllipse, IconPainter.Ellipse,     "O", "Стена: гладкий эллипс (O)", AccentRole.Accent);
        AddToolBtn(Tool.WallCircle,  IconPainter.CircleExact, "C", "Стена: точный аналитический круг (C)", AccentRole.Accent);
        AddToolBtn(Tool.WallPoly,    IconPainter.PolygonHex,  "P", "Стена: правильный N-угольник (P)", AccentRole.Accent);
        AddToolBtn(Tool.WallStar,    IconPainter.StarShape,   "S", "Стена: N-лучевая звезда (S)", AccentRole.Accent);
        AddToolBtn(Tool.ArcRing,     IconPainter.ArcGap,      "A", "Ротор: аналитическое кольцо с вырезом (A)", AccentRole.Orange);

        AddToolBtn(Tool.GoalFree,    IconPainter.Pencil,      "F", "Цель: произвольный контур (F)", AccentRole.Green);
        AddToolBtn(Tool.GoalRect,    IconPainter.Rect,        "T", "Цель: прямоугольник (T)", AccentRole.Green);
        AddToolBtn(Tool.GoalEllipse, IconPainter.Ellipse,     "Y", "Цель: эллипс / круг (Y)", AccentRole.Green);
        AddToolBtn(Tool.GoalPoly,    IconPainter.PolygonHex,  "G", "Цель: правильный многоугольник (G)", AccentRole.Green);

        AddToolBtn(Tool.Ball,        IconPainter.Ball,        "B", "Шар: позиция и вектор скорости (B)", AccentRole.Orange);
        AddToolBtn(Tool.WaveSource,  IconPainter.WavePoint,   "U", "Источник волнового фронта (U)", AccentRole.Green);

        tips.SetToolTip(btnQuickSpiral, "Создать серию концентрических колец / спираль «Ротора»");
        btnQuickSpiral.Click += (s, e) => OpenSpiralGenerator(btnQuickSpiral);
        palettePanel.Controls.Add(btnQuickSpiral);

        tips.SetToolTip(quickColorBtn, "Цвет новых шаров (или СПЕКТР — радужное чередование)");
        quickColorBtn.Changed += () =>
        {
            if (syncingUi) return;
            ApplyNewBallColor(quickColorBtn.Selected);
        };
        palettePanel.Controls.Add(quickColorBtn);

        palettePanel.HeadersProvider = () => paletteHeaders;
        palettePanel.FooterStatsProvider = () =>
        {
            var d = canvas.Design;
            int wCount = d.Walls.Count + d.Circles.Count;
            return $"СТЕН: {wCount}  ·  КОЛЕЦ: {d.Arcs.Count}\nЦЕЛЕЙ: {d.Goals.Count}  ·  ШАРОВ: {d.Balls.Count}";
        };
        palettePanel.Resize += (s, e) => LayoutPalette();
        LayoutPalette();
    }

    private void AddToolBtn(Tool tool, Action<Graphics, Rectangle, Color> icon, string keyBadge, string tip, AccentRole role)
    {
        var b = new NeoButton
        {
            Icon = icon,
            KeyBadge = keyBadge,
            Size = new Size(48, 38),
            IconPx = 20,
            Role = role,
            Tag = tool,
        };
        tips.SetToolTip(b, tip);
        b.Click += (s, e) => SelectTool(tool);
        toolBtns.Add(b);
        palettePanel.Controls.Add(b);
    }

    private void LayoutPalette()
    {
        if (palettePanel.Width <= 0) return;
        float k = Math.Max(1f, palettePanel.Width / (float)BasePaletteWidth);
        int padX = (int)Math.Round(11 * k);
        int colGap = (int)Math.Round(6 * k);
        int rowGap = (int)Math.Round(5 * k);
        int headH = (int)Math.Round(18 * k);
        int secGap = (int)Math.Round(8 * k);

        paletteHeaders.Clear();
        int y = (int)Math.Round(8 * k);

        void PlacePair(int idx0, int idx1)
        {
            var b0 = toolBtns[idx0];
            var b1 = toolBtns[idx1];
            b0.Location = new Point(padX, y);
            b1.Location = new Point(padX + b0.Width + colGap, y);
            y += b0.Height + rowGap;
        }

        paletteHeaders.Add((y, "ПРАВКА"));
        y += headH;
        PlacePair(0, 1);

        y += secGap;
        paletteHeaders.Add((y, "СТЕНЫ"));
        y += headH;
        PlacePair(2, 3);
        PlacePair(4, 5);
        PlacePair(6, 7);
        PlacePair(8, 9);

        y += secGap;
        paletteHeaders.Add((y, "ЦЕЛИ"));
        y += headH;
        PlacePair(10, 11);
        PlacePair(12, 13);

        y += secGap;
        paletteHeaders.Add((y, "ОБЪЕКТЫ"));
        y += headH;
        PlacePair(14, 15);

        y += secGap;
        paletteHeaders.Add((y, "БЫСТРО"));
        y += headH;
        btnQuickSpiral.Location = new Point(padX, y);
        y += btnQuickSpiral.Height + rowGap;
        quickColorBtn.Location = new Point(padX, y);
        palettePanel.FooterTopY = quickColorBtn.Bottom + (int)Math.Round(10 * k);
        palettePanel.Invalidate();
    }

    // ------------------------------------------------- правый инспектор (СЦЕНА / ОБЪЕКТ)

    private void BuildInspector()
    {
        tabSceneBtn.Click += (s, e) => SetInspectorTab(0);
        tabObjBtn.Click += (s, e) => SetInspectorTab(1);
        inspectorPanel.Controls.Add(tabSceneBtn);
        inspectorPanel.Controls.Add(tabObjBtn);

        // --- Вкладка «СЦЕНА» ---
        tips.SetToolTip(presetClassicBtn, "Классический режим броска шаров с прицелом направления");
        tips.SetToolTip(presetRotorBtn, "Включить набор правил «Ротора»: рассыпание колец, клик-старт v=0, упругую гравитацию и вращение");
        tips.SetToolTip(presetWaveBtn, "Включить режим математического бильярда с непрерывной волной и дифракцией");

        presetClassicBtn.Click += (s, e) => ApplyModePreset(0);
        presetRotorBtn.Click += (s, e) => ApplyModePreset(1);
        presetWaveBtn.Click += (s, e) => ApplyModePreset(2);

        tips.SetToolTip(scGravity, "Притяжение вниз (клавиша G в игре)");
        tips.SetToolTip(scBorder, "Упругие стенки по краям экрана 1280×720");
        tips.SetToolTip(scCollide, "Упругие столкновения шаров друг с другом");
        tips.SetToolTip(scElastic, "Без затухания микро-отскоков при гравитации (для вечного движения в «Роторе»)");
        tips.SetToolTip(scGravScale, "Множитель силы тяжести (в классике 1.00×, в «Роторе» ~0.58×)");

        scGravity.Click += (s, e) => MutateScene(d => d.Gravity = scGravity.IsOn);
        scBorder.Click += (s, e) => MutateScene(d => d.Border = scBorder.IsOn);
        scCollide.Click += (s, e) => MutateScene(d => d.NoBallCollisions = !scCollide.IsOn);
        scElastic.Click += (s, e) => MutateScene(d => d.ElasticBounce = scElastic.IsOn);
        scGravScale.Changed += v => MutateScene(d => d.GravityScale = Math.Round(v, 2));

        tips.SetToolTip(scSpinSpeed, "Угловая скорость вращения колец и стенок вокруг их центров (рад/с; < 0 — против часовой)");
        tips.SetToolTip(scRotorRules, "Кольца рассыпаются на осколки при вылете шара наружу + счётчик колец в центре");
        tips.SetToolTip(scInstantDrop, "При старте уровня вращение ждёт на паузе, а клик ЛКМ ставит шар со скоростью 0");
        tips.SetToolTip(scContract, "Внешние кольца плавно сжимаются к внутреннему радиусу по мере прохождения");
        tips.SetToolTip(scFreezeGap, "Останавливать вращение колец в момент, когда шар пролетает сквозь пустой вырез");
        tips.SetToolTip(scReverseBreak, "Каждый разрушенный круг меняет направление вращения всех остальных («РЕВЕРС»)");
        tips.SetToolTip(scAccelBreak, "Множитель ускорения вращения при каждом разрушенном кольце («РАЗГОН»)");
        tips.SetToolTip(scOpenSpiralGen, "Открыть генератор концентрических колец и спиралей «Ротора»");

        scSpinSpeed.Changed += v =>
        {
            double clean = Math.Abs(v) < 0.04 ? 0.0 : Math.Round(v, 2);
            MutateScene(d => d.SpinSpeed = clean);
        };
        scRotorRules.Click += (s, e) => MutateScene(d => d.RotorRules = scRotorRules.IsOn);
        scInstantDrop.Click += (s, e) => MutateScene(d => d.InstantDrop = scInstantDrop.IsOn);
        scContract.Click += (s, e) => MutateScene(d => d.ContractRings = scContract.IsOn);
        scFreezeGap.Click += (s, e) => MutateScene(d => d.FreezeSpinInGap = scFreezeGap.IsOn);
        scReverseBreak.Click += (s, e) => MutateScene(d => d.ReverseOnBreak = scReverseBreak.IsOn);
        scAccelBreak.Changed += v => MutateScene(d => d.AccelOnBreak = Math.Round(v, 3));
        scOpenSpiralGen.Click += (s, e) => OpenSpiralGenerator(scOpenSpiralGen);

        tips.SetToolTip(scWaveMode, "Превратить уровень в арену «Волнового фронта» с точечным источником лучей");
        tips.SetToolTip(scWaveCont, "Непрерывный адаптивный фронт + дифракционные волны Келлера на углах (выкл — дискретные частицы)");
        tips.SetToolTip(scWaveRays, "Начальное количество лучей (активно в режиме частиц, когда «ДИФРАКЦИЯ» выключена)");
        tips.SetToolTip(scWaveSpeed, "Скорость распространения волнового фронта (px/s)");
        tips.SetToolTip(scHintBox.Inner, "Текст подсказки в нижней полосе при запуске уровня");

        scWaveMode.Click += (s, e) => MutateScene(d => d.WaveEmitter = scWaveMode.IsOn);
        scWaveCont.Click += (s, e) => MutateScene(d => d.WaveContinuous = scWaveCont.IsOn);
        scWaveRays.Changed += v =>
        {
            int r = SnapWaveRays(v);
            scWaveRays.Value = r;
            MutateScene(d => d.WaveRays = r, pushUndo: false);
        };
        scWaveSpeed.Changed += v => MutateScene(d => d.WaveSpeed = Math.Round(v / 10.0) * 10.0, pushUndo: false);
        scHintBox.InputChanged += (s, e) =>
        {
            if (!syncingUi) canvas.Design.Hint = scHintBox.Text;
        };

        sceneControls = new Control[]
        {
            presetClassicBtn, presetRotorBtn, presetWaveBtn,
            scGravity, scBorder, scCollide, scElastic, scGravScale,
            scSpinSpeed, scRotorRules, scInstantDrop, scContract, scFreezeGap, scReverseBreak, scAccelBreak, scOpenSpiralGen,
            scWaveMode, scWaveCont, scWaveRays, scWaveSpeed, scHintBox,
        };
        foreach (var c in sceneControls) inspectorPanel.Controls.Add(c);

        // --- Вкладка «ОБЪЕКТ» ---
        objBallColor.Changed += () =>
        {
            if (syncingUi) return;
            if (canvas.SelectedBall != null)
            {
                canvas.PushUndo();
                canvas.SelectedBall.Argb = objBallColor.Selected == BallColors.Spectrum ? 0 : objBallColor.Selected;
                canvas.NotifyDesignMutated();
            }
            ApplyNewBallColor(objBallColor.Selected);
        };
        objBallR.Changed += v =>
        {
            if (syncingUi) return;
            double r = Math.Round(v);
            canvas.NewBallR = r;
            if (canvas.SelectedBall != null)
            {
                canvas.SelectedBall.R = r;
                canvas.NotifyDesignMutated();
            }
        };
        objBallV.Changed += v =>
        {
            if (syncingUi) return;
            double sp = Math.Round(v / 25.0) * 25.0;
            canvas.SetBallSpeed(sp);
            SyncInspector();
        };
        objBallAng.Changed += v =>
        {
            if (syncingUi || canvas.SelectedBall == null) return;
            double rad = v * Math.PI / 180.0;
            double sp = canvas.SelectedBall.HasVel && canvas.SelectedBall.Vel.Len > 1 ? canvas.SelectedBall.Vel.Len : Math.Max(100, canvas.EditorBallSpeed);
            canvas.SelectedBall.Vel = new Vec2(Math.Cos(rad), Math.Sin(rad)) * sp;
            canvas.SelectedBall.HasVel = true;
            canvas.NotifyDesignMutated();
        };
        objBallZeroV.Click += (s, e) =>
        {
            if (syncingUi) return;
            if (canvas.SelectedBall != null)
            {
                canvas.PushUndo();
                if (objBallZeroV.IsOn)
                {
                    canvas.SelectedBall.Vel = new Vec2();
                    canvas.SelectedBall.HasVel = false;
                }
                else
                {
                    double sp = Math.Max(200, canvas.EditorBallSpeed);
                    canvas.SelectedBall.Vel = new Vec2(sp, 0);
                    canvas.SelectedBall.HasVel = true;
                }
                canvas.NotifyDesignMutated();
            }
            else
            {
                canvas.EditorBallSpeed = objBallZeroV.IsOn ? 0 : 800;
            }
            SyncInspector();
        };

        objCircleR.Changed += v =>
        {
            if (syncingUi || canvas.SelectedObject is not CircleData cd) return;
            cd.Radius = Math.Round(v);
            canvas.NotifyDesignMutated();
        };

        objArcR.Changed += v =>
        {
            if (syncingUi || canvas.SelectedObject is not ArcData ad) return;
            ad.Radius = Math.Round(v);
            canvas.NotifyDesignMutated();
        };
        objArcGap.Changed += v =>
        {
            if (syncingUi) return;
            double gap = Math.Clamp(Math.Round(v), 12, 260);
            canvas.EditorArcGapDeg = gap;
            if (canvas.SelectedObject is ArcData ad)
            {
                ad.SweepDeg = 360.0 - gap;
                canvas.NotifyDesignMutated();
            }
        };
        objArcStart.Changed += v =>
        {
            if (syncingUi || canvas.SelectedObject is not ArcData ad) return;
            ad.StartDeg = Math.Round(v) % 360.0;
            canvas.NotifyDesignMutated();
        };
        objArcSpinMul.Changed += v =>
        {
            if (syncingUi) return;
            double m = Math.Abs(v) < 0.04 ? 0.0 : Math.Round(v, 2);
            canvas.EditorArcSpinMul = m;
            if (canvas.SelectedObject is ArcData ad)
            {
                ad.SpinMul = m;
                canvas.NotifyDesignMutated();
            }
        };
        objArcThick.Changed += v =>
        {
            if (syncingUi) return;
            double hw = Math.Round(v, 1) * 0.5;
            canvas.EditorArcHalfWidth = hw;
            if (canvas.SelectedObject is ArcData ad)
            {
                ad.HalfWidth = hw;
                canvas.NotifyDesignMutated();
            }
        };
        objArcFlipSpin.Click += (s, e) =>
        {
            if (canvas.SelectedObject is ArcData ad)
            {
                canvas.PushUndo();
                ad.SpinMul = ad.SpinMul == 0 ? -1.0 : -ad.SpinMul;
                canvas.EditorArcSpinMul = ad.SpinMul;
                canvas.NotifyDesignMutated();
                SyncInspector();
            }
            else
            {
                canvas.EditorArcSpinMul = canvas.EditorArcSpinMul == 0 ? -1.0 : -canvas.EditorArcSpinMul;
                SyncInspector();
            }
        };

        objPolySides.Changed += v =>
        {
            if (syncingUi) return;
            canvas.PolySides = Math.Clamp((int)Math.Round(v), 3, 24);
        };
        objStarRatio.Changed += v =>
        {
            if (syncingUi) return;
            canvas.StarInnerRatio = Math.Clamp(Math.Round(v) / 100.0, 0.18, 0.85);
        };
        objPolyClosed.Click += (s, e) =>
        {
            if (syncingUi) return;
            if (canvas.SelectedObject is PolyData pd)
            {
                if (pd.Pts.Count >= 3 || !objPolyClosed.IsOn)
                {
                    canvas.PushUndo();
                    pd.Closed = objPolyClosed.IsOn;
                    canvas.NotifyDesignMutated();
                }
            }
            else
            {
                canvas.EditorFreehandClosed = objPolyClosed.IsOn;
            }
            SyncInspector();
        };
        objPolyPivot.Click += (s, e) =>
        {
            if (syncingUi) return;
            if (canvas.SelectedObject is PolyData pd)
            {
                canvas.PushUndo();
                pd.Pivot = objPolyPivot.IsOn ? GameCanvas.Centroid(pd.Pts) : null;
                canvas.NotifyDesignMutated();
            }
            else
            {
                canvas.EditorWallLocalPivot = objPolyPivot.IsOn;
            }
            SyncInspector();
        };
        objConvertWallGoal.Click += (s, e) => ConvertSelectedWallOrGoal();

        objWaveColor.Changed += () =>
        {
            if (syncingUi) return;
            canvas.Design.WaveArgb = objWaveColor.Selected == BallColors.Spectrum ? 0 : objWaveColor.Selected;
            canvas.Invalidate();
        };
        objWaveRadius.Changed += v =>
        {
            if (syncingUi) return;
            canvas.Design.WaveRayRadius = Math.Round(v);
        };

        objCenterBtn.Click += (s, e) => { canvas.CenterSelected(); SyncInspector(); };
        objDupBtn.Click += (s, e) => { canvas.DuplicateSelected(); SyncInspector(); };
        objDelBtn.Click += (s, e) => { canvas.DeleteSelected(); SyncInspector(); };

        allObjectControls = new Control[]
        {
            objBallColor, objBallR, objBallV, objBallAng, objBallZeroV,
            objCircleR,
            objArcR, objArcGap, objArcStart, objArcSpinMul, objArcThick, objArcFlipSpin,
            objPolySides, objStarRatio, objPolyClosed, objPolyPivot, objConvertWallGoal,
            objWaveColor, objWaveRadius,
            objCenterBtn, objDupBtn, objDelBtn,
        };
        foreach (var c in allObjectControls) inspectorPanel.Controls.Add(c);

        inspectorPanel.HeadersProvider = () => inspectorHeaders;
        inspectorPanel.Resize += (s, e) => LayoutInspector();
        LayoutInspector();
    }

    private void MutateScene(Action<LevelData> edit, bool pushUndo = true)
    {
        if (syncingUi) return;
        if (pushUndo) canvas.PushUndo();
        edit(canvas.Design);
        canvas.RotorSkin = canvas.Testing ? canvas.Design.RotorRules : false;
        canvas.NotifyDesignMutated();
        SyncInspector();
    }

    private void ApplyModePreset(int mode)
    {
        if (syncingUi) return;
        canvas.PushUndo();
        var d = canvas.Design;
        switch (mode)
        {
            case 0: // КЛАССИКА
                d.WaveEmitter = false;
                d.RotorRules = false;
                d.InstantDrop = false;
                break;
            case 1: // РОТОР
                d.WaveEmitter = false;
                d.RotorRules = true;
                d.InstantDrop = true;
                d.Gravity = true;
                if (Math.Abs(d.GravityScale - 1.0) < 0.01) d.GravityScale = 0.58;
                d.ElasticBounce = true;
                d.ContractRings = true;
                d.FreezeSpinInGap = true;
                if (Math.Abs(d.SpinSpeed) < 0.05) d.SpinSpeed = 1.10;
                break;
            case 2: // ВОЛНА
                d.RotorRules = false;
                d.InstantDrop = false;
                d.WaveEmitter = true;
                d.WaveContinuous = true;
                d.NoBallCollisions = true;
                break;
        }
        canvas.NotifyDesignMutated();
        SyncInspector();
    }

    private void ApplyNewBallColor(int selected)
    {
        syncingUi = true;
        quickColorBtn.Selected = selected;
        objBallColor.Selected = selected;
        quickColorBtn.Invalidate();
        objBallColor.Invalidate();
        canvas.NewBallSpectrum = selected == BallColors.Spectrum;
        canvas.NewBallArgb = selected;
        syncingUi = false;
    }

    private void ConvertSelectedWallOrGoal()
    {
        if (canvas.SelectedObject is PolyData pd && pd.Pts.Count >= 3)
        {
            canvas.PushUndo();
            var g = new GoalData { Pts = new List<Vec2>(pd.Pts) };
            canvas.Design.Walls.Remove(pd);
            canvas.Design.Goals.Add(g);
            canvas.SelectObject(g);
            canvas.NotifyDesignMutated();
            SyncInspector();
        }
        else if (canvas.SelectedObject is GoalData gd && gd.Pts.Count >= 3)
        {
            canvas.PushUndo();
            var p = new PolyData { Closed = true, Pts = new List<Vec2>(gd.Pts) };
            canvas.Design.Goals.Remove(gd);
            canvas.Design.Walls.Add(p);
            canvas.SelectObject(p);
            canvas.NotifyDesignMutated();
            SyncInspector();
        }
    }

    private void OnCanvasSelectionChanged()
    {
        // Если пользователь явно кликнул объект инструментом «Выбор» — сразу раскрываем его свойства
        if (canvas.CurTool == Tool.Select && canvas.SelectedObject != null && inspectorTab != 1)
            inspectorTab = 1;
        SyncInspector();
    }

    public void SetInspectorTab(int tab)
    {
        inspectorTab = tab == 1 ? 1 : 0;
        SyncInspector();
    }

    private void SyncInspector()
    {
        if (syncingUi) return;
        syncingUi = true;
        try
        {
            var d = canvas.Design;
            tabSceneBtn.IsOn = inspectorTab == 0;
            tabObjBtn.IsOn = inspectorTab == 1;
            tabObjBtn.Text = canvas.SelectedObject != null ? "ОБЪЕКТ •" : "ОБЪЕКТ";
            tabSceneBtn.Invalidate();
            tabObjBtn.Invalidate();

            // СЦЕНА
            presetClassicBtn.IsOn = !d.WaveEmitter && !d.RotorRules;
            presetRotorBtn.IsOn = d.RotorRules && !d.WaveEmitter;
            presetWaveBtn.IsOn = d.WaveEmitter;

            scGravity.IsOn = d.Gravity;
            scBorder.IsOn = d.Border;
            scCollide.IsOn = !d.NoBallCollisions;
            scElastic.IsOn = d.ElasticBounce;
            scGravScale.Value = d.GravityScale <= 0 ? 1.0 : d.GravityScale;
            scGravScale.Enabled = d.Gravity;

            scSpinSpeed.Value = d.SpinSpeed;
            scRotorRules.IsOn = d.RotorRules;
            scInstantDrop.IsOn = d.InstantDrop;
            scContract.IsOn = d.ContractRings;
            scFreezeGap.IsOn = d.FreezeSpinInGap;
            scReverseBreak.IsOn = d.ReverseOnBreak;
            scAccelBreak.Value = Math.Clamp(d.AccelOnBreak <= 0 ? 1.0 : d.AccelOnBreak, 1.0, 1.15);
            scContract.Enabled = d.RotorRules;
            scReverseBreak.Enabled = d.RotorRules;
            scAccelBreak.Enabled = d.RotorRules;
            scFreezeGap.Enabled = d.RotorRules || Math.Abs(d.SpinSpeed) > 0.001 || d.Arcs.Count > 0;

            scWaveMode.IsOn = d.WaveEmitter;
            scWaveCont.IsOn = d.WaveContinuous;
            scWaveRays.Value = Math.Clamp(d.WaveRays <= 0 ? 180 : d.WaveRays, 12, 20000);
            scWaveSpeed.Value = Math.Clamp(d.WaveSpeed <= 0 ? 320 : d.WaveSpeed, 60, 1000);
            scWaveCont.Enabled = d.WaveEmitter;
            scWaveRays.Enabled = d.WaveEmitter && !d.WaveContinuous;
            scWaveSpeed.Enabled = d.WaveEmitter;
            if (!scHintBox.FocusedInput && scHintBox.Text != (d.Hint ?? ""))
                scHintBox.Text = d.Hint ?? "";

            foreach (var c in sceneControls) c.Invalidate();

            // ОБЪЕКТ / АКТИВНЫЙ ИНСТРУМЕНТ
            var sel = canvas.SelectedObject;
            if (sel is BallData sb)
            {
                objHeaderTitle = "ВЫБРАНО: ШАР";
                objBallColor.Selected = sb.Argb == 0 ? unchecked((int)0xFFF0F3FF) : sb.Argb;
                objBallR.Value = sb.R;
                objBallV.Value = sb.HasVel ? sb.Vel.Len : 0;
                double deg = sb.HasVel && sb.Vel.Len > 1 ? Math.Atan2(sb.Vel.Y, sb.Vel.X) * 180.0 / Math.PI : 0;
                if (deg < 0) deg += 360;
                objBallAng.Value = deg;
                objBallZeroV.IsOn = !sb.HasVel || sb.Vel.Len <= 1;
            }
            else if (sel is CircleData sc)
            {
                objHeaderTitle = "ВЫБРАНО: ТОЧНЫЙ КРУГ";
                objCircleR.Value = sc.Radius;
            }
            else if (sel is ArcData sa)
            {
                objHeaderTitle = "ВЫБРАНО: КОЛЬЦО РОТОРА";
                objArcR.Value = sa.Radius;
                objArcGap.Value = Math.Clamp(360.0 - sa.SweepDeg, 12, 260);
                objArcStart.Value = ((sa.StartDeg % 360) + 360) % 360;
                objArcSpinMul.Value = sa.SpinMul;
                objArcThick.Value = sa.HalfWidth * 2.0;
            }
            else if (sel is PolyData sp)
            {
                objHeaderTitle = sp.Closed ? "ВЫБРАНО: ЗАМКНУТАЯ СТЕНА" : "ВЫБРАНО: ПОЛИЛИНИЯ-СТЕНА";
                objPolyClosed.IsOn = sp.Closed;
                objPolyPivot.IsOn = sp.Pivot.HasValue;
                objConvertWallGoal.Text = "СДЕЛАТЬ ЦЕЛЬЮ (ЗЕЛЁНОЙ)";
                objConvertWallGoal.Role = AccentRole.Green;
            }
            else if (sel is GoalData)
            {
                objHeaderTitle = "ВЫБРАНО: ЦЕЛЕВАЯ ЗОНА";
                objConvertWallGoal.Text = "СДЕЛАТЬ СТЕНОЙ (СИНЕЙ)";
                objConvertWallGoal.Role = AccentRole.Accent;
            }
            else if (sel is WaveSourceHandle)
            {
                objHeaderTitle = "ВЫБРАНО: ИСТОЧНИК ВОЛНЫ";
                objWaveColor.Selected = d.WaveArgb == 0 ? BallColors.Spectrum : d.WaveArgb;
                objWaveRadius.Value = d.WaveRayRadius <= 0 ? 3 : d.WaveRayRadius;
            }
            else
            {
                objHeaderTitle = canvas.CurTool switch
                {
                    Tool.ArcRing => "НОВЫЕ КОЛЬЦА РОТОРА",
                    Tool.WallPoly or Tool.GoalPoly => "НОВЫЙ МНОГОУГОЛЬНИК",
                    Tool.WallStar => "НОВАЯ ЗВЕЗДА",
                    Tool.WaveSource => "ИСТОЧНИК ВОЛНЫ",
                    Tool.WallFree => "КИСТЬ СТЕНЫ",
                    _ => "НОВЫЕ ШАРЫ И ФИГУРЫ",
                };
                objBallColor.Selected = canvas.NewBallSpectrum ? BallColors.Spectrum : canvas.NewBallArgb;
                objBallR.Value = canvas.NewBallR;
                objBallV.Value = canvas.EditorBallSpeed;
                objBallZeroV.IsOn = canvas.EditorBallSpeed <= 1;
                objArcGap.Value = canvas.EditorArcGapDeg;
                objArcSpinMul.Value = canvas.EditorArcSpinMul;
                objArcThick.Value = canvas.EditorArcHalfWidth * 2.0;
                objPolySides.Value = canvas.PolySides;
                objStarRatio.Value = Math.Round(canvas.StarInnerRatio * 100.0);
                objPolyClosed.IsOn = canvas.EditorFreehandClosed;
                objPolyPivot.IsOn = canvas.EditorWallLocalPivot;
                objWaveColor.Selected = d.WaveArgb == 0 ? BallColors.Spectrum : d.WaveArgb;
                objWaveRadius.Value = d.WaveRayRadius <= 0 ? 3 : d.WaveRayRadius;
            }

            foreach (var c in allObjectControls) c.Invalidate();
            LayoutInspector();
        }
        finally
        {
            syncingUi = false;
        }
    }

    private void LayoutInspector()
    {
        if (inspectorPanel.Width <= 0) return;
        float k = Math.Max(1f, inspectorPanel.Width / (float)BaseInspectorWidth);
        int padX = (int)Math.Round(12 * k);
        int colGap = (int)Math.Round(6 * k);
        int rowGap = (int)Math.Round(5 * k);
        int headH = (int)Math.Round(17 * k);
        int secGap = (int)Math.Round(7 * k);

        tabSceneBtn.Location = new Point(padX, (int)Math.Round(10 * k));
        tabObjBtn.Location = new Point(padX + tabSceneBtn.Width + colGap, (int)Math.Round(10 * k));

        inspectorHeaders.Clear();
        bool showScene = inspectorTab == 0;
        foreach (var c in sceneControls) c.Visible = showScene;
        foreach (var c in allObjectControls) c.Visible = false;

        int y = tabSceneBtn.Bottom + (int)Math.Round(10 * k);

        if (showScene)
        {
            inspectorHeaders.Add((y, "ТИП УРОВНЯ"));
            y += headH;
            int g3 = (int)Math.Round(5 * k);
            presetClassicBtn.Location = new Point(padX, y);
            presetRotorBtn.Location = new Point(presetClassicBtn.Right + g3, y);
            presetWaveBtn.Location = new Point(presetRotorBtn.Right + g3, y);
            y += presetClassicBtn.Height + secGap;

            inspectorHeaders.Add((y, "ФИЗИКА МИРА"));
            y += headH;
            scGravity.Location = new Point(padX, y);
            scBorder.Location = new Point(padX + scGravity.Width + colGap, y);
            y += scGravity.Height + rowGap;

            scCollide.Location = new Point(padX, y);
            scElastic.Location = new Point(padX + scCollide.Width + colGap, y);
            y += scCollide.Height + rowGap;

            scGravScale.Location = new Point(padX, y);
            y += scGravScale.Height + secGap;

            inspectorHeaders.Add((y, "ВРАЩЕНИЕ И РОТОР"));
            y += headH;
            scSpinSpeed.Location = new Point(padX, y);
            y += scSpinSpeed.Height + rowGap;

            scRotorRules.Location = new Point(padX, y);
            scInstantDrop.Location = new Point(padX + scRotorRules.Width + colGap, y);
            y += scRotorRules.Height + rowGap;

            scContract.Location = new Point(padX, y);
            scFreezeGap.Location = new Point(padX + scContract.Width + colGap, y);
            y += scContract.Height + rowGap;

            scReverseBreak.Location = new Point(padX, y);
            scAccelBreak.Location = new Point(padX + scReverseBreak.Width + colGap, y);
            y += scReverseBreak.Height + rowGap;

            scOpenSpiralGen.Location = new Point(padX, y);
            y += scOpenSpiralGen.Height + secGap;

            inspectorHeaders.Add((y, "ВОЛНОВОЙ ФРОНТ"));
            y += headH;
            scWaveMode.Location = new Point(padX, y);
            scWaveCont.Location = new Point(padX + scWaveMode.Width + colGap, y);
            y += scWaveMode.Height + rowGap;

            scWaveRays.Location = new Point(padX, y);
            scWaveSpeed.Location = new Point(padX + scWaveRays.Width + colGap, y);
            y += scWaveRays.Height + secGap;

            inspectorHeaders.Add((y, "ПОДСКАЗКА ИГРОКУ"));
            y += headH;
            scHintBox.Location = new Point(padX, y);
            inspectorPanel.Invalidate();
            return;
        }

        // --- Вкладка «ОБЪЕКТ» ---
        inspectorHeaders.Add((y, objHeaderTitle));
        y += headH;

        void ShowFull(Control c)
        {
            c.Visible = true;
            c.Location = new Point(padX, y);
            y += c.Height + rowGap;
        }

        var sel = canvas.SelectedObject;
        var tool = canvas.CurTool;

        if (sel is BallData)
        {
            ShowFull(objBallColor);
            ShowFull(objBallR);
            ShowFull(objBallV);
            ShowFull(objBallAng);
            ShowFull(objBallZeroV);
        }
        else if (sel is CircleData)
        {
            ShowFull(objCircleR);
        }
        else if (sel is ArcData)
        {
            ShowFull(objArcR);
            ShowFull(objArcGap);
            ShowFull(objArcStart);
            ShowFull(objArcSpinMul);
            ShowFull(objArcThick);
            ShowFull(objArcFlipSpin);
        }
        else if (sel is PolyData pd)
        {
            if (pd.Pts.Count >= 3 || pd.Closed) ShowFull(objPolyClosed);
            ShowFull(objPolyPivot);
            if (pd.Pts.Count >= 3) ShowFull(objConvertWallGoal);
        }
        else if (sel is GoalData)
        {
            ShowFull(objConvertWallGoal);
        }
        else if (sel is WaveSourceHandle)
        {
            ShowFull(objWaveColor);
            ShowFull(objWaveRadius);
        }
        else
        {
            // Ничего не выбрано — показываем настройки текущего инструмента рисования + базовые параметры шара
            if (tool == Tool.ArcRing)
            {
                ShowFull(objArcGap);
                ShowFull(objArcSpinMul);
                ShowFull(objArcThick);
                ShowFull(objArcFlipSpin);
            }
            else if (tool is Tool.WallPoly or Tool.GoalPoly)
            {
                ShowFull(objPolySides);
                if (tool == Tool.WallPoly) ShowFull(objPolyPivot);
            }
            else if (tool == Tool.WallStar)
            {
                ShowFull(objPolySides);
                ShowFull(objStarRatio);
                ShowFull(objPolyPivot);
            }
            else if (tool == Tool.WallFree)
            {
                ShowFull(objPolyClosed);
                ShowFull(objPolyPivot);
            }
            else if (tool == Tool.WaveSource)
            {
                ShowFull(objWaveColor);
                ShowFull(objWaveRadius);
            }
            else
            {
                ShowFull(objBallColor);
                ShowFull(objBallR);
                ShowFull(objBallV);
                ShowFull(objBallZeroV);
                y += secGap;
                inspectorHeaders.Add((y, "МНОГОУГОЛЬНИКИ И КОЛЬЦА"));
                y += headH;
                ShowFull(objPolySides);
                ShowFull(objArcGap);
            }
        }

        if (sel != null)
        {
            y += secGap;
            inspectorHeaders.Add((y, "ДЕЙСТВИЯ С ОБЪЕКТОМ"));
            y += headH;
            ShowFull(objCenterBtn);
            if (sel is not WaveSourceHandle)
            {
                objDupBtn.Visible = true;
                objDelBtn.Visible = true;
                objDupBtn.Location = new Point(padX, y);
                objDelBtn.Location = new Point(padX + objDupBtn.Width + colGap, y);
            }
            else
            {
                objDelBtn.Visible = true;
                objDelBtn.Location = new Point(padX, y);
            }
        }

        inspectorPanel.Invalidate();
    }

    // ------------------------------------------------- строка состояния

    private void BuildStatusBar()
    {
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Height = 34;
        statusBar.BackColor = Theme.Panel;

        statusLbl.Dock = DockStyle.Fill;
        statusLbl.BackColor = Theme.Panel;
        statusLbl.ForeColor = Theme.TextDim;
        statusLbl.Font = new Font("Segoe UI", 8.85F);
        statusLbl.TextAlign = ContentAlignment.MiddleLeft;
        statusLbl.Padding = new Padding(4, 0, 0, 0);

        coordLbl.Dock = DockStyle.Right;
        coordLbl.Width = 176;
        coordLbl.BackColor = Theme.Panel;
        coordLbl.ForeColor = Theme.TextDim;
        coordLbl.Font = new Font("Consolas", 9F);
        coordLbl.TextAlign = ContentAlignment.MiddleRight;
        coordLbl.Padding = new Padding(0, 0, 8, 0);

        statusBar.Controls.Add(statusLbl);
        statusBar.Controls.Add(coordLbl);
    }

    // ------------------------------------------------- действия редактора и галереи

    public void SelectTool(Tool t)
    {
        canvas.CurTool = t;
        foreach (var b in toolBtns)
            b.IsOn = b.Tag is Tool tt && tt == t;
        foreach (var b in toolBtns) b.Invalidate();
        if (ToolHints.TryGetValue(t, out var hint))
            statusLbl.Text = "  " + hint;
        SyncInspector();
    }

    public void ToggleTest()
    {
        canvas.Testing = !canvas.Testing;
        if (canvas.Testing)
        {
            canvas.Design.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Мой уровень" : nameBox.Text.Trim();
            runtime.RestartCurrent();
            btnTest.Text = "СТОП";
            btnTest.Role = AccentRole.Red;
            btnTest.Icon = IconPainter.Stop;
            btnRestartTest.Enabled = true;
            btnPauseTest.Enabled = true;
            btnPauseTest.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
            palettePanel.Enabled = false;
            inspectorPanel.Enabled = false;

            var d = canvas.Design;
            statusLbl.Text = d.WaveEmitter
                ? "  ТЕСТ ВОЛНЫ: кликайте по полю для переноса источника  ·  R — переиспустить  ·  Esc / Пробел — вернуться к правкам"
                : d.InstantDrop
                    ? "  ТЕСТ РОТОРА: клик ЛКМ ставит шар (v=0) и запускает вращение  ·  R — заново  ·  Esc — вернуться к правкам"
                    : "  ТЕСТ: ЛКМ — бросок шара  ·  ПКМ — убрать шар  ·  R — перезапуск теста  ·  Esc — вернуться к правкам";
        }
        else
        {
            runtime.StopToEditor();
            btnTest.Text = "ТЕСТ";
            btnTest.Role = AccentRole.Green;
            btnTest.Icon = IconPainter.Play;
            btnRestartTest.Enabled = false;
            btnPauseTest.Enabled = false;
            btnPauseTest.Icon = IconPainter.Pause;
            palettePanel.Enabled = true;
            inspectorPanel.Enabled = true;
            SyncInspector();
        }
        btnTest.Invalidate();
        btnRestartTest.Invalidate();
        btnPauseTest.Invalidate();
    }

    /// <summary>Загрузить готовый шаблон уровня в редактор (с поддержкой отмены Ctrl+Z).</summary>
    public void LoadTemplate(LevelData template)
    {
        if (template == null) return;
        if (canvas.Testing) ToggleTest();
        canvas.PushUndo();
        canvas.Design = template.Clone();
        canvas.Design.Sanitize();
        canvas.SelectObject(null);
        canvas.NotifyDesignMutated();
        fileName = null;
        nameBox.Text = canvas.Design.Name;
        SyncTopChips();
        SyncInspector();
        FlashStatus($"  Загружен шаблон «{canvas.Design.Name}» — редактируйте любые детали или нажмите «ТЕСТ»!");
    }

    /// <summary>Сгенерировать концентрическую спираль колец «Ротора» прямо в текущий уровень.</summary>
    public void GenerateSpiral(
        int count, double r0, double step, double gapDeg, double twistDeg,
        SpiralSpinPattern pattern, bool replaceExisting = true, bool enableRotorRules = true)
    {
        if (canvas.Testing) ToggleTest();
        canvas.PushUndo();
        var d = canvas.Design;
        LevelShapes.AddConcentricSpiral(
            d, new Vec2(LevelData.W / 2, LevelData.H / 2),
            count, r0, step, gapDeg, startDeg0: 145.0, twistDeg: twistDeg,
            pattern: pattern, halfWidth: canvas.EditorArcHalfWidth, clearExisting: replaceExisting);

        if (enableRotorRules)
        {
            d.WaveEmitter = false;
            d.RotorRules = true;
            d.InstantDrop = true;
            d.Gravity = true;
            if (Math.Abs(d.GravityScale - 1.0) < 0.01) d.GravityScale = 0.58;
            d.ElasticBounce = true;
            d.ContractRings = true;
            d.FreezeSpinInGap = true;
            if (Math.Abs(d.SpinSpeed) < 0.05) d.SpinSpeed = 1.15;
        }
        canvas.SelectObject(d.Arcs.Count > 0 ? d.Arcs[0] : null);
        canvas.NotifyDesignMutated();
        SyncInspector();
        FlashStatus($"  Создано колец: {count} (внутр. R={r0:0}, шаг={step:0}, вырез={gapDeg:0}°). Нажмите «ТЕСТ»!");
    }

    private void OpenTemplatesGallery()
    {
        var classicItems = BuiltinLevels.All().Select((l, i) => new LevelCardItem
        {
            Title = $"{i + 1}. {l.Name}",
            Badge = l.Goals.Count > 0 ? "ЦЕЛЬ" : "СВОБОДНЫЙ",
            Preview = l,
            Role = l.Goals.Count > 0 ? AccentRole.Green : AccentRole.Accent,
        }).ToList();

        var rotorBadges = RotorView.PresetBadges;
        var rotorItems = RotorView.BuildPresetLevels().Select((l, i) => new LevelCardItem
        {
            Title = $"{i + 1}. {l.Name}",
            Badge = i < rotorBadges.Length ? rotorBadges[i] : "РОТОР",
            Preview = l,
            Role = AccentRole.Orange,
            RotorStyle = true,
        }).ToList();

        var waveItems = WaveView.BuildFigures().Select((f, i) =>
        {
            var wl = f.Level.Clone();
            wl.WaveEmitter = true;
            wl.WaveSourcePos = f.Source;
            wl.WaveContinuous = true;
            wl.WaveRays = 180;
            wl.WaveSpeed = 320;
            wl.WaveRayRadius = 3;
            wl.NoBallCollisions = true;
            wl.Hint = "Математический бильярд: клик по полю переносит источник волны";
            return new LevelCardItem
            {
                Title = $"{i + 1}. {wl.Name}",
                Badge = "ВОЛНА",
                Preview = wl,
                Source = f.Source,
                Role = AccentRole.Green,
            };
        }).ToList();

        var userItems = LevelStore.ListSaves().Select((s, i) =>
        {
            var l = s.Level;
            return new LevelCardItem
            {
                Title = $"{i + 1}. {l.Name}",
                Badge = l.WaveEmitter ? "ВОЛНА" : l.RotorRules ? "РОТОР" : l.Goals.Count > 0 ? "ЦЕЛЬ" : "СВОЙ",
                Preview = l,
                Source = l.WaveEmitter ? l.WaveSourcePos : null,
                Role = l.WaveEmitter || l.Goals.Count > 0 ? AccentRole.Green : l.RotorRules ? AccentRole.Orange : AccentRole.Accent,
                RotorStyle = l.RotorRules,
            };
        }).ToList();

        var categories = new List<(string, AccentRole, List<LevelCardItem>)>
        {
            ($"УРОВНИ ({classicItems.Count})", AccentRole.Accent, classicItems),
            ($"РОТОР ({rotorItems.Count})", AccentRole.Orange, rotorItems),
            ($"БИЛЬЯРДЫ ({waveItems.Count})", AccentRole.Green, waveItems),
            ($"СОХРАНЕНИЯ ({userItems.Count})", AccentRole.Violet, userItems),
        };

        var popup = new TemplateGalleryPopup(categories);
        popup.OnPick = LoadTemplate;
        PopupPlacement.BelowLeftAligned(popup, btnTemplates);
        popup.Show(this);
        popup.Activate();
    }

    private void OpenSpiralGenerator(Control anchor)
    {
        var popup = new SpiralGeneratorPopup(canvas.EditorArcGapDeg);
        popup.OnGenerate = (cnt, r0, step, gap, twist, pat, replace, rules) =>
            GenerateSpiral(cnt, r0, step, gap, twist, pat, replace, rules);
        PopupPlacement.BelowCentered(popup, anchor);
        popup.Show(this);
        popup.Activate();
    }

    /// <summary>Сохранить текущий уровень напрямую во внутриигровую библиотеку (без проводника Windows).</summary>
    public string SaveCurrentInGame(bool asNewSlot = false)
    {
        canvas.Design.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Мой уровень" : nameBox.Text.Trim();
        nameBox.Text = canvas.Design.Name;
        string target = (!asNewSlot && !string.IsNullOrWhiteSpace(fileName) && File.Exists(fileName)) ? fileName : null;
        string savedPath = LevelStore.SaveToLibrary(canvas.Design, target);
        fileName = savedPath;
        int totalSlots = LevelStore.ListSaves().Count;
        FlashStatus($"  Сохранено в игре: «{canvas.Design.Name}» (слотов: {totalSlots}) — доступно в «Сохранениях» и «Уровнях»!");
        return savedPath;
    }

    /// <summary>Импортировать внешний .json файл сохранения во внутриигровую библиотеку и открыть его.</summary>
    public string ImportSaveFile(string externalPath)
    {
        var slot = LevelStore.ImportToLibrary(externalPath);
        LoadSavedFile(slot.FilePath);
        return slot.FilePath;
    }

    /// <summary>Загрузить сохранение из внутриигровой библиотеки (с поддержкой отмены Ctrl+Z).</summary>
    public void LoadSavedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (canvas.Testing) ToggleTest();
        var loaded = LevelStore.Load(path);
        canvas.PushUndo();
        canvas.Design = loaded;
        canvas.Design.Sanitize();
        canvas.SelectObject(null);
        canvas.NotifyDesignMutated();
        fileName = path;
        nameBox.Text = canvas.Design.Name;
        SyncTopChips();
        SyncInspector();
        FlashStatus($"  Загружено сохранение: «{canvas.Design.Name}» ({Path.GetFileName(path)})");
    }

    private void SaveLevel()
    {
        try
        {
            SaveCurrentInGame(asNewSlot: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Не удалось сохранить: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void OpenSavesManager()
    {
        var popup = new SaveManagerPopup(this);
        PopupPlacement.BelowLeftAligned(popup, btnSaves);
        popup.Show(this);
        popup.Activate();
    }

    private bool ImportFromFileDialog(IWin32Window owner)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Добавить сохранение из файла (.json)",
            Filter = "Уровень (*.json)|*.json",
            Multiselect = true,
            InitialDirectory = Directory.Exists(LevelStore.LevelsDir) ? LevelStore.LevelsDir : "",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK || dlg.FileNames.Length == 0) return false;
        int imported = 0;
        string lastImported = null;
        foreach (var fn in dlg.FileNames)
        {
            try
            {
                lastImported = LevelStore.ImportToLibrary(fn).FilePath;
                imported++;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, $"Не удалось добавить «{Path.GetFileName(fn)}»: {ex.Message}",
                    "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        if (lastImported != null)
        {
            LoadSavedFile(lastImported);
            if (imported > 1)
                FlashStatus($"  Добавлено файлов сохранений в игру: {imported}!");
            return true;
        }
        return false;
    }

    private void ExportToFileDialog(IWin32Window owner)
    {
        canvas.Design.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Мой уровень" : nameBox.Text.Trim();
        nameBox.Text = canvas.Design.Name;
        using var dlg = new SaveFileDialog
        {
            Title = "Выгрузить уровень в .json файл",
            Filter = "Уровень (*.json)|*.json",
            FileName = LevelStore.SanitizeFileStem(canvas.Design.Name) + ".json",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            LevelStore.Save(canvas.Design, dlg.FileName);
            FlashStatus($"  Выгружено в файл: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Не удалось выгрузить файл: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NewLevel()
    {
        if (canvas.Testing) ToggleTest();
        canvas.LoadLevel(new LevelData());
        fileName = null;
        nameBox.Text = canvas.Design.Name;
        SyncTopChips();
        SyncInspector();
        FlashStatus("  Чистый лист: рисуйте фигуры, ставьте кольца «Ротора» или источник волны, затем нажмите «ТЕСТ».");
    }

    private void FlashStatus(string text)
    {
        statusLbl.Text = text;
        statusHoldUntil = DateTime.UtcNow.AddSeconds(3.5);
    }

    private void SyncTopChips()
    {
        chipSnap.IsOn = canvas.SnapOn;
        chipGrid.IsOn = canvas.ShowGrid;
        chipFx.IsOn = canvas.FxOn;
        chipSnap.Invalidate();
        chipGrid.Invalidate();
        chipFx.Invalidate();
    }

    private void RefreshUiState()
    {
        btnUndo.Enabled = !canvas.Testing && canvas.CanUndo;
        btnRedo.Enabled = !canvas.Testing && canvas.CanRedo;
        btnClear.Enabled = !canvas.Testing;
        double mx = Math.Clamp(canvas.LastMouseWorld.X, 0, LevelData.W);
        double my = Math.Clamp(canvas.LastMouseWorld.Y, 0, LevelData.H);
        coordLbl.Text = $"X {mx:0}   Y {my:0}";
        if (!canvas.Testing && DateTime.UtcNow >= statusHoldUntil && ToolHints.TryGetValue(canvas.CurTool, out var h))
            statusLbl.Text = "  " + h;
    }

    /// <summary>Гравитация редактируемого уровня (синхронизируется с глобальным меню).</summary>
    public bool GravityOn
    {
        get => canvas.Design.Gravity;
        set
        {
            canvas.Design.Gravity = value;
            canvas.Sim.GravityOn = value;
            canvas.WaveFront.GravityOn = value;
            SyncInspector();
        }
    }

    /// <summary>Треки шаров в тесте редактора.</summary>
    public bool TrailsOn
    {
        get => canvas.Trails;
        set => canvas.Trails = value;
    }

    public void SetActive(bool active)
    {
        canvas.SetLoopActive(active);
        if (active) { uiTimer.Start(); RefreshUiState(); }
        else uiTimer.Stop();
    }

    public bool HandleKey(Keys keyData)
    {
        bool typing = nameBox.FocusedInput || scHintBox.FocusedInput;

        if (keyData == (Keys.Control | Keys.S)) { SaveLevel(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { OpenSavesManager(); return true; }
        if (!typing && keyData == (Keys.Control | Keys.Z))
        {
            if (canvas.Testing) canvas.UndoBallPlacement();
            else { canvas.DoUndo(); SyncInspector(); RefreshUiState(); }
            return true;
        }
        if (!typing && (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)))
        {
            if (!canvas.Testing) { canvas.DoRedo(); SyncInspector(); RefreshUiState(); }
            return true;
        }
        if (!typing && !canvas.Testing && keyData == (Keys.Control | Keys.D))
        {
            if (canvas.DuplicateSelected()) SyncInspector();
            return true;
        }
        if (!typing && !canvas.Testing && (keyData == Keys.Delete || keyData == Keys.Back))
        {
            if (canvas.DeleteSelected()) SyncInspector();
            return true;
        }

        if (keyData == Keys.Escape && canvas.Testing) { ToggleTest(); return true; }
        if (!typing && keyData == Keys.Space) { ToggleTest(); return true; }

        if (canvas.Testing && !typing)
        {
            if (keyData == Keys.R) { runtime.RestartCurrent(); return true; }
            if (keyData == Keys.G) { GravityOn = !GravityOn; return true; }
            return false;
        }

        if (!typing && !canvas.Testing)
        {
            switch (keyData)
            {
                case Keys.V: SelectTool(Tool.Select); return true;
                case Keys.E: SelectTool(Tool.Eraser); return true;
                case Keys.W: SelectTool(Tool.WallFree); return true;
                case Keys.L: SelectTool(Tool.WallLine); return true;
                case Keys.R: SelectTool(Tool.WallRect); return true;
                case Keys.O: SelectTool(Tool.WallEllipse); return true;
                case Keys.C: SelectTool(Tool.WallCircle); return true;
                case Keys.P: SelectTool(Tool.WallPoly); return true;
                case Keys.S: SelectTool(Tool.WallStar); return true;
                case Keys.A: SelectTool(Tool.ArcRing); return true;
                case Keys.F: SelectTool(Tool.GoalFree); return true;
                case Keys.T: SelectTool(Tool.GoalRect); return true;
                case Keys.Y: SelectTool(Tool.GoalEllipse); return true;
                case Keys.G: SelectTool(Tool.GoalPoly); return true;
                case Keys.B: SelectTool(Tool.Ball); return true;
                case Keys.U: SelectTool(Tool.WaveSource); return true;
            }
        }
        return false;
    }

    // ------------------------------------------------- боковые стеклянно-неоновые док-панели

    private sealed class DockPanel : Panel
    {
        public Func<List<(int Y, string Text)>> HeadersProvider;
        public Func<string> FooterStatsProvider;
        public int FooterTopY;

        private readonly bool rightBorder;
        private static readonly Font CapFont = new("Segoe UI", 7.5F, FontStyle.Bold);
        private static readonly Font StatFont = new("Segoe UI", 7.5F);

        public DockPanel(bool rightBorder)
        {
            this.rightBorder = rightBorder;
            BackColor = Theme.Panel;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Panel;
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int bx = rightBorder ? Width - 1 : 0;
            using (var glow = new Pen(Color.FromArgb(24, Theme.Accent), 3f))
                g.DrawLine(glow, bx, 0, bx, Height);
            using (var line = new Pen(Theme.Mix(Theme.PanelBorder, Theme.Accent, 0.22f), 1f))
                g.DrawLine(line, bx, 0, bx, Height);

            int mx = Math.Max(11, Width / 14);
            var headers = HeadersProvider?.Invoke();
            if (headers != null)
            {
                int capH = Math.Max(15, TextRenderer.MeasureText("АЯ", CapFont).Height + 2);
                Color capCol = Color.FromArgb(205, Theme.TextDim);
                using var sep = new Pen(Color.FromArgb(110, Theme.PanelBorder), 1f);
                for (int i = 0; i < headers.Count; i++)
                {
                    var (hy, text) = headers[i];
                    if (i > 0)
                        g.DrawLine(sep, mx, hy - 3, Width - mx, hy - 3);
                    TextRenderer.DrawText(g, text, CapFont, new Rectangle(mx, hy, Math.Max(20, Width - mx * 2), capH), capCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }

            if (FooterStatsProvider != null && FooterTopY > 0 && Height - FooterTopY >= 38)
            {
                using var sep = new Pen(Color.FromArgb(110, Theme.PanelBorder), 1f);
                g.DrawLine(sep, mx, FooterTopY, Width - mx, FooterTopY);
                var statRect = new Rectangle(8, FooterTopY + 6, Math.Max(20, Width - 16), Height - FooterTopY - 8);
                TextRenderer.DrawText(g, FooterStatsProvider(), StatFont, statRect, Color.FromArgb(180, Theme.TextDim),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            }
        }
    }

    // ------------------------------------------------- генератор концентрических колец и спиралей

    private sealed class SpiralGeneratorPopup : Form
    {
        public Action<int, double, double, double, double, SpiralSpinPattern, bool, bool> OnGenerate;

        private int count = 24;
        private double r0 = 116;
        private double step = 18;
        private double gapDeg;
        private double twistDeg = 22;
        private SpiralSpinPattern pattern = SpiralSpinPattern.Sync;

        private static readonly Font TitleFont = new("Segoe UI", 9.25F, FontStyle.Bold);

        public SpiralGeneratorPopup(double defaultGapDeg)
        {
            gapDeg = Math.Clamp(Math.Round(defaultGapDeg), 20, 180);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Theme.Panel;
            ClientSize = new Size(276, 336);

            var btnClose = new NeoButton
            {
                Icon = IconPainter.Cross, IconPx = 14, Bounds = new Rectangle(242, 6, 24, 24),
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            const int pad = 14, fullW = 248;
            int y = 38;

            var slCount = new SliderButton
            {
                Location = new Point(pad, y), Size = new Size(fullW, 28),
                Min = 1, Max = 200, Value = count, Prefix = "ЧИСЛО КОЛЕЦ: ",
                Format = static v => Math.Round(v).ToString(),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            slCount.Changed += v => count = (int)Math.Round(v);
            Controls.Add(slCount);
            y += 33;

            var slR0 = new SliderButton
            {
                Location = new Point(pad, y), Size = new Size(fullW, 28),
                Min = 50, Max = 260, Value = r0, Prefix = "ВНУТР. РАДИУС: ",
                Format = static v => Math.Round(v) + " px",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            slR0.Changed += v => r0 = Math.Round(v);
            Controls.Add(slR0);
            y += 33;

            var slStep = new SliderButton
            {
                Location = new Point(pad, y), Size = new Size(fullW, 28),
                Min = 6, Max = 40, Value = step, Prefix = "ШАГ МЕЖДУ КОЛЬЦАМИ: ",
                Format = static v => v.ToString("0.#") + " px",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            slStep.Changed += v => step = Math.Round(v * 2.0) / 2.0;
            Controls.Add(slStep);
            y += 33;

            var slGap = new SliderButton
            {
                Location = new Point(pad, y), Size = new Size(fullW, 28),
                Min = 20, Max = 180, Value = gapDeg, Prefix = "ШИРИНА ВЫРЕЗА: ",
                Format = static v => Math.Round(v) + "°",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            slGap.Changed += v => gapDeg = Math.Round(v);
            Controls.Add(slGap);
            y += 33;

            var slTwist = new SliderButton
            {
                Location = new Point(pad, y), Size = new Size(fullW, 28),
                Min = 0, Max = 90, Value = twistDeg, Prefix = "СДВИГ ВЫРЕЗА (СПИРАЛЬ): ",
                Format = static v => Math.Round(v) + "°",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            slTwist.Changed += v => twistDeg = Math.Round(v);
            Controls.Add(slTwist);
            y += 38;

            var pSync = new NeoButton { Text = "СИНХРОН", IsToggle = true, IsOn = true, Role = AccentRole.Orange, Bounds = new Rectangle(pad, y, 79, 28), Font = new Font("Segoe UI", 7.5F, FontStyle.Bold) };
            var pAlt  = new NeoButton { Text = "ВСТРЕЧ.", IsToggle = true, Role = AccentRole.Orange, Bounds = new Rectangle(pad + 84, y, 80, 28), Font = new Font("Segoe UI", 7.5F, FontStyle.Bold) };
            var pVort = new NeoButton { Text = "ВИХРЬ", IsToggle = true, Role = AccentRole.Orange, Bounds = new Rectangle(pad + 169, y, 79, 28), Font = new Font("Segoe UI", 7.5F, FontStyle.Bold) };

            void PickPattern(SpiralSpinPattern p)
            {
                pattern = p;
                pSync.IsOn = p == SpiralSpinPattern.Sync;
                pAlt.IsOn = p == SpiralSpinPattern.Alternating;
                pVort.IsOn = p == SpiralSpinPattern.Vortex;
                pSync.Invalidate(); pAlt.Invalidate(); pVort.Invalidate();
            }
            pSync.Click += (s, e) => PickPattern(SpiralSpinPattern.Sync);
            pAlt.Click += (s, e) => PickPattern(SpiralSpinPattern.Alternating);
            pVort.Click += (s, e) => PickPattern(SpiralSpinPattern.Vortex);
            Controls.Add(pSync);
            Controls.Add(pAlt);
            Controls.Add(pVort);
            y += 34;

            var chipReplace = new NeoButton
            {
                Text = "ЗАМЕНИТЬ КОЛЬЦА + ПРАВИЛА РОТОРА", IsToggle = true, IsOn = true,
                Bounds = new Rectangle(pad, y, fullW, 26), Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            };
            Controls.Add(chipReplace);
            y += 34;

            var btnCreate = new NeoButton
            {
                Text = "СОЗДАТЬ СПИРАЛЬ", Icon = IconPainter.Spiral, IconPx = 16, Role = AccentRole.Green,
                Bounds = new Rectangle(pad, y, fullW, 34), Font = new Font("Segoe UI", 9.0F, FontStyle.Bold),
            };
            btnCreate.Click += (s, e) =>
            {
                OnGenerate?.Invoke(count, r0, step, gapDeg, twistDeg, pattern, chipReplace.IsOn, chipReplace.IsOn);
                Close();
            };
            Controls.Add(btnCreate);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = new SolidBrush(Theme.Panel))
                g.FillRectangle(bg, ClientRectangle);
            using (var bp = new Pen(Theme.Mix(Theme.Border, Theme.Orange, 0.55f), 1.4f))
                g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);

            TextRenderer.DrawText(g, "ГЕНЕРАТОР КОЛЕЦ «РОТОРА»", TitleFont,
                new Rectangle(14, 7, Width - 46, 24), Theme.Orange,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using var sep = new Pen(Theme.PanelBorder, 1f);
            g.DrawLine(sep, 14, 33, Width - 14, 33);
        }
    }

    // ------------------------------------------------- внутриигровой менеджер сохранений

    private sealed class SaveManagerPopup : Form
    {
        private readonly EditorView owner;
        private List<SaveSlotInfo> slots = new();
        private readonly NeoButton btnSaveNew;
        private readonly NeoButton btnOverwrite;
        private readonly NeoButton btnImport;
        private readonly NeoButton btnExport;

        private const int Cols = 4;
        private const int CardW = 192;
        private const int CardH = 128;
        private const int Gap = 9;
        private const int Pad = 14;
        private const int GridTop = 86;
        private const int GridBottomPad = 14;
        private const int VisRows = 3;

        private int scrollY;
        private int maxScrollY;
        private int hoverCard = -1;
        private int hoverDelete = -1;
        private bool suppressDeactivate;

        private static readonly Font HeaderFont = new("Segoe UI", 9.75F, FontStyle.Bold);
        private static readonly Font SubFont = new("Segoe UI", 8.0F);
        private static readonly Font CardTitleFont = new("Segoe UI", 8.5F, FontStyle.Bold);
        private static readonly Font CardMetaFont = new("Segoe UI", 7.25F);
        private static readonly Font BadgeFont = new("Segoe UI", 6.75F, FontStyle.Bold);
        private static readonly Font EmptyFont = new("Segoe UI", 9.5F);

        public SaveManagerPopup(EditorView editor)
        {
            owner = editor;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Theme.Panel;
            KeyPreview = true;

            int w = Pad * 2 + Cols * CardW + (Cols - 1) * Gap + 10;
            int h = GridTop + VisRows * CardH + (VisRows - 1) * Gap + GridBottomPad;
            ClientSize = new Size(w, h);

            var btnClose = new NeoButton
            {
                Icon = IconPainter.Cross, IconPx = 15, Bounds = new Rectangle(w - 38, 8, 28, 28),
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            int bx = Pad, by = 42;
            btnSaveNew = new NeoButton
            {
                Text = "+ СОХРАНИТЬ НОВЫМ", Icon = IconPainter.Floppy, IconPx = 15, Role = AccentRole.Green,
                Bounds = new Rectangle(bx, by, 196, 34), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            bx += 196 + 8;

            btnOverwrite = new NeoButton
            {
                Text = "ПЕРЕЗАПИСАТЬ", Icon = IconPainter.Floppy, IconPx = 15, Role = AccentRole.Accent,
                Bounds = new Rectangle(bx, by, 156, 34), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            bx += 156 + 8;

            btnImport = new NeoButton
            {
                Text = "ДОБАВИТЬ ФАЙЛОМ...", Icon = IconPainter.ImportFile, IconPx = 15, Role = AccentRole.Violet,
                Bounds = new Rectangle(bx, by, 198, 34), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };
            bx += 198 + 8;

            btnExport = new NeoButton
            {
                Text = "ВЫГРУЗИТЬ .JSON", Icon = IconPainter.ExportFile, IconPx = 15, Role = AccentRole.Orange,
                Bounds = new Rectangle(bx, by, w - Pad - bx, 34), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            };

            btnSaveNew.Click += (s, e) =>
            {
                owner.SaveCurrentInGame(asNewSlot: true);
                ReloadSlots();
            };
            btnOverwrite.Click += (s, e) =>
            {
                owner.SaveCurrentInGame(asNewSlot: false);
                ReloadSlots();
            };
            btnImport.Click += (s, e) =>
            {
                suppressDeactivate = true;
                try
                {
                    if (owner.ImportFromFileDialog(this))
                        ReloadSlots();
                }
                finally
                {
                    suppressDeactivate = false;
                    Activate();
                }
            };
            btnExport.Click += (s, e) =>
            {
                suppressDeactivate = true;
                try
                {
                    owner.ExportToFileDialog(this);
                }
                finally
                {
                    suppressDeactivate = false;
                    Activate();
                }
            };

            Controls.Add(btnSaveNew);
            Controls.Add(btnOverwrite);
            Controls.Add(btnImport);
            Controls.Add(btnExport);

            Deactivate += (s, e) => { if (!suppressDeactivate) Close(); };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            ReloadSlots();
        }

        private void ReloadSlots()
        {
            slots = LevelStore.ListSaves();
            bool hasActive = !string.IsNullOrWhiteSpace(owner.fileName) && File.Exists(owner.fileName);
            btnOverwrite.Enabled = hasActive;

            int rows = Math.Max(1, (slots.Count + Cols - 1) / Cols);
            int totalH = rows * CardH + (rows - 1) * Gap;
            int viewH = Height - GridTop - GridBottomPad;
            maxScrollY = Math.Max(0, totalH - viewH);
            scrollY = Math.Clamp(scrollY, 0, maxScrollY);
            hoverCard = -1;
            hoverDelete = -1;
            Invalidate();
        }

        private Rectangle CardRect(int idx)
        {
            int c = idx % Cols, r = idx / Cols;
            return new Rectangle(Pad + c * (CardW + Gap), GridTop + r * (CardH + Gap) - scrollY, CardW, CardH);
        }

        private static Rectangle DeleteRect(Rectangle card) =>
            new(card.Right - 28, card.Y + 6, 22, 22);

        private void HitTest(Point pt, out int cardIdx, out int delIdx)
        {
            cardIdx = -1;
            delIdx = -1;
            if (pt.Y < GridTop || pt.Y > Height - GridBottomPad) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var r = CardRect(i);
                if (!r.Contains(pt)) continue;
                cardIdx = i;
                if (DeleteRect(r).Contains(pt)) delIdx = i;
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HitTest(e.Location, out int c, out int d);
            Cursor = c >= 0 ? Cursors.Hand : Cursors.Default;
            if (c != hoverCard || d != hoverDelete)
            {
                hoverCard = c;
                hoverDelete = d;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverCard != -1 || hoverDelete != -1)
            {
                hoverCard = -1;
                hoverDelete = -1;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (maxScrollY <= 0) return;
            int next = Math.Clamp(scrollY - Math.Sign(e.Delta) * 56, 0, maxScrollY);
            if (next != scrollY)
            {
                scrollY = next;
                HitTest(PointToClient(Cursor.Position), out hoverCard, out hoverDelete);
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            HitTest(e.Location, out int c, out int d);
            if (d >= 0 && d < slots.Count)
            {
                var victim = slots[d];
                if (string.Equals(owner.fileName, victim.FilePath, StringComparison.OrdinalIgnoreCase))
                    owner.fileName = null;
                LevelStore.DeleteSave(victim.FilePath);
                owner.FlashStatus($"  Удалено сохранение: «{victim.Level.Name}»");
                ReloadSlots();
                return;
            }
            if (c >= 0 && c < slots.Count)
            {
                var picked = slots[c];
                Close();
                owner.LoadSavedFile(picked.FilePath);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(Theme.Panel))
                g.FillRectangle(bg, ClientRectangle);

            TextRenderer.DrawText(g, $"СОХРАНЕНИЯ В ИГРЕ ({slots.Count})", HeaderFont,
                new Rectangle(Pad, 8, 320, 26), Theme.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "клик по карточке — открыть  ·  можно создавать сколько угодно слотов или добавить .json файл",
                SubFont, new Rectangle(246, 9, Width - 294, 24), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            using (var sep = new Pen(Theme.PanelBorder, 1f))
                g.DrawLine(sep, Pad, GridTop - 6, Width - Pad, GridTop - 6);

            var gridArea = new Rectangle(4, GridTop, Width - 8, Height - GridTop - GridBottomPad);
            if (slots.Count == 0)
            {
                TextRenderer.DrawText(g,
                    "У вас пока нет сохранённых уровней.\nНажмите «+ СОХРАНИТЬ НОВЫМ» сверху, чтобы сохранить текущую сцену прямо в игре,\nили «ДОБАВИТЬ ФАЙЛОМ...», чтобы импортировать готовый .json уровень.",
                    EmptyFont, gridArea, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            else
            {
                var st = g.Save();
                g.SetClip(gridArea);
                for (int i = 0; i < slots.Count; i++)
                {
                    var r = CardRect(i);
                    if (r.Bottom < GridTop || r.Top > gridArea.Bottom) continue;
                    DrawSlotCard(g, i, r, slots[i]);
                }
                g.Restore(st);

                if (maxScrollY > 0)
                {
                    int trackTop = GridTop + 2, trackH = gridArea.Height - 4;
                    int thumbH = Math.Max(28, (int)((float)trackH * trackH / (trackH + maxScrollY)));
                    int thumbY = trackTop + (int)((trackH - thumbH) * ((float)scrollY / maxScrollY));
                    using var trBr = new SolidBrush(Color.FromArgb(90, Theme.Panel2));
                    g.FillRectangle(trBr, Width - 10, trackTop, 4, trackH);
                    using var thBr = new SolidBrush(Color.FromArgb(195, Theme.Accent));
                    g.FillRectangle(thBr, Width - 10, thumbY, 4, thumbH);
                }
            }

            using var bp = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.48f), 1.4f);
            g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        }

        private void DrawSlotCard(Graphics g, int idx, Rectangle r, SaveSlotInfo slot)
        {
            var l = slot.Level;
            bool isActive = !string.IsNullOrWhiteSpace(owner.fileName) &&
                            string.Equals(owner.fileName, slot.FilePath, StringComparison.OrdinalIgnoreCase);
            bool hov = idx == hoverCard;
            bool delHov = idx == hoverDelete;

            AccentRole role = l.WaveEmitter || l.Goals.Count > 0
                ? AccentRole.Green
                : l.RotorRules ? AccentRole.Orange : AccentRole.Accent;
            Color acc = l.RotorRules ? Color.FromArgb(246, 118, 240) : Theme.ResolveRole(role);

            using (var path = Theme.RoundRect(r, 9))
            {
                Color fill = isActive ? Theme.Mix(Theme.Panel2, acc, 0.16f)
                           : hov      ? Theme.Hover
                           :            Theme.Panel2;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                Color bc = isActive ? acc : hov ? Theme.Mix(Theme.Border, acc, 0.55f) : Theme.Border;
                using (var p = new Pen(bc, isActive ? 1.7f : 1.1f)) g.DrawPath(p, path);
            }

            var prev = new Rectangle(r.X + 8, r.Y + 7, r.Width - 16, r.Height - 48);
            var previewItem = new LevelCardItem
            {
                Title = l.Name,
                Preview = l,
                Source = l.WaveEmitter ? l.WaveSourcePos : null,
                Role = role,
                RotorStyle = l.RotorRules,
            };
            using (var pp = Theme.RoundRect(prev, 6))
            {
                using (var pb = new SolidBrush(Theme.Bg)) g.FillPath(pb, pp);
                LevelPickerPopup.DrawMiniPreview(g, prev, previewItem, acc);
                using (var pr = new Pen(Theme.PanelBorder, 1f)) g.DrawPath(pr, pp);
            }

            string badge = isActive ? "АКТИВЕН"
                         : l.WaveEmitter ? "ВОЛНА"
                         : l.RotorRules ? "РОТОР"
                         : l.Goals.Count > 0 ? "ЦЕЛЬ" : "СВОЙ";
            var bsz = TextRenderer.MeasureText(badge, BadgeFont);
            var br = new Rectangle(prev.X + 4, prev.Y + 4, bsz.Width + 6, 15);
            using (var bbg = new SolidBrush(Color.FromArgb(200, Theme.Panel)))
                g.FillRectangle(bbg, br);
            TextRenderer.DrawText(g, badge, BadgeFont, br, isActive ? Theme.Green : Theme.Mix(acc, Theme.Text, 0.2f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // кнопка удаления слота в правом верхнем углу превью
            var dr = DeleteRect(r);
            using (var dp = Theme.RoundRect(dr, 5))
            {
                Color df = delHov ? Color.FromArgb(225, Theme.Red) : Color.FromArgb(185, Theme.Panel);
                using (var db = new SolidBrush(df)) g.FillPath(db, dp);
                using (var dpen = new Pen(delHov ? Theme.Red : Theme.PanelBorder, 1f)) g.DrawPath(dpen, dp);
            }
            IconPainter.Trash(g, new Rectangle(dr.X + 4, dr.Y + 4, 14, 14), delHov ? Color.White : Theme.TextDim);

            // заголовок и время сохранения
            TextRenderer.DrawText(g, l.Name, CardTitleFont,
                new Rectangle(r.X + 8, r.Bottom - 38, r.Width - 16, 19),
                isActive || hov ? acc : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            string meta = $"{slot.SavedLocal:dd.MM.yyyy HH:mm}  ·  {slot.FileName}";
            TextRenderer.DrawText(g, meta, CardMetaFont,
                new Rectangle(r.X + 8, r.Bottom - 19, r.Width - 16, 15),
                Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}

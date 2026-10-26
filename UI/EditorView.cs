using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Редактор. Верхняя панель (Toolbar): файловые иконки + название + отмена/повтор,
/// в центре чипы-переключатели, справа крупная кнопка «ТЕСТ». Слева палитра
/// иконок (как в фотошопе/крите) с группами СТЕНЫ / ЦЕЛИ / ШАР. Внизу — строка
/// состояния с координатами и подсказкой.
/// </summary>
public class EditorView : UserControl, IModeView
{
    private readonly GameCanvas canvas = new() { Editor = true, Dock = DockStyle.Fill, ShowGrid = true };

    private readonly TextBox nameBox = new();
    private readonly NeoButton btnTest = new()
    {
        Text = "ТЕСТ", Accent = Theme.Green, Icon = IconPainter.Play,
        Size = new Size(110, 38), Font = new Font("Segoe UI", 10F, FontStyle.Bold),
    };
    private readonly NeoButton btnUndo = new() { Icon = IconPainter.Undo, Size = new Size(38, 38) };
    private readonly NeoButton btnRedo = new() { Icon = IconPainter.Redo, Size = new Size(38, 38) };

    private readonly NeoButton chipGrid = MakeChip("СЕТКА", 68);
    private readonly NeoButton chipSnap = MakeChip("ПРИВЯЗКА", 88);
    private readonly NeoButton chipBorder = MakeChip("РАМКА", 70);
    private readonly NeoButton chipCollide = MakeChip("СОУДАРЕНИЯ", 96);
    private readonly NeoButton chipFx = MakeChip("ЭФФЕКТЫ", 84);
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF) }; // БЕЛЫЙ

    private readonly Bar statusBar = new() { LineAtTop = true };
    private readonly Label statusLbl = new();
    private readonly Label coordLbl = new();
    private readonly Label radiusLbl = new();

    private string fileName;
    private DateTime statusHoldUntil;
    private readonly List<NeoButton> toolBtns = new();
    private readonly ToolTip tips = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 250 };

    private static readonly Dictionary<Tool, string> ToolHints = new()
    {
        { Tool.Select,      "Выбор: перетаскивайте шары, цели и стенки. ПКМ или ластик — удалить." },
        { Tool.WallFree,    "СТЕНА: зажмите и ведите мышью — дорожка, от которой рикошетят шары." },
        { Tool.WallLine,    "СТЕНА: нажмите в начале, отпустите в конце — прямой отрезок." },
        { Tool.WallRect,    "СТЕНА: потяните из угла в угол — замкнутый прямоугольник." },
        { Tool.WallEllipse, "СТЕНА: потяните по диагонали рамки — эллипс или круг." },
        { Tool.GoalFree,    "ЦЕЛЬ: обведите замкнутую область — касание шаром проходит уровень." },
        { Tool.GoalRect,    "ЦЕЛЬ: потяните из угла в угол — прямоугольная зона." },
        { Tool.GoalEllipse, "ЦЕЛЬ: потяните — круглая зона." },
        { Tool.Ball,        "ШАР: зажмите — центр, потяните — направление, отпустите. Клик по существующему шару — выбрать его и менять скорость («V −/+»). Радиус — колесо или «+/−»." },
        { Tool.Eraser,      "ЛАСТИК: кликайте по объектам, чтобы удалить (ПКМ работает всегда)." },
    };

    public EditorView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        BuildTopBar();
        BuildPalette();
        BuildStatusBar();

        Controls.Add(canvas);
        Controls.Add(statusBar);
        Controls.Add(palettePanel);
        Controls.Add(topPanel);

        chipGrid.IsOn = true;
        chipFx.IsOn = true;
        nameBox.Text = canvas.Design.Name;

        uiTimer.Tick += (s, e) => RefreshUiState();
        uiTimer.Start();
        Disposed += (s, e) => uiTimer.Stop();
        canvas.Won += () => statusLbl.Text = "  Цель задета — нажмите «СТОП» (Esc), чтобы вернуться к правкам";
        RefreshUiState();
    }

    private readonly Toolbar topPanel = new() { Dock = DockStyle.Top, Height = 56 };
    private readonly Panel palettePanel = new();

    private static NeoButton MakeChip(string text, int width)
    {
        return new NeoButton { Text = text, IsToggle = true, Size = new Size(width, 32), Font = new Font("Segoe UI", 8.75F) };
    }

    private NeoButton IconBtn(Action<Graphics, Rectangle, Color> icon, string tip)
    {
        var b = new NeoButton { Icon = icon, Size = new Size(38, 38), IconPx = 20 };
        tips.SetToolTip(b, tip);
        return b;
    }

    // ------------------------------------------------- верхняя панель

    private void BuildTopBar()
    {
        tips.SetToolTip(btnTest, "Запустить/остановить тест уровня (пробел)");
        btnTest.Click += (s, e) => ToggleTest();

        tips.SetToolTip(chipGrid, "Показывать сетку");
        tips.SetToolTip(chipSnap, "Привязка рисования к сетке 20 px");
        tips.SetToolTip(chipBorder, "Упругая рамка поля. ВЫКЛ по умолчанию: улетевший шар теряется");
        tips.SetToolTip(chipCollide, "Столкновения шаров друг с другом. Выключено — шары пролетают друг сквозь друга");
        tips.SetToolTip(chipFx, "Шейдерные эффекты: свечение, аберрация, виньетка, зерно");
        tips.SetToolTip(colorBtn, "Цвет новых шаров (СПЕКТР — каждый шар нового тона)");
        chipCollide.IsOn = true; // столкновения включены по умолчанию

        chipGrid.Click += (s, e) => { canvas.ShowGrid = chipGrid.IsOn; canvas.Invalidate(); };
        chipSnap.Click += (s, e) => canvas.SnapOn = chipSnap.IsOn;
        chipBorder.Click += (s, e) => { canvas.Design.Border = chipBorder.IsOn; canvas.Sim.HasBorder = chipBorder.IsOn; canvas.Invalidate(); };
        chipCollide.Click += (s, e) => { canvas.Design.NoBallCollisions = !chipCollide.IsOn; canvas.Sim.BallCollisions = chipCollide.IsOn; };
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;
        colorBtn.Changed += () =>
        {
            canvas.NewBallSpectrum = colorBtn.Selected == BallColors.Spectrum;
            canvas.NewBallArgb = colorBtn.Selected;
        };

        var btnNew = IconBtn(IconPainter.Doc, "Новый уровень");
        var btnOpen = IconBtn(IconPainter.Folder, "Открыть уровень (levels/*.json)");
        var btnSave = IconBtn(IconPainter.Floppy, "Сохранить уровень (Ctrl+S)");
        btnNew.Click += (s, e) => NewLevel();
        btnOpen.Click += (s, e) => OpenLevel();
        btnSave.Click += (s, e) => SaveLevel();

        nameBox.Width = 120;
        nameBox.Height = 34;
        nameBox.BorderStyle = BorderStyle.FixedSingle;
        nameBox.BackColor = Theme.Panel2;
        nameBox.ForeColor = Theme.Text;
        nameBox.Font = new Font("Segoe UI", 10F);

        tips.SetToolTip(btnUndo, "Отменить (Ctrl+Z)");
        tips.SetToolTip(btnRedo, "Повторить (Ctrl+Y)");
        btnUndo.Click += (s, e) => { canvas.DoUndo(); RefreshUiState(); };
        btnRedo.Click += (s, e) => { canvas.DoRedo(); RefreshUiState(); };

        topPanel.AddLeft(btnNew).AddLeft(btnOpen).AddLeft(btnSave).AddLeft(nameBox).AddLeft(btnUndo).AddLeft(btnRedo).AddLeft(colorBtn);
        topPanel.AddCenter(chipGrid).AddCenter(chipSnap)
                .AddCenter(chipBorder).AddCenter(chipCollide).AddCenter(chipFx);
        topPanel.AddRight(btnTest);
    }

    // ------------------------------------------------- палитра инструментов

    private void BuildPalette()
    {
        palettePanel.Dock = DockStyle.Left;
        palettePanel.Width = 132;
        palettePanel.BackColor = Theme.Panel;

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(10, 8, 10, 8),
        };

        Caption(stack, "ОБЩЕЕ");
        stack.Controls.Add(MakeToolBtn(Tool.Select, IconPainter.CursorArrow, "Выбор / перемещение (V)", Theme.TextDim));
        stack.Controls.Add(MakeToolBtn(Tool.Eraser, IconPainter.Eraser, "Ластик (E)", Theme.TextDim));

        Caption(stack, "СТЕНЫ");
        stack.Controls.Add(MakeToolBtn(Tool.WallFree, IconPainter.Pencil, "Стена: рисовать от руки (W)", Theme.Accent));
        stack.Controls.Add(MakeToolBtn(Tool.WallLine, IconPainter.LineSeg, "Стена: отрезок (L)", Theme.Accent));
        stack.Controls.Add(MakeToolBtn(Tool.WallRect, IconPainter.Rect, "Стена: прямоугольник (R)", Theme.Accent));
        stack.Controls.Add(MakeToolBtn(Tool.WallEllipse, IconPainter.Ellipse, "Стена: эллипс (O)", Theme.Accent));

        Caption(stack, "ЦЕЛИ");
        stack.Controls.Add(MakeToolBtn(Tool.GoalFree, IconPainter.Pencil, "Цель: рисовать от руки (F)", Theme.Green));
        stack.Controls.Add(MakeToolBtn(Tool.GoalRect, IconPainter.Rect, "Цель: прямоугольник (T)", Theme.Green));
        stack.Controls.Add(MakeToolBtn(Tool.GoalEllipse, IconPainter.Ellipse, "Цель: эллипс (Y)", Theme.Green));

        Caption(stack, "ШАР");
        stack.Controls.Add(MakeToolBtn(Tool.Ball, IconPainter.Ball, "Шар: зажать-потянуть-отпустить (B)", Theme.Orange));

        // степпер радиуса
        var radiusRow = new Panel { Width = 106, Height = 36, BackColor = Theme.Panel2, Margin = new Padding(2, 10, 2, 0) };
        var btnMinus = new NeoButton { Icon = IconPainter.Minus, Size = new Size(34, 32), IconPx = 16 };
        var btnPlus = new NeoButton { Icon = IconPainter.Plus, Size = new Size(34, 32), IconPx = 16 };
        radiusLbl.Text = "R 12";
        radiusLbl.ForeColor = Theme.Text;
        radiusLbl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        radiusLbl.Dock = DockStyle.Fill;
        radiusLbl.TextAlign = ContentAlignment.MiddleCenter;
        btnMinus.Dock = DockStyle.Left;
        btnPlus.Dock = DockStyle.Right;
        btnMinus.Click += (s, e) => SetRadius(canvas.NewBallR - 2);
        btnPlus.Click += (s, e) => SetRadius(canvas.NewBallR + 2);
        radiusRow.Controls.Add(radiusLbl);
        radiusRow.Controls.Add(btnPlus);
        radiusRow.Controls.Add(btnMinus);
        tips.SetToolTip(radiusRow, "Радиус нового шара (колесо мыши)");

        stack.Controls.Add(radiusRow);

        // степпер скорости шара: меняет скорость выбранного шара (клик по шару в правке)
        // или скорость, с которой будут ставиться следующие шары
        var speedRow = new Panel { Width = 106, Height = 36, BackColor = Theme.Panel2, Margin = new Padding(2, 6, 2, 0) };
        var btnSMinus = new NeoButton { Icon = IconPainter.Minus, Size = new Size(34, 32), IconPx = 16 };
        var btnSPlus = new NeoButton { Icon = IconPainter.Plus, Size = new Size(34, 32), IconPx = 16 };
        var speedLbl = new Label
        {
            Text = "V 800",
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        btnSMinus.Dock = DockStyle.Left;
        btnSPlus.Dock = DockStyle.Right;
        btnSMinus.Click += (s, e) => canvas.SetBallSpeed(SpeedValue() - 100);
        btnSPlus.Click += (s, e) => canvas.SetBallSpeed(SpeedValue() + 100);
        speedRow.Controls.Add(speedLbl);
        speedRow.Controls.Add(btnSPlus);
        speedRow.Controls.Add(btnSMinus);
        tips.SetToolTip(speedRow, "Скорость шара. Кликните по шару в режиме правки — изменится его скорость; иначе задайте скорость для новых шаров");
        stack.Controls.Add(speedRow);

        double SpeedValue() =>
            canvas.SelectedBall != null && canvas.SelectedBall.HasVel
                ? canvas.SelectedBall.Vel.Len
                : canvas.EditorBallSpeed;
        void RefreshSpeedLbl() => speedLbl.Text = "V " + (int)Math.Round(SpeedValue());
        canvas.SelectionChanged += RefreshSpeedLbl;
        canvas.BallSpeedChanged += RefreshSpeedLbl;

        palettePanel.Controls.Add(stack);

        canvas.BallRadiusChanged += () => SetRadius(canvas.NewBallR);
        SetTool(Tool.WallFree);
    }

    private static void Caption(FlowLayoutPanel stack, string text)
    {
        stack.Controls.Add(new Label
        {
            Text = text,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(2, 10, 0, 4),
        });
    }

    private NeoButton MakeToolBtn(Tool tool, Action<Graphics, Rectangle, Color> icon, string tip, Color color)
    {
        var b = new NeoButton
        {
            Icon = icon,
            Size = new Size(46, 42),
            IconPx = 22,
            ForeColor = color,
            Accent = color,
            Margin = new Padding(2, 2, 2, 2),
            Tag = tool,
        };
        tips.SetToolTip(b, tip);
        b.Click += (s, e) => SetTool(tool);
        toolBtns.Add(b);
        return b;
    }

    private void SetRadius(double r)
    {
        canvas.NewBallR = Math.Clamp(r, 2, 40);
        radiusLbl.Text = "R " + (int)Math.Round(canvas.NewBallR);
        radiusLbl.Invalidate();
    }

    // ------------------------------------------------- строка состояния

    private void BuildStatusBar()
    {
        statusBar.Dock = DockStyle.Bottom;
        statusBar.Height = 30;
        statusBar.BackColor = Theme.Panel;

        statusLbl.Dock = DockStyle.Fill;
        statusLbl.ForeColor = Theme.TextDim;
        statusLbl.Font = new Font("Segoe UI", 9F);
        statusLbl.TextAlign = ContentAlignment.MiddleLeft;
        statusLbl.Padding = new Padding(10, 0, 0, 0);

        coordLbl.Dock = DockStyle.Right;
        coordLbl.Width = 190;
        coordLbl.BackColor = Theme.Panel;
        coordLbl.ForeColor = Theme.TextDim;
        coordLbl.Font = new Font("Consolas", 9F);
        coordLbl.TextAlign = ContentAlignment.MiddleRight;
        coordLbl.Padding = new Padding(0, 0, 12, 0);

        statusBar.Controls.Add(statusLbl);
        statusBar.Controls.Add(coordLbl);
    }

    // ------------------------------------------------- действия

    private void SetTool(Tool t)
    {
        canvas.CurTool = t;
        foreach (var b in toolBtns)
            b.IsOn = b.Tag is Tool tt && tt == t;
        statusLbl.Text = "  " + ToolHints[t];
    }

    private void ToggleTest()
    {
        canvas.Testing = !canvas.Testing;
        if (canvas.Testing)
        {
            canvas.RestartSim();
            canvas.Sim.GravityOn = canvas.Design.Gravity;
            canvas.Sim.HasBorder = canvas.Design.Border;
            canvas.Sim.BallCollisions = !canvas.Design.NoBallCollisions;
            btnTest.Text = "СТОП";
            btnTest.Accent = Theme.Red;
            btnTest.Icon = IconPainter.Stop;
            palettePanel.Enabled = false;
            statusLbl.Text = "  ТЕСТ: ставьте шары как в игре — зажать, потянуть, отпустить. Касание зелёной цели — «проходится». Esc — вернуться.";
        }
        else
        {
            btnTest.Text = "ТЕСТ";
            btnTest.Accent = Theme.Green;
            btnTest.Icon = IconPainter.Play;
            palettePanel.Enabled = true;
            canvas.RestartSim();
        }
    }

    private void SaveLevel()
    {
        canvas.Design.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? "Мой уровень" : nameBox.Text.Trim();
        nameBox.Text = canvas.Design.Name;
        if (fileName == null)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "Сохранить уровень",
                Filter = "Уровень (*.json)|*.json",
                InitialDirectory = Directory.Exists(LevelStore.LevelsDir) ? LevelStore.LevelsDir : "",
                FileName = canvas.Design.Name + ".json",
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            fileName = dlg.FileName;
        }
        try
        {
            LevelStore.Save(canvas.Design, fileName);
            FlashStatus($"  Сохранено: {fileName} (уровень появится в списке «Уровни»)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Не удалось сохранить: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLevel()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Открыть уровень",
            Filter = "Уровень (*.json)|*.json",
            InitialDirectory = Directory.Exists(LevelStore.LevelsDir) ? LevelStore.LevelsDir : "",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            if (canvas.Testing) ToggleTest();
            canvas.LoadLevel(LevelStore.Load(dlg.FileName));
            fileName = dlg.FileName;
            nameBox.Text = canvas.Design.Name;
            SyncChips();
            FlashStatus($"  Открыт: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "Не удалось открыть: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void NewLevel()
    {
        if (canvas.Testing) ToggleTest();
        canvas.LoadLevel(new LevelData());
        fileName = null;
        nameBox.Text = canvas.Design.Name;
        SyncChips();
        FlashStatus("  Чистый лист: нарисуйте фигуры (стены), при желании цели и шары. Затем — ТЕСТ.");
    }

    private void FlashStatus(string text)
    {
        statusLbl.Text = text;
        statusHoldUntil = DateTime.UtcNow.AddSeconds(3.5);
    }

    private void SyncChips()
    {
        chipSnap.IsOn = canvas.SnapOn;
        chipGrid.IsOn = canvas.ShowGrid;
        chipBorder.IsOn = canvas.Design.Border;
        chipCollide.IsOn = !canvas.Design.NoBallCollisions;
        chipFx.IsOn = canvas.FxOn;
        foreach (var c in new[] { chipSnap, chipGrid, chipBorder, chipCollide, chipFx }) c.Invalidate();
    }

    private void RefreshUiState()
    {
        btnUndo.Enabled = canvas.CanUndo;
        btnRedo.Enabled = canvas.CanRedo;
        double mx = Math.Clamp(canvas.LastMouseWorld.X, 0, LevelData.W);
        double my = Math.Clamp(canvas.LastMouseWorld.Y, 0, LevelData.H);
        coordLbl.Text = $"X {mx:0}   Y {my:0}";
        if (!canvas.Testing && DateTime.UtcNow >= statusHoldUntil)
            statusLbl.Text = "  " + ToolHints[canvas.CurTool];
    }

    /// <summary>Гравитация редактируемого уровня (чип в боковой панели).</summary>
    public bool GravityOn
    {
        get => canvas.Design.Gravity;
        set { canvas.Design.Gravity = value; canvas.Sim.GravityOn = value; }
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

    public bool HandleKey(Keys keyData)    {
        if (keyData == (Keys.Control | Keys.Z)) { canvas.DoUndo(); RefreshUiState(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        { canvas.DoRedo(); RefreshUiState(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { SaveLevel(); return true; }
        if (keyData == Keys.Escape && canvas.Testing) { ToggleTest(); return true; }
        if (keyData == Keys.Space && !nameBox.Focused) { ToggleTest(); return true; }

        bool typing = nameBox.Focused;
        if (!typing && !canvas.Testing)
        {
            switch (keyData)
            {
                case Keys.V: SetTool(Tool.Select); return true;
                case Keys.W: SetTool(Tool.WallFree); return true;
                case Keys.L: SetTool(Tool.WallLine); return true;
                case Keys.R: SetTool(Tool.WallRect); return true;
                case Keys.O: SetTool(Tool.WallEllipse); return true;
                case Keys.F: SetTool(Tool.GoalFree); return true;
                case Keys.T: SetTool(Tool.GoalRect); return true;
                case Keys.Y: SetTool(Tool.GoalEllipse); return true;
                case Keys.B: SetTool(Tool.Ball); return true;
                case Keys.E: SetTool(Tool.Eraser); return true;
            }
        }
        return false;
    }
}

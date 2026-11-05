using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>Режим уровней: простые фигуры, игрок ставит шары; скорость шара фиксирована.</summary>
public class PlayView : UserControl, IModeView
{
    private readonly GameCanvas canvas = new()
    {
        Editor = false, Dock = DockStyle.Fill, ShowGrid = false, Trails = false,
        GoalCelebrationOn = true,
        NewBallArgb = unchecked((int)0xFFF0F3FF),
    };
    private readonly LevelRuntime runtime;
    public GameCanvas Canvas => canvas;
    public LevelData CurrentLevel => levels[index];
    private readonly List<LevelData> levels = new();
    private readonly int builtinCount;
    private int index;
    private Label hintLbl;

    private readonly LevelSelectButton lblName = new()
    {
        Size = new Size(316, 38), Role = AccentRole.Accent,
    };
    private readonly NeoButton btnEdit = new()
    {
        Text = "В РЕДАКТОР", Icon = IconPainter.Pencil, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(118, 38), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
    };
    private readonly NeoButton btnNext = new()
    {
        Icon = IconPainter.Forward, Role = AccentRole.Green, Size = new Size(42, 38), IconPx = 20,
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Pause, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(46, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF) }; // БЕЛЫЙ
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 40, Value = 12 };

    public PlayView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        runtime = new LevelRuntime(canvas);
        runtime.StateChanged += () =>
        {
            btnPause.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
            btnPause.Invalidate();
        };

        var builtins = BuiltinLevels.All();
        builtinCount = builtins.Count;
        levels.AddRange(builtins);
        levels.AddRange(LevelStore.UserLevels());

        var top = new Toolbar { Dock = DockStyle.Top, Height = 56 };
        var hints = BuildHints(
            "ЛКМ — бросок  ·  ПКМ — убрать шар  ·  колесо или +/− — размер шара  ·  G — гравитация  ·  R — заново  ·  пробел — пауза",
            out hintLbl);

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(38, 38), IconPx = 20 };
        var btnNextLvl = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(38, 38), IconPx = 20 };
        var btnRestart = new NeoButton { Icon = IconPainter.Restart, Size = new Size(42, 38), IconPx = 20 };

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий уровень");
        tips.SetToolTip(btnNextLvl, "Следующий уровень");
        tips.SetToolTip(lblName, "Открыть сетку уровней с мини-превью");
        tips.SetToolTip(btnEdit, "Перенести копию текущего уровня в «Редактор»");
        tips.SetToolTip(chipFx, "Шейдерные эффекты: свечение, аберрация, виньетка");
        tips.SetToolTip(btnRestart, "Заново (R)");
        tips.SetToolTip(btnPause, "Пауза (пробел)");
        tips.SetToolTip(btnNext, "Следующий уровень (появляется после победы)");
        tips.SetToolTip(colorBtn, "Цвет новых шаров (СПЕКТР — каждый шар нового тона)");
        tips.SetToolTip(sizeBtn, "Размер нового шара — ползунок (или колесо мыши, +/−)");

        lblName.ItemsProvider = () =>
        {
            var items = new List<LevelCardItem>(levels.Count);
            for (int i = 0; i < levels.Count; i++)
            {
                var l = levels[i];
                var (badge, role, rotorStyle, src) = DescribeLevel(l);
                items.Add(new LevelCardItem
                {
                    Title = $"{i + 1}. {l.Name}",
                    Badge = badge,
                    Preview = l,
                    Source = src,
                    Role = role,
                    RotorStyle = rotorStyle,
                });
            }
            return (index, items);
        };
        lblName.LevelSelected += ShowLevel;

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + levels.Count) % levels.Count);
        btnNextLvl.Click += (s, e) => ShowLevel((index + 1) % levels.Count);
        btnEdit.Click += (s, e) => MainForm.RequestOpenInEditor(levels[index].Clone());
        btnRestart.Click += (s, e) => Restart();
        btnNext.Click += (s, e) => ShowLevel((index + 1) % levels.Count);
        btnPause.Click += (s, e) => TogglePause();
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;
        colorBtn.Changed += () =>
        {
            canvas.NewBallSpectrum = colorBtn.Selected == BallColors.Spectrum;
            canvas.NewBallArgb = colorBtn.Selected;
        };
        sizeBtn.Changed += v => canvas.NewBallR = v;
        canvas.BallRadiusChanged += () => sizeBtn.Value = (int)Math.Round(canvas.NewBallR); // колесо

        canvas.Won += () => btnNext.Enabled = true;

        top.AddLeft(btnPrev).AddLeft(btnNextLvl).AddLeft(lblName).AddLeft(btnEdit);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn)
           .AddRight(btnRestart).AddRight(btnPause).AddRight(btnNext);

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

    private static Bar BuildHints(string text, out Label lbl)
    {
        var bar = new Bar { Dock = DockStyle.Bottom, Height = 34, LineAtTop = true, BackColor = Theme.Panel };
        lbl = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  " + text,
            BackColor = Theme.Panel,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        bar.Controls.Add(lbl);
        return bar;
    }

    private static (string Badge, AccentRole Role, bool RotorStyle, Vec2? Source) DescribeLevel(LevelData l)
    {
        if (l.WaveEmitter)
            return ("ВОЛНА", AccentRole.Green, false, l.WaveSourcePos);
        if (l.RotorRules)
            return ("РОТОР", AccentRole.Orange, true, null);
        if (l.Goals.Count > 0)
            return ("ЦЕЛЬ", AccentRole.Green, false, null);
        return ("СВОБОДНЫЙ", AccentRole.Accent, false, null);
    }

    public void ReloadUserLevels()
    {
        var users = LevelStore.UserLevels();
        levels.RemoveRange(builtinCount, levels.Count - builtinCount);
        levels.AddRange(users);
        if (index >= levels.Count) ShowLevel(0);
        else if (index >= builtinCount) ShowLevel(index);
        else UpdateTitleLabel(levels[index]);
    }

    public void LoadCustomLevel(LevelData lvl)
    {
        levels.Add(lvl);
        ShowLevel(levels.Count - 1);
    }

    private void UpdateTitleLabel(LevelData lvl)
    {
        var (badge, _, _, _) = DescribeLevel(lvl);
        string suffix = lvl.Goals.Count == 0 || lvl.WaveEmitter || lvl.RotorRules ? $"  ·  {badge}" : "";
        lblName.Text = $"  {index + 1}/{levels.Count} · {lvl.Name}{suffix}";
    }

    private void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, levels.Count - 1);
        var lvl = levels[index];
        runtime.LoadAndStart(lvl);
        bool free = lvl.Goals.Count == 0;
        UpdateTitleLabel(lvl);
        if (hintLbl != null)
        {
            string baseHint = lvl.WaveEmitter
                ? "ЛКМ — перенести источник волны  ·  колесо или +/− — толщина лучей  ·  G — гравитация  ·  R — заново  ·  пробел — пауза"
                : lvl.InstantDrop
                    ? "ЛКМ — поставить шар (скорость 0)  ·  ПКМ — убрать шар  ·  колесо — размер  ·  R — заново  ·  пробел — пауза"
                    : "ЛКМ — бросок  ·  ПКМ — убрать шар  ·  колесо или +/− — размер шара  ·  G — гравитация  ·  R — заново  ·  пробел — пауза";
            hintLbl.Text = string.IsNullOrWhiteSpace(lvl.Hint)
                ? "  " + baseHint
                : $"  {lvl.Hint}  ·  R — заново  ·  пробел — пауза";
        }
        btnPause.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
        btnPause.Invalidate();
        sizeBtn.Value = (int)Math.Clamp(Math.Round(canvas.NewBallR), sizeBtn.Min, sizeBtn.Max);
        // в свободном уровне проходить нечего — «дальше» доступна сразу
        btnNext.Enabled = free;
    }

    private void Restart()
    {
        runtime.RestartCurrent();
        btnPause.Icon = canvas.Paused ? IconPainter.Play : IconPainter.Pause;
        btnPause.Invalidate();
        if (levels[index].Goals.Count > 0) btnNext.Enabled = false;
    }

    /// <summary>Гравитация режима уровней: применяется и к дизайну уровня.</summary>
    public bool GravityOn
    {
        get => canvas.WaveMode && canvas.UseWaveFront ? canvas.WaveFront.GravityOn : canvas.Sim.GravityOn;
        set
        {
            canvas.Sim.GravityOn = value;
            canvas.WaveFront.GravityOn = value;
            canvas.Design.Gravity = value;
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
        if (!runtime.IsWaitingForFirstDrop)
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
                Restart();
                return true;
            case Keys.G:
                GravityOn = !GravityOn; // чип в боковой панели синхронизируется сам
                return true;
            case Keys.Space:
                TogglePause();
                return true;
            case Keys.Control | Keys.Z:
                canvas.UndoBallPlacement();
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                sizeBtn.Value -= 2;
                canvas.NewBallR = sizeBtn.Value;
                return true;
            case Keys.Oemplus:
            case Keys.Add:
                sizeBtn.Value += 2;
                canvas.NewBallR = sizeBtn.Value;
                return true;
        }
        return false;
    }
}

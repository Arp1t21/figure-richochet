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
        NewBallArgb = unchecked((int)0xFFF0F3FF),
    };
    private readonly List<LevelData> levels = new();
    private readonly int builtinCount;
    private int index;
    private Label hintLbl;

    private readonly Label lblName = new()
    {
        AutoSize = false, Size = new Size(340, 34), ForeColor = Theme.Accent,
        Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly NeoButton btnNext = new()
    {
        Icon = IconPainter.Forward, Accent = Theme.Green, Size = new Size(42, 38), IconPx = 20,
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
        tips.SetToolTip(chipFx, "Шейдерные эффекты: свечение, аберрация, виньетка");
        tips.SetToolTip(btnRestart, "Заново (R)");
        tips.SetToolTip(btnPause, "Пауза (пробел)");
        tips.SetToolTip(btnNext, "Следующий уровень (появляется после победы)");
        tips.SetToolTip(colorBtn, "Цвет новых шаров (СПЕКТР — каждый шар нового тона)");
        tips.SetToolTip(sizeBtn, "Размер нового шара — ползунок (или колесо мыши, +/−)");

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + levels.Count) % levels.Count);
        btnNextLvl.Click += (s, e) => ShowLevel((index + 1) % levels.Count);
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

        top.AddLeft(btnPrev).AddLeft(btnNextLvl).AddLeft(lblName);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn)
           .AddRight(btnRestart).AddRight(btnPause).AddRight(btnNext);

        Controls.Add(canvas);
        Controls.Add(hints);
        Controls.Add(top);

        ShowLevel(0);
    }

    private static Bar BuildHints(string text, out Label lbl)
    {
        var bar = new Bar { Dock = DockStyle.Bottom, Height = 30, LineAtTop = true, BackColor = Theme.Panel };
        lbl = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  " + text,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        bar.Controls.Add(lbl);
        return bar;
    }

    public void ReloadUserLevels()
    {
        var users = LevelStore.UserLevels();
        if (levels.Count - builtinCount == users.Count)
        {
            bool same = true;
            for (int i = 0; i < users.Count; i++)
                if (levels[builtinCount + i].Name != users[i].Name) { same = false; break; }
            if (same) return;
        }
        levels.RemoveRange(builtinCount, levels.Count - builtinCount);
        levels.AddRange(users);
        if (index >= levels.Count) ShowLevel(0);
        else lblName.Text = $"  {index + 1}/{levels.Count} · {levels[index].Name}{(levels[index].Goals.Count == 0 ? "  ·  СВОБОДНЫЙ" : "")}";
    }

    private void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, levels.Count - 1);
        var lvl = levels[index];
        canvas.LoadLevel(lvl);
        bool free = lvl.Goals.Count == 0;
        lblName.Text = $"  {index + 1}/{levels.Count} · {lvl.Name}{(free ? "  ·  СВОБОДНЫЙ" : "")}";
        if (hintLbl != null)
        {
            const string baseHint = "ЛКМ — бросок  ·  ПКМ — убрать шар  ·  колесо или +/− — размер шара  ·  G — гравитация  ·  R — заново  ·  пробел — пауза";
            hintLbl.Text = string.IsNullOrWhiteSpace(lvl.Hint)
                ? "  " + baseHint
                : $"  {lvl.Hint}  ·  ЛКМ — бросок  ·  ПКМ — убрать  ·  R — заново";
        }
        canvas.Paused = false;
        btnPause.Icon = IconPainter.Pause;
        // в свободном уровне проходить нечего — «дальше» доступна сразу
        btnNext.Enabled = free;
    }

    private void Restart()
    {
        canvas.RestartSim();
        canvas.Sim.GravityOn = canvas.Design.Gravity;
        canvas.Paused = false;
        btnPause.Icon = IconPainter.Pause;
        if (levels[index].Goals.Count > 0) btnNext.Enabled = false;
    }

    /// <summary>Гравитация режима уровней: применяется и к дизайну уровня.</summary>
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

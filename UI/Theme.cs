using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Ricochet.UI;

/// <summary>Палитра минималистично-футуристичной неоновой темы.</summary>
public static class Theme
{
    public static readonly Color Bg      = Color.FromArgb(8, 10, 15);
    public static readonly Color Panel   = Color.FromArgb(13, 16, 24);
    public static readonly Color Panel2  = Color.FromArgb(19, 23, 34);
    public static readonly Color Hover   = Color.FromArgb(27, 33, 48);
    public static readonly Color Pressed = Color.FromArgb(11, 14, 21);
    public static readonly Color Border  = Color.FromArgb(36, 45, 64);
    public static readonly Color PanelBorder = Color.FromArgb(30, 38, 56);
    public static readonly Color Text    = Color.FromArgb(226, 232, 242);
    public static readonly Color TextDim = Color.FromArgb(124, 134, 154);
    public static readonly Color Accent  = Color.FromArgb(94, 197, 255);
    public static readonly Color Green   = Color.FromArgb(64, 225, 140);
    public static readonly Color Orange  = Color.FromArgb(255, 176, 88);
    public static readonly Color Red     = Color.FromArgb(255, 92, 92);
    public static readonly Color Violet  = Color.FromArgb(166, 128, 255);

    public static Color Mix(Color a, Color b, float k)
    {
        k = Math.Clamp(k, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * k),
            (int)(a.G + (b.G - a.G) * k),
            (int)(a.B + (b.B - a.B) * k));
    }

    public static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        if (r.Width < 2 || r.Height < 2) { p.AddRectangle(r); return p; }
        int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void StyleForm(Form f)
    {
        f.BackColor = Bg;
        f.ForeColor = Text;
        f.Font = new Font("Segoe UI", 9.5F);
    }
}

/// <summary>Панель-бар с тонкой акцентной линией (сверху или снизу) — нижние панели окон.</summary>
public class Bar : Panel
{
    public bool LineAtTop;

    public Bar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int y = LineAtTop ? 0 : Height - 1;
        using var p = new Pen(Theme.PanelBorder, 1);
        e.Graphics.DrawLine(p, 0, y, Width - 1, y);
    }
}

/// <summary>Режим в главном окне: клавиши сначала получает активный режим,
/// а общие настройки (гравитация, треки) читаются и применяются через свойства.</summary>
public interface IModeView
{
    bool HandleKey(Keys keyData);
    bool GravityOn { get; set; }
    bool TrailsOn { get; set; }
    void SetActive(bool active);
}

/// <summary>
/// Палитра цветов шаров — только неоновые цвета, гармонирующие с тёмной темой.
/// СПЕКТР (Argb == 0) — особый режим: каждый следующий шар/луч нового тона.
/// </summary>
public static class BallColors
{
    public const int Spectrum = 0;

    public static readonly (string Name, int Argb)[] Palette =
    {
        ("БЕЛЫЙ",   unchecked((int)0xFFF0F3FF)),
        ("ЦИАН",    unchecked((int)0xFF5EC5FF)),
        ("ЗЕЛЁНЫЙ", unchecked((int)0xFF40E18C)),
        ("ОРАНЖ",   unchecked((int)0xFFFFB058)),
        ("ФИОЛЕТ",  unchecked((int)0xFFA680FF)),
        ("РОЗОВЫЙ", unchecked((int)0xFFFF78BE)),
        ("КРАСНЫЙ", unchecked((int)0xFFFF5C5C)),
    };
}

/// <summary>Кнопка выбора цвета шара: свотч текущего цвета + шеврон, клик — всплывающая палитра.</summary>
public class BallColorButton : Control
{
    /// <summary>Текущий выбор: ARGB-цвет или BallColors.Spectrum (радуга).</summary>
    public int Selected = BallColors.Spectrum;
    public event Action Changed;

    public BallColorButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(46, 38);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(rect, 8))
        {
            using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, path);
            using (var p = new Pen(Theme.Border, 1.1f)) g.DrawPath(p, path);
        }
        var sw = new Rectangle(9, (Height - 16) / 2, 16, 16);
        if (Selected == BallColors.Spectrum)
            using (var lb = RainbowBrush(sw))
                g.FillEllipse(lb, sw);
        else
        {
            using (var b = new SolidBrush(Color.FromArgb(Selected))) g.FillEllipse(b, sw);
            using var p = new Pen(Theme.TextDim, 1f);
            g.DrawEllipse(p, sw);
        }
        float cx = Width - 11, cy = Height / 2f;
        using (var cp = new Pen(Theme.TextDim, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(cp, cx - 3, cy - 1.5f, cx, cy + 1.5f);
            g.DrawLine(cp, cx, cy + 1.5f, cx + 3, cy - 1.5f);
        }
    }

    internal static LinearGradientBrush RainbowBrush(Rectangle r)
    {
        var blend = new ColorBlend
        {
            Colors = new[] { Color.FromArgb(255, 92, 92), Color.FromArgb(255, 176, 88), Color.FromArgb(64, 225, 140), Color.FromArgb(94, 197, 255), Color.FromArgb(166, 128, 255) },
            Positions = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
        };
        return new LinearGradientBrush(r, Color.Black, Color.Black, 20f) { InterpolationColors = blend };
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        // popup живёт собственной жизнью: Close() сам его освободит —
        // using здесь был багом (форма уничтожалась сразу после открытия)
        var popup = new ColorPopup(Selected);
        popup.OnPick = argb =>
        {
            Selected = argb;
            Invalidate();
            Changed?.Invoke();
        };
        PopupPlacement.BelowCentered(popup, this);
        popup.Show(this);
        popup.Activate();
    }
}

/// <summary>Размещение всплывающих карточек: горизонтально по центру кнопки,
/// верх карточки — сразу под кнопкой (если снизу нет места — раскрывается вверх).</summary>
internal static class PopupPlacement
{
    public static void BelowCentered(Form popup, Control button)
    {
        var wa = Screen.FromControl(button).WorkingArea;
        var b = button.PointToScreen(Point.Empty);
        int x = b.X + button.Width / 2 - popup.Width / 2;
        int y = b.Y + button.Height + 6;
        if (y + popup.Height > wa.Bottom) y = b.Y - popup.Height - 6;
        if (x < wa.Left) x = wa.Left;
        if (x > wa.Right - popup.Width) x = wa.Right - popup.Width;
        if (y < wa.Top) y = wa.Top;
        popup.Location = new Point(x, y);
    }
}

/// <summary>Всплывающая палитра: СПЕКТР + неоновые цвета; закрывается по выбору или клику мимо.</summary>
internal sealed class ColorPopup : Form
{
    public Action<int> OnPick;

    public ColorPopup(int current)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Theme.Panel;
        const int cell = 28, gap = 6, pad = 10;
        ClientSize = new Size(pad + 4 * cell + 3 * gap, pad + 2 * cell + gap);

        var cells = new List<Swatch> { new(BallColors.Spectrum, true) };
        foreach (var (_, argb) in BallColors.Palette)
            cells.Add(new Swatch(argb, false));
        for (int i = 0; i < cells.Count; i++)
        {
            var s = cells[i];
            s.Current = s.Argb == current;
            s.Location = new Point(pad + (i % 4) * (cell + gap), pad + (i / 4) * (cell + gap));
            s.Click += (o, _) =>
            {
                OnPick?.Invoke(((Swatch)o).Argb);
                Close();
            };
            Controls.Add(s);
        }
        Deactivate += (s, _) => Close();
        KeyDown += (s, _) => Close();
        Paint += (s, pe) =>
        {
            using var bp = new Pen(Theme.Border, 1f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

    private sealed class Swatch : Control
    {
        public readonly int Argb;
        public bool IsSpectrum;
        public bool Current;

        public Swatch(int argb, bool spectrum)
        {
            Argb = argb;
            IsSpectrum = spectrum;
            Size = new Size(28, 28);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (IsSpectrum)
                using (var lb = BallColorButton.RainbowBrush(r)) g.FillEllipse(lb, r);
            else
                using (var b = new SolidBrush(Color.FromArgb(Argb))) g.FillEllipse(b, r);
            using var p = new Pen(Current ? Theme.Accent : Theme.PanelBorder, Current ? 2f : 1f);
            g.DrawEllipse(p, r);
        }
    }
}

/// <summary>Компактная кнопка размера шара: показывает «R 12», клик — карточка с горизонтальным ползунком и подписями значений.</summary>
public class SizeButton : Control
{
    public int Min = 4;
    public int Max = 40;
    public event Action<int> Changed;

    private int val = 12;

    public int Value
    {
        get => val;
        set { var v = Math.Clamp(value, Min, Max); if (v == val) return; val = v; Invalidate(); openPopup?.Sync(v); }
    }

    private SizePopup openPopup;

    public SizeButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(54, 38);
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(rect, 8))
        {
            using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, path);
            using var p = new Pen(Theme.Border, 1.1f);
            g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, "R " + val, Font, rect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var popup = new SizePopup(Min, Max, val); // Close() сам освободит форму
        openPopup = popup;
        popup.FormClosed += (s, _) => openPopup = null;
        popup.OnChange = v =>
        {
            if (v == val) return;
            val = v;
            Invalidate();
            Changed?.Invoke(v);
        };
        PopupPlacement.BelowCentered(popup, this);
        popup.Show(this);
        popup.Activate();
    }
}

/// <summary>Карточка выбора размера: текущее значение (клик — ввод цифры), ползунок и подписи значений.</summary>
internal sealed class SizePopup : Form
{
    public Action<int> OnChange;
    private readonly TrackBar tb;
    private readonly Label lbl;
    private TextBox editBox;

    public SizePopup(int min, int max, int current)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Theme.Panel;
        ClientSize = new Size(240, 100);

        lbl = new Label
        {
            Text = "R " + current,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 8, 240, 24),
            Cursor = Cursors.Hand,
        };
        lbl.Click += (s, _) => BeginEdit();
        Controls.Add(lbl);

        tb = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(current, min, max),
            TickFrequency = Math.Max(1, (max - min) / 5),
            SmallChange = 1,
            LargeChange = Math.Max(1, (max - min) / 5),
            Bounds = new Rectangle(18, 34, 204, 44),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
        };
        tb.ValueChanged += (s, _) =>
        {
            lbl.Text = "R " + tb.Value;
            OnChange?.Invoke(tb.Value);
        };
        Controls.Add(tb);

        // подписи значений под ползунком в пяти точках
        for (int i = 0; i < 5; i++)
        {
            int v = min + (max - min) * i / 4;
            var l = new Label
            {
                Text = v.ToString(),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Theme.TextDim,
                AutoSize = false,
                Size = new Size(28, 14),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            l.Location = new Point(18 + 204 * i / 4 - l.Width / 2 + (i == 4 ? -8 : i == 0 ? 8 : 0), 80);
            Controls.Add(l);
        }

        Deactivate += (s, _) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Paint += (s, pe) =>
        {
            using var bp = new Pen(Theme.Border, 1f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

    /// <summary>Клик по значению — ввести цифру вручную.</summary>
    private void BeginEdit()
    {
        if (editBox != null) return;
        editBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            Bounds = new Rectangle(75, 7, 90, 24),
            Text = tb.Value.ToString(),
        };
        editBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (int.TryParse(editBox.Text.Trim(), out int v))
                    Sync(v);
                EndEdit();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                EndEdit();
                e.SuppressKeyPress = true;
            }
        };
        editBox.LostFocus += (s, _) => EndEdit();
        Controls.Add(editBox);
        editBox.BringToFront();
        editBox.Focus();
        editBox.SelectAll();
    }

    private void EndEdit()
    {
        if (editBox == null) return;
        Controls.Remove(editBox);
        editBox.Dispose();
        editBox = null;
    }

    /// <summary>Внешняя синхронизация (например, колесом мыши) — карточка обновляется на лету.</summary>
    public void Sync(int v)
    {
        var clamped = Math.Clamp(v, tb.Minimum, tb.Maximum);
        if (clamped == tb.Value) return;
        tb.Value = clamped; // ValueChanged поднимет OnChange — он безопасен (совпадающее значение игнорируется)
    }
}

/// <summary>
/// Кнопка со всплывающей карточкой-ползунком для дробных величин (скорость, плотность лучей).
/// Поддерживает линейную и логарифмическую шкалы; изменение применяется на лету.
/// </summary>
public class SliderButton : Control
{
    public double Min = 0.01;
    public double Max = 5.0;
    public bool Log;                                  // логарифмическая шкала
    public string Prefix = "";
    public Func<double, string> Format = static v => Math.Round(v).ToString();
    public event Action<double> Changed;

    private double val;
    private SliderPopup openPopup;

    public double Value
    {
        get => val;
        set
        {
            var v = Math.Clamp(value, Math.Min(Min, Max), Math.Max(Min, Max));
            if (v == val) return;
            val = v;
            Invalidate();
            openPopup?.Sync(v);
        }
    }

    public SliderButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(96, 38);
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(rect, 8))
        {
            using (var b = new SolidBrush(Theme.Panel2)) g.FillPath(b, path);
            using var p = new Pen(Theme.Border, 1.1f);
            g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, Prefix + Format(val), Font, rect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var popup = new SliderPopup(Min, Max, Log, val, Format);
        openPopup = popup;
        popup.FormClosed += (s, _) => openPopup = null;
        popup.OnChange = v =>
        {
            if (v == val) return;
            val = v;
            Invalidate();
            Changed?.Invoke(v);
        };
        PopupPlacement.BelowCentered(popup, this);
        popup.Show(this);
        popup.Activate();
    }
}

/// <summary>Карточка-ползунок: крупное текущее значение, трекбар и подписи в пяти точках шкалы.</summary>
internal sealed class SliderPopup : Form
{
    public Action<double> OnChange;

    private readonly double min, max;
    private readonly bool log;
    private readonly TrackBar tb;
    private readonly Label lbl;
    private readonly Func<double, string> format;
    private TextBox editBox;

    public SliderPopup(double min, double max, bool log, double current, Func<double, string> format)
    {
        this.min = min;
        this.max = max;
        this.log = log;
        this.format = format;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Theme.Panel;
        ClientSize = new Size(250, 104);

        lbl = new Label
        {
            Text = format(current),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 8, 250, 24),
            Cursor = Cursors.Hand,
        };
        lbl.Click += (s, _) => BeginEdit();
        Controls.Add(lbl);

        tb = new TrackBar
        {
            Minimum = 0,
            Maximum = 1000,
            SmallChange = 5,
            LargeChange = 50,
            TickFrequency = 250,
            Bounds = new Rectangle(22, 36, 206, 44),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
        };
        tb.ValueChanged += (s, _) =>
        {
            double v = FromT(tb.Value);
            lbl.Text = format(v);
            OnChange?.Invoke(v);
        };
        tb.Value = ToT(current);
        Controls.Add(tb);

        // подписи значений в пяти точках шкалы
        for (int i = 0; i < 5; i++)
        {
            var l = new Label
            {
                Text = format(FromT(i * 250)),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Theme.TextDim,
                AutoSize = false,
                Size = new Size(44, 14),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            l.Location = new Point(22 + 206 * i / 4 - l.Width / 2, 84);
            Controls.Add(l);
        }

        Deactivate += (s, _) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Paint += (s, pe) =>
        {
            using var bp = new Pen(Theme.Border, 1f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

    /// <summary>Клик по значению — ввести цифру вручную.</summary>
    private void BeginEdit()
    {
        if (editBox != null) return;
        editBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            Bounds = new Rectangle(80, 7, 90, 24),
            Text = format(FromT(tb.Value)).Replace("×", "").Replace("K", "").Replace("К", ""),
        };
        editBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                var txt = editBox.Text.Trim().Replace(',', '.');
                if (double.TryParse(txt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    Sync(dv);
                EndEdit();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                EndEdit();
                e.SuppressKeyPress = true;
            }
        };
        editBox.LostFocus += (s, _) => EndEdit();
        Controls.Add(editBox);
        editBox.BringToFront();
        editBox.Focus();
        editBox.SelectAll();
    }

    private void EndEdit()
    {
        if (editBox == null) return;
        Controls.Remove(editBox);
        editBox.Dispose();
        editBox = null;
    }

    private double FromT(int t) =>
        log ? min * Math.Pow(max / min, t / 1000.0) : min + (max - min) * t / 1000.0;

    private int ToT(double v) => (int)Math.Clamp(Math.Round(
        log ? 1000 * Math.Log(v / min) / Math.Log(max / min)
            : 1000 * (v - min) / (max - min)), 0, 1000);

    public void Sync(double v)
    {
        int t = ToT(Math.Clamp(v, Math.Min(min, max), Math.Max(min, max)));
        if (t == tb.Value) return;
        tb.Value = t;
    }
}

/// <summary>
/// Верхняя панель инструментов: элементы в три группы (слева / центр / справа).
/// Расстановка и вертикальное центрирование выполняются вручную при каждом
/// изменении размера — раскладка не зависит от докинга и автомасштабирования,
/// поэтому элементы не обрезаются верхом панели ни при каких масштабах.
/// </summary>
public class Toolbar : Panel
{
    private readonly List<Control> left = new(), center = new(), right = new();
    private const int Gap = 6;

    public Toolbar AddLeft(Control c)   { left.Add(c);   return Place(c); }
    public Toolbar AddCenter(Control c) { center.Add(c); return Place(c); }
    public Toolbar AddRight(Control c)  { right.Add(c);  return Place(c); }

    private Toolbar Place(Control c)
    {
        Controls.Add(c);
        Relayout();
        return this;
    }

    public Toolbar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Relayout(); // любой проход раскладки (в т.ч. масштабирование) заново центрирует элементы
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Theme.PanelBorder, 1);
        e.Graphics.DrawLine(p, 0, Height - 1, Width - 1, Height - 1);
    }

    public void Relayout()
    {
        if (Width == 0 || Height == 0) return;
        int leftEnd = 10;
        foreach (var c in left)
        {
            c.Location = new Point(leftEnd, Math.Max(0, (Height - c.Height) / 2));
            leftEnd += c.Width + Gap;
        }
        if (left.Count > 0) leftEnd -= Gap;

        int rightStart = Width - 10;
        for (int i = right.Count - 1; i >= 0; i--)
        {
            var c = right[i];
            rightStart -= c.Width;
            c.Location = new Point(rightStart, Math.Max(0, (Height - c.Height) / 2));
            rightStart -= Gap;
        }
        if (right.Count > 0) rightStart += Gap;

        if (center.Count > 0)
        {
            int w = 0;
            foreach (var c in center) w += c.Width + Gap;
            w -= Gap;
            int minX = left.Count > 0 ? leftEnd + 12 : 10;
            int maxX = right.Count > 0 ? rightStart - 12 - w : Width - 10 - w;
            int x = maxX >= minX
                ? Math.Clamp((Width - w) / 2, minX, maxX)
                : minX;
            foreach (var c in center)
            {
                c.Location = new Point(x, Math.Max(0, (Height - c.Height) / 2));
                x += c.Width + Gap;
            }
        }
    }
}

/// <summary>
/// Минималистичная кнопка: скругление, тонкая рамка, ховер-свечение; режим-переключатель (IsToggle)
/// с акцентной чертой снизу, опциональная векторная иконка (Icon) и акцентный цвет (Accent).
/// </summary>
public class NeoButton : Control
{
    public bool IsToggle;
    public bool IsOn;
    public Color? Accent;
    public Action<Graphics, Rectangle, Color> Icon;
    public int IconPx = 20;

    private bool hover, pressed;

    public NeoButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        TabStop = false;
        Font = new Font("Segoe UI", 9.5F);
        ForeColor = Theme.Text;
        Size = new Size(92, 34);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        if (IsToggle) { IsOn = !IsOn; Invalidate(); }
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width < 4 || rect.Height < 4) return;

        Color accent = Accent ?? Theme.Accent;
        bool on = IsToggle && IsOn;
        bool dim = !Enabled;

        Color fill = !Enabled ? Theme.Panel
                   : on        ? Theme.Mix(Theme.Panel2, accent, 0.18f)
                   : pressed   ? Theme.Pressed
                   : hover     ? Theme.Hover
                   :             Theme.Panel2;
        Color bc = !Enabled ? Theme.Border
                 : (on || Accent != null) ? accent
                 : Theme.Border;

        using (var path = Theme.RoundRect(rect, 8))
        {
            // «всплытие» при наведении: мягкое свечение вокруг кнопки
            if (hover && Enabled)
            {
                using var glowPath = Theme.RoundRect(new Rectangle(-2, -2, Width + 3, Height + 3), 10);
                using var glow = new SolidBrush(Color.FromArgb(30, accent));
                g.FillPath(glow, glowPath);
            }
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            Color hbc = hover && Enabled ? Theme.Mix(bc, accent, 0.45f) : bc;
            using (var p = new Pen(hbc, on ? 1.6f : 1.1f)) g.DrawPath(p, path);
        }

        // включённый переключатель: тонкая акцентная черта у нижней кромки
        if (on)
        {
            using var lp = new Pen(accent, 2f);
            g.DrawLine(lp, rect.Left + 10, rect.Bottom - 3, rect.Right - 10, rect.Bottom - 3);
        }

        Color fg = !Enabled ? Theme.TextDim
                 : (on || Accent != null) ? accent
                 : ForeColor;

        int dy = pressed ? 1 : 0; // тактильный сдвиг контента при нажатии
        var tsz = TextRenderer.MeasureText(Text, Font);
        int iconW = Icon != null ? IconPx : 0;
        int gap = iconW > 0 && tsz.Width > 0 ? 7 : 0;
        int total = iconW + gap + tsz.Width;
        int x = (Width - total) / 2;
        if (Icon != null)
        {
            Icon(g, new Rectangle(x, (Height - IconPx) / 2 + dy, IconPx, IconPx), fg);
            x += iconW + gap;
        }
        if (tsz.Width > 0)
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(x - 2, (Height - tsz.Height) / 2 + dy, tsz.Width + 4, tsz.Height + 2),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Векторные иконки (в логических координатах 24×24), как в графических редакторах.</summary>
public static class IconPainter
{
    private static Pen P(Color c, float w) =>
        new(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    private static void Poly(Graphics g, Color c, float w, bool fill, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        if (fill) using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        else using (var p = P(c, w)) g.DrawPolygon(p, pts);
    }

    private static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
    { using var p = P(c, w); g.DrawLine(p, x1, y1, x2, y2); }

    private static void Dot(Graphics g, Color c, float cx, float cy, float r)
    { using var b = new SolidBrush(c); g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2); }

    // каждая иконка рисует себя в rect (масштабируется к 24×24)
    public static void Doc(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { Poly(gg, c, 1.8f, false, 7, 3, 14, 3, 17, 6, 17, 21, 7, 21); Line(gg, c, 1.6f, 14, 3, 14, 6); Line(gg, c, 1.6f, 14, 6, 17, 6); }); }

    public static void Folder(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 1.8f, false, 3, 6, 9, 6, 11, 8, 21, 8, 21, 19, 3, 19)); }

    public static void Floppy(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 4, 4, 15, 4, 20, 9, 20, 20, 4, 20);
            Line(gg, c, 1.6f, 15, 4, 15, 9);
            Line(gg, c, 1.6f, 15, 9, 20, 9);
            Poly(gg, c, 1.6f, false, 8, 13, 16, 13, 16, 20, 8, 20);
        });
    }

    public static void Undo(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2f, 18, 10, 7, 10);
            Poly(gg, c, 2f, false, 11.5f, 6, 7, 10, 11.5f, 14);
        });
    }

    public static void Redo(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2f, 6, 10, 17, 10);
            Poly(gg, c, 2f, false, 12.5f, 6, 17, 10, 12.5f, 14);
        });
    }

    public static void CursorArrow(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 1.2f, true, 7.5f, 3, 7.5f, 18, 11.5f, 14.5f, 14, 19.5f, 16.2f, 18.4f, 13.7f, 13.4f, 17.8f, 13.4f)); }

    public static void Pencil(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 15, 3.5f, 20.5f, 9, 9.5f, 20, 4, 14.5f);
            Poly(gg, c, 1.4f, true, 4, 14.5f, 3, 21, 9.5f, 20);
            Line(gg, c, 1.3f, 16.6f, 6.8f, 12.2f, 11.2f);
        });
    }

    public static void LineSeg(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2f, 7, 17, 17, 7);
            Dot(gg, c, 7, 17, 2.4f);
            Dot(gg, c, 17, 7, 2.4f);
        });
    }

    public static void Rect(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { using var p = P(c, 2f); gg.DrawRectangle(p, 4.5f, 6.5f, 15, 11); }); }

    public static void Ellipse(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { using var p = P(c, 2f); gg.DrawEllipse(p, 4f, 5.5f, 16, 13); }); }

    public static void Ball(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            using var b = new SolidBrush(c);
            gg.FillEllipse(b, 4.5f, 4.5f, 15, 15);
            Dot(gg, Color.FromArgb(235, 255, 255, 255), 8.6f, 8.2f, 2.1f);
        });
    }

    public static void Eraser(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 8, 12.5f, 13, 17.5f, 20.5f, 10, 15.5f, 5);
            Line(gg, c, 1.8f, 3.5f, 20.5f, 13.5f, 20.5f);
            Line(gg, c, 1.3f, 10.8f, 10.2f, 15.5f, 14.9f);
        });
    }

    public static void Play(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 1f, true, 8, 5.5f, 18.5f, 12, 8, 18.5f)); }

    public static void Stop(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { using var b = new SolidBrush(c); gg.FillRectangle(b, 6.5f, 6.5f, 11, 11); }); }

    public static void Plus(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { Line(gg, c, 2f, 7, 12, 17, 12); Line(gg, c, 2f, 12, 7, 12, 17); }); }

    public static void Minus(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Line(gg, c, 2f, 7, 12, 17, 12)); }

    public static void ChevronLeft(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 2.4f, false, 14.5f, 5, 8, 12, 14.5f, 19)); }

    public static void ChevronRight(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 2.4f, false, 9.5f, 5, 16, 12, 9.5f, 19)); }

    public static void Restart(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            using var p = P(c, 2.2f);
            gg.DrawArc(p, 4.5f, 4.5f, 15, 15, 120, 290);
            // стрелка на конце дуги (угол 50°)
            double a = 50 * Math.PI / 180;
            var pt = new PointF(12 + 7.5f * (float)Math.Cos(a), 12 + 7.5f * (float)Math.Sin(a));
            var t = new PointF(-(float)Math.Sin(a), (float)Math.Cos(a));
            var n = new PointF((float)Math.Cos(a), (float)Math.Sin(a));
            var pts = new[]
            {
                pt,
                new PointF(pt.X - t.X * 4 + n.X * 3, pt.Y - t.Y * 4 + n.Y * 3),
                new PointF(pt.X - t.X * 4 - n.X * 3, pt.Y - t.Y * 4 - n.Y * 3),
            };
            using var b = new SolidBrush(c);
            gg.FillPolygon(b, pts);
        });
    }

    public static void Pause(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            using var b = new SolidBrush(c);
            gg.FillRectangle(b, 7.2f, 5.5f, 3.6f, 13);
            gg.FillRectangle(b, 13.2f, 5.5f, 3.6f, 13);
        });
    }

    public static void Home(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.9f, false, 3.5f, 11.5f, 12, 4, 20.5f, 11.5f);
            Poly(gg, c, 1.9f, false, 6.5f, 11f, 17.5f, 11f, 17.5f, 20f, 6.5f, 20f);
            Poly(gg, c, 1.5f, false, 10.3f, 14.3f, 13.7f, 14.3f, 13.7f, 20f, 10.3f, 20f);
        });
    }

    public static void Forward(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1f, true, 5.5f, 5.5f, 13.5f, 12, 5.5f, 18.5f);
            using var b = new SolidBrush(c);
            gg.FillRectangle(b, 15.5f, 5.5f, 3, 13);
        });
    }

    public static void Gravity(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2f, 12, 4, 12, 14.5f);
            Poly(gg, c, 1.6f, true, 8.3f, 11.3f, 12, 15.7f, 15.7f, 11.3f);
            Line(gg, c, 2f, 5, 19.5f, 19, 19.5f);
        });
    }

    public static void Waves(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Dot(gg, c, 12, 13, 1.8f);
            using var p = P(c, 1.8f);
            gg.DrawArc(p, 12 - 4.5f, 13 - 4.5f, 9, 9, 180, 180);
            gg.DrawArc(p, 12 - 8f, 13 - 8f, 16, 16, 180, 180);
            gg.DrawArc(p, 12 - 11.5f, 13 - 11.5f, 23, 23, 180, 180);
        });
    }

    private static void Scale(Graphics g, Rectangle r, Action<Graphics> body)
    {
        var st = g.Save();
        float u = Math.Min(r.Width, r.Height) / 24f;
        g.TranslateTransform(r.Left, r.Top);
        g.ScaleTransform(u, u);
        body(g);
        g.Restore(st);
    }
}

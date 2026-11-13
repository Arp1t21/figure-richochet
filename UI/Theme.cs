using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>Семантическая роль акцента кнопки — автоматически обновляется при смене темы.</summary>
public enum AccentRole
{
    None,
    Accent,
    Green,
    Orange,
    Violet,
    Red,
}

/// <summary>Палитра минималистично-футуристичной неоновой темы с поддержкой горячей смены пресетов.</summary>
public static class Theme
{
    public readonly record struct Preset(
        string Name,
        Color Bg,
        Color Panel,
        Color Panel2,
        Color Hover,
        Color Pressed,
        Color Border,
        Color PanelBorder,
        Color Text,
        Color TextDim,
        Color Accent,
        Color Green,
        Color Orange,
        Color Red,
        Color Violet);

    public static readonly Preset[] Presets =
    {
        // 0: КИБЕР — классический глубокий космос + электрический циан
        new("КИБЕР",
            Bg:          Color.FromArgb(8, 10, 15),
            Panel:       Color.FromArgb(13, 16, 24),
            Panel2:      Color.FromArgb(19, 23, 34),
            Hover:       Color.FromArgb(27, 33, 48),
            Pressed:     Color.FromArgb(11, 14, 21),
            Border:      Color.FromArgb(38, 48, 68),
            PanelBorder: Color.FromArgb(30, 38, 56),
            Text:        Color.FromArgb(228, 234, 245),
            TextDim:     Color.FromArgb(126, 138, 160),
            Accent:      Color.FromArgb(94, 197, 255),
            Green:       Color.FromArgb(64, 225, 140),
            Orange:      Color.FromArgb(255, 176, 88),
            Red:         Color.FromArgb(255, 92, 92),
            Violet:      Color.FromArgb(166, 128, 255)),

        // 1: СИНТВЕЙВ — полночный пурпур + неоновая маджента и бирюза
        new("СИНТВЕЙВ",
            Bg:          Color.FromArgb(11, 7, 18),
            Panel:       Color.FromArgb(18, 11, 29),
            Panel2:      Color.FromArgb(27, 17, 43),
            Hover:       Color.FromArgb(39, 24, 62),
            Pressed:     Color.FromArgb(14, 8, 22),
            Border:      Color.FromArgb(68, 39, 100),
            PanelBorder: Color.FromArgb(50, 29, 76),
            Text:        Color.FromArgb(245, 233, 255),
            TextDim:     Color.FromArgb(164, 136, 194),
            Accent:      Color.FromArgb(255, 96, 212),
            Green:       Color.FromArgb(0, 238, 212),
            Orange:      Color.FromArgb(255, 186, 82),
            Red:         Color.FromArgb(255, 82, 118),
            Violet:      Color.FromArgb(186, 116, 255)),

        // 2: АМОЛЕД — угольно-чёрный минимализм + ледяной голубой и мята
        new("АМОЛЕД",
            Bg:          Color.FromArgb(2, 3, 5),
            Panel:       Color.FromArgb(7, 9, 14),
            Panel2:      Color.FromArgb(13, 17, 25),
            Hover:       Color.FromArgb(22, 28, 40),
            Pressed:     Color.FromArgb(5, 7, 11),
            Border:      Color.FromArgb(36, 45, 62),
            PanelBorder: Color.FromArgb(24, 31, 44),
            Text:        Color.FromArgb(240, 245, 253),
            TextDim:     Color.FromArgb(132, 142, 162),
            Accent:      Color.FromArgb(132, 216, 255),
            Green:       Color.FromArgb(62, 238, 172),
            Orange:      Color.FromArgb(255, 188, 98),
            Red:         Color.FromArgb(255, 96, 108),
            Violet:      Color.FromArgb(178, 144, 255)),

        // 3: ЯНТАРЬ — тёплая ретро-лаборатория + плазменное золото и лайм
        new("ЯНТАРЬ",
            Bg:          Color.FromArgb(12, 9, 7),
            Panel:       Color.FromArgb(19, 15, 11),
            Panel2:      Color.FromArgb(28, 22, 16),
            Hover:       Color.FromArgb(41, 32, 23),
            Pressed:     Color.FromArgb(14, 11, 8),
            Border:      Color.FromArgb(68, 51, 35),
            PanelBorder: Color.FromArgb(48, 36, 25),
            Text:        Color.FromArgb(248, 238, 224),
            TextDim:     Color.FromArgb(168, 148, 124),
            Accent:      Color.FromArgb(255, 184, 72),
            Green:       Color.FromArgb(128, 232, 90),
            Orange:      Color.FromArgb(255, 134, 74),
            Red:         Color.FromArgb(255, 94, 84),
            Violet:      Color.FromArgb(224, 144, 255)),
    };

    public static int CurrentPreset { get; private set; }
    public static string CurrentName => Presets[CurrentPreset].Name;
    public static event Action Changed;

    public static Color Bg          = Presets[0].Bg;
    public static Color Panel       = Presets[0].Panel;
    public static Color Panel2      = Presets[0].Panel2;
    public static Color Hover       = Presets[0].Hover;
    public static Color Pressed     = Presets[0].Pressed;
    public static Color Border      = Presets[0].Border;
    public static Color PanelBorder = Presets[0].PanelBorder;
    public static Color Text        = Presets[0].Text;
    public static Color TextDim     = Presets[0].TextDim;
    public static Color Accent      = Presets[0].Accent;
    public static Color Green       = Presets[0].Green;
    public static Color Orange      = Presets[0].Orange;
    public static Color Red         = Presets[0].Red;
    public static Color Violet      = Presets[0].Violet;

    public static Color ResolveRole(AccentRole role) => role switch
    {
        AccentRole.Accent => Accent,
        AccentRole.Green  => Green,
        AccentRole.Orange => Orange,
        AccentRole.Violet => Violet,
        AccentRole.Red    => Red,
        _                 => Accent,
    };

    public static void SetPreset(int index)
    {
        CurrentPreset = ((index % Presets.Length) + Presets.Length) % Presets.Length;
        var p = Presets[CurrentPreset];
        Bg          = p.Bg;
        Panel       = p.Panel;
        Panel2      = p.Panel2;
        Hover       = p.Hover;
        Pressed     = p.Pressed;
        Border      = p.Border;
        PanelBorder = p.PanelBorder;
        Text        = p.Text;
        TextDim     = p.TextDim;
        Accent      = p.Accent;
        Green       = p.Green;
        Orange      = p.Orange;
        Red         = p.Red;
        Violet      = p.Violet;
        Changed?.Invoke();
    }

    public static void CyclePreset() => SetPreset(CurrentPreset + 1);

    public static Color Mix(Color a, Color b, float k)
    {
        k = Math.Clamp(k, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * k),
            (int)(a.G + (b.G - a.G) * k),
            (int)(a.B + (b.B - a.B) * k));
    }

    private static readonly Dictionary<int, SolidBrush> brushCache = new(128);
    private static readonly Dictionary<(int Argb, int Width20, bool Round), Pen> penCache = new(128);
    private static readonly GraphicsPath pooledRoundPath = new();

    /// <summary>Кэшированная сплошная кисть для UI-потока (не вызывать Dispose на возвращённой кисти).</summary>
    public static SolidBrush BrushOf(Color c)
    {
        int key = c.ToArgb();
        if (brushCache.TryGetValue(key, out var b)) return b;
        if (brushCache.Count >= 256)
        {
            foreach (var old in brushCache.Values) old.Dispose();
            brushCache.Clear();
        }
        b = new SolidBrush(c);
        brushCache[key] = b;
        return b;
    }

    /// <summary>Кэшированное перо для UI-потока (не менять свойства и не вызывать Dispose).</summary>
    public static Pen PenOf(Color c, float width = 1f, bool roundCap = false)
    {
        int w20 = Math.Max(1, (int)Math.Round(width * 20f));
        var key = (c.ToArgb(), w20, roundCap);
        if (penCache.TryGetValue(key, out var p)) return p;
        if (penCache.Count >= 256)
        {
            foreach (var old in penCache.Values) old.Dispose();
            penCache.Clear();
        }
        p = new Pen(c, w20 / 20f);
        if (roundCap)
        {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
        }
        penCache[key] = p;
        return p;
    }

    /// <summary>Заполняет прямоугольник со скруглением в пул-контур без аллокации GraphicsPath (не для вложенных вызовов).</summary>
    public static void FillDrawRoundRect(Graphics g, Rectangle r, int rad, Brush fill, Pen stroke = null)
    {
        pooledRoundPath.Reset();
        if (r.Width < 2 || r.Height < 2)
        {
            pooledRoundPath.AddRectangle(r);
        }
        else
        {
            int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            pooledRoundPath.AddArc(r.X, r.Y, d, d, 180, 90);
            pooledRoundPath.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            pooledRoundPath.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            pooledRoundPath.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            pooledRoundPath.CloseFigure();
        }
        if (fill != null) g.FillPath(fill, pooledRoundPath);
        if (stroke != null) g.DrawPath(stroke, pooledRoundPath);
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

/// <summary>Парящая нижняя капсула подсказок и статуса в стиле Floating Glass HUD.</summary>
public class Bar : Panel
{
    public bool LineAtTop;

    public Bar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Padding = new Padding(14, 4, 14, 4);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Theme.Bg), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cap = new Rectangle(8, 2, Math.Max(4, Width - 17), Math.Max(4, Height - 5));
        using (var path = Theme.RoundRect(cap, 8))
        {
            using (var grad = new LinearGradientBrush(cap, Theme.Mix(Theme.Panel2, Theme.Accent, 0.04f), Theme.Panel, LinearGradientMode.Vertical))
                g.FillPath(grad, path);
            g.DrawPath(Theme.PenOf(Theme.Mix(Theme.PanelBorder, Theme.Accent, 0.22f), 1f), path);
        }
        if (cap.Width > 28)
            g.DrawLine(Theme.PenOf(Color.FromArgb(20, 255, 255, 255), 1f), cap.Left + 12, cap.Top + 1, cap.Right - 12, cap.Top + 1);
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
    public int Selected = BallColors.Spectrum;
    public bool ShowColorName;
    public event Action Changed;
    private bool hover;
    private static readonly Font LabelFont = new("Segoe UI", 8.25F, FontStyle.Bold);

    public BallColorButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        Size = new Size(46, 38);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillDrawRoundRect(g, rect, 9,
            Theme.BrushOf(hover ? Theme.Hover : Theme.Panel2),
            Theme.PenOf(hover ? Theme.Mix(Theme.Border, Theme.Accent, 0.45f) : Theme.Border, 1.1f));
        int d = Math.Clamp(Height - 16, 14, 22);
        int swX = ShowColorName ? Math.Max(9, Height / 4) : Math.Max(7, (Width - d - 12) / 2);
        var sw = new Rectangle(swX, (Height - d) / 2, d, d);
        if (Selected == BallColors.Spectrum)
            using (var lb = RainbowBrush(sw))
                g.FillEllipse(lb, sw);
        else
        {
            g.FillEllipse(Theme.BrushOf(Color.FromArgb(Selected)), sw);
            g.DrawEllipse(Theme.PenOf(Theme.TextDim, 1f), sw);
        }
        float cx = Width - Math.Max(11, Height / 4), cy = Height / 2f;
        if (ShowColorName)
        {
            string cname = Selected == BallColors.Spectrum ? "СПЕКТР" : "ЦВЕТ";
            foreach (var (nm, argb) in BallColors.Palette)
                if (argb == Selected) { cname = nm; break; }
            string lbl = string.IsNullOrEmpty(Text) ? cname : $"{Text}: {cname}";
            int tx = sw.Right + 6;
            TextRenderer.DrawText(g, lbl, LabelFont, new Rectangle(tx, 0, Math.Max(10, (int)cx - tx - 8), Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        var cp = Theme.PenOf(Theme.TextDim, 1.6f, roundCap: true);
        g.DrawLine(cp, cx - 3, cy - 1.5f, cx, cy + 1.5f);
        g.DrawLine(cp, cx, cy + 1.5f, cx + 3, cy - 1.5f);
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

    public static void BelowLeftAligned(Form popup, Control button)
    {
        var wa = Screen.FromControl(button).WorkingArea;
        var b = button.PointToScreen(Point.Empty);
        int x = b.X;
        int y = b.Y + button.Height + 6;
        if (y + popup.Height > wa.Bottom) y = b.Y - popup.Height - 6;
        if (x < wa.Left + 8) x = wa.Left + 8;
        if (x > wa.Right - popup.Width - 8) x = wa.Right - popup.Width - 8;
        if (y < wa.Top + 8) y = wa.Top + 8;
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
            using var bp = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.35f), 1.2f);
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
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
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

/// <summary>Компактная кнопка размера шара: показывает «R 12», клик — карточка с горизонтальным ползунком.</summary>
public class SizeButton : Control
{
    public int Min = 4;
    public int Max = 40;
    public event Action<int> Changed;

    private int val = 12;
    private bool hover;

    public int Value
    {
        get => val;
        set { var v = Math.Clamp(value, Min, Max); if (v == val) return; val = v; Invalidate(); openPopup?.Sync(v); }
    }

    private SizePopup openPopup;

    public SizeButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        Size = new Size(54, 38);
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillDrawRoundRect(g, rect, 9,
            Theme.BrushOf(hover ? Theme.Hover : Theme.Panel2),
            Theme.PenOf(hover ? Theme.Mix(Theme.Border, Theme.Accent, 0.45f) : Theme.Border, 1.1f));
        TextRenderer.DrawText(g, "R " + val, Font, rect, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var popup = new SizePopup(Min, Max, val);
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
            using var bp = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.35f), 1.2f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

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

    public void Sync(int v)
    {
        var clamped = Math.Clamp(v, tb.Minimum, tb.Maximum);
        if (clamped == tb.Value) return;
        tb.Value = clamped;
    }
}

/// <summary>Кнопка со всплывающей карточкой-ползунком для дробных величин (скорость, плотность лучей).</summary>
public class SliderButton : Control
{
    public double Min = 0.01;
    public double Max = 5.0;
    public double Step = 0;
    public bool Log;
    public string Prefix = "";
    public Func<double, string> Format = static v => Math.Round(v).ToString();
    public event Action<double> Changed;

    private double val;
    private bool hover;
    private SliderPopup openPopup;

    public double Value
    {
        get => val;
        set
        {
            var v = Math.Clamp(value, Math.Min(Min, Max), Math.Max(Min, Max));
            if (Step > 0 && !Log)
            {
                double lo = Math.Min(Min, Max);
                v = Math.Clamp(Math.Round((v - lo) / Step) * Step + lo, lo, Math.Max(Min, Max));
            }
            if (v == val) return;
            val = v;
            Invalidate();
            openPopup?.Sync(v);
        }
    }

    public SliderButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        Size = new Size(96, 38);
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled) hover = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = !Enabled ? Color.FromArgb(150, Theme.Panel) : hover ? Theme.Hover : Theme.Panel2;
        Color bord = !Enabled ? Color.FromArgb(88, Theme.Border)
                   : hover ? Theme.Mix(Theme.Border, Theme.Accent, 0.45f) : Theme.Border;
        Theme.FillDrawRoundRect(g, rect, 9, Theme.BrushOf(fill), Theme.PenOf(bord, 1.1f));
        Color fg = Enabled ? Theme.Text : Color.FromArgb(100, Theme.TextDim);
        TextRenderer.DrawText(g, Prefix + Format(val), Font, rect, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        base.OnClick(e);
        if (openPopup != null && !openPopup.IsDisposed)
        {
            openPopup.Close();
            return;
        }
        var popup = new SliderPopup(Min, Max, Step, Log, val, Format);
        openPopup = popup;
        popup.FormClosed += (s, _) => openPopup = null;
        popup.OnChange = v =>
        {
            if (Math.Abs(v - val) < 1e-9) return;
            val = v;
            Invalidate();
            Changed?.Invoke(v);
        };
        PopupPlacement.BelowCentered(popup, this);
        popup.Show(this);
        popup.Activate();
    }
}

/// <summary>Карточка-ползунок: кнопки −/+, поле ввода числа, трекбар и подписи шкалы.</summary>
internal sealed class SliderPopup : Form
{
    public Action<double> OnChange;

    private readonly double min, max, step;
    private readonly bool log;
    private readonly TrackBar tb;
    private readonly Label lbl;
    private readonly Func<double, string> format;
    private double current;
    private bool internalSync;
    private TextBox editBox;
    private readonly Rectangle valueBoxRect = new(56, 9, 164, 28);

    public SliderPopup(double min, double max, double step, bool log, double current, Func<double, string> format)
    {
        this.min = Math.Min(min, max);
        this.max = Math.Max(min, max);
        this.step = step;
        this.log = log;
        this.format = format;
        this.current = ClampSnap(current);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ClientSize = new Size(276, 118);
        SetStyle(ControlStyles.StandardDoubleClick, false);

        var btnMinus = new NeoButton
        {
            Text = "−",
            Size = new Size(32, 28),
            Location = new Point(16, 9),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        };
        btnMinus.Click += (s, _) => StepBy(-1);
        Controls.Add(btnMinus);

        var btnPlus = new NeoButton
        {
            Text = "+",
            Size = new Size(32, 28),
            Location = new Point(228, 9),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        };
        btnPlus.Click += (s, _) => StepBy(1);
        Controls.Add(btnPlus);

        lbl = new Label
        {
            Text = format(this.current),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Theme.Panel2,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(valueBoxRect.X + 4, valueBoxRect.Y + 3, valueBoxRect.Width - 8, valueBoxRect.Height - 6),
            Cursor = Cursors.IBeam,
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
            Bounds = new Rectangle(16, 44, 244, 44),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
        };
        tb.Value = ToT(this.current);
        tb.ValueChanged += (s, _) =>
        {
            if (internalSync) return;
            double v = FromT(tb.Value);
            this.current = v;
            lbl.Text = format(v);
            OnChange?.Invoke(v);
        };
        Controls.Add(tb);

        for (int i = 0; i < 5; i++)
        {
            var l = new Label
            {
                Text = format(FromT(i * 250)),
                Font = new Font("Segoe UI", 7.5F),
                ForeColor = Theme.TextDim,
                AutoSize = false,
                Size = new Size(46, 14),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            l.Location = new Point(16 + 244 * i / 4 - l.Width / 2, 94);
            Controls.Add(l);
        }

        Deactivate += (s, _) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Paint += (s, pe) =>
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var vPath = Theme.RoundRect(valueBoxRect, 7))
            {
                using var vFill = new SolidBrush(Theme.Panel2);
                pe.Graphics.FillPath(vFill, vPath);
                using var vPen = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.35f), 1.1f);
                pe.Graphics.DrawPath(vPen, vPath);
            }
            using var bp = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.35f), 1.2f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

    private void StepBy(int dir)
    {
        double next;
        if (log && min > 0)
            next = current * (dir > 0 ? 1.15 : 1.0 / 1.15);
        else
        {
            double delta = step > 0 ? step : (max - min >= 200 ? 10 : max - min >= 20 ? 1 : max - min >= 2 ? 0.1 : 0.05);
            next = current + dir * delta;
        }
        next = ClampSnap(next);
        Sync(next);
        OnChange?.Invoke(next);
    }

    private double ClampSnap(double v)
    {
        v = Math.Clamp(v, min, max);
        if (step > 0 && !log)
            v = Math.Clamp(Math.Round((v - min) / step) * step + min, min, max);
        return v;
    }

    private void BeginEdit()
    {
        if (editBox != null) return;
        editBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel2,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            Bounds = new Rectangle(valueBoxRect.X + 2, valueBoxRect.Y + 2, valueBoxRect.Width - 4, valueBoxRect.Height - 4),
            Text = format(current).Replace("×", "").Replace("K", "").Replace("К", "").Trim().TrimStart('+'),
        };
        editBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                EndEdit(commit: true);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                EndEdit(commit: false);
                e.SuppressKeyPress = true;
            }
        };
        editBox.LostFocus += (s, _) => EndEdit(commit: true);
        Controls.Add(editBox);
        editBox.BringToFront();
        editBox.Focus();
        editBox.SelectAll();
    }

    private void EndEdit(bool commit)
    {
        if (editBox == null) return;
        var box = editBox;
        editBox = null;
        if (commit)
        {
            var txt = box.Text.Trim().Replace(',', '.').TrimStart('+');
            if (double.TryParse(txt, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var dv))
            {
                dv = ClampSnap(dv);
                Sync(dv);
                OnChange?.Invoke(dv);
            }
        }
        if (Controls.Contains(box)) Controls.Remove(box);
        box.Dispose();
    }

    private double FromT(int t)
    {
        double raw = log ? min * Math.Pow(max / min, t / 1000.0) : min + (max - min) * t / 1000.0;
        return ClampSnap(raw);
    }

    private int ToT(double v) => (int)Math.Clamp(Math.Round(
        log ? 1000 * Math.Log(v / min) / Math.Log(max / min)
            : 1000 * (v - min) / (max - min)), 0, 1000);

    public void Sync(double v)
    {
        v = ClampSnap(v);
        current = v;
        lbl.Text = format(v);
        int t = ToT(v);
        if (t == tb.Value) return;
        internalSync = true;
        try { tb.Value = t; }
        finally { internalSync = false; }
    }
}

/// <summary>Карточка уровня/фигуры для визуального селектора уровней с мини-превью геометрии.</summary>
public sealed class LevelCardItem
{
    public string Title = "";
    public string Badge = "";
    public LevelData Preview;
    public Vec2? Source;
    public AccentRole Role = AccentRole.Accent;
    public bool RotorStyle;
}

/// <summary>Интерактивная капсула выбора уровня в верхней панели: открывает сетку карточек с мини-превью.</summary>
public class LevelSelectButton : Control
{
    public Func<(int CurrentIndex, List<LevelCardItem> Items)> ItemsProvider;
    public event Action<int> LevelSelected;
    public AccentRole Role = AccentRole.Accent;

    private bool hover;

    public LevelSelectButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        Size = new Size(300, 38);
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color acc = Theme.ResolveRole(Role);

        Color fill = hover ? Theme.Mix(Theme.Panel2, acc, 0.16f) : Theme.Mix(Theme.Panel2, acc, 0.06f);
        Color bc = hover ? Theme.Mix(Theme.Border, acc, 0.68f) : Theme.Mix(Theme.Border, acc, 0.28f);
        Theme.FillDrawRoundRect(g, rect, 9, Theme.BrushOf(fill), Theme.PenOf(bc, 1.15f));
        if (rect.Width > 20)
            g.DrawLine(Theme.PenOf(Color.FromArgb(hover ? 42 : 20, 255, 255, 255), 1f), rect.Left + 8, rect.Top + 1, rect.Right - 8, rect.Top + 1);

        // иконка сетки карточек слева
        IconPainter.GridCards(g, new Rectangle(9, (Height - 16) / 2, 16, 16), hover ? acc : Theme.Mix(acc, Theme.TextDim, 0.35f));

        // текст уровня
        string clean = (Text ?? "").Trim();
        var textRect = new Rectangle(30, 0, Math.Max(10, Width - 52), Height);
        TextRenderer.DrawText(g, clean, Font, textRect, acc,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // шеврон выпадающей сетки справа
        float cx = Width - 14, cy = Height / 2f;
        var cp = Theme.PenOf(hover ? acc : Theme.TextDim, 1.7f, roundCap: true);
        g.DrawLine(cp, cx - 3.5f, cy - 1.5f, cx, cy + 2f);
        g.DrawLine(cp, cx, cy + 2f, cx + 3.5f, cy - 1.5f);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (ItemsProvider == null) return;
        var (cur, items) = ItemsProvider();
        if (items == null || items.Count == 0) return;

        var popup = new LevelPickerPopup(cur, items);
        popup.OnPick = idx => LevelSelected?.Invoke(idx);
        PopupPlacement.BelowLeftAligned(popup, this);
        popup.Show(this);
        popup.Activate();
    }
}

/// <summary>Всплывающая сетка карточек уровней с векторным мини-превью геометрии и прокруткой колесом.</summary>
internal sealed class LevelPickerPopup : Form
{
    public Action<int> OnPick;
    private readonly int currentIdx;
    private readonly List<LevelCardItem> items;
    private int hoverIdx = -1;
    private int scrollY;
    private readonly int maxScrollY;

    private readonly int cols;
    private readonly int rows;
    private const int CardW = 166;
    private const int CardH = 104;
    private const int Gap = 8;
    private const int Pad = 12;
    private const int MaxVisRows = 5;

    private static readonly Font TitleFont = new("Segoe UI", 8.5F, FontStyle.Bold);
    private static readonly Font BadgeFont = new("Segoe UI", 7F, FontStyle.Bold);

    public LevelPickerPopup(int current, List<LevelCardItem> list)
    {
        currentIdx = current;
        items = list;
        cols = list.Count <= 6 ? 3 : 4;
        rows = Math.Max(1, (list.Count + cols - 1) / cols);
        int visRows = Math.Min(rows, MaxVisRows);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.StandardDoubleClick, false);
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;

        int sbExtra = rows > MaxVisRows ? 10 : 0;
        int w = Pad * 2 + cols * CardW + (cols - 1) * Gap + sbExtra;
        int h = Pad * 2 + visRows * CardH + (visRows - 1) * Gap;
        int totalContentH = rows * CardH + (rows - 1) * Gap;
        int viewContentH = visRows * CardH + (visRows - 1) * Gap;
        maxScrollY = Math.Max(0, totalContentH - viewContentH);

        if (current >= 0 && current < list.Count && maxScrollY > 0)
        {
            int curRow = current / cols;
            if (curRow >= MaxVisRows)
                scrollY = Math.Clamp((curRow - MaxVisRows + 1) * (CardH + Gap), 0, maxScrollY);
        }

        ClientSize = new Size(w, h);

        Deactivate += (s, _) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private int HitCard(Point pt)
    {
        if (pt.Y < 4 || pt.Y >= Height - 4) return -1;
        for (int i = 0; i < items.Count; i++)
            if (CardRect(i).Contains(pt)) return i;
        return -1;
    }

    private Rectangle CardRect(int i)
    {
        int c = i % cols, r = i / cols;
        return new Rectangle(Pad + c * (CardW + Gap), Pad + r * (CardH + Gap) - scrollY, CardW, CardH);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (maxScrollY <= 0) return;
        int next = Math.Clamp(scrollY - Math.Sign(e.Delta) * 56, 0, maxScrollY);
        if (next != scrollY)
        {
            scrollY = next;
            hoverIdx = HitCard(PointToClient(Cursor.Position));
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitCard(e.Location);
        if (hit != hoverIdx) { hoverIdx = hit; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIdx != -1) { hoverIdx = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int hit = HitCard(e.Location);
        if (hit >= 0)
        {
            OnPick?.Invoke(hit);
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        g.FillRectangle(Theme.BrushOf(Theme.Panel), ClientRectangle);

        var clipState = g.Save();
        g.SetClip(new Rectangle(2, 2, Width - 4, Height - 4));
        for (int i = 0; i < items.Count; i++)
        {
            var r = CardRect(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            DrawCard(g, i, r, items[i]);
        }
        g.Restore(clipState);

        if (maxScrollY > 0)
        {
            int trackTop = Pad, trackH = Height - Pad * 2;
            int thumbH = Math.Max(28, (int)((float)trackH * trackH / (trackH + maxScrollY)));
            int thumbY = trackTop + (int)((trackH - thumbH) * ((float)scrollY / maxScrollY));
            g.FillRectangle(Theme.BrushOf(Color.FromArgb(90, Theme.Panel2)), Width - 9, trackTop, 4, trackH);
            g.FillRectangle(Theme.BrushOf(Color.FromArgb(190, Theme.Accent)), Width - 9, thumbY, 4, thumbH);
        }

        g.DrawRectangle(Theme.PenOf(Theme.Mix(Theme.Border, Theme.Accent, 0.42f), 1.3f), 0, 0, Width - 1, Height - 1);
    }

    private void DrawCard(Graphics g, int idx, Rectangle r, LevelCardItem item)
    {
        bool cur = idx == currentIdx;
        bool hov = idx == hoverIdx;
        bool rotor = item.RotorStyle || (item.Preview?.RotorRules ?? false);
        Color acc = rotor ? Color.FromArgb(246, 118, 240) : Theme.ResolveRole(item.Role);

        Color fill = cur ? Theme.Mix(Theme.Panel2, acc, 0.16f)
                   : hov ? Theme.Hover
                   :       Theme.Panel2;
        Color bc = cur ? acc : hov ? Theme.Mix(Theme.Border, acc, 0.55f) : Theme.Border;
        Theme.FillDrawRoundRect(g, r, 9, Theme.BrushOf(fill), Theme.PenOf(bc, cur ? 1.7f : 1.1f));

        // окно мини-превью геометрии (150 × 68)
        var prev = new Rectangle(r.X + 8, r.Y + 7, r.Width - 16, r.Height - 38);
        using (var pp = Theme.RoundRect(prev, 6))
        {
            g.FillPath(Theme.BrushOf(Theme.Bg), pp);
            DrawMiniPreview(g, prev, item, acc);
            g.DrawPath(Theme.PenOf(Theme.PanelBorder, 1f), pp);
        }

        // подпись и бейдж
        var titleRect = new Rectangle(r.X + 8, r.Bottom - 26, r.Width - 16, 20);
        TextRenderer.DrawText(g, item.Title, TitleFont, titleRect, cur || hov ? acc : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (!string.IsNullOrEmpty(item.Badge))
        {
            var bsz = TextRenderer.MeasureText(item.Badge, BadgeFont);
            var br = new Rectangle(r.Right - bsz.Width - 10, r.Y + 11, bsz.Width + 4, 15);
            g.FillRectangle(Theme.BrushOf(Color.FromArgb(190, Theme.Panel)), br);
            TextRenderer.DrawText(g, item.Badge, BadgeFont, br, Theme.Mix(acc, Theme.Text, 0.25f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal static void DrawMiniPreview(Graphics g, Rectangle box, LevelCardItem item, Color acc)
    {
        var lvl = item.Preview;
        if (lvl == null) return;

        var st = g.Save();
        g.SetClip(box);

        float sx = (box.Width - 8f) / (float)LevelData.W;
        float sy = (box.Height - 6f) / (float)LevelData.H;
        float s = Math.Min(sx, sy);
        float ox = box.X + (box.Width - (float)LevelData.W * s) / 2f;
        float oy = box.Y + (box.Height - (float)LevelData.H * s) / 2f;

        PointF Map(Vec2 v) => new(ox + (float)v.X * s, oy + (float)v.Y * s);

        using var wallPen = new Pen(acc, 1.4f);
        using var arcPen = new Pen(lvl.RotorRules || item.RotorStyle ? Color.FromArgb(246, 118, 240) : acc, 1.4f);
        using var goalPen = new Pen(Theme.Green, 1.4f);
        using var goalFill = new SolidBrush(Color.FromArgb(55, Theme.Green));

        foreach (var goal in lvl.Goals)
        {
            if (goal.Pts.Count < 3) continue;
            var pts = new PointF[goal.Pts.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = Map(goal.Pts[i]);
            g.FillPolygon(goalFill, pts);
            g.DrawPolygon(goalPen, pts);
        }

        foreach (var c in lvl.Circles)
        {
            var cp = Map(c.Center);
            float cr = (float)c.Radius * s;
            g.DrawEllipse(wallPen, cp.X - cr, cp.Y - cr, cr * 2, cr * 2);
        }

        Color baseWallCol = wallPen.Color;
        foreach (var w in lvl.Walls)
        {
            if (w.Pts.Count < 2) continue;
            var pts = new PointF[w.Pts.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = Map(w.Pts[i]);
            wallPen.Color = w.Argb != 0 ? Color.FromArgb(w.Argb) : baseWallCol;
            if (w.Closed && pts.Length >= 3) g.DrawPolygon(wallPen, pts);
            else g.DrawLines(wallPen, pts);
        }
        wallPen.Color = baseWallCol;

        if (lvl.Arcs.Count > 0)
        {
            Color baseArcCol = arcPen.Color;
            int step = Math.Max(1, lvl.Arcs.Count / 16);
            int drawn = 0;
            for (int i = 0; i < lvl.Arcs.Count && drawn < 16; i += step)
            {
                var a = lvl.Arcs[i];
                float ar = (float)a.Radius * s;
                if (ar < 2 || ar > box.Height * 0.72f) continue;
                var cp = Map(a.Center);
                arcPen.Color = a.Argb != 0 ? Color.FromArgb(a.Argb) : baseArcCol;
                g.DrawArc(arcPen, cp.X - ar, cp.Y - ar, ar * 2, ar * 2, (float)a.StartDeg, (float)a.SweepDeg);
                drawn++;
            }
        }

        foreach (var b in lvl.Balls)
        {
            var bp = Map(b.Pos);
            float br = Math.Max(2f, (float)b.R * s);
            Color bc = b.Argb != 0 ? Color.FromArgb(b.Argb) : Color.FromArgb(238, 243, 255);
            using var bb = new SolidBrush(bc);
            g.FillEllipse(bb, bp.X - br, bp.Y - br, br * 2, br * 2);
        }

        Vec2? src = item.Source ?? (lvl.WaveEmitter ? lvl.WaveSourcePos : null);
        if (src.HasValue)
        {
            var sp = Map(src.Value);
            using var sb = new SolidBrush(Theme.Green);
            g.FillEllipse(sb, sp.X - 2.5f, sp.Y - 2.5f, 5f, 5f);
            using var srp = new Pen(Color.FromArgb(140, Theme.Green), 1f);
            g.DrawEllipse(srp, sp.X - 5f, sp.Y - 5f, 10f, 10f);
        }

        g.Restore(st);
    }
}

/// <summary>Всплывающая галерея шаблонов редактора с вкладками по всем режимам игры и прокруткой колесом.</summary>
internal sealed class TemplateGalleryPopup : Form
{
    public Action<LevelData> OnPick;

    private readonly List<(string TabName, AccentRole Role, List<LevelCardItem> Items)> tabs;
    private int tabIdx;
    private int hoverTab = -1;
    private int hoverCard = -1;
    private int scrollY;

    private const int Cols = 6;
    private const int CardW = 148;
    private const int CardH = 92;
    private const int Gap = 7;
    private const int Pad = 12;
    private const int HeaderH = 44;

    private static readonly Font TabFont = new("Segoe UI", 8.75F, FontStyle.Bold);
    private static readonly Font TitleFont = new("Segoe UI", 8.0F, FontStyle.Bold);
    private static readonly Font BadgeFont = new("Segoe UI", 6.75F, FontStyle.Bold);

    public TemplateGalleryPopup(List<(string TabName, AccentRole Role, List<LevelCardItem> Items)> categories)
    {
        tabs = categories;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.StandardDoubleClick, false);
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;

        int maxRows = 4;
        foreach (var t in tabs)
            maxRows = Math.Max(maxRows, (t.Items.Count + Cols - 1) / Cols);
        int visRows = Math.Min(maxRows, 5);

        int w = Pad * 2 + Cols * CardW + (Cols - 1) * Gap + 10;
        int h = HeaderH + Pad * 2 + visRows * CardH + (visRows - 1) * Gap;
        ClientSize = new Size(w, h);

        Deactivate += (s, _) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private int MaxScrollForTab()
    {
        if (tabs.Count == 0) return 0;
        int cnt = tabs[tabIdx].Items.Count;
        int rows = Math.Max(1, (cnt + Cols - 1) / Cols);
        int contentH = rows * CardH + (rows - 1) * Gap + 12;
        int viewH = Height - HeaderH - 6;
        return Math.Max(0, contentH - viewH);
    }

    private Rectangle TabRect(int i)
    {
        int x = Pad;
        for (int k = 0; k < i; k++)
            x += TextRenderer.MeasureText(tabs[k].TabName, TabFont).Width + 28 + 6;
        int w = TextRenderer.MeasureText(tabs[i].TabName, TabFont).Width + 28;
        return new Rectangle(x, 10, w, 30);
    }

    private Rectangle CardRect(int i)
    {
        int c = i % Cols, r = i / Cols;
        return new Rectangle(Pad + c * (CardW + Gap), HeaderH + 8 + r * (CardH + Gap) - scrollY, CardW, CardH);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int maxS = MaxScrollForTab();
        if (maxS <= 0) return;
        int next = Math.Clamp(scrollY - Math.Sign(e.Delta) * 52, 0, maxS);
        if (next != scrollY)
        {
            scrollY = next;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int ht = -1, hc = -1;
        for (int i = 0; i < tabs.Count; i++)
            if (TabRect(i).Contains(e.Location)) { ht = i; break; }
        if (ht < 0 && tabs.Count > 0 && e.Y > HeaderH)
        {
            var list = tabs[tabIdx].Items;
            for (int i = 0; i < list.Count; i++)
                if (CardRect(i).Contains(e.Location)) { hc = i; break; }
        }
        if (ht != hoverTab || hc != hoverCard)
        {
            hoverTab = ht;
            hoverCard = hc;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverTab != -1 || hoverCard != -1) { hoverTab = hoverCard = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        for (int i = 0; i < tabs.Count; i++)
        {
            if (TabRect(i).Contains(e.Location))
            {
                tabIdx = i;
                scrollY = 0;
                hoverCard = -1;
                Invalidate();
                return;
            }
        }
        if (tabs.Count == 0 || e.Y <= HeaderH) return;
        var list = tabs[tabIdx].Items;
        for (int i = 0; i < list.Count; i++)
        {
            if (CardRect(i).Contains(e.Location) && list[i].Preview != null)
            {
                OnPick?.Invoke(list[i].Preview.Clone());
                Close();
                return;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Panel))
            g.FillRectangle(bg, ClientRectangle);

        // Вкладки категорий сверху
        for (int i = 0; i < tabs.Count; i++)
        {
            var tr = TabRect(i);
            bool active = i == tabIdx;
            bool hov = i == hoverTab;
            Color acc = Theme.ResolveRole(tabs[i].Role);
            Theme.FillDrawRoundRect(g, tr, 8,
                Theme.BrushOf(active ? Theme.Mix(Theme.Panel2, acc, 0.22f) : hov ? Theme.Hover : Theme.Panel2),
                Theme.PenOf(active ? acc : hov ? Theme.Mix(Theme.Border, acc, 0.5f) : Theme.Border, active ? 1.5f : 1f));
            TextRenderer.DrawText(g, tabs[i].TabName, TabFont, tr, active || hov ? acc : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        g.DrawLine(Theme.PenOf(Theme.PanelBorder, 1f), Pad, HeaderH, Width - Pad, HeaderH);

        if (tabs.Count > 0)
        {
            var list = tabs[tabIdx].Items;
            if (list.Count == 0)
            {
                TextRenderer.DrawText(g, "Пока нет сохранённых уровней — создайте уровень и нажмите «Сохранить»!", TabFont,
                    new Rectangle(0, HeaderH, Width, Height - HeaderH), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                var st = g.Save();
                g.SetClip(new Rectangle(2, HeaderH + 1, Width - 4, Height - HeaderH - 3));
                for (int i = 0; i < list.Count; i++)
                {
                    var r = CardRect(i);
                    if (r.Bottom < HeaderH || r.Top > Height) continue;
                    var item = list[i];
                    bool hov = i == hoverCard;
                    bool rotor = item.RotorStyle || (item.Preview?.RotorRules ?? false);
                    Color acc = rotor ? Color.FromArgb(246, 118, 240) : Theme.ResolveRole(item.Role);

                    Theme.FillDrawRoundRect(g, r, 8,
                        Theme.BrushOf(hov ? Theme.Mix(Theme.Panel2, acc, 0.14f) : Theme.Panel2),
                        Theme.PenOf(hov ? acc : Theme.Border, hov ? 1.5f : 1.0f));

                    var prev = new Rectangle(r.X + 7, r.Y + 6, r.Width - 14, r.Height - 34);
                    using (var pp = Theme.RoundRect(prev, 5))
                    {
                        g.FillPath(Theme.BrushOf(Theme.Bg), pp);
                        LevelPickerPopup.DrawMiniPreview(g, prev, item, acc);
                        g.DrawPath(Theme.PenOf(Theme.PanelBorder, 1f), pp);
                    }

                    var titleRect = new Rectangle(r.X + 7, r.Bottom - 24, r.Width - 14, 18);
                    TextRenderer.DrawText(g, item.Title, TitleFont, titleRect, hov ? acc : Theme.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    if (!string.IsNullOrEmpty(item.Badge))
                    {
                        var bsz = TextRenderer.MeasureText(item.Badge, BadgeFont);
                        var br = new Rectangle(r.Right - bsz.Width - 9, r.Y + 9, bsz.Width + 4, 14);
                        g.FillRectangle(Theme.BrushOf(Color.FromArgb(190, Theme.Panel)), br);
                        TextRenderer.DrawText(g, item.Badge, BadgeFont, br, Theme.Mix(acc, Theme.Text, 0.25f),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                }
                g.Restore(st);

                int maxS = MaxScrollForTab();
                if (maxS > 0)
                {
                    int trackTop = HeaderH + 8, trackH = Height - trackTop - 8;
                    int thumbH = Math.Max(26, (int)((float)trackH * trackH / (trackH + maxS)));
                    int thumbY = trackTop + (int)((trackH - thumbH) * ((float)scrollY / maxS));
                    g.FillRectangle(Theme.BrushOf(Color.FromArgb(90, Theme.Panel2)), Width - 9, trackTop, 4, trackH);
                    g.FillRectangle(Theme.BrushOf(Color.FromArgb(195, Theme.ResolveRole(tabs[tabIdx].Role))), Width - 9, thumbY, 4, thumbH);
                }
            }
        }

        g.DrawRectangle(Theme.PenOf(Theme.Mix(Theme.Border, Theme.Accent, 0.45f), 1.3f), 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>
/// Парящая верхняя капсула инструментов (Floating Glass Toolbar): элементы в три группы
/// (слева / центр / справа) с гарантией отсутствия перекрытий на любом масштабе.
/// </summary>
public class Toolbar : Panel
{
    public static event Action MenuClicked;

    private readonly List<Control> left = new(), center = new(), right = new();
    private readonly NeoButton menuBtn;
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
        BackColor = Theme.Panel;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        menuBtn = new NeoButton
        {
            Text = "МЕНЮ",
            Icon = IconPainter.Menu,
            IconPx = 18,
            Role = AccentRole.Accent,
            Size = new Size(88, 38),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
        menuBtn.Click += (s, e) => MenuClicked?.Invoke();
        var tip = new ToolTip();
        tip.SetToolTip(menuBtn, "Открыть меню режимов и настроек поверх игры");
        Controls.Add(menuBtn);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Relayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Theme.Bg), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cap = new Rectangle(8, 4, Math.Max(8, Width - 17), Math.Max(8, Height - 9));
        using var path = Theme.RoundRect(cap, 11);
        using (var grad = new LinearGradientBrush(cap, Theme.Mix(Theme.Panel2, Theme.Accent, 0.07f), Theme.Panel, LinearGradientMode.Vertical))
            g.FillPath(grad, path);
        g.DrawPath(Theme.PenOf(Color.FromArgb(20, Theme.Accent), 2.5f), path);
        g.DrawPath(Theme.PenOf(Theme.Mix(Theme.PanelBorder, Theme.Accent, 0.25f), 1.1f), path);
        if (cap.Width > 32)
        {
            g.DrawLine(Theme.PenOf(Color.FromArgb(30, 255, 255, 255), 1f), cap.Left + 14, cap.Top + 1, cap.Right - 14, cap.Top + 1);
            g.DrawLine(Theme.PenOf(Color.FromArgb(80, 0, 0, 0), 1f), cap.Left + 14, cap.Bottom + 1, cap.Right - 14, cap.Bottom + 1);
        }
    }

    public void Relayout()
    {
        if (Width == 0 || Height == 0) return;
        int leftEnd = 16;
        if (menuBtn != null)
        {
            menuBtn.Location = new Point(leftEnd, Math.Max(0, (Height - menuBtn.Height) / 2));
            leftEnd += menuBtn.Width + Gap + 4;
        }
        foreach (var c in left)
        {
            c.Location = new Point(leftEnd, Math.Max(0, (Height - c.Height) / 2));
            leftEnd += c.Width + Gap;
        }
        if (left.Count > 0) leftEnd -= Gap;

        int rightStart = Width - 16;
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
            int minX = left.Count > 0 ? leftEnd + 12 : 16;
            int maxX = right.Count > 0 ? rightStart - 12 - w : Width - 16 - w;
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
/// Кнопка в стиле Glass-Neon: скругление, мягкий градиент, тонкий блик сверху,
/// ховер-свечение, семантическая роль акцента и неоновый индикатор активного состояния.
/// </summary>
public class NeoButton : Control
{
    public bool IsToggle;
    public bool IsOn;
    public Color? Accent;
    public AccentRole Role = AccentRole.None;
    public Action<Graphics, Rectangle, Color> Icon;
    public int IconPx = 20;
    public string KeyBadge;

    private static readonly Font KeyBadgeFont = new("Segoe UI", 6.25F, FontStyle.Bold);
    private bool hover, pressed;

    public NeoButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        TabStop = false;
        Font = new Font("Segoe UI", 9.5F);
        ForeColor = Theme.Text;
        Size = new Size(92, 34);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        if (Math.Abs(factor.Height - 1f) > 0.01f)
            IconPx = Math.Max(12, (int)Math.Round(IconPx * factor.Height));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnClick(EventArgs e)
    {
        if (IsToggle) { IsOn = !IsOn; Invalidate(); }
        base.OnClick(e);
    }

    private Color ResolveAccent()
    {
        if (Role != AccentRole.None) return Theme.ResolveRole(Role);
        return Accent ?? Theme.Accent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width < 4 || rect.Height < 4) return;

        Color accent = ResolveAccent();
        bool hasExplicitAccent = Role != AccentRole.None || Accent != null;
        bool on = IsToggle && IsOn;

        Color baseFill = !Enabled ? Theme.Panel
                       : on        ? Theme.Mix(Theme.Panel2, accent, 0.22f)
                       : pressed   ? Theme.Pressed
                       : hover     ? Theme.Hover
                       :             Theme.Panel2;
        Color topFill = Enabled ? Theme.Mix(baseFill, Color.White, hover || on ? 0.06f : 0.03f) : baseFill;

        Color bc = !Enabled       ? Color.FromArgb(135, Theme.Border)
                 : on             ? accent
                 : IsToggle       ? Theme.Border
                 : hasExplicitAccent ? accent
                 : Theme.Border;

        Color glowCol = IsToggle && !on ? Theme.Text : accent;
        using (var path = Theme.RoundRect(rect, 9))
        {
            if ((hover || on) && Enabled)
            {
                using var glowPath = Theme.RoundRect(new Rectangle(-1, -1, Width + 1, Height + 1), 10);
                g.FillPath(Theme.BrushOf(Color.FromArgb(on ? 30 : 18, glowCol)), glowPath);
            }
            using (var grad = new LinearGradientBrush(rect, topFill, baseFill, LinearGradientMode.Vertical))
                g.FillPath(grad, path);

            Color hbc = hover && Enabled
                ? (IsToggle && !on ? Theme.Mix(Theme.Border, Theme.Text, 0.35f) : Theme.Mix(bc, accent, 0.58f))
                : bc;
            g.DrawPath(Theme.PenOf(hbc, on ? 1.6f : 1.1f), path);

            if (Enabled && !pressed && rect.Width > 18)
                g.DrawLine(Theme.PenOf(Color.FromArgb(on || hover ? 42 : 20, 255, 255, 255), 1f), rect.Left + 8, rect.Top + 1, rect.Right - 8, rect.Top + 1);
        }

        if (on && rect.Width > 16)
        {
            int pad = Math.Min(12, Math.Max(5, Width / 5));
            g.DrawLine(Theme.PenOf(accent, 2.4f, roundCap: true), rect.Left + pad, rect.Bottom - 3, rect.Right - pad, rect.Bottom - 3);
        }

        Color fg = !Enabled       ? Color.FromArgb(145, Theme.TextDim)
                 : on             ? accent
                 : IsToggle       ? Theme.Text
                 : hasExplicitAccent ? accent
                 : Theme.Text;

        int dy = pressed ? 1 : 0;
        bool hasText = !string.IsNullOrEmpty(Text);
        bool hasBadge = !string.IsNullOrEmpty(KeyBadge);
        var tsz = hasText ? TextRenderer.MeasureText(Text, Font) : Size.Empty;
        int iconW = Icon != null ? IconPx : 0;
        int gap = iconW > 0 && hasText ? 7 : 0;
        int total = iconW + gap + (hasText ? tsz.Width : 0);
        int x = (Width - total) / 2 + (!hasText && hasBadge ? -2 : 0);
        if (Icon != null)
        {
            int iy = (Height - IconPx) / 2 + dy + (!hasText && hasBadge ? -1 : 0);
            Icon(g, new Rectangle(x, iy, IconPx, IconPx), fg);
            x += iconW + gap;
        }
        if (hasText)
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(x - 2, (Height - tsz.Height) / 2 + dy, tsz.Width + 4, tsz.Height + 2),
                fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

        if (hasBadge)
        {
            Color kb = !Enabled ? Color.FromArgb(80, Theme.TextDim)
                     : (on || hover) ? accent
                     : Color.FromArgb(145, Theme.TextDim);
            TextRenderer.DrawText(g, KeyBadge, KeyBadgeFont,
                new Rectangle(Math.Max(2, Width - 18), Math.Max(2, Height - 15), 14, 12),
                kb, TextFormatFlags.Right | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Стеклянно-неоновое поле ввода текста со скруглением и мягкой подсветкой фокуса.</summary>
public class NeoTextBox : Panel
{
    public readonly TextBox Inner = new() { BorderStyle = BorderStyle.None };
    private bool hover;

    public new string Text
    {
        get => Inner.Text;
        set => Inner.Text = value ?? "";
    }

    public bool FocusedInput => Inner.Focused;
    public event EventHandler InputChanged { add => Inner.TextChanged += value; remove => Inner.TextChanged -= value; }

    public NeoTextBox(float fontSize = 9.5F)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Size = new Size(156, 36);
        BackColor = Theme.Panel;
        Cursor = Cursors.IBeam;

        Inner.BackColor = Theme.Panel2;
        Inner.ForeColor = Theme.Text;
        Inner.Font = new Font("Segoe UI", fontSize);
        Inner.GotFocus += (s, e) => Invalidate();
        Inner.LostFocus += (s, e) => Invalidate();
        Inner.MouseEnter += (s, e) => { hover = true; Invalidate(); };
        Inner.MouseLeave += (s, e) => { hover = false; Invalidate(); };
        Controls.Add(Inner);
        PositionInner();
    }

    public void ApplyTheme()
    {
        BackColor = Parent?.BackColor ?? Theme.Panel;
        Inner.BackColor = Theme.Panel2;
        Inner.ForeColor = Theme.Text;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionInner();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        PositionInner();
    }

    private void PositionInner()
    {
        int th = Inner.PreferredHeight;
        int px = Math.Max(9, Height / 4);
        Inner.Bounds = new Rectangle(px, Math.Max(3, (Height - th) / 2), Math.Max(16, Width - px * 2), th);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Inner.Focus(); base.OnClick(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Theme.BrushOf(Parent?.BackColor ?? Theme.Panel), ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width < 4 || rect.Height < 4) return;
        bool foc = Inner.Focused;
        Color bc = foc ? Theme.Accent : hover ? Theme.Mix(Theme.Border, Theme.Accent, 0.45f) : Theme.Border;
        Theme.FillDrawRoundRect(g, rect, 8, Theme.BrushOf(Theme.Panel2), Theme.PenOf(bc, foc ? 1.5f : 1.1f));
    }
}

/// <summary>Векторные иконки (в логических координатах 24×24), как в графических редакторах.</summary>
public static class IconPainter
{
    private static readonly PointF[][] PtPool = CreatePtPool();
    private static PointF[][] CreatePtPool()
    {
        var pool = new PointF[16][];
        for (int i = 1; i < pool.Length; i++) pool[i] = new PointF[i];
        return pool;
    }

    private static Pen P(Color c, float w) => Theme.PenOf(c, w, roundCap: true);

    private static void Poly(Graphics g, Color c, float w, bool fill, params float[] xy)
    {
        int cnt = xy.Length / 2;
        var pts = cnt < PtPool.Length ? PtPool[cnt] : new PointF[cnt];
        for (int i = 0; i < cnt; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        if (fill) g.FillPolygon(Theme.BrushOf(c), pts);
        else g.DrawPolygon(P(c, w), pts);
    }

    private static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
        => g.DrawLine(P(c, w), x1, y1, x2, y2);

    private static void Dot(Graphics g, Color c, float cx, float cy, float r)
        => g.FillEllipse(Theme.BrushOf(c), cx - r, cy - r, r * 2, r * 2);

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
    { Scale(g, r, gg => gg.DrawRectangle(P(c, 2f), 4.5f, 6.5f, 15, 11)); }

    public static void Ellipse(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => gg.DrawEllipse(P(c, 2f), 4f, 5.5f, 16, 13)); }

    public static void Ball(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.FillEllipse(Theme.BrushOf(c), 4.5f, 4.5f, 15, 15);
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
    { Scale(g, r, gg => gg.FillRectangle(Theme.BrushOf(c), 6.5f, 6.5f, 11, 11)); }

    public static void Plus(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => { Line(gg, c, 2f, 7, 12, 17, 12); Line(gg, c, 2f, 12, 7, 12, 17); }); }

    public static void Minus(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Line(gg, c, 2f, 7, 12, 17, 12)); }

    public static void ChevronLeft(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 2.4f, false, 14.5f, 5, 8, 12, 14.5f, 19)); }

    public static void ChevronRight(Graphics g, Rectangle r, Color c)
    { Scale(g, r, gg => Poly(gg, c, 2.4f, false, 9.5f, 5, 16, 12, 9.5f, 19)); }

    public static void Menu(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2.2f, 4.5f, 6.8f, 19.5f, 6.8f);
            Line(gg, c, 2.2f, 4.5f, 12.0f, 19.5f, 12.0f);
            Line(gg, c, 2.2f, 4.5f, 17.2f, 19.5f, 17.2f);
        });
    }

    public static void SidebarCollapse(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2.0f, 6, 5, 6, 19);
            Line(gg, c, 2.1f, 15.5f, 7, 10.5f, 12);
            Line(gg, c, 2.1f, 10.5f, 12, 15.5f, 17);
        });
    }

    public static void SidebarExpand(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2.0f, 6, 5, 6, 19);
            Line(gg, c, 2.1f, 10.5f, 7, 15.5f, 12);
            Line(gg, c, 2.1f, 15.5f, 12, 10.5f, 17);
        });
    }

    public static void GridCards(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            var p = P(c, 1.8f);
            gg.DrawRectangle(p, 4, 4, 6.5f, 6.5f);
            gg.DrawRectangle(p, 13.5f, 4, 6.5f, 6.5f);
            gg.DrawRectangle(p, 4, 13.5f, 6.5f, 6.5f);
            gg.DrawRectangle(p, 13.5f, 13.5f, 6.5f, 6.5f);
        });
    }

    public static void Palette(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawEllipse(P(c, 1.9f), 3.5f, 3.5f, 17, 17);
            Dot(gg, c, 9, 9.5f, 1.6f);
            Dot(gg, c, 14.5f, 9f, 1.6f);
            Dot(gg, c, 15.5f, 14f, 1.6f);
            Dot(gg, c, 9.5f, 15f, 1.6f);
        });
    }

    public static void Exit(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawArc(P(c, 2.0f), 5f, 5f, 14f, 14f, -55, 290);
            Line(gg, c, 2.1f, 12, 3.5f, 12, 11.5f);
        });
    }

    public static void Restart(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawArc(P(c, 2.2f), 4.5f, 4.5f, 15, 15, 120, 290);
            double a = 50 * Math.PI / 180;
            var pt = new PointF(12 + 7.5f * (float)Math.Cos(a), 12 + 7.5f * (float)Math.Sin(a));
            var t = new PointF(-(float)Math.Sin(a), (float)Math.Cos(a));
            var n = new PointF((float)Math.Cos(a), (float)Math.Sin(a));
            var pts = PtPool[3];
            pts[0] = pt;
            pts[1] = new PointF(pt.X - t.X * 4 + n.X * 3, pt.Y - t.Y * 4 + n.Y * 3);
            pts[2] = new PointF(pt.X - t.X * 4 - n.X * 3, pt.Y - t.Y * 4 - n.Y * 3);
            gg.FillPolygon(Theme.BrushOf(c), pts);
        });
    }

    public static void Pause(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            var b = Theme.BrushOf(c);
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
            gg.FillRectangle(Theme.BrushOf(c), 15.5f, 5.5f, 3, 13);
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
            var p = P(c, 1.8f);
            gg.DrawArc(p, 12 - 4.5f, 13 - 4.5f, 9, 9, 180, 180);
            gg.DrawArc(p, 12 - 8f, 13 - 8f, 16, 16, 180, 180);
            gg.DrawArc(p, 12 - 11.5f, 13 - 11.5f, 23, 23, 180, 180);
        });
    }

    public static void CircleExact(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawEllipse(P(c, 2.0f), 4f, 4f, 16f, 16f);
            Dot(gg, c, 12f, 12f, 1.9f);
            Line(gg, c, 1.4f, 12f, 12f, 17.5f, 6.5f);
        });
    }

    public static void PolygonHex(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
            Poly(gg, c, 2.0f, false, 12f, 3.6f, 19.3f, 7.8f, 19.3f, 16.2f, 12f, 20.4f, 4.7f, 16.2f, 4.7f, 7.8f));
    }

    public static void StarShape(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
            Poly(gg, c, 1.8f, false,
                12f, 3.2f, 14.3f, 8.9f, 20.4f, 9.3f, 15.7f, 13.1f, 17.2f, 19.2f,
                12f, 15.9f, 6.8f, 19.2f, 8.3f, 13.1f, 3.6f, 9.3f, 9.7f, 8.9f));
    }

    public static void ArcGap(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawArc(P(c, 2.3f), 4.2f, 4.2f, 15.6f, 15.6f, 38f, 284f);
            Dot(gg, c, 12f, 12f, 1.7f);
        });
    }

    public static void Spiral(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            var p = P(c, 1.9f);
            gg.DrawArc(p, 3.5f, 3.5f, 17f, 17f, 30f, 290f);
            gg.DrawArc(p, 7.2f, 7.2f, 9.6f, 9.6f, 205f, 285f);
            Dot(gg, c, 12f, 12f, 1.4f);
        });
    }

    public static void WavePoint(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Dot(gg, c, 12f, 12f, 2.3f);
            gg.DrawEllipse(P(c, 1.6f), 7f, 7f, 10f, 10f);
            Line(gg, c, 1.5f, 12f, 2.8f, 12f, 5.0f);
            Line(gg, c, 1.5f, 12f, 19.0f, 12f, 21.2f);
            Line(gg, c, 1.5f, 2.8f, 12f, 5.0f, 12f);
            Line(gg, c, 1.5f, 19.0f, 12f, 21.2f, 12f);
        });
    }

    public static void Trash(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 1.9f, 5f, 6.8f, 19f, 6.8f);
            Poly(gg, c, 1.7f, false, 9.5f, 4.2f, 14.5f, 4.2f, 15.2f, 6.8f, 8.8f, 6.8f);
            Poly(gg, c, 1.8f, false, 6.8f, 6.8f, 17.2f, 6.8f, 16.2f, 20f, 7.8f, 20f);
            Line(gg, c, 1.5f, 10.3f, 10f, 10.6f, 17f);
            Line(gg, c, 1.5f, 13.7f, 10f, 13.4f, 17f);
        });
    }

    public static void Duplicate(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 8.5f, 4.5f, 19.5f, 4.5f, 19.5f, 15.5f, 8.5f, 15.5f);
            Line(gg, c, 1.8f, 4.5f, 8.5f, 4.5f, 19.5f);
            Line(gg, c, 1.8f, 4.5f, 19.5f, 15.5f, 19.5f);
        });
    }

    public static void Sliders(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 1.8f, 4.5f, 7.2f, 19.5f, 7.2f);
            Dot(gg, c, 9.5f, 7.2f, 2.4f);
            Line(gg, c, 1.8f, 4.5f, 12.2f, 19.5f, 12.2f);
            Dot(gg, c, 15.2f, 12.2f, 2.4f);
            Line(gg, c, 1.8f, 4.5f, 17.2f, 19.5f, 17.2f);
            Dot(gg, c, 11.0f, 17.2f, 2.4f);
        });
    }

    public static void Cross(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Line(gg, c, 2.1f, 6.5f, 6.5f, 17.5f, 17.5f);
            Line(gg, c, 2.1f, 17.5f, 6.5f, 6.5f, 17.5f);
        });
    }

    public static void TrailLine(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 2.0f, false, 4f, 17f, 10.5f, 7f, 15.5f, 14f, 20f, 6.5f);
            Dot(gg, c, 20f, 6.5f, 2.2f);
        });
    }

    public static void ImportFile(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 4f, 13.5f, 4f, 19.5f, 20f, 19.5f, 20f, 13.5f);
            Line(gg, c, 2.0f, 12f, 4f, 12f, 14.5f);
            Poly(gg, c, 1.9f, false, 8f, 10.8f, 12f, 14.8f, 16f, 10.8f);
        });
    }

    public static void ExportFile(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Poly(gg, c, 1.8f, false, 4f, 13.5f, 4f, 19.5f, 20f, 19.5f, 20f, 13.5f);
            Line(gg, c, 2.0f, 12f, 14.5f, 12f, 4f);
            Poly(gg, c, 1.9f, false, 8f, 8f, 12f, 4f, 16f, 8f);
        });
    }

    public static void GrowBall(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawEllipse(P(c, 1.8f), 3.5f, 3.5f, 17f, 17f);
            Dot(gg, c, 10.5f, 13.5f, 4.2f);
            Line(gg, c, 1.7f, 13.5f, 10.5f, 17.2f, 6.8f);
            Poly(gg, c, 1.6f, false, 14.2f, 6.8f, 17.2f, 6.8f, 17.2f, 9.8f);
        });
    }

    public static void ChainNodes(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            gg.DrawEllipse(P(c, 1.7f), 3.5f, 3.5f, 17f, 17f);
            Dot(gg, c, 9f, 9.5f, 2.2f);
            Dot(gg, c, 15.2f, 10.5f, 2.2f);
            Dot(gg, c, 11.5f, 15.8f, 2.2f);
            Line(gg, c, 1.3f, 9f, 9.5f, 15.2f, 10.5f);
            Line(gg, c, 1.3f, 11.5f, 15.8f, 15.2f, 10.5f);
        });
    }

    public static void BreakGrid(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            var p = P(c, 1.7f);
            gg.DrawRectangle(p, 4f, 4.5f, 7f, 6.5f);
            gg.DrawRectangle(p, 13f, 4.5f, 7f, 6.5f);
            gg.DrawRectangle(p, 4f, 13f, 7f, 6.5f);
            Poly(gg, c, 1.6f, false, 14f, 13.5f, 19.5f, 14.5f, 16.5f, 19.5f);
        });
    }

    public static void GravityWells(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            Dot(gg, c, 12f, 12f, 2.7f);
            var p = P(c, 1.6f);
            gg.DrawEllipse(p, 5.2f, 5.2f, 13.6f, 13.6f);
            gg.DrawArc(p, 3.0f, 3.0f, 18.0f, 18.0f, 35f, 120f);
            Dot(gg, c, 18.5f, 8.0f, 1.9f);
        });
    }

    public static void ColorClash(Graphics g, Rectangle r, Color c)
    {
        Scale(g, r, gg =>
        {
            var p = P(c, 1.8f);
            gg.DrawRectangle(p, 4.2f, 4.2f, 15.6f, 15.6f);
            Line(gg, c, 1.8f, 4.6f, 19.0f, 19.0f, 5.0f);
            Dot(gg, c, 8.6f, 8.6f, 2.2f);
            Dot(gg, c, 15.4f, 15.4f, 2.2f);
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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Ricochet.UI;

/// <summary>Базовый элемент всплывающей панели песочницы.</summary>
public abstract class SandboxEntry { }

/// <summary>Ползунок с точным вводом числа и шагом для песочницы.</summary>
public sealed class SandboxSlider : SandboxEntry
{
    public string Label { get; init; } = "";
    public double Min { get; init; }
    public double Max { get; init; } = 1.0;
    public double Step { get; init; } = 0.05;
    public bool Log { get; init; }
    public Func<double, string> Format { get; init; }
    public Func<double> Get { get; init; }
    public Action<double> Set { get; init; }
}

/// <summary>Переключатель (вкл/выкл) для песочницы.</summary>
public sealed class SandboxToggle : SandboxEntry
{
    public string Label { get; init; } = "";
    public Func<bool> Get { get; init; }
    public Action<bool> Set { get; init; }
}

/// <summary>Кнопка быстрого действия в песочнице (сброс, спавн и т.д.).</summary>
public sealed class SandboxAction : SandboxEntry
{
    public string Label { get; init; } = "";
    public AccentRole Role { get; init; } = AccentRole.Accent;
    public bool RefreshAllAfter { get; init; } = true;
    public Action Run { get; init; }
}

/// <summary>Плавающая карточка «Песочница» для тонкой настройки физики и параметров режима в реальном времени.</summary>
public sealed class SandboxPopup : Form
{
    private const int PopupW = 334;
    private readonly List<Action> syncCallbacks = new();
    private bool pinned;

    private SandboxPopup(string title, IReadOnlyList<SandboxEntry> entries)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        KeyPreview = true;
        SetStyle(ControlStyles.StandardDoubleClick, false);

        var hdr = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Panel2 };
        var titleLbl = new Label
        {
            Text = title.ToUpperInvariant(),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Theme.Accent,
            Location = new Point(14, 10),
            Size = new Size(216, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var pinBtn = new NeoButton
        {
            Text = "ПОВЕРХ",
            IsToggle = true,
            IsOn = false,
            Role = AccentRole.Accent,
            Size = new Size(64, 24),
            Location = new Point(PopupW - 98, 8),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
        };
        pinBtn.Click += (s, _) => pinned = pinBtn.IsOn;

        var closeBtn = new NeoButton
        {
            Text = "✕",
            Size = new Size(26, 24),
            Location = new Point(PopupW - 30, 8),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Theme.TextDim,
        };
        closeBtn.Click += (s, _) => Close();
        hdr.Controls.AddRange(new Control[] { titleLbl, pinBtn, closeBtn });

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Panel,
            Padding = new Padding(0, 6, 0, 8),
        };

        var actions = new List<SandboxAction>();
        int y = 8;
        int rowW = PopupW - 24;

        foreach (var entry in entries)
        {
            if (entry is SandboxAction act)
            {
                actions.Add(act);
                continue;
            }

            if (entry is SandboxSlider sl)
            {
                var row = new SliderRow(sl, rowW) { Location = new Point(12, y) };
                scrollHost.Controls.Add(row);
                syncCallbacks.Add(row.SyncFromGetter);
                y += row.Height + 6;
            }
            else if (entry is SandboxToggle tg)
            {
                var row = new ToggleRow(tg, rowW) { Location = new Point(12, y) };
                scrollHost.Controls.Add(row);
                syncCallbacks.Add(row.SyncFromGetter);
                y += row.Height + 5;
            }
        }

        if (actions.Count > 0)
        {
            y += 4;
            int cols = actions.Count == 1 ? 1 : 2;
            int gap = 8;
            int bw = (rowW - (cols - 1) * gap) / cols;
            for (int i = 0; i < actions.Count; i++)
            {
                var act = actions[i];
                int col = i % cols;
                int rowIdx = i / cols;
                bool isLastOdd = (i == actions.Count - 1) && (actions.Count % 2 == 1) && cols == 2;
                int w = isLastOdd ? rowW : bw;
                int x = 12 + col * (bw + gap);
                int by = y + rowIdx * 36;
                var btn = new NeoButton
                {
                    Text = act.Label,
                    Role = act.Role,
                    Size = new Size(w, 30),
                    Location = new Point(x, by),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                };
                btn.Click += (s, _) =>
                {
                    act.Run?.Invoke();
                    if (act.RefreshAllAfter) RefreshValues();
                };
                scrollHost.Controls.Add(btn);
            }
            int rows = (actions.Count + cols - 1) / cols;
            y += rows * 36 + 2;
        }

        int maxBodyH = 440;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        if (wa.Height > 0) maxBodyH = Math.Clamp(wa.Height - 170, 220, 480);

        int contentH = Math.Min(maxBodyH, Math.Max(120, y + 10));
        if (y + 10 > contentH)
        {
            int narrowW = rowW - 16;
            foreach (Control c in scrollHost.Controls)
                if (c is SliderRow || c is ToggleRow) c.Width = narrowW;
        }

        ClientSize = new Size(PopupW, hdr.Height + contentH);
        Controls.Add(scrollHost);
        Controls.Add(hdr);

        Deactivate += (s, _) => { if (!pinned) Close(); };
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Paint += (s, pe) =>
        {
            using var bp = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.4f), 1.3f);
            pe.Graphics.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
        };
    }

    public void RefreshValues()
    {
        foreach (var cb in syncCallbacks) cb();
    }

    /// <summary>Привязать кнопку «Песочница» к динамическому списку параметров.</summary>
    public static void Attach(Control anchor, string title, Func<IReadOnlyList<SandboxEntry>> provider)
    {
        SandboxPopup open = null;
        anchor.Click += (s, _) =>
        {
            if (open != null && !open.IsDisposed)
            {
                open.Close();
                return;
            }
            var entries = provider?.Invoke();
            if (entries == null || entries.Count == 0) return;

            var popup = new SandboxPopup(title, entries);
            open = popup;
            popup.FormClosed += (_, _) => { if (ReferenceEquals(open, popup)) open = null; };
            PopupPlacement.BelowCentered(popup, anchor);
            popup.Show(anchor);
            popup.Activate();
        };
    }

    private sealed class ToggleRow : Control
    {
        private readonly SandboxToggle def;
        private bool state;
        private bool hover;

        public ToggleRow(SandboxToggle def, int width)
        {
            this.def = def;
            state = def.Get?.Invoke() ?? false;
            Size = new Size(width, 30);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
        }

        public void SyncFromGetter()
        {
            bool next = def.Get?.Invoke() ?? false;
            if (next != state) { state = next; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            state = !state;
            def.Set?.Invoke(state);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillDrawRoundRect(g, r, 7,
                Theme.BrushOf(hover ? Theme.Hover : Theme.Panel2),
                Theme.PenOf(state ? Theme.Mix(Theme.Border, Theme.Accent, 0.55f) : Theme.Border, 1f));

            var txtR = new Rectangle(10, 0, Width - 62, Height);
            TextRenderer.DrawText(g, def.Label, Font, txtR, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var sw = new Rectangle(Width - 48, 6, 38, 18);
            Theme.FillDrawRoundRect(g, sw, 9,
                Theme.BrushOf(state ? Theme.Accent : Theme.Panel),
                Theme.PenOf(state ? Theme.Accent : Theme.Border, 1f));
            int kx = state ? sw.Right - 16 : sw.Left + 2;
            g.FillEllipse(Theme.BrushOf(state ? Color.White : Theme.TextDim), kx, sw.Top + 2, 14, 14);
        }
    }

    private sealed class SliderRow : Control
    {
        private readonly SandboxSlider def;
        private readonly Func<double, string> fmt;
        private double current;
        private bool dragging;
        private bool valHover;
        private TextBox editBox;

        private Rectangle TrackRect => new(10, 30, Width - 20, 14);
        private Rectangle MinusRect => new(Width - 118, 4, 20, 20);
        private Rectangle ValueRect => new(Width - 95, 4, 62, 20);
        private Rectangle PlusRect => new(Width - 30, 4, 20, 20);

        public SliderRow(SandboxSlider def, int width)
        {
            this.def = def;
            fmt = def.Format ?? DefaultFormatter(def.Step);
            current = ClampQuantize(def.Get?.Invoke() ?? def.Min);
            Size = new Size(width, 50);
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
        }

        private static Func<double, string> DefaultFormatter(double step)
        {
            if (step >= 1.0) return v => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            if (step >= 0.1) return v => v.ToString("0.0", CultureInfo.InvariantCulture);
            return v => v.ToString("0.00", CultureInfo.InvariantCulture);
        }

        public void SyncFromGetter()
        {
            double v = ClampQuantize(def.Get?.Invoke() ?? def.Min);
            if (Math.Abs(v - current) > 1e-9)
            {
                current = v;
                Invalidate();
            }
        }

        private double ClampQuantize(double v)
        {
            double lo = Math.Min(def.Min, def.Max);
            double hi = Math.Max(def.Min, def.Max);
            v = Math.Clamp(v, lo, hi);
            if (def.Step > 0 && !def.Log)
            {
                double q = Math.Round((v - lo) / def.Step) * def.Step + lo;
                v = Math.Clamp(q, lo, hi);
            }
            return v;
        }

        private double RatioFromVal(double v)
        {
            double lo = Math.Min(def.Min, def.Max);
            double hi = Math.Max(def.Min, def.Max);
            if (hi - lo < 1e-9) return 0;
            if (def.Log && lo > 0 && hi > 0)
                return Math.Clamp(Math.Log(v / lo) / Math.Log(hi / lo), 0, 1);
            return Math.Clamp((v - lo) / (hi - lo), 0, 1);
        }

        private double ValFromRatio(double t)
        {
            t = Math.Clamp(t, 0, 1);
            double lo = Math.Min(def.Min, def.Max);
            double hi = Math.Max(def.Min, def.Max);
            double raw = (def.Log && lo > 0 && hi > 0) ? lo * Math.Pow(hi / lo, t) : lo + (hi - lo) * t;
            return ClampQuantize(raw);
        }

        private void ApplyValue(double v)
        {
            double q = ClampQuantize(v);
            if (Math.Abs(q - current) < 1e-9) return;
            current = q;
            def.Set?.Invoke(q);
            Invalidate();
        }

        private void ApplyFromMouseX(int x)
        {
            var tr = TrackRect;
            double t = tr.Width > 0 ? (x - tr.Left) / (double)tr.Width : 0;
            ApplyValue(ValFromRatio(t));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (MinusRect.Contains(e.Location))
            {
                double delta = def.Step > 0 ? def.Step : Math.Max(0.05, Math.Abs(def.Max - def.Min) / 25.0);
                ApplyValue(def.Log ? current / 1.15 : current - delta);
                return;
            }
            if (PlusRect.Contains(e.Location))
            {
                double delta = def.Step > 0 ? def.Step : Math.Max(0.05, Math.Abs(def.Max - def.Min) / 25.0);
                ApplyValue(def.Log ? current * 1.15 : current + delta);
                return;
            }
            if (ValueRect.Contains(e.Location))
            {
                BeginEdit();
                return;
            }
            if (e.Y >= TrackRect.Top - 6)
            {
                dragging = true;
                Capture = true;
                ApplyFromMouseX(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                ApplyFromMouseX(e.X);
                return;
            }
            bool vh = ValueRect.Contains(e.Location);
            if (vh != valHover) { valHover = vh; Invalidate(); }
            Cursor = (MinusRect.Contains(e.Location) || PlusRect.Contains(e.Location) || e.Y >= TrackRect.Top - 6)
                ? Cursors.Hand
                : vh ? Cursors.IBeam : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (valHover) { valHover = false; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Capture = false; }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int dir = Math.Sign(e.Delta);
            if (dir == 0) return;
            double delta = def.Step > 0 ? def.Step : Math.Max(0.05, Math.Abs(def.Max - def.Min) / 30.0);
            ApplyValue(def.Log ? current * (dir > 0 ? 1.1 : 1.0 / 1.1) : current + dir * delta);
        }

        private void BeginEdit()
        {
            if (editBox != null) return;
            var vr = ValueRect;
            string clean = fmt(current).Replace("×", "").Replace("x", "").Replace("%", "").Replace("с", "").Replace("s", "").Trim().TrimStart('+');
            editBox = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                Bounds = new Rectangle(vr.X - 2, vr.Y - 1, vr.Width + 4, vr.Height + 2),
                Text = clean,
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
                if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                    ApplyValue(dv);
            }
            if (Controls.Contains(box)) Controls.Remove(box);
            box.Dispose();
        }

        private static readonly Font MiniBtnFont = new("Segoe UI", 9F, FontStyle.Bold);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillDrawRoundRect(g, card, 7, Theme.BrushOf(Theme.Panel2), Theme.PenOf(Theme.Border, 1f));

            var lblR = new Rectangle(10, 4, Width - 134, 20);
            TextRenderer.DrawText(g, def.Label, Font, lblR, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            DrawMiniBtn(g, MinusRect, "−");
            DrawMiniBtn(g, PlusRect, "+");

            Theme.FillDrawRoundRect(g, ValueRect, 5, Theme.BrushOf(Theme.Panel), Theme.PenOf(valHover ? Theme.Accent : Theme.Border, 1f));
            TextRenderer.DrawText(g, fmt(current), Font, ValueRect, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var tr = TrackRect;
            int barY = tr.Y + tr.Height / 2 - 2;
            var barRect = new Rectangle(tr.X, barY, tr.Width, 4);
            Theme.FillDrawRoundRect(g, barRect, 2, Theme.BrushOf(Theme.Panel));

            double ratio = RatioFromVal(current);
            int fillW = Math.Max(4, (int)Math.Round(tr.Width * ratio));
            var fillRect = new Rectangle(tr.X, barY, fillW, 4);
            Theme.FillDrawRoundRect(g, fillRect, 2, Theme.BrushOf(Theme.Accent));

            int cx = tr.X + (int)Math.Round(tr.Width * ratio);
            int cy = barY + 2;
            g.FillEllipse(Theme.BrushOf(Color.FromArgb(65, Theme.Accent)), cx - 8, cy - 8, 16, 16);
            g.FillEllipse(Theme.BrushOf(Color.White), cx - 5, cy - 5, 10, 10);
            g.DrawEllipse(Theme.PenOf(Theme.Accent, 1.8f), cx - 5, cy - 5, 10, 10);
        }

        private static void DrawMiniBtn(Graphics g, Rectangle r, string text)
        {
            Theme.FillDrawRoundRect(g, r, 5, Theme.BrushOf(Theme.Panel), Theme.PenOf(Theme.Border, 1f));
            TextRenderer.DrawText(g, text, MiniBtnFont, r, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

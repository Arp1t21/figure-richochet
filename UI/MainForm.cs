using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Ricochet.UI;

/// <summary>
/// Главное (и единственное) окно: активный режим занимает всё окно без смещения UI,
/// а левая неоновая панель режимов и настроек открывается поверх игры и верхней шапки (header).
/// </summary>
public class MainForm : Form, IMessageFilter
{
    private const int OverlayBaseWidth = 216;

    private readonly Sidebar sidebar = new();
    private readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };

    private readonly NeoButton btnCollapse = new()
    {
        Icon = IconPainter.SidebarCollapse, Size = new Size(30, 30), IconPx = 17,
    };

    private readonly NeoButton navPlay = new()
    {
        Text = "УРОВНИ", Icon = IconPainter.Play, Role = AccentRole.Accent,
        IsToggle = true, Size = new Size(188, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navEdit = new()
    {
        Text = "РЕДАКТОР", Icon = IconPainter.Pencil, Role = AccentRole.Accent,
        IsToggle = true, Size = new Size(188, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navWave = new()
    {
        Text = "ВОЛНОВОЙ ФРОНТ", Icon = IconPainter.Waves, Role = AccentRole.Green,
        IsToggle = true, Size = new Size(188, 42), Font = new Font("Segoe UI", 9.0F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navRotor = new()
    {
        Text = "РОТОР", Icon = IconPainter.Restart, Role = AccentRole.Orange,
        IsToggle = true, Size = new Size(188, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton btnExit = new()
    {
        Text = "ВЫХОД", Icon = IconPainter.Exit, IconPx = 16,
        Dock = DockStyle.Bottom, Size = new Size(188, 40),
        Font = new Font("Segoe UI", 9F), ForeColor = Theme.TextDim, Margin = new Padding(14, 0, 14, 14),
    };

    // настройки — компактные строки, визуально отличные от крупных кнопок режимов
    private readonly NeoButton chipHalo = new()
    {
        Text = "ОРЕОЛЫ", IsToggle = true, Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGlow = new()
    {
        Text = "СВЕЧЕНИЕ", IsToggle = true, IsOn = true, Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGravity = new()
    {
        Text = "ГРАВИТАЦИЯ", IsToggle = true, Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipTrails = new()
    {
        Text = "ТРЕКИ", IsToggle = true, Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipSmooth = new()
    {
        Text = "СГЛАЖИВАНИЕ", IsToggle = true, IsOn = true, Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly SliderButton speedBtn = new()
    {
        Size = new Size(176, 28), Min = 0.01, Max = 5.0, Value = 1.0,
        Prefix = "СКОРОСТЬ ",
        Format = static v => v.ToString("0.00") + "×",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton btnTheme = new()
    {
        Text = "ТЕМА: КИБЕР", Icon = IconPainter.Palette, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(176, 28), Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = 200 };

    private PlayView playView;
    private EditorView editorView;
    private WaveView waveView;
    private RotorView rotorView;
    private UserControl active;
    private SizeF appliedScale = new(1f, 1f);

    /// <summary>Глобальный запрос из любого игрового режима: открыть копию уровня в «Редакторе».</summary>
    public static event Action<Core.LevelData> OpenInEditorRequested;
    public static void RequestOpenInEditor(Core.LevelData lvl) => OpenInEditorRequested?.Invoke(lvl);

    /// <summary>Активный режим (для служебных скриншотов и тестов).</summary>
    public UserControl ActiveView => active;

    /// <summary>Открыта ли боковая панель-оверлей поверх игры и шапки.</summary>
    public bool IsMenuOpen => sidebar.Visible;

    /// <summary>Перенести любой готовый уровень в «Редактор» для просмотра, правки и сохранения.</summary>
    public void OpenInEditor(Core.LevelData lvl)
    {
        if (lvl == null) return;
        ShowView(1);
        editorView?.LoadTemplate(lvl);
        SetMenuOpen(false);
    }

    public MainForm()
    {
        Theme.StyleForm(this);
        AutoScaleMode = AutoScaleMode.None;
        Text = "Фигурный рикошет";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1360, 780);
        MinimumSize = new Size(1320, 760);

        sidebar.Dock = DockStyle.None;
        sidebar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        sidebar.Bounds = new Rectangle(0, 0, OverlayBaseWidth, ClientSize.Height);
        sidebar.Visible = false;

        sidebar.Controls.Add(btnCollapse);
        sidebar.Controls.Add(navPlay);
        sidebar.Controls.Add(navEdit);
        sidebar.Controls.Add(navWave);
        sidebar.Controls.Add(navRotor);
        sidebar.Controls.Add(chipHalo);
        sidebar.Controls.Add(chipGlow);
        sidebar.Controls.Add(chipGravity);
        sidebar.Controls.Add(chipTrails);
        sidebar.Controls.Add(chipSmooth);
        sidebar.Controls.Add(speedBtn);
        sidebar.Controls.Add(btnTheme);
        sidebar.Controls.Add(btnExit);
        LayoutSidebar();
        sidebar.Resize += (s, e) => LayoutSidebar();
        Resize += (s, e) => SyncOverlayBounds();

        var tips = new ToolTip();
        tips.SetToolTip(btnCollapse, "Скрыть меню (Esc или клик по полю)");
        tips.SetToolTip(navPlay, "Уровни: готовые фигуры, цели и свободные арены (14 встроенных + ваши сохранения)");
        tips.SetToolTip(navEdit, "Редактор / песочница: нарисуй свои стены, цели и шары или перенеси любой уровень");
        tips.SetToolTip(navWave, "Волновой фронт: математические бильярды и дифракция (39 фигур)");
        tips.SetToolTip(navRotor, "Ротор: шар внутри вращающихся кругов с разрывом (10 уровней)");
        tips.SetToolTip(chipHalo, "Ореол вокруг шаров: показать/скрыть (во всех режимах)");
        tips.SetToolTip(chipGlow, "Свечение ярких элементов (bloom): включить/выключить");
        tips.SetToolTip(chipGravity, "Гравитация в активном режиме (G)");
        tips.SetToolTip(chipTrails, "Затухающие траектории шаров в активном режиме");
        tips.SetToolTip(chipSmooth, "Сглаживание линий и краёв лучей");
        tips.SetToolTip(speedBtn, "Скорость симуляции 0.01–5× — применяется на лету");
        tips.SetToolTip(btnTheme, "Сменить цветовую тему оформления (КИБЕР · СИНТВЕЙВ · АМОЛЕД · ЯНТАРЬ)");
        tips.SetToolTip(btnExit, "Выход из игры");

        Action onMenuClick = () => SetMenuOpen(!sidebar.Visible);
        Toolbar.MenuClicked += onMenuClick;
        Action<Core.LevelData> onOpenInEditor = OpenInEditor;
        OpenInEditorRequested += onOpenInEditor;

        btnCollapse.Click += (s, e) => SetMenuOpen(false);
        navPlay.Click += (s, e) => { ShowView(0); SetMenuOpen(false); };
        navEdit.Click += (s, e) => { ShowView(1); SetMenuOpen(false); };
        navWave.Click += (s, e) => { ShowView(2); SetMenuOpen(false); };
        navRotor.Click += (s, e) => { ShowView(3); SetMenuOpen(false); };
        btnExit.Click += (s, e) => Close();
        chipHalo.Click += (s, e) => GameCanvas.HaloOn = chipHalo.IsOn;
        chipGlow.Click += (s, e) => Fx.BloomOn = chipGlow.IsOn;
        chipGravity.Click += (s, e) => { if (active is IModeView mv) mv.GravityOn = chipGravity.IsOn; };
        chipTrails.Click += (s, e) => { if (active is IModeView mv) mv.TrailsOn = chipTrails.IsOn; };
        chipSmooth.Click += (s, e) => GameCanvas.SmoothOn = chipSmooth.IsOn;
        speedBtn.Changed += v => GameCanvas.SimSpeed = v;
        btnTheme.Click += (s, e) => Theme.CyclePreset();

        Action onTheme = () =>
        {
            BackColor = Theme.Bg;
            host.BackColor = Theme.Bg;
            sidebar.ApplyTheme();
            btnTheme.Text = "ТЕМА: " + Theme.CurrentName;
            Invalidate(true);
        };
        Theme.Changed += onTheme;

        Application.AddMessageFilter(this);
        syncTimer.Tick += (s, e) => SyncSettings();
        syncTimer.Start();
        Disposed += (s, e) =>
        {
            Application.RemoveMessageFilter(this);
            Toolbar.MenuClicked -= onMenuClick;
            OpenInEditorRequested -= onOpenInEditor;
            Theme.Changed -= onTheme;
            syncTimer.Stop();
        };

        // Сначала добавляем sidebar, затем host — в WinForms индекс 0 рисуется поверх остальных!
        Controls.Add(sidebar);
        Controls.Add(host);
        sidebar.BringToFront();

        ShowView(0);
    }

    public bool PreFilterMessage(ref Message m)
    {
        // Клик ЛКМ/ПКМ вне открытого меню-оверлея (по игре или верхней шапке) аккуратно закрывает меню
        if (sidebar.Visible && (m.Msg == 0x0201 || m.Msg == 0x0204))
        {
            var target = Control.FromHandle(m.HWnd);
            if (target != null && ReferenceEquals(target.FindForm(), this))
            {
                var pt = PointToClient(Cursor.Position);
                if (!sidebar.Bounds.Contains(pt))
                    SetMenuOpen(false);
            }
        }
        return false;
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        appliedScale = new SizeF(appliedScale.Width * factor.Width, appliedScale.Height * factor.Height);
        foreach (var v in new UserControl[] { playView, editorView, waveView, rotorView })
            if (v != null && !ReferenceEquals(v, active))
                v.Scale(factor);
        SyncOverlayBounds();
        LayoutSidebar();
    }

    private void SyncOverlayBounds()
    {
        float k = Math.Max(1f, navPlay.Height / 42f);
        int w = (int)Math.Round(OverlayBaseWidth * k);
        sidebar.Bounds = new Rectangle(0, 0, w, ClientSize.Height);
    }

    /// <summary>Показать или скрыть меню поверх игры и верхней панели без малейшего сдвига UI.</summary>
    public void SetMenuOpen(bool open)
    {
        if (sidebar.Visible == open) return;
        SyncOverlayBounds();
        LayoutSidebar();
        sidebar.Visible = open;
        if (open)
        {
            sidebar.BringToFront();
            sidebar.Invalidate(true);
        }
        else
        {
            active?.Invalidate(true);
        }
    }

    /// <summary>Совместимость со служебными вызовами: collapsed=true скрывает оверлей, false — открывает.</summary>
    public void SetSidebarCollapsed(bool collapsed) => SetMenuOpen(!collapsed);

    private void LayoutSidebar()
    {
        float k = Math.Max(1f, navPlay.Height / 42f);
        int navGap = (int)Math.Round(5 * k);
        int chipGap = (int)Math.Round(4 * k);

        int x = (sidebar.Width - navPlay.Width) / 2;
        int sx = (sidebar.Width - chipHalo.Width) / 2;
        btnCollapse.Location = new Point(sidebar.Width - btnCollapse.Width - (int)Math.Round(10 * k), (int)Math.Round(12 * k));

        int yExp = (int)Math.Round(98 * k);
        foreach (var b in new[] { navPlay, navEdit, navWave, navRotor })
        {
            b.Location = new Point(x, yExp);
            yExp += b.Height + navGap;
        }

        sidebar.SettingsDividerY = yExp + (int)Math.Round(6 * k);
        yExp = sidebar.SettingsDividerY + (int)Math.Round(18 * k);

        foreach (var c in new Control[] { chipHalo, chipGlow, chipGravity, chipTrails, chipSmooth })
        {
            c.Location = new Point(sx, yExp);
            yExp += c.Height + chipGap;
        }
        speedBtn.Location = new Point(sx, yExp + (int)Math.Round(2 * k));
        btnTheme.Location = new Point(sx, speedBtn.Bottom + chipGap);
        sidebar.StreaksTopY = btnTheme.Bottom + (int)Math.Round(8 * k);
    }

    private void SyncSettings()
    {
        if (WindowState == FormWindowState.Minimized) return;
        if (active is not IModeView mv) return;
        if (chipGravity.IsOn != mv.GravityOn) { chipGravity.IsOn = mv.GravityOn; chipGravity.Invalidate(); }
        if (chipTrails.IsOn != mv.TrailsOn) { chipTrails.IsOn = mv.TrailsOn; chipTrails.Invalidate(); }
    }

    /// <summary>Переключить режим (0 — уровни, 1 — редактор, 2 — волновой фронт, 3 — ротор).</summary>
    public void ShowView(int index)
    {
        bool created = false;
        UserControl Init<T>(ref T slot, Func<T> factory) where T : UserControl
        {
            if (slot == null) { slot = factory(); created = true; }
            return slot;
        }
        UserControl v = index switch
        {
            1 => Init(ref editorView, static () => new EditorView()),
            2 => Init(ref waveView, static () => new WaveView()),
            3 => Init(ref rotorView, static () => new RotorView()),
            _ => Init(ref playView, static () => new PlayView()),
        };
        if (created && (Math.Abs(appliedScale.Width - 1f) > 0.01f || Math.Abs(appliedScale.Height - 1f) > 0.01f))
            v.Scale(appliedScale);
        if (ReferenceEquals(v, playView))
            playView.ReloadUserLevels();

        if (!ReferenceEquals(active, v))
        {
            if (active is IModeView oldMv) oldMv.SetActive(false);
            host.Controls.Clear();
            host.Controls.Add(v);
            active = v;
        }
        if (v is IModeView newMv) newMv.SetActive(true);
        if (sidebar.Visible) sidebar.BringToFront();

        navPlay.IsOn = ReferenceEquals(v, playView);
        navEdit.IsOn = ReferenceEquals(v, editorView);
        navWave.IsOn = ReferenceEquals(v, waveView);
        navRotor.IsOn = ReferenceEquals(v, rotorView);
        navPlay.Invalidate();
        navEdit.Invalidate();
        navWave.Invalidate();
        navRotor.Invalidate();
        SyncSettings();
        Text = v switch
        {
            EditorView => "Фигурный рикошет — редактор",
            WaveView => "Фигурный рикошет — волновой фронт",
            RotorView => "Фигурный рикошет — ротор",
            _ => "Фигурный рикошет — уровни",
        };
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (sidebar.Visible && keyData == Keys.Escape)
        {
            SetMenuOpen(false);
            return true;
        }
        if (active is IModeView mv && mv.HandleKey(keyData)) return true;
        if (keyData == Keys.Escape) { ShowView(0); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Плавающая панель-оверлей: логотип, режимы, настройки, неоновая кромка поверх игры и шапки.</summary>
    private sealed class Sidebar : Panel
    {
        public int SettingsDividerY = 304;
        public int StreaksTopY = 560;

        private sealed class Streak
        {
            public float X, Y, VX, VY;
            public int ColorIdx;
            public Pen TailPen;
            public SolidBrush HeadBrush;
        }

        private readonly List<Streak> streaks = new();
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 32 };
        private readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        private double last;

        private static readonly Font LogoFont = new("Segoe UI Light", 14.5F);
        private static readonly Font CapFont = new("Segoe UI", 7.5F, FontStyle.Bold);
        private static readonly Font TagFont = new("Segoe UI", 8.0F);
        private readonly Pen accentLinePen = new(Color.FromArgb(120, Theme.Accent), 2f);
        private readonly Pen sepPen = new(Theme.PanelBorder, 1f);
        private readonly Pen edgeGlowPen = new(Color.FromArgb(55, Theme.Accent), 4f);
        private readonly Pen edgeCrispPen = new(Color.FromArgb(170, Theme.Accent), 1.3f);

        public Sidebar()
        {
            BackColor = Theme.Panel;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            var rnd = new Random(11);
            for (int i = 0; i < 5; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                double sp = 60 + rnd.NextDouble() * 90;
                streaks.Add(new Streak
                {
                    X = 20 + rnd.Next(160), Y = 560 + rnd.Next(90),
                    VX = (float)(Math.Cos(a) * sp), VY = (float)(Math.Sin(a) * sp),
                    ColorIdx = i % 4,
                    TailPen = new Pen(Color.White, 1.4f) { EndCap = System.Drawing.Drawing2D.LineCap.Round },
                    HeadBrush = new SolidBrush(Color.White),
                });
            }
            ApplyTheme();

            timer.Tick += (s, e) =>
            {
                if (!Visible || FindForm()?.WindowState == FormWindowState.Minimized) return;
                Step();
                Invalidate(new Rectangle(8, Math.Max(0, StreaksTopY - 4), Math.Max(1, Width - 16), Math.Max(1, Height - StreaksTopY)));
            };
            timer.Start();
            Disposed += (s, e) =>
            {
                timer.Stop();
                accentLinePen.Dispose();
                sepPen.Dispose();
                edgeGlowPen.Dispose();
                edgeCrispPen.Dispose();
                foreach (var st in streaks) { st.TailPen.Dispose(); st.HeadBrush.Dispose(); }
            };
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Panel;
            accentLinePen.Color = Color.FromArgb(135, Theme.Accent);
            sepPen.Color = Theme.Mix(Theme.PanelBorder, Theme.Accent, 0.18f);
            edgeGlowPen.Color = Color.FromArgb(55, Theme.Accent);
            edgeCrispPen.Color = Theme.Mix(Theme.Border, Theme.Accent, 0.65f);
            var cols = new[] { Theme.Accent, Theme.Green, Theme.Violet, Theme.Orange };
            foreach (var st in streaks)
            {
                var c = cols[st.ColorIdx % cols.Length];
                st.TailPen.Color = Color.FromArgb(75, c);
                st.HeadBrush.Color = Color.FromArgb(150, c);
            }
            Invalidate();
        }

        private void Step()
        {
            double dt = sw.Elapsed.TotalSeconds - last;
            last = sw.Elapsed.TotalSeconds;
            if (dt <= 0 || dt > 0.06) dt = 0.032;
            int bx0 = 12, bx1 = Math.Max(bx0 + 20, Width - 12);
            int by0 = StreaksTopY, by1 = Math.Max(by0 + 20, Height - (int)(54 * Math.Max(1f, Width / (float)OverlayBaseWidth)));
            foreach (var st in streaks)
            {
                st.X += st.VX * (float)dt;
                st.Y += st.VY * (float)dt;
                if (st.X < bx0) { st.X = bx0; st.VX = Math.Abs(st.VX); }
                if (st.X > bx1) { st.X = bx1; st.VX = -Math.Abs(st.VX); }
                if (st.Y < by0) { st.Y = by0; st.VY = Math.Abs(st.VY); }
                if (st.Y > by1) { st.Y = by1; st.VY = -Math.Abs(st.VY); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            float k = Math.Max(1f, Width / (float)OverlayBaseWidth);
            Color capDim = Color.FromArgb(190, Theme.TextDim);

            // логотип (сдвинут чуть влево от кнопки закрытия) и тонкая акцентная линия
            TextRenderer.DrawText(g, "РИКОШЕТ", LogoFont,
                new Rectangle(10, (int)(11 * k), Width - (int)(44 * k), (int)(28 * k)),
                Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            float lineX = (Width - 28 * k) / 2f;
            float lineY = 43 * k;
            g.DrawLine(accentLinePen, lineX - 26 * k, lineY, lineX + 26 * k, lineY);
            TextRenderer.DrawText(g, "фигуры · шары · отскок", TagFont,
                new Rectangle(4, (int)(47 * k), Width - 8, (int)(18 * k)),
                Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "РЕЖИМЫ", CapFont,
                new Rectangle((int)(14 * k), (int)(74 * k), Width - 28, (int)(18 * k)),
                capDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // визуальный разделитель: режимы сверху, настройки снизу
            g.DrawLine(sepPen, (int)(14 * k), SettingsDividerY, Width - (int)(14 * k), SettingsDividerY);
            TextRenderer.DrawText(g, "НАСТРОЙКИ", CapFont,
                new Rectangle((int)(18 * k), SettingsDividerY + (int)(2 * k), Width - 32, (int)(15 * k)),
                capDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // штрихи, рикошетящие между кнопками
            foreach (var st in streaks)
            {
                float n = MathF.Sqrt(st.VX * st.VX + st.VY * st.VY);
                if (n < 1) continue;
                const float len = 18;
                float tx = st.X - st.VX / n * len, ty = st.Y - st.VY / n * len;
                g.DrawLine(st.TailPen, tx, ty, st.X, st.Y);
                g.FillEllipse(st.HeadBrush, st.X - 1.5f, st.Y - 1.5f, 3, 3);
            }

            // неоновая кромка оверлея справа (отделяет плавающее меню от шапки и игры под ним)
            g.DrawLine(edgeGlowPen, Width - 2, 0, Width - 2, Height);
            g.DrawLine(edgeCrispPen, Width - 1, 0, Width - 1, Height);
        }
    }
}

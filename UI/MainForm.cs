using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Ricochet.UI;

/// <summary>
/// Главное (и единственное) окно: слева сворачиваемая неоновая панель навигации и настроек
/// (Expanded 188 px ↔ Compact Rail 62 px), переключатель цветовых тем и активный режим справа.
/// </summary>
public class MainForm : Form
{
    private readonly Sidebar sidebar = new();
    private readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };
    private bool sidebarCollapsed;

    private readonly NeoButton btnCollapse = new()
    {
        Icon = IconPainter.SidebarCollapse, Size = new Size(28, 28), IconPx = 16,
    };

    private readonly NeoButton navPlay = new()
    {
        Text = "УРОВНИ", Icon = IconPainter.Play, Role = AccentRole.Accent,
        IsToggle = true, Size = new Size(164, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navEdit = new()
    {
        Text = "РЕДАКТОР", Icon = IconPainter.Pencil, Role = AccentRole.Accent,
        IsToggle = true, Size = new Size(164, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navWave = new()
    {
        Text = "ВОЛНОВОЙ ФРОНТ", Icon = IconPainter.Waves, Role = AccentRole.Green,
        IsToggle = true, Size = new Size(164, 42), Font = new Font("Segoe UI", 8.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navRotor = new()
    {
        Text = "РОТОР", Icon = IconPainter.Restart, Role = AccentRole.Orange,
        IsToggle = true, Size = new Size(164, 42), Font = new Font("Segoe UI", 9.75F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton btnExit = new()
    {
        Text = "ВЫХОД", Icon = IconPainter.Exit, IconPx = 16,
        Dock = DockStyle.Bottom, Size = new Size(164, 40),
        Font = new Font("Segoe UI", 9F), ForeColor = Theme.TextDim, Margin = new Padding(12, 0, 12, 14),
    };

    // настройки — компактные строки, визуально отличные от крупных кнопок режимов
    private readonly NeoButton chipHalo = new()
    {
        Text = "ОРЕОЛЫ", IsToggle = true, Size = new Size(152, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGlow = new()
    {
        Text = "СВЕЧЕНИЕ", IsToggle = true, IsOn = true, Size = new Size(152, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGravity = new()
    {
        Text = "ГРАВИТАЦИЯ", IsToggle = true, Size = new Size(152, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipTrails = new()
    {
        Text = "ТРЕКИ", IsToggle = true, Size = new Size(152, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipSmooth = new()
    {
        Text = "СГЛАЖИВАНИЕ", IsToggle = true, IsOn = true, Size = new Size(152, 28), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly SliderButton speedBtn = new()
    {
        Size = new Size(152, 28), Min = 0.01, Max = 5.0, Value = 1.0,
        Prefix = "СКОРОСТЬ ",
        Format = static v => v.ToString("0.00") + "×",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
    };
    private readonly NeoButton btnTheme = new()
    {
        Text = "ТЕМА: КИБЕР", Icon = IconPainter.Palette, IconPx = 15, Role = AccentRole.Violet,
        Size = new Size(152, 28), Font = new Font("Segoe UI", 8F, FontStyle.Bold),
    };
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = 200 };

    private PlayView playView;
    private EditorView editorView;
    private WaveView waveView;
    private RotorView rotorView;
    private UserControl active;

    /// <summary>Активный режим (для служебных скриншотов).</summary>
    public UserControl ActiveView => active;

    public MainForm()
    {
        Theme.StyleForm(this);
        AutoScaleMode = AutoScaleMode.None;
        Text = "Фигурный рикошет";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1360, 780);
        MinimumSize = new Size(1320, 760);

        sidebar.Dock = DockStyle.Left;
        sidebar.Width = 188;
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

        var tips = new ToolTip();
        tips.SetToolTip(btnCollapse, "Свернуть / развернуть боковую панель (компактный рельс)");
        tips.SetToolTip(navPlay, "Уровни: готовые фигуры, цели и свободные арены");
        tips.SetToolTip(navEdit, "Редактор / песочница: нарисуй свои стены, цели и шары");
        tips.SetToolTip(navWave, "Волновой фронт: математические бильярды (24 фигуры)");
        tips.SetToolTip(navRotor, "Ротор: шар внутри вращающихся кругов с разрывом (6 уровней)");
        tips.SetToolTip(chipHalo, "Ореол вокруг шаров: показать/скрыть (во всех режимах)");
        tips.SetToolTip(chipGlow, "Свечение ярких элементов (bloom): включить/выключить");
        tips.SetToolTip(chipGravity, "Гравитация в активном режиме (G)");
        tips.SetToolTip(chipTrails, "Затухающие траектории шаров в активном режиме");
        tips.SetToolTip(chipSmooth, "Сглаживание линий и краёв лучей");
        tips.SetToolTip(speedBtn, "Скорость симуляции 0.01–5× — применяется на лету");
        tips.SetToolTip(btnTheme, "Сменить цветовую тему оформления (КИБЕР · СИНТВЕЙВ · АМОЛЕД · ЯНТАРЬ)");
        tips.SetToolTip(btnExit, "Выход из игры");

        btnCollapse.Click += (s, e) => SetSidebarCollapsed(!sidebarCollapsed);
        navPlay.Click += (s, e) => ShowView(0);
        navEdit.Click += (s, e) => ShowView(1);
        navWave.Click += (s, e) => ShowView(2);
        navRotor.Click += (s, e) => ShowView(3);
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
            btnTheme.Text = sidebarCollapsed ? "" : "ТЕМА: " + Theme.CurrentName;
            Invalidate(true);
        };
        Theme.Changed += onTheme;

        syncTimer.Tick += (s, e) => SyncSettings();
        syncTimer.Start();
        Disposed += (s, e) => { Theme.Changed -= onTheme; syncTimer.Stop(); };

        Controls.Add(host);
        Controls.Add(sidebar);

        ShowView(0);
    }

    public void SetSidebarCollapsed(bool collapsed)
    {
        sidebarCollapsed = collapsed;
        sidebar.Collapsed = collapsed;
        float k = Math.Max(1f, navPlay.Height / 42f);

        sidebar.Width = (int)Math.Round((collapsed ? 64 : 188) * k);
        btnCollapse.Icon = collapsed ? IconPainter.SidebarExpand : IconPainter.SidebarCollapse;

        int navW = (int)Math.Round((collapsed ? 44 : 164) * k);
        int chipW = (int)Math.Round((collapsed ? 44 : 152) * k);

        navPlay.Text  = collapsed ? "" : "УРОВНИ";
        navEdit.Text  = collapsed ? "" : "РЕДАКТОР";
        navWave.Text  = collapsed ? "" : "ВОЛНОВОЙ ФРОНТ";
        navRotor.Text = collapsed ? "" : "РОТОР";
        foreach (var b in new[] { navPlay, navEdit, navWave, navRotor })
            b.Width = navW;

        chipHalo.Text    = collapsed ? "ОР" : "ОРЕОЛЫ";
        chipGlow.Text    = collapsed ? "СВ" : "СВЕЧЕНИЕ";
        chipGravity.Text = collapsed ? "ГР" : "ГРАВИТАЦИЯ";
        chipTrails.Text  = collapsed ? "ТР" : "ТРЕКИ";
        chipSmooth.Text  = collapsed ? "СГ" : "СГЛАЖИВАНИЕ";
        foreach (var c in new Control[] { chipHalo, chipGlow, chipGravity, chipTrails, chipSmooth })
            c.Width = chipW;

        speedBtn.Prefix = collapsed ? "" : "СКОРОСТЬ ";
        speedBtn.Width = chipW;

        btnTheme.Text = collapsed ? "" : "ТЕМА: " + Theme.CurrentName;
        btnTheme.Width = chipW;

        btnExit.Text = collapsed ? "" : "ВЫХОД";

        LayoutSidebar();
        sidebar.Invalidate(true);
    }

    private void LayoutSidebar()
    {
        float k = Math.Max(1f, navPlay.Height / 42f);
        int navGap = (int)Math.Round(5 * k);
        int chipGap = (int)Math.Round(4 * k);

        if (sidebarCollapsed)
        {
            btnCollapse.Location = new Point((sidebar.Width - btnCollapse.Width) / 2, (int)Math.Round(12 * k));
            int y = (int)Math.Round(52 * k);
            foreach (var b in new[] { navPlay, navEdit, navWave, navRotor })
            {
                b.Location = new Point((sidebar.Width - b.Width) / 2, y);
                y += b.Height + navGap;
            }
            sidebar.SettingsDividerY = y + (int)Math.Round(6 * k);
            y = sidebar.SettingsDividerY + (int)Math.Round(10 * k);
            foreach (var c in new Control[] { chipHalo, chipGlow, chipGravity, chipTrails, chipSmooth, speedBtn, btnTheme })
            {
                c.Location = new Point((sidebar.Width - c.Width) / 2, y);
                y += c.Height + chipGap;
            }
            sidebar.StreaksTopY = btnTheme.Bottom + (int)Math.Round(10 * k);
            return;
        }

        int x = (sidebar.Width - navPlay.Width) / 2;
        int sx = (sidebar.Width - chipHalo.Width) / 2;
        btnCollapse.Location = new Point(sidebar.Width - btnCollapse.Width - (int)Math.Round(8 * k), (int)Math.Round(12 * k));

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
        UserControl v = index switch
        {
            1 => editorView ??= new EditorView(),
            2 => waveView ??= new WaveView(),
            3 => rotorView ??= new RotorView(),
            _ => playView ??= new PlayView(),
        };
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
        if (active is IModeView mv && mv.HandleKey(keyData)) return true;
        if (keyData == Keys.Escape) { ShowView(0); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Боковая панель: логотип, подпись «РЕЖИМЫ», линия справа, штрихи-рикошеты.</summary>
    private sealed class Sidebar : Panel
    {
        public bool Collapsed;
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
                    X = 20 + rnd.Next(140), Y = 560 + rnd.Next(90),
                    VX = (float)(Math.Cos(a) * sp), VY = (float)(Math.Sin(a) * sp),
                    ColorIdx = i % 4,
                    TailPen = new Pen(Color.White, 1.4f) { EndCap = System.Drawing.Drawing2D.LineCap.Round },
                    HeadBrush = new SolidBrush(Color.White),
                });
            }
            ApplyTheme();

            timer.Tick += (s, e) =>
            {
                if (Collapsed || FindForm()?.WindowState == FormWindowState.Minimized) return;
                Step();
                Invalidate(new Rectangle(8, Math.Max(0, StreaksTopY - 4), Math.Max(1, Width - 16), Math.Max(1, Height - StreaksTopY)));
            };
            timer.Start();
            Disposed += (s, e) =>
            {
                timer.Stop();
                accentLinePen.Dispose();
                sepPen.Dispose();
                foreach (var st in streaks) { st.TailPen.Dispose(); st.HeadBrush.Dispose(); }
            };
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Panel;
            accentLinePen.Color = Color.FromArgb(135, Theme.Accent);
            sepPen.Color = Theme.Mix(Theme.PanelBorder, Theme.Accent, 0.18f);
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
            int by0 = StreaksTopY, by1 = Math.Max(by0 + 20, Height - (int)(54 * Math.Max(1f, Width / 188f)));
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

            if (Collapsed)
            {
                g.DrawLine(sepPen, 10, SettingsDividerY, Width - 10, SettingsDividerY);
                g.DrawLine(sepPen, Width - 1, 0, Width - 1, Height - 1);
                return;
            }

            float k = Math.Max(1f, Width / 188f);
            Color capDim = Color.FromArgb(190, Theme.TextDim);

            // логотип (сдвинут чуть влево от кнопки сворачивания) и тонкая акцентная линия
            TextRenderer.DrawText(g, "РИКОШЕТ", LogoFont,
                new Rectangle(8, (int)(11 * k), Width - (int)(36 * k), (int)(28 * k)),
                Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            float lineX = (Width - 28 * k) / 2f;
            float lineY = 43 * k;
            g.DrawLine(accentLinePen, lineX - 24 * k, lineY, lineX + 24 * k, lineY);
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

            // линия-разделитель справа
            g.DrawLine(sepPen, Width - 1, 0, Width - 1, Height - 1);
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Ricochet.UI;

/// <summary>
/// Главное (и единственное) окно: слева неоновая панель навигации по режимам,
/// справа — активный режим (уровни / редактор / волновой фронт). Режимы
/// переключаются на месте, состояние каждого сохраняется. Анимированное меню
/// с рикошетящими штрихами — в шапке боковой панели.
/// </summary>
public class MainForm : Form
{
    private readonly Sidebar sidebar = new();
    private readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };

    private readonly NeoButton navPlay = new()
    {
        Text = "УРОВНИ", Icon = IconPainter.Play, Accent = Theme.Accent,
        IsToggle = true, Size = new Size(164, 44), Font = new Font("Segoe UI", 10F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navEdit = new()
    {
        Text = "РЕДАКТОР", Icon = IconPainter.Pencil,
        IsToggle = true, Size = new Size(164, 44), Font = new Font("Segoe UI", 10F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navWave = new()
    {
        Text = "ВОЛНОВОЙ ФРОНТ", Icon = IconPainter.Waves, Accent = Theme.Green,
        IsToggle = true, Size = new Size(164, 44), Font = new Font("Segoe UI", 9F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton navRotor = new()
    {
        Text = "РОТОР", Icon = IconPainter.Restart, Accent = Theme.Orange,
        IsToggle = true, Size = new Size(164, 44), Font = new Font("Segoe UI", 10F, FontStyle.Bold), IconPx = 20,
    };
    private readonly NeoButton btnExit = new()
    {
        Text = "ВЫХОД", Dock = DockStyle.Bottom, Size = new Size(164, 42),
        Font = new Font("Segoe UI", 9F), ForeColor = Theme.TextDim, Margin = new Padding(12, 0, 12, 14),
    };

    // настройки — компактные строки, визуально отличные от крупных кнопок режимов
    private readonly NeoButton chipHalo = new()
    {
        Text = "ОРЕОЛЫ", IsToggle = true, Size = new Size(152, 30), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGlow = new()
    {
        Text = "СВЕЧЕНИЕ", IsToggle = true, IsOn = true, Size = new Size(152, 30), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipGravity = new()
    {
        Text = "ГРАВИТАЦИЯ", IsToggle = true, Size = new Size(152, 30), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipTrails = new()
    {
        Text = "ТРЕКИ", IsToggle = true, Size = new Size(152, 30), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly NeoButton chipSmooth = new()
    {
        Text = "СГЛАЖИВАНИЕ", IsToggle = true, IsOn = true, Size = new Size(152, 30), Font = new Font("Segoe UI", 8.25F),
    };
    private readonly SliderButton speedBtn = new()
    {
        Size = new Size(152, 30), Min = 0.01, Max = 5.0, Value = 1.0,
        Prefix = "СКОРОСТЬ ",
        Format = static v => v.ToString("0.00") + "×",
        Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
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
        sidebar.Controls.Add(btnExit);
        LayoutSidebar();
        sidebar.Resize += (s, e) => LayoutSidebar();

        var tips = new ToolTip();
        tips.SetToolTip(navPlay, "Готовые фигуры: круг, квадрат, прямоугольник, треугольник");
        tips.SetToolTip(navEdit, "Нарисуй свою фигуру, цели и шары — и играй");
        tips.SetToolTip(navWave, "Математические бильярды: веер лучей и каустики");
        tips.SetToolTip(navRotor, "Шар в неполном вращающемся круге: вылети через разрыв");
        tips.SetToolTip(chipHalo, "Ореол вокруг шаров: показать/скрыть (во всех режимах)");
        tips.SetToolTip(chipGlow, "Свечение ярких элементов (bloom): включить/выключить. Всю пост-обработку целиком отключает чип FX в панели режима");
        tips.SetToolTip(chipGravity, "Гравитация в активном режиме (G). Переключается для каждого режима отдельно");
        tips.SetToolTip(chipTrails, "Затухающие траектории шаров в активном режиме");
        tips.SetToolTip(chipSmooth, "Сглаживание линий и краёв лучей. Выключите, чтобы ускорить гигантские плотности");
        tips.SetToolTip(speedBtn, "Скорость симуляции 0.01–5× — применяется на лету во всех режимах, без перезапуска");

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

        // синхронизация чипов гравитации/треков с активным режимом (G, смена уровня...)
        syncTimer.Tick += (s, e) => SyncSettings();
        syncTimer.Start();
        Disposed += (s, e) => syncTimer.Stop();

        Controls.Add(host);
        Controls.Add(sidebar);

        ShowView(0);
    }

    private void LayoutSidebar()
    {
        int x = 12, sx = 18;
        navPlay.Location = new Point(x, 104);
        navEdit.Location = new Point(x, 154);
        navWave.Location = new Point(x, 204);
        navRotor.Location = new Point(x, 254);
        chipHalo.Location = new Point(sx, 332);
        chipGlow.Location = new Point(sx, 366);
        chipGravity.Location = new Point(sx, 400);
        chipTrails.Location = new Point(sx, 434);
        chipSmooth.Location = new Point(sx, 468);
        speedBtn.Location = new Point(sx, 506);
        btnExit.Height = 42;
    }

    private void SyncSettings()
    {
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
        if (!ReferenceEquals(active, v))
        {
            host.Controls.Clear();
            host.Controls.Add(v);
            active = v;
        }
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
        private sealed class Streak
        {
            public float X, Y, VX, VY;
            public Color C;
        }

        private readonly List<Streak> streaks = new();
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
        private readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        private double last;

        private static readonly Font LogoFont = new("Segoe UI Light", 15F);
        private static readonly Font CapFont = new("Segoe UI", 7.5F, FontStyle.Bold);
        private static readonly Font TagFont = new("Segoe UI", 8.25F);

        public Sidebar()
        {
            BackColor = Theme.Panel;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            var rnd = new Random(11);
            var cols = new[] { Theme.Accent, Theme.Green, Theme.Violet, Color.FromArgb(150, 170, 200) };
            for (int i = 0; i < 5; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                double sp = 60 + rnd.NextDouble() * 90;
                streaks.Add(new Streak
                {
                    X = 20 + rnd.Next(148), Y = 550 + rnd.Next(100),
                    VX = (float)(Math.Cos(a) * sp), VY = (float)(Math.Sin(a) * sp),
                    C = cols[i % cols.Length],
                });
            }
            timer.Tick += (s, e) => { Step(); Invalidate(); };
            timer.Start();
            Disposed += (s, e) => timer.Stop();
        }

        private void Step()
        {
            double dt = sw.Elapsed.TotalSeconds - last;
            last = sw.Elapsed.TotalSeconds;
            if (dt <= 0 || dt > 0.05) dt = 0.016;
            // штрихи летают в свободной зоне между настройками и «ВЫХОДом»
            int bx0 = 12, bx1 = Math.Max(bx0 + 20, Width - 12);
            int by0 = 550, by1 = Math.Max(by0 + 20, Height - 66);
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

            // логотип и тонкая акцентная линия
            TextRenderer.DrawText(g, "РИКОШЕТ", LogoFont, new Rectangle(6, 14, Width - 12, 28),
                Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Color.FromArgb(120, Theme.Accent), 2))
                g.DrawLine(p, Width / 2f - 24, 46, Width / 2f + 24, 46);
            TextRenderer.DrawText(g, "фигуры · шары · отскок", TagFont, new Rectangle(4, 52, Width - 8, 18),
                Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "РЕЖИМЫ", CapFont, new Rectangle(14, 80, Width - 28, 16),
                Color.FromArgb(190, Theme.TextDim), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // визуальный разделитель: режимы сверху, настройки снизу — компактные строки
            using (var sep = new Pen(Theme.PanelBorder, 1))
                g.DrawLine(sep, 14, 314, Width - 14, 314);
            TextRenderer.DrawText(g, "НАСТРОЙКИ", CapFont, new Rectangle(18, 318, Width - 32, 14),
                Color.FromArgb(190, Theme.TextDim), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // штрихи, рикошетящие между кнопками, — фирменная анимация
            foreach (var st in streaks)
            {
                float n = MathF.Sqrt(st.VX * st.VX + st.VY * st.VY);
                if (n < 1) continue;
                const float len = 18;
                float tx = st.X - st.VX / n * len, ty = st.Y - st.VY / n * len;
                using (var p = new Pen(Color.FromArgb(70, st.C), 1.4f) { EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLine(p, tx, ty, st.X, st.Y);
                using var b = new SolidBrush(Color.FromArgb(140, st.C));
                g.FillEllipse(b, st.X - 1.5f, st.Y - 1.5f, 3, 3);
            }

            // линия-разделитель справа
            using (var p = new Pen(Theme.PanelBorder, 1))
                g.DrawLine(p, Width - 1, 0, Width - 1, Height - 1);
        }
    }
}

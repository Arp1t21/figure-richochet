using System;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Ротор»: шар внутри неполных вращающихся кругов с вырезом.
/// 1) «ВЫХОД» — один круг: как только шар вылетает через разрыв, круг с анимацией
///    рассыпается на осколки, а шар свободно улетает в пустоту.
/// 2) «200 КРУГОВ» — вложенная розовая спираль из 200 кругов со счётчиком в центре:
///    каждый круг, из которого вышел шар, с анимацией рассыпается, а остальные
///    плавно смыкаются к центру, пока шар не выйдет из всех 200 кругов.
/// </summary>
public class RotorView : UserControl, IModeView
{
    private const double SpinL0 = -0.68;  // рад/с против часовой стрелки (уровень 1)
    private const double SpinL1 = 1.32;   // рад/с по часовой стрелке: винт спирали возвращает шар в центр
    public const int SpiralCount = 200;
    public const double SpiralR0 = 70.0;
    public const double SpiralStep = 9.0; // шаг радиусов: чёткий зазор между розовыми кольцами
    public const double SpiralGapDeg = 60.0;
    public const double SpiralTwistDeg = 3.0;

    private readonly GameCanvas canvas = new()
    {
        Editor = false, Dock = DockStyle.Fill, ShowGrid = false, Trails = false,
        RotorSkin = true, NewBallR = 10, NewBallArgb = unchecked((int)0xFFF0F3FF),
    };
    private int index;
    private Vec2 center = new(640, 360);

    private readonly Label lblName = new()
    {
        AutoSize = false, Size = new Size(300, 34), ForeColor = Theme.Accent,
        Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Pause, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton btnRestart = new() { Icon = IconPainter.Restart, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(46, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF) }; // БЕЛЫЙ (как на референсе)
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 30, Value = 10 };
    private readonly Label hintLbl;

    public RotorView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;

        var top = new Toolbar { Dock = DockStyle.Top, Height = 56 };
        var hints = new Bar { Dock = DockStyle.Bottom, Height = 30, LineAtTop = true, BackColor = Theme.Panel };
        hintLbl = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.75F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        hints.Controls.Add(hintLbl);

        var btnPrev = new NeoButton { Icon = IconPainter.ChevronLeft, Size = new Size(38, 38), IconPx = 20 };
        var btnNext = new NeoButton { Icon = IconPainter.ChevronRight, Size = new Size(38, 38), IconPx = 20 };

        var tips = new ToolTip();
        tips.SetToolTip(btnPrev, "Предыдущий уровень");
        tips.SetToolTip(btnNext, "Следующий уровень");
        tips.SetToolTip(chipFx, "Шейдерные эффекты");
        tips.SetToolTip(colorBtn, "Цвет шара (СПЕКТР — каждый шар нового тона)");
        tips.SetToolTip(sizeBtn, "Размер шара — ползунок (или колесо мыши, +/−)");
        tips.SetToolTip(btnPause, "Пауза (пробел)");
        tips.SetToolTip(btnRestart, "Заново (R)");

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + 2) % 2);
        btnNext.Click += (s, e) => ShowLevel((index + 1) % 2);
        btnPause.Click += (s, e) => TogglePause();
        btnRestart.Click += (s, e) => ShowLevel(index);
        chipFx.Click += (s, e) => canvas.FxOn = chipFx.IsOn;
        colorBtn.Changed += () =>
        {
            canvas.NewBallSpectrum = colorBtn.Selected == BallColors.Spectrum;
            canvas.NewBallArgb = colorBtn.Selected;
            int argb = canvas.NewBallSpectrum ? unchecked((int)0xFF5EC5FF) : colorBtn.Selected;
            foreach (var b in canvas.Sim.Balls) b.Argb = argb;
            canvas.Invalidate();
        };
        sizeBtn.Changed += v => SetBallRadius(v);
        canvas.BallRadiusChanged += () => SetBallRadius(canvas.NewBallR);
        canvas.Simulated += OnSimulated;

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn).AddRight(btnRestart).AddRight(btnPause);

        Controls.Add(canvas);
        Controls.Add(hints);
        Controls.Add(top);

        ShowLevel(0);
    }

    private void SetBallRadius(double r)
    {
        double clamped = Math.Clamp(Math.Round(r), sizeBtn.Min, sizeBtn.Max);
        canvas.NewBallR = clamped;
        sizeBtn.Value = (int)clamped;
        foreach (var b in canvas.Sim.Balls) b.R = clamped;
    }

    /// <summary>Свежая копия уровня (кольца рассыпаются — уровень пересобирается при рестарте).</summary>
    private LevelData BuildLevel(int i, out Vec2 c, out string hint)
    {
        c = new Vec2(640, 360);
        int argb = colorBtn.Selected == BallColors.Spectrum ? unchecked((int)0xFF5EC5FF) : colorBtn.Selected;
        double r = canvas.NewBallR;

        if (i == 0)
        {
            var exit = new LevelData
            {
                Name = "ВЫХОД",
                Gravity = true,
                GravityScale = 0.56,
                ElasticBounce = true,
            };
            exit.Arcs.Add(new ArcData
            {
                Center = c,
                Radius = 260,
                StartDeg = 35,
                SweepDeg = 360 - 62,
                HalfWidth = 2.25,
            });
            exit.Balls.Add(new BallData
            {
                Pos = new Vec2(606, 292),
                R = r,
                Vel = new Vec2(320, -210),
                HasVel = true,
                Argb = argb,
            });
            hint = "  вылетите через разрыв круга — круг рассыпется, а шар улетит в пустоту  ·  ЛКМ — бросок  ·  R — заново";
            return exit;
        }

        // «200 КРУГОВ»: плотная розовая спираль из 200 неполных кругов со счётчиком в центре.
        // Вырез 60° каждого следующего круга сдвинут на +3° по часовой стрелке — образуется спиральный рукав.
        var spiral = new LevelData
        {
            Name = "200 КРУГОВ",
            Gravity = true,
            GravityScale = 0.56,
            ElasticBounce = true,
        };
        for (int k = 0; k < SpiralCount; k++)
        {
            spiral.Arcs.Add(new ArcData
            {
                Center = c,
                Radius = SpiralR0 + k * SpiralStep,
                StartDeg = 158.0 + k * SpiralTwistDeg, // разрыв 98°..158° у первого кольца, как на референсе
                SweepDeg = 360.0 - SpiralGapDeg,
                HalfWidth = 2.25,
            });
        }
        spiral.Balls.Add(new BallData
        {
            Pos = new Vec2(626, 404), // под счётчиком, как на референсе
            R = r,
            Vel = new Vec2(-180, -260),
            HasVel = true,
            Argb = argb,
        });
        hint = $"  кругов осталось: {SpiralCount}  ·  каждый пройденный круг рассыпается  ·  ЛКМ — бросок  ·  R — заново";
        return spiral;
    }

    /// <summary>Неполный круг в виде полилинии (сохранён для совместимости и тестов стенок).</summary>
    public static PolyData ArcPoly(double cx, double cy, double r, double gapCenterDeg, double gapDeg = 60, int n = 56)
    {
        var p = new PolyData { Pivot = new Vec2(cx, cy) };
        double a0 = (gapCenterDeg + gapDeg / 2) * Math.PI / 180;
        double sweep = (360 - gapDeg) * Math.PI / 180;
        for (int i = 0; i <= n; i++)
        {
            double a = a0 + sweep * i / n;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return p;
    }

    /// <summary>Переключить уровень «Ротора» (0 — ВЫХОД, 1 — 200 КРУГОВ).</summary>
    public void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, 1);
        var lvl = BuildLevel(index, out var c, out var hint);
        center = c;
        canvas.SpinSpeed = index == 0 ? SpinL0 : SpinL1;
        if (index == 1)
        {
            canvas.HubCenter = center;
            canvas.HubRadius = SpiralR0 - 8;
            canvas.HubText = () => canvas.Sim.Arcs.Count > 0 ? canvas.Sim.Arcs.Count.ToString() : "";
        }
        else
        {
            canvas.HubCenter = null;
            canvas.HubText = null;
        }
        canvas.LoadLevel(lvl);
        canvas.Paused = false;
        btnPause.Icon = IconPainter.Pause;
        lblName.Text = $"  {index + 1}/2 · {lvl.Name}";
        hintLbl.Text = hint;
    }

    /// <summary>Рассыпание пройденных кругов и плавное сжатие оставшейся спирали к центру.</summary>
    private void OnSimulated(double dt)
    {
        var arcs = canvas.Sim.Arcs;
        var balls = canvas.Sim.Balls;
        if (arcs.Count == 0) return;

        bool changed = false;
        while (arcs.Count > 0 && balls.Count > 0)
        {
            var arc0 = arcs[0];
            bool lastRing = arcs.Count == 1;
            bool escaped = false;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (b.MinCenterDist >= arc0.Radius - 2.0) continue; // шар ни разу не был внутри этого кольца
                double dist = Vec2.Dist(b.Pos, center);
                double exitDist = lastRing ? (arc0.Radius + b.R + arc0.HalfWidth) : (arc0.Radius + 1.2);
                if (dist > exitDist)
                {
                    escaped = true;
                    break;
                }
            }
            if (!escaped) break;

            canvas.DissolveArc(arc0, inwardMix: !lastRing);
            changed = true;
        }

        // Во втором уровне оставшиеся круги плавно смыкаются к радиусу SpiralR0,
        // сохраняя шаг SpiralStep, чтобы все 200 колец по очереди входили в кадр.
        if (index == 1 && arcs.Count > 0 && arcs[0].Radius > SpiralR0)
        {
            var arc0 = arcs[0];
            double minAllowedR0 = SpiralR0;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= arc0.Radius + 1.2 && !arc0.IsInSolid(Math.Atan2(dy, dx)))
                    minAllowedR0 = Math.Max(minAllowedR0, dist + 2.5);
            }

            double diff = arc0.Radius - minAllowedR0;
            if (diff > 0.01)
            {
                double oldR0 = arc0.Radius;
                double speed = Math.Max(220.0, diff * 7.5);
                double dr = Math.Min(diff, speed * dt);
                for (int k = 0; k < arcs.Count; k++)
                    arcs[k].Radius -= dr;

                // если шар опирался на сплошную стенку сжимающегося внутреннего кольца — мягко ведём внутрь
                for (int bi = 0; bi < balls.Count; bi++)
                {
                    var b = balls[bi];
                    double dx = b.Pos.X - center.X, dy = b.Pos.Y - center.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double lim = arc0.Radius - b.R - arc0.HalfWidth - 0.02;
                    if (dist > lim && dist <= oldR0 + 1.2 && dist > 1e-6 && arc0.IsInSolid(Math.Atan2(dy, dx)))
                    {
                        double rx = dx / dist, ry = dy / dist;
                        b.Pos = new Vec2(center.X + rx * lim, center.Y + ry * lim);
                        double vr = b.Vel.X * rx + b.Vel.Y * ry;
                        if (vr > 0) b.Vel -= new Vec2(rx, ry) * (1.8 * vr);
                        if (lim < b.MinCenterDist) b.MinCenterDist = lim;
                    }
                }
            }
        }

        // Мягкое гашение избыточного разгона в сжимающейся спирали, чтобы шар
        // прыгал внутри центральной камеры, как на референсе, а не улетал ракетой
        if (index == 1 && arcs.Count > 0)
        {
            const double MaxSpiralV = 500.0;
            double damp = Math.Max(0.4, 1.0 - 4.0 * dt);
            for (int bi = 0; bi < balls.Count; bi++)
            {
                double vl = balls[bi].Vel.Len;
                if (vl > MaxSpiralV)
                    balls[bi].Vel *= (MaxSpiralV + (vl - MaxSpiralV) * damp) / vl;
            }
        }

        if (changed) UpdateProgress();
    }

    private void UpdateProgress()
    {
        int left = canvas.Sim.Arcs.Count;
        if (index == 0)
            hintLbl.Text = "  круг рассыпался — шар улетает в пустоту  ·  R — сыграть ещё";
        else if (left == 0)
            hintLbl.Text = "  все 200 кругов рассыпались — шар улетает в пустоту  ·  R — сыграть ещё";
        else
            hintLbl.Text = $"  кругов осталось: {left} из {SpiralCount}  ·  ЛКМ — бросок  ·  R — заново";
    }

    /// <summary>Гравитация режима «Ротор».</summary>
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
                ShowLevel(index);
                return true;
            case Keys.G:
                GravityOn = !GravityOn;
                return true;
            case Keys.Space:
                TogglePause();
                return true;
            case Keys.Control | Keys.Z:
                canvas.UndoBallPlacement();
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                SetBallRadius(canvas.NewBallR - 2);
                return true;
            case Keys.Oemplus:
            case Keys.Add:
                SetBallRadius(canvas.NewBallR + 2);
                return true;
        }
        return false;
    }
}

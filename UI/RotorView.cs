using System;
using System.Drawing;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet.UI;

/// <summary>
/// Режим «Ротор»: шар внутри неполных вращающихся кругов с вырезом.
/// При запуске любого уровня круги стоят на паузе, а шар не создаётся сам —
/// игрок кликом ЛКМ ставит шар с нулевой начальной скоростью (без выбора направления),
/// что сразу запускает вращение кругов.
/// </summary>
public class RotorView : UserControl, IModeView
{
    public const int LevelCount = 6;
    public const int SpiralCount = 200;
    public const double SpiralR0 = 70.0;
    public const double SpiralStep = 9.0;
    public const double SpiralGapDeg = 60.0;
    public const double SpiralTwistDeg = 3.0;

    private readonly GameCanvas canvas = new()
    {
        Editor = false,
        Dock = DockStyle.Fill,
        ShowGrid = false,
        Trails = false,
        RotorSkin = true,
        InstantDropNoAim = true,
        NewBallR = 10,
        NewBallArgb = unchecked((int)0xFFF0F3FF),
    };

    private int index;
    private Vec2 center = new(640, 360);
    private double curBaseR0 = SpiralR0;
    private int curTotalRings = 1;
    private bool curReverseOnBreak;
    private double curAccelOnBreak = 1.0;
    private bool waitingForBall;

    private readonly Label lblName = new()
    {
        AutoSize = false, Size = new Size(280, 34), ForeColor = Theme.Accent,
        Font = new Font("Segoe UI", 11.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly NeoButton btnPause = new() { Icon = IconPainter.Play, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton btnRestart = new() { Icon = IconPainter.Restart, Size = new Size(42, 38), IconPx = 20 };
    private readonly NeoButton chipFx = new()
    {
        Text = "FX", IsToggle = true, IsOn = true, Size = new Size(46, 38),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };
    private readonly BallColorButton colorBtn = new() { Selected = unchecked((int)0xFFF0F3FF) };
    private readonly SizeButton sizeBtn = new() { Min = 2, Max = 26, Value = 10 };
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
        tips.SetToolTip(btnPause, "Пауза / продолжить (пробел)");
        tips.SetToolTip(btnRestart, "Заново (R)");

        btnPrev.Click += (s, e) => ShowLevel((index - 1 + LevelCount) % LevelCount);
        btnNext.Click += (s, e) => ShowLevel((index + 1) % LevelCount);
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
        canvas.ClampDropPos = ClampToInnerChamber;
        canvas.BallPlaced += _ => OnBallPlaced();
        canvas.Simulated += OnSimulated;

        top.AddLeft(btnPrev).AddLeft(btnNext).AddLeft(lblName);
        top.AddRight(chipFx).AddRight(colorBtn).AddRight(sizeBtn).AddRight(btnRestart).AddRight(btnPause);

        Controls.Add(canvas);
        Controls.Add(hints);
        Controls.Add(top);

        ShowLevel(0);
    }

    public void SetActive(bool active) => canvas.SetLoopActive(active);

    /// <summary>Ограничивает точку постановки шара внутренней камерой текущего кольца.</summary>
    private Vec2 ClampToInnerChamber(Vec2 p)
    {
        if (canvas.Sim.Arcs.Count > 0)
        {
            var arc0 = canvas.Sim.Arcs[0];
            double maxDist = Math.Max(4.0, arc0.Radius - canvas.NewBallR - arc0.HalfWidth - 3.5);
            var d = p - center;
            double len = d.Len;
            if (len > maxDist && len > 1e-6)
                return center + d * (maxDist / len);
            return p;
        }
        return new Vec2(
            Math.Clamp(p.X, canvas.NewBallR + 6, LevelData.W - canvas.NewBallR - 6),
            Math.Clamp(p.Y, canvas.NewBallR + 6, LevelData.H - canvas.NewBallR - 6));
    }

    private void OnBallPlaced()
    {
        if (waitingForBall || canvas.Paused)
        {
            waitingForBall = false;
            canvas.Paused = false;
            canvas.SuppressPauseBanner = false;
            canvas.PromptTitle = null;
            canvas.PromptSub = null;
            btnPause.Icon = IconPainter.Pause;
            btnPause.Invalidate();
        }
        UpdateProgress();
    }

    private void SetBallRadius(double r)
    {
        double clamped = Math.Clamp(Math.Round(r), sizeBtn.Min, sizeBtn.Max);
        canvas.NewBallR = clamped;
        sizeBtn.Value = (int)clamped;
        foreach (var b in canvas.Sim.Balls) b.R = clamped;
        canvas.Invalidate();
    }

    /// <summary>Свежая геометрия уровня без предсозданных шаров (шар ставит сам игрок).</summary>
    private LevelData BuildLevel(int i, out Vec2 c, out double spinSpeed, out string startHint)
    {
        c = new Vec2(640, 360);
        curReverseOnBreak = false;
        curAccelOnBreak = 1.0;

        switch (i)
        {
            case 0: // 1. «ВЫХОД» — один большой вращающийся круг
            {
                curBaseR0 = 250.0;
                curTotalRings = 1;
                spinSpeed = -0.72;
                var lvl = new LevelData
                {
                    Name = "ВЫХОД",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                };
                lvl.Arcs.Add(new ArcData
                {
                    Center = c,
                    Radius = curBaseR0,
                    StartDeg = 35,
                    SweepDeg = 360 - 62,
                    HalfWidth = 2.25,
                });
                startHint = "  ЛКМ внутри круга — поставить шар (скорость 0) и запустить вращение  ·  R — заново";
                return lvl;
            }

            case 1: // 2. «200 КРУГОВ» — классическая плотная розовая спираль со счётчиком
            {
                curBaseR0 = SpiralR0;
                curTotalRings = SpiralCount;
                spinSpeed = 1.32;
                var lvl = new LevelData
                {
                    Name = "200 КРУГОВ",
                    Gravity = true,
                    GravityScale = 0.58,
                    ElasticBounce = true,
                };
                for (int k = 0; k < SpiralCount; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = SpiralR0 + k * SpiralStep,
                        StartDeg = 158.0 + k * SpiralTwistDeg,
                        SweepDeg = 360.0 - SpiralGapDeg,
                        HalfWidth = 2.25,
                    });
                }
                startHint = $"  ЛКМ в центре — поставить шар (скорость 0) и запустить спираль из {SpiralCount} кругов  ·  R — заново";
                return lvl;
            }

            case 2: // 3. «РЕВЕРС» — каждый пройденный круг меняет направление вращения всех остальных на противоположное
            {
                curBaseR0 = 76.0;
                curTotalRings = 30;
                curReverseOnBreak = true;
                spinSpeed = 1.15;
                var lvl = new LevelData
                {
                    Name = "РЕВЕРС",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                };
                for (int k = 0; k < curTotalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = curBaseR0 + k * 24.0,
                        StartDeg = 135.0 + k * 52.0,
                        SweepDeg = 360.0 - 68.0,
                        HalfWidth = 2.25,
                    });
                }
                startHint = "  каждый пройденный круг меняет направление вращения всех кругов на противоположное  ·  ЛКМ — старт";
                return lvl;
            }

            case 3: // 4. «ВСТРЕЧНЫЕ» — чётные и нечётные круги вращаются навстречу друг другу
            {
                curBaseR0 = 76.0;
                curTotalRings = 30;
                spinSpeed = 0.98;
                var lvl = new LevelData
                {
                    Name = "ВСТРЕЧНЫЕ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                };
                for (int k = 0; k < curTotalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = curBaseR0 + k * 24.0,
                        StartDeg = 90.0 + k * 43.0,
                        SweepDeg = 360.0 - 70.0,
                        HalfWidth = 2.25,
                        SpinMul = (k % 2 == 0) ? 1.0 : -1.0,
                    });
                }
                startHint = "  соседние круги вращаются в противоположные стороны  ·  ЛКМ в центре — поставить шар (скорость 0)";
                return lvl;
            }

            case 4: // 5. «РАЗГОН» — с каждым разрушенным кругом вращение ускоряется
            {
                curBaseR0 = 76.0;
                curTotalRings = 35;
                curAccelOnBreak = 1.045;
                spinSpeed = 0.48;
                var lvl = new LevelData
                {
                    Name = "РАЗГОН",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                };
                for (int k = 0; k < curTotalRings; k++)
                {
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = curBaseR0 + k * 22.0,
                        StartDeg = 120.0 + k * 47.0,
                        SweepDeg = 360.0 - 70.0,
                        HalfWidth = 2.25,
                    });
                }
                startHint = "  каждый пройденный круг ускоряет вращение остальных  ·  ЛКМ в центре — поставить шар (скорость 0)";
                return lvl;
            }

            default: // 6. «ВИХРЬ» — дифференциальные скорости и встречные потоки колец
            {
                curBaseR0 = 76.0;
                curTotalRings = 36;
                spinSpeed = 1.05;
                var lvl = new LevelData
                {
                    Name = "ВИХРЬ",
                    Gravity = true,
                    GravityScale = 0.60,
                    ElasticBounce = true,
                };
                for (int k = 0; k < curTotalRings; k++)
                {
                    double dir = (k % 2 == 0) ? 1.0 : -1.0;
                    double speedMul = 0.65 + (k % 5) * 0.22;
                    lvl.Arcs.Add(new ArcData
                    {
                        Center = c,
                        Radius = curBaseR0 + k * 22.0,
                        StartDeg = 150.0 + k * 37.0,
                        SweepDeg = 360.0 - 72.0,
                        HalfWidth = 2.25,
                        SpinMul = dir * speedMul,
                    });
                }
                startHint = "  каждое кольцо крутится со своей скоростью и направлением  ·  ЛКМ в центре — поставить шар";
                return lvl;
            }
        }
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

    /// <summary>Переключить уровень «Ротора» (0..5): круги на паузе, ожидание постановки шара игроком.</summary>
    public void ShowLevel(int i)
    {
        index = Math.Clamp(i, 0, LevelCount - 1);
        var lvl = BuildLevel(index, out var c, out double spinSpeed, out var startHint);
        center = c;
        canvas.SpinSpeed = spinSpeed;

        if (curTotalRings > 1)
        {
            canvas.HubCenter = center;
            canvas.HubRadius = curBaseR0 - 8;
            canvas.HubText = () => canvas.Sim.Arcs.Count > 0 ? canvas.Sim.Arcs.Count.ToString() : "";
        }
        else
        {
            canvas.HubCenter = null;
            canvas.HubText = null;
        }

        canvas.LoadLevel(lvl);

        // На старте шара нет, а все круги стоят на паузе до первого клика игрока
        waitingForBall = true;
        canvas.Paused = true;
        canvas.SuppressPauseBanner = true;
        canvas.PromptTitle = "КЛИКНИТЕ ЛКМ, ЧТОБЫ ПОСТАВИТЬ ШАР";
        canvas.PromptSub = "шар появится с нулевой скоростью и запустит вращение кругов";
        btnPause.Icon = IconPainter.Play;
        btnPause.Invalidate();

        lblName.Text = $"  {index + 1}/{LevelCount} · {lvl.Name}";
        hintLbl.Text = startHint;
        canvas.Invalidate();
    }

    /// <summary>Рассыпание пройденных кругов, модификаторы уровня и плавное сжатие спирали к центру.</summary>
    private void OnSimulated(double dt)
    {
        var arcs = canvas.Sim.Arcs;
        var balls = canvas.Sim.Balls;
        if (arcs.Count == 0) return;

        int broken = 0;
        while (arcs.Count > 0 && balls.Count > 0)
        {
            var arc0 = arcs[0];
            bool lastRing = arcs.Count == 1;
            bool escaped = false;
            for (int bi = 0; bi < balls.Count; bi++)
            {
                var b = balls[bi];
                if (b.MinCenterDist >= arc0.Radius - 2.0) continue; // шар должен побывать внутри кольца
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
            broken++;
        }

        if (broken > 0)
        {
            // «РЕВЕРС»: каждый пройденный круг меняет направление вращения всех остальных колец
            if (curReverseOnBreak)
            {
                if ((broken & 1) == 1)
                    canvas.SpinSpeed = -canvas.SpinSpeed;
                else
                    canvas.SpinSpeed = -canvas.SpinSpeed; // даже при редком двойном пробое даём явный реверс!
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }

            // «РАЗГОН»: плавное ускорение вращения с каждым разрушенным кольцом
            if (curAccelOnBreak > 1.0)
            {
                double sign = Math.Sign(canvas.SpinSpeed);
                double mag = Math.Abs(canvas.SpinSpeed) * Math.Pow(curAccelOnBreak, broken);
                canvas.SpinSpeed = (sign == 0 ? 1.0 : sign) * Math.Min(2.45, mag);
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            }
        }

        // В многокольцевых уровнях оставшиеся круги плавно смыкаются к радиусу curBaseR0,
        // сохраняя шаг между кольцами, чтобы вся серия по очереди входила в кадр.
        if (curTotalRings > 1 && arcs.Count > 0 && arcs[0].Radius > curBaseR0)
        {
            var arc0 = arcs[0];
            double minAllowedR0 = curBaseR0;
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

        // Мягкое гашение избыточного разгона в многокольцевых уровнях, чтобы шар
        // динамично прыгал внутри камеры и не улетал ракетой сквозь кадр
        if (curTotalRings > 1 && arcs.Count > 0)
        {
            const double MaxChamberV = 500.0;
            double damp = Math.Max(0.4, 1.0 - 4.0 * dt);
            for (int bi = 0; bi < balls.Count; bi++)
            {
                double vl = balls[bi].Vel.Len;
                if (vl > MaxChamberV)
                    balls[bi].Vel *= (MaxChamberV + (vl - MaxChamberV) * damp) / vl;
            }
        }

        if (broken > 0) UpdateProgress();
    }

    private void UpdateProgress()
    {
        int left = canvas.Sim.Arcs.Count;
        if (curTotalRings == 1)
        {
            hintLbl.Text = left == 0
                ? "  круг рассыпался — шар улетает в пустоту  ·  R — сыграть ещё  ·  ЛКМ — добавить шар"
                : "  вылетите через разрыв вращающегося круга  ·  ЛКМ — добавить шар (скорость 0)  ·  ПКМ — убрать  ·  R — заново";
            return;
        }

        if (left == 0)
        {
            hintLbl.Text = $"  все круги ({curTotalRings}) рассыпались — шар улетает в пустоту  ·  R — сыграть ещё";
            return;
        }

        string extra = curReverseOnBreak
            ? (canvas.SpinSpeed >= 0 ? "  ·  вращение: ↻ по часовой" : "  ·  вращение: ↺ против часовой")
            : curAccelOnBreak > 1.0
                ? $"  ·  скорость кругов: {Math.Abs(canvas.SpinSpeed):0.00} рад/с"
                : "";
        hintLbl.Text = $"  кругов осталось: {left} из {curTotalRings}{extra}  ·  ЛКМ — шар (скорость 0)  ·  R — заново";
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
        if (!waitingForBall)
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

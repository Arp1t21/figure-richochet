using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Ricochet.Core;

namespace Ricochet;

/// <summary>Численная самопроверка физики и сериализации (запуск: FigureRicochet --selftest).</summary>
public static class SelfTest
{
    public static int Run()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        int failed = 0;
        failed += Test("отскок от границ (Border=true) без потерь", () =>
        {
            var lvl = new LevelData { Border = true };
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(640, 360), new Vec2(900, 0), 12); // летит вправо
            for (int i = 0; i < 60 * 10; i++) sim.Step(1.0 / 60);
            if (Math.Abs(sim.Balls[0].Vel.Len - 900) > 1) return "скорость изменилась: " + sim.Balls[0].Vel.Len;
            var p = sim.Balls[0].Pos;
            if (p.X < 12 || p.X > LevelData.W - 12 || p.Y < 12 || p.Y > LevelData.H - 12) return "шар вылетел за поле: " + p;
            return null;
        });

        failed += Test("без границы улетевший шар удаляется", () =>
        {
            var sim = new Simulation(); // Border по умолчанию выключен
            sim.ResetFrom(new LevelData());
            sim.AddBall(new Vec2(640, 360), new Vec2(2000, 0), 12);
            for (int i = 0; i < 60 * 3; i++) sim.Step(1.0 / 60);
            if (sim.Balls.Count != 0) return "улетевший шар не удалён: " + sim.Balls[0].Pos;
            return null;
        });

        failed += Test("отскок от стенки-полилинии", () =>
        {
            var lvl = new LevelData(); // границ нет: держат только пол и потолок
            lvl.Walls.Add(new PolyData { Pts = new() { new(0, 600), new(1280, 600) } });   // пол
            lvl.Walls.Add(new PolyData { Pts = new() { new(0, 100), new(1280, 100) } });   // потолок
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(640, 300), new Vec2(0, 500), 12);
            double maxY = 0;
            for (int i = 0; i < 60 * 5; i++)
            {
                sim.Step(1.0 / 60);
                if (sim.Balls.Count == 0) return "шар исчез между полом и потолком";
                maxY = Math.Max(maxY, sim.Balls[0].Pos.Y);
            }
            if (maxY > 600 - 12 + 1) return "шар пробил пол: maxY=" + maxY.ToString("0.0");
            if (Math.Abs(sim.Balls[0].Vel.Len - 500) > 2) return "упругость потеряна: v=" + sim.Balls[0].Vel.Len;
            return null;
        });

        failed += Test("гравитация и фиксация финиша", () =>
        {
            var lvl = new LevelData { Gravity = true };
            lvl.Walls.Add(new PolyData { Pts = new() { new(0, 700), new(1280, 700) } });
            lvl.Goals.Add(new GoalData { Pts = new() { new(540, 640), new(740, 640), new(740, 700), new(540, 700) } });
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(640, 100), new Vec2(0, 0), 12);
            bool finished = false;
            for (int i = 0; i < 60 * 6 && !finished; i++) { sim.Step(1.0 / 60); finished = sim.Finished; }
            if (!finished) return "шар не коснулся цели за 6 секунд падения";
            return null;
        });

        failed += Test("столкновение шаров", () =>
        {
            var sim = new Simulation();
            sim.AddBall(new Vec2(500, 360), new Vec2(300, 0), 12);
            sim.AddBall(new Vec2(780, 360), new Vec2(-300, 0), 12);
            for (int i = 0; i < 60 * 2; i++) sim.Step(1.0 / 60);
            if (Math.Abs(sim.Balls[0].Vel.X - (-300)) > 1 || Math.Abs(sim.Balls[1].Vel.X - 300) > 1)
                return "обмен импульсом неверен: " + sim.Balls[0].Vel.X + ", " + sim.Balls[1].Vel.X;
            return null;
        });

        failed += Test("сохранение/загрузка уровня (JSON)", () =>
        {
            var lvl = BuiltinLevels.All()[3]; // «Квадрат»: есть и стенки, и цель
            string path = Path.Combine(Path.GetTempPath(), "ricochet_test.json");
            LevelStore.Save(lvl, path);
            var back = LevelStore.Load(path);
            if (back.Walls.Count != lvl.Walls.Count || back.Goals.Count != lvl.Goals.Count) return "потеряны объекты";
            if (back.Circles.Count != lvl.Circles.Count) return "потеряны круги";
            if (back.Walls[0].Pts[1].X != lvl.Walls[0].Pts[1].X) return "исказились координаты";
            var circ = BuiltinLevels.All()[0]; // «Свободный круг» — аналитический круг
            LevelStore.Save(circ, path);
            var back2 = LevelStore.Load(path);
            if (back2.Circles.Count != 1 || Math.Abs(back2.Circles[0].Radius - circ.Circles[0].Radius) > 0.001)
                return "круг не пережил JSON";
            return null;
        });

        failed += Test("все встроенные уровни валидны", () =>
        {
            int withGoals = 0, withoutGoals = 0;
            foreach (var l in BuiltinLevels.All())
            {
                if (l.Walls.Count == 0 && l.Circles.Count == 0) return "нет фигур: " + l.Name;
                if (l.Border) return "по умолчанию уровни должны быть без границы: " + l.Name;
                var sim = new Simulation();
                sim.ResetFrom(l);
                for (int i = 0; i < 60; i++) sim.Step(1.0 / 60);
                if (l.Goals.Count > 0) withGoals++; else withoutGoals++;
            }
            if (withoutGoals == 0) return "нет ни одного свободного уровня без целей";
            if (withGoals == 0) return "нет ни одного уровня с целью";
            return null;
        });

        failed += Test("рендеринг: кадры без ошибок GDI (в т.ч. повторных)", () =>
        {
            var canvas = new UI.GameCanvas { Size = new Size(1280, 720) };
            canvas.LoadLevel(BuiltinLevels.All()[0]);            // круг со стеной и целью
            canvas.Sim.AddBall(new Vec2(640, 360), new Vec2(300, 100), 12);
            canvas.Sim.Step(1.0 / 60);

            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic);
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));

            onPaint.Invoke(canvas, new object[] { pe });          // кадр 1 (режим уровней)
            onPaint.Invoke(canvas, new object[] { pe });          // кадр 2 — ловит disposed-ресурсы
            canvas.Editor = true;
            canvas.Design.Balls.Add(new BallData { Pos = new Vec2(200, 200), R = 12 });
            onPaint.Invoke(canvas, new object[] { pe });          // кадр в режиме правки
            canvas.Testing = true;
            canvas.RestartSim();
            onPaint.Invoke(canvas, new object[] { pe });          // кадр в режиме теста
            return null;
        });

        failed += Test("шейдеры: пост-обработка кадра (свечение, виньетка, зерно) без ошибок", () =>
        {
            using var bmp = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(128, 128, 128));
            UI.Fx.Apply(bmp, 0);
            UI.Fx.Apply(bmp, 123);
            double Avg(int x0, int y0)
            {
                double s = 0;
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    { var c = bmp.GetPixel(x0 + x, y0 + y); s += c.R + c.G + c.B; }
                return s / (32 * 32 * 3);
            }
            double corner = Avg(8, 8), center = Avg(624, 344);
            if (corner > center - 6) return $"виньетка не затемняет углы: угол {corner:0.0}, центр {center:0.0}";
            if (center < 90 || center > 165) return "пост-обработка исказила центр кадра: " + center.ToString("0.0");
            return null;
        });

        failed += Test("волновой фронт: физика (сетка стен) и программный рендер 1440 лучей", () =>
        {
            var canvas = new UI.GameCanvas { Size = new Size(1280, 720), WaveMode = true, WaveSource = new Vec2(640, 360) };
            var lvl = new LevelData();
            var poly = new PolyData { Closed = true };
            for (int i = 0; i < 96; i++)
            {
                double a = i * 2 * Math.PI / 96;
                poly.Pts.Add(new Vec2(640 + Math.Cos(a) * 430, 360 + Math.Sin(a) * 270));
            }
            lvl.Walls.Add(poly);
            canvas.LoadLevel(lvl);
            canvas.Sim.BallCollisions = false;
            for (int i = 0; i < 1440; i++)
            {
                double a = i * 2 * Math.PI / 1440;
                canvas.Sim.AddBall(new Vec2(640, 360), new Vec2(Math.Cos(a), Math.Sin(a)) * 320, 3, (int)(i * 360.0 / 1440));
            }
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic);
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));
            for (int i = 0; i < 30; i++)
            {
                canvas.Sim.Step(1.0 / 60);
                onPaint.Invoke(canvas, new object[] { pe });
            }
            if (canvas.Sim.Balls.Count < 1440) return "часть лучей потеряна: " + canvas.Sim.Balls.Count;
            return null;
        });

        failed += Test("цвет шара и треки: рендер без ошибок GDI", () =>
        {
            var canvas = new UI.GameCanvas { Size = new Size(1280, 720) };
            canvas.Trails = true;
            canvas.LoadLevel(BuiltinLevels.All()[0]);
            canvas.Sim.AddBall(new Vec2(640, 360), new Vec2(500, 260), 12, -1, unchecked((int)0xFFA680FF));
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic);
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));
            UI.GameCanvas.HaloOn = true;
            for (int i = 0; i < 30; i++)
            {
                canvas.Sim.Step(1.0 / 60);
                onPaint.Invoke(canvas, new object[] { pe });
            }
            UI.GameCanvas.HaloOn = false;
            return null;
        });

        failed += Test("волновой фронт: 39 фигур, источник внутри каждой, шаг без ошибок", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            if (figs.Count < 39) return "ожидалось не менее 39 фигур, есть " + figs.Count;
            foreach (var (lvl, src) in figs)
            {
                if (lvl.Walls.Count == 0 && lvl.Circles.Count == 0) return "нет фигур: " + lvl.Name;
                // чётно-нечётное правило: замкнутые контуры и круги должны содержать источник нечётное число раз
                int depth = 0;
                foreach (var c in lvl.Circles)
                    if (Vec2.Dist(src, c.Center) < c.Radius - 1) depth++;
                foreach (var w in lvl.Walls)
                    if (w.Closed && w.Pts.Count >= 3 && Simulation.PointInPoly(src, w.Pts)) depth++;
                if (depth % 2 == 0) return $"источник вне фигуры: {lvl.Name} ({src})";
                var sim = new Simulation();
                sim.ResetFrom(lvl);
                sim.BallCollisions = false;
                for (int i = 0; i < 24; i++)
                {
                    double a = i * 2 * Math.PI / 24;
                    sim.AddBall(src, new Vec2(Math.Cos(a), Math.Sin(a)) * 320, 3, i);
                }
                for (int i = 0; i < 60; i++) sim.Step(1.0 / 60);
            }
            return null;
        });

        failed += Test("волновой фронт: лучи r=2 не покидают фигуру (эллипс/стадион/квадрат)", () =>
        {
            foreach (var (name, lvl0) in WaveTestFigures())
            {
                var lvl = lvl0.Clone();
                lvl.NoBallCollisions = true; // как в приложении: лучи не сталкиваются между собой
                var sim = new Simulation();
                sim.ResetFrom(lvl);
                for (int i = 0; i < 360; i++)
                {
                    double a = i * 2 * Math.PI / 360 + 0.0037; // смещение — лучи под разными углами к граням
                    sim.AddBall(new Vec2(640, 360), new Vec2(Math.Cos(a), Math.Sin(a)) * 320, 2, i);
                }
                // переменный dt как в реальном приложении (пропуски кадров до 1/32 c)
                double[] dts = { 1.0 / 60, 1.0 / 45, 1.0 / 32, 1.0 / 60, 1.0 / 38 };
                for (long i = 0; i < 60 * 25; i++) sim.Step(dts[i % 5]);
                if (sim.Balls.Count != 360)
                    return $"{name}: потеряно лучей r=2 — {360 - sim.Balls.Count} шт. улетело за фигуру";
                foreach (var b in sim.Balls)
                {
                    if (b.Pos.X < 100 || b.Pos.X > 1180 || b.Pos.Y < 40 || b.Pos.Y > 680)
                        return $"{name}: луч r=2 вне фигуры: {b.Pos}";
                }
                Console.WriteLine($"      {name}: 360 лучей r=2 удержаны 20 c");
            }
            return null;
        });

        failed += Test("ротор (уровень 1 «ВЫХОД»): старт на паузе без шара, клик (v=0) запускает и разрушает круг", () =>
        {
            using var view = new UI.RotorView { Size = new Size(1172, 780) };
            var canvas = (UI.GameCanvas)typeof(UI.RotorView).GetField("canvas",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            canvas.Size = new Size(1280, 720);
            var onSim = typeof(UI.RotorView).GetMethod("OnSimulated",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));

            view.ShowLevel(0);
            if (canvas.Sim.Balls.Count != 0) return "шар не должен появляться сам при запуске Ротора";
            if (!canvas.Paused) return "при запуске Ротора круги должны быть на паузе";
            if (string.IsNullOrEmpty(canvas.PromptTitle)) return "нет приглашения поставить шар";
            onPaint.Invoke(canvas, new object[] { pe }); // рендер стартовой плашки

            var placed = canvas.PlaceInstantBall(new Vec2(610, 320));
            if (placed.Vel.Len != 0) return "шар должен появляться с 0 начальной скоростью: " + placed.Vel;
            if (canvas.Paused) return "после постановки шара пауза должна сняться";
            if (canvas.PromptTitle != null) return "плашка приглашения должна скрыться после клика";
            canvas.Sim.SpinSpeed = canvas.SpinSpeed;
            if (canvas.Sim.Arcs.Count != 1) return "ожидался 1 круг, есть " + canvas.Sim.Arcs.Count;

            bool shattered = false, ballGone = false;
            int peakShards = 0;
            for (int i = 0; i < 60 * 18; i++)
            {
                canvas.StepVisuals(1.0 / 60);
                canvas.Sim.Step(1.0 / 60);
                onSim.Invoke(view, new object[] { 1.0 / 60 });
                if (!shattered && canvas.Sim.Arcs.Count == 0)
                {
                    shattered = true;
                    peakShards = canvas.ActiveShardCount;
                    onPaint.Invoke(canvas, new object[] { pe }); // рендер разлетающихся осколков
                }
                if (i % 20 == 0) onPaint.Invoke(canvas, new object[] { pe });
                if (!ballGone && canvas.Sim.Balls.Count == 0) ballGone = true;
                if (shattered && ballGone && canvas.ActiveShardCount == 0) break;
            }
            if (!shattered) return "круг не рассыпался после выхода шара";
            if (peakShards < 30) return "нет анимации осколков: " + peakShards;
            if (!ballGone) return "шар не улетел в пустоту";
            if (canvas.ActiveShardCount != 0) return "осколки не догорели до конца: " + canvas.ActiveShardCount;
            if (canvas.Sim.Finished) return "Finished не должен выставляться в Роторе";
            return null;
        });

        failed += Test("ротор (уровень 2 «200 КРУГОВ»): с v=0 рассыпает все 200 колец и вылетает в пустоту", () =>
        {
            using var view = new UI.RotorView { Size = new Size(1172, 780) };
            var canvas = (UI.GameCanvas)typeof(UI.RotorView).GetField("canvas",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            canvas.Size = new Size(1280, 720);
            var onSim = typeof(UI.RotorView).GetMethod("OnSimulated",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));

            view.ShowLevel(1);
            if (canvas.Sim.Balls.Count != 0 || !canvas.Paused) return "старт 200 КРУГОВ должен быть на паузе без шара";
            if (canvas.Sim.Arcs.Count != 200) return "ожидалось 200 кругов, есть " + canvas.Sim.Arcs.Count;
            if (canvas.HubText?.Invoke() != "200") return "счётчик в центре должен показывать 200";

            canvas.PlaceInstantBall(new Vec2(634, 360));
            canvas.Sim.SpinSpeed = canvas.SpinSpeed;

            double maxDistWhileArcs = 0;
            bool midRendered = false;
            for (int i = 0; i < 60 * 55; i++)
            {
                canvas.StepVisuals(1.0 / 60);
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
                canvas.Sim.Step(1.0 / 60);
                onSim.Invoke(view, new object[] { 1.0 / 60 });
                if (canvas.Sim.Arcs.Count > 0 && canvas.Sim.Balls.Count > 0)
                    maxDistWhileArcs = Math.Max(maxDistWhileArcs, Vec2.Dist(canvas.Sim.Balls[0].Pos, new Vec2(640, 360)));
                if (!midRendered && canvas.Sim.Arcs.Count <= 175 && canvas.ActiveShardCount > 0)
                {
                    midRendered = true;
                    onPaint.Invoke(canvas, new object[] { pe });
                }
                if (canvas.Sim.Arcs.Count == 0 && canvas.Sim.Balls.Count == 0) break;
            }
            if (canvas.Sim.Arcs.Count != 0) return "остались нерассыпавшиеся кольца: " + canvas.Sim.Arcs.Count;
            if (canvas.Sim.Balls.Count != 0) return "после 200 колец шар должен улететь в пустоту";
            if (maxDistWhileArcs > 360) return $"шар уходил за пределы кадра до рассыпания колец: dist={maxDistWhileArcs:0.0}";
            if (canvas.Sim.Finished) return "Finished не должен выставляться в Роторе";
            Console.WriteLine($"      все 200 колец рассыпались в кадре с v=0 (maxDist={maxDistWhileArcs:0.0} px)");
            return null;
        });

        failed += Test("ротор (уровни 3–6, включая «РЕВЕРС»): смена направления вращения при прохождении круга", () =>
        {
            using var view = new UI.RotorView { Size = new Size(1172, 780) };
            var canvas = (UI.GameCanvas)typeof(UI.RotorView).GetField("canvas",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            canvas.Size = new Size(1280, 720);
            var onSim = typeof(UI.RotorView).GetMethod("OnSimulated",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Проверяем «РЕВЕРС» (индекс 2)
            view.ShowLevel(2);
            if (!canvas.Paused || canvas.Sim.Balls.Count != 0) return "РЕВЕРС должен стартовать на паузе без шара";
            int prevArcs = canvas.Sim.Arcs.Count;
            int prevSign = Math.Sign(canvas.SpinSpeed);
            canvas.PlaceInstantBall(new Vec2(640, 360));
            int flips = 0;
            for (int i = 0; i < 60 * 20 && flips < 4; i++)
            {
                canvas.Sim.SpinSpeed = canvas.SpinSpeed;
                canvas.Sim.Step(1.0 / 60);
                onSim.Invoke(view, new object[] { 1.0 / 60 });
                if (canvas.Sim.Arcs.Count < prevArcs)
                {
                    int newSign = Math.Sign(canvas.SpinSpeed);
                    if (newSign != -prevSign) return $"направление не сменилось: было {prevSign}, стало {newSign}";
                    prevSign = newSign;
                    prevArcs = canvas.Sim.Arcs.Count;
                    flips++;
                }
            }
            if (flips < 3) return "в РЕВЕРСЕ разрушилось слишком мало колец за 20 с: " + flips;

            // Проверяем остальные новые уровни (3..5)
            for (int lvlIdx = 3; lvlIdx < UI.RotorView.LevelCount; lvlIdx++)
            {
                view.ShowLevel(lvlIdx);
                if (!canvas.Paused || canvas.Sim.Balls.Count != 0) return $"уровень {lvlIdx} должен стартовать на паузе без шара";
                int startArcs = canvas.Sim.Arcs.Count;
                canvas.PlaceInstantBall(new Vec2(636, 360));
                for (int i = 0; i < 60 * 8; i++)
                {
                    canvas.Sim.SpinSpeed = canvas.SpinSpeed;
                    canvas.Sim.Step(1.0 / 60);
                    onSim.Invoke(view, new object[] { 1.0 / 60 });
                }
                if (canvas.Sim.Arcs.Count >= startArcs) return $"уровень {lvlIdx}: ни одно кольцо не пройдено за 8 с";
            }
            return null;
        });

        failed += Test("флаг «без столкновений шаров»", () =>
        {
            // выкл: шары пролетают сквозь друг друга без обмена импульсом
            var lvl = new LevelData { NoBallCollisions = true };
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(500, 360), new Vec2(400, 0), 12);
            sim.AddBall(new Vec2(724, 360), new Vec2(-400, 0), 12);
            for (int i = 0; i < 60 * 2; i++) sim.Step(1.0 / 60);
            if (Math.Abs(sim.Balls[0].Vel.X - 400) > 1) return "сквозной пролет нарушен: vx=" + sim.Balls[0].Vel.X;
            if (sim.Balls[0].Pos.X <= sim.Balls[1].Pos.X) return "шары не разошлись";

            // вкл (обычный уровень): упругий обмен при равных массах
            var sim2 = new Simulation();
            sim2.ResetFrom(new LevelData());
            sim2.AddBall(new Vec2(500, 360), new Vec2(400, 0), 12);
            sim2.AddBall(new Vec2(724, 360), new Vec2(-400, 0), 12);
            for (int i = 0; i < 60 * 2; i++) sim2.Step(1.0 / 60);
            if (Math.Abs(sim2.Balls[0].Vel.X - (-400)) > 1) return "обмен импульсом не произошёл: vx=" + sim2.Balls[0].Vel.X;
            return null;
        });

        failed += Test("стык двух стенок: одно отражение, шар не уходит под пол", () =>
        {
            // пол из двух сегментов с изломом на 20°; шар падает прямо на стык
            var lvl = new LevelData();
            lvl.Walls.Add(new PolyData
            {
                Pts = new() { new(140, 300), new(640, 300), new(1140, 245) },
            });
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(628, 100), new Vec2(30, 260), 10);
            double minVy = 0;
            for (int i = 0; i < 60 * 2; i++)
            {
                sim.Step(1.0 / 60);
                if (sim.Balls.Count == 0) return "шар исчез";
                if (sim.Balls[0].Pos.Y > 300 + 2) return "шар оказался под полом: " + sim.Balls[0].Pos;
                minVy = Math.Min(minVy, sim.Balls[0].Vel.Y);
            }
            if (minVy > -100) return "шар не отразился от стыка вверх: vy=" + minVy.ToString("0");
            if (Math.Abs(sim.Balls[0].Vel.Len - Math.Sqrt(30.0 * 30 + 260.0 * 260)) > 2)
                return "скорость не сохранилась";
            return null;
        });

        failed += Test("круг-64: симметрия отражений сохраняет лучи внутри", () =>
        {
            // круг r=330 в центре (640,360); 180 лучей из центра, без взаимных столкновений
            var lvl = new LevelData { NoBallCollisions = true };
            var wall = new PolyData { Closed = true };
            for (int i = 0; i < 64; i++)
            {
                double a = i * 2 * Math.PI / 64;
                wall.Pts.Add(new Vec2(640 + Math.Cos(a) * 330, 360 + Math.Sin(a) * 330));
            }
            lvl.Walls.Add(wall);
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            for (int i = 0; i < 180; i++)
            {
                double a = i * 2 * Math.PI / 180;
                sim.AddBall(new Vec2(640, 360), new Vec2(Math.Cos(a), Math.Sin(a)) * 320, 4, i * 2);
            }
            for (int i = 0; i < 60 * 5; i++) sim.Step(1.0 / 60);
            if (sim.Balls.Count != 180) return "часть лучей потеряна: " + sim.Balls.Count;
            foreach (var b in sim.Balls)
                if (Vec2.Dist(b.Pos, new Vec2(640, 360)) > 330 - 3) return "луч вышел за круг: " + b.Pos;
            return null;
        });

        failed += Test("аналитический круг: луч из центра точно возвращается в центр", () =>
        {
            var lvl = new LevelData();
            lvl.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 330 });
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(640, 360), new Vec2(1, 0.3).Norm() * 320, 4);
            // мелкий шаг замера: шар пролетает центр между точками выборки, поэтому dt = 1/600
            double minDist = double.MaxValue;
            for (int i = 0; i < 600 * 6; i++)
            {
                sim.Step(1.0 / 600);
                if (i >= 900) minDist = Math.Min(minDist, Vec2.Dist(sim.Balls[0].Pos, new Vec2(640, 360)));
            }
            if (minDist > 1.2) return "луч не вернулся в центр: мин. расстояние " + minDist.ToString("0.000");
            if (Math.Abs(sim.Balls[0].Vel.Len - 320) > 0.5) return "скорость изменилась";
            return null;
        });

        failed += Test("сохранение энергии отскоков под гравитацией (45 секунд в кольце без просадки на дно)", () =>
        {
            var lvl = new LevelData { Gravity = true, GravityScale = 0.60, ElasticBounce = true };
            lvl.Arcs.Add(new ArcData
            {
                Center = new Vec2(640, 360),
                Radius = 220,
                StartDeg = 0,
                SweepDeg = 359.9, // замкнутое кольцо без выхода — чистая проверка 45 секунд отскоков
                HalfWidth = 2.25,
            });
            var sim = new Simulation();
            sim.ResetFrom(lvl);
            sim.AddBall(new Vec2(595, 290), new Vec2(0, 0), 10);

            double earlyPeakV = 0, earlyMinY = double.MaxValue;
            double latePeakV = 0, lateMinY = double.MaxValue;
            for (int i = 0; i < 60 * 45; i++)
            {
                sim.Step(1.0 / 60);
                var b = sim.Balls[0];
                if (i < 60 * 5)
                {
                    earlyPeakV = Math.Max(earlyPeakV, b.Vel.Len);
                    earlyMinY = Math.Min(earlyMinY, b.Pos.Y);
                }
                else if (i >= 60 * 40)
                {
                    latePeakV = Math.Max(latePeakV, b.Vel.Len);
                    lateMinY = Math.Min(lateMinY, b.Pos.Y);
                }
            }
            if (latePeakV < earlyPeakV * 0.98)
                return $"скорость затухла за 45 с: было {earlyPeakV:0.0}, стало {latePeakV:0.0}";
            if (lateMinY > 330)
                return $"шар опустился на дно за 45 с: minY={lateMinY:0.0} (ранний {earlyMinY:0.0})";
            return null;
        });

        failed += Test("ротор: пауза вращения кругов при прохождении шара через пустое место", () =>
        {
            var lvl = new LevelData { Gravity = true, GravityScale = 0.60, ElasticBounce = true };
            lvl.Arcs.Add(new ArcData
            {
                Center = new Vec2(640, 360),
                Radius = 180,
                StartDeg = 30,     // вырез в районе 330°..30° (справа, около 0 рад)
                SweepDeg = 300,
                HalfWidth = 2.25,
            });
            var sim = new Simulation { SpinSpeed = 1.6 };
            sim.ResetFrom(lvl);

            // 1. Шар прямо в пустом месте кольца (угол 0 рад, радиус 180)
            var b = sim.AddBall(new Vec2(640 + 180, 360), new Vec2(120, 0), 10);
            double radBefore = sim.Arcs[0].StartRad;
            sim.Step(1.0 / 60);
            if (!sim.IsSpinFrozenInGap)
                return "вращение не встало на паузу в вырезе";
            if (Math.Abs(sim.Arcs[0].StartRad - radBefore) > 1e-9)
                return $"круг повернулся, пока шар в вырезе: delta={Math.Abs(sim.Arcs[0].StartRad - radBefore)}";

            // 2. Шар глубоко внутри камеры — круги снова вращаются
            b.Pos = new Vec2(640, 360);
            b.Vel = new Vec2(0, 80);
            radBefore = sim.Arcs[0].StartRad;
            sim.Step(1.0 / 60);
            if (sim.IsSpinFrozenInGap)
                return "вращение не возобновилось внутри камеры";
            if (Math.Abs(sim.Arcs[0].StartRad - radBefore) < 1e-4)
                return "круг не повернулся, когда шар вне выреза";
            return null;
        });

        failed += Test("непрерывная волна: подстановка лучей при растяжении фронта и дифракция в вершинах", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            // «КРУГ+ПАЛКА» (свободные концы перегородки) и «РАМКА» (внутренний квадрат с углами 270°)
            var stickFig = figs.First(f => f.Level.Name == "КРУГ+ПАЛКА");
            var wf = new WaveFrontSim();
            wf.ResetFrom(stickFig.Level, stickFig.Source);
            wf.EmitPointSource(stickFig.Source, initialRays: 96, speed: 320, spectrum: true, fixedArgb: 0);
            int startNodes = wf.NodeCount;

            for (int i = 0; i < 60 * 2; i++)
                wf.Step(1.0 / 60);

            if (wf.NodeCount <= startNodes * 3)
                return $"фронт не уплотнился при растяжении: было {startNodes}, стало {wf.NodeCount}";
            if (wf.DiffractionCount < 2)
                return $"не возникли дифракционные волны на концах перегородки: count={wf.DiffractionCount}";
            for (int i = 0; i < wf.NodeCount; i++)
            {
                if (wf.Nodes[i].Alive == 0) continue;
                var p = wf.Nodes[i].Pos;
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || Vec2.Dist(p, new Vec2(640, 360)) > 331.0)
                    return $"луч непрерывной волны вышел за круг: {p}";
            }

            // Проверяем рендер непрерывной волны в GameCanvas без ошибок
            using var canvas = new UI.GameCanvas { Size = new Size(1280, 720), WaveMode = true, UseWaveFront = true, WaveSource = stickFig.Source };
            canvas.LoadLevel(stickFig.Level);
            canvas.WaveFront.ResetFrom(stickFig.Level, stickFig.Source);
            canvas.WaveFront.EmitPointSource(stickFig.Source, 120, 320, true, 0);
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));
            for (int i = 0; i < 20; i++)
            {
                canvas.WaveFront.Step(1.0 / 60);
                onPaint.Invoke(canvas, new object[] { pe });
            }
            return null;
        });

        failed += Test("волновой фронт: 4 параболы — круглый фронт выпрямляется и на миг становится квадратным", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            var parabolaFig = figs.FirstOrDefault(f => f.Level.Name == UI.WaveView.SquareFrontName);
            if (parabolaFig.Level == null) return "фигура '" + UI.WaveView.SquareFrontName + "' не найдена";

            double a = UI.WaveView.ParabolaVertexDist;
            var wf = new WaveFrontSim();
            wf.ResetFrom(parabolaFig.Level, parabolaFig.Source);
            wf.EmitPointSource(parabolaFig.Source, 360, 320, true, 0);

            // Момент образования квадрата: t = 2√2·a / 320
            double tSq = 2.0 * Math.Sqrt(2.0) * a / 320.0;
            double dt = 1.0 / 240.0;
            int steps = (int)Math.Round(tSq / dt);
            for (int s = 0; s < steps; s++)
                wf.Step(dt);

            double expectedHalf = (2.0 * Math.Sqrt(2.0) - 2.0) * a;
            int nodeCount = 0;
            double maxDev = 0;
            for (int i = 0; i < wf.NodeCount; i++)
            {
                ref var nd = ref wf.Nodes[i];
                if (nd.Alive == 0 || nd.Gen > 0) continue;
                double ch = Math.Max(Math.Abs(nd.Pos.X - parabolaFig.Source.X), Math.Abs(nd.Pos.Y - parabolaFig.Source.Y));
                double dev = Math.Abs(ch - expectedHalf);
                if (dev > maxDev) maxDev = dev;
                nodeCount++;
            }

            if (nodeCount < 200)
                return $"слишком мало узлов на фронте квадрата: {nodeCount}";
            if (maxDev > 2.5)
                return $"фронт отклонился от идеального квадрата: максимальное отклонение {maxDev:0.2} px (ожидалось <= 2.5 px)";

            return null;
        });

        failed += Test("волновой фронт: семейство N-парабол (3, 6, 8, 12) — правильные многоугольники и возврат в фокус", () =>
        {
            var figs = UI.WaveView.BuildFigures();

            // 1. «6 ПАРАБОЛ»: проверяем, что в момент t = 4*a/320 фронт строго фокусируется обратно в центр
            var hexFig = figs.FirstOrDefault(f => f.Level.Name == "6 ПАРАБОЛ");
            if (hexFig.Level == null) return "фигура '6 ПАРАБОЛ' не найдена";

            var wf6 = new WaveFrontSim();
            wf6.ResetFrom(hexFig.Level, hexFig.Source);
            wf6.EmitPointSource(hexFig.Source, 240, 320, true, 0);
            double tBack6 = 4.0 * 290.0 / 320.0; // 3.625 с
            double dt = 1.0 / 240.0;
            int steps6 = (int)Math.Round(tBack6 / dt);
            for (int s = 0; s < steps6; s++) wf6.Step(dt);

            double avgR6 = 0; int cnt6 = 0;
            for (int i = 0; i < wf6.NodeCount; i++)
            {
                ref var nd = ref wf6.Nodes[i];
                if (nd.Alive == 0 || nd.Gen > 0) continue;
                avgR6 += Vec2.Dist(nd.Pos, hexFig.Source);
                cnt6++;
            }
            if (cnt6 < 40) return $"слишком мало узлов в '6 ПАРАБОЛ': {cnt6}";
            avgR6 /= cnt6;
            if (avgR6 > 28.0) return $"фронт в '6 ПАРАБОЛ' не сфокусировался в центр: средний радиус {avgR6:0.1} px (ожидалось <= 28 px)";

            // 2. Проверяем наличие фигур 3, 5, 8 и 12 парабол
            string[] names = { "3 ПАРАБОЛЫ", "5 ПАРАБОЛ", "8 ПАРАБОЛ", "12 ПАРАБОЛ" };
            foreach (var n in names)
            {
                var fig = figs.FirstOrDefault(f => f.Level.Name == n);
                if (fig.Level == null) return $"фигура '{n}' не найдена";
                if (fig.Level.Walls.Count == 0 || fig.Level.Walls[0].Pts.Count < 100)
                    return $"некорректная геометрия фигуры '{n}'";
            }
            return null;
        });

        failed += Test("волновой фронт: фокусы эллипса — периодический бифокальный перенос F1 -> F2 -> F1", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            var ellFig = figs.FirstOrDefault(f => f.Level.Name == "ФОКУСЫ ЭЛЛИПСА");
            if (ellFig.Level == null) return "фигура 'ФОКУСЫ ЭЛЛИПСА' не найдена";

            double A = 440, B = 280;
            double f = Math.Sqrt(A * A - B * B);
            var F1 = new Vec2(640 - f, 360);
            var F2 = new Vec2(640 + f, 360);

            var wf = new WaveFrontSim();
            wf.ResetFrom(ellFig.Level, F1);
            wf.EmitPointSource(F1, 240, 320, true, 0);

            // Путь F1 -> стенка -> F2 равен точно 2*A = 880 px! Время t = 880 / 320 = 2.75 с
            double tF2 = 2.0 * A / 320.0;
            double dt = 1.0 / 240.0;
            int steps = (int)Math.Round(tF2 / dt);
            for (int s = 0; s < steps; s++) wf.Step(dt);

            double avgDistF2 = 0; int cnt = 0;
            for (int i = 0; i < wf.NodeCount; i++)
            {
                ref var nd = ref wf.Nodes[i];
                if (nd.Alive == 0 || nd.Gen > 0) continue;
                avgDistF2 += Vec2.Dist(nd.Pos, F2);
                cnt++;
            }
            if (cnt < 40) return $"слишком мало узлов в 'ФОКУСЫ ЭЛЛИПСА': {cnt}";
            avgDistF2 /= cnt;
            if (avgDistF2 > 25.0)
                return $"волна не сошлась в правый фокус F2: среднее расстояние {avgDistF2:0.1} px (ожидалось <= 25 px)";

            return null;
        });

        failed += Test("физика дифракции: нет ложного распыления 360° на углах и дифракция только в тень", () =>
        {
            // В выпуклых фигурах (квадрат, прямоугольник) все внутренние углы <= 180° —
            // волна отражается чисто зеркально, дифракционных вершин быть НЕ должно
            var figs = UI.WaveView.BuildFigures();
            var squareFig = figs.First(f => f.Level.Name == "КВАДРАТ");
            var wfSquare = new WaveFrontSim();
            wfSquare.ResetFrom(squareFig.Level, squareFig.Source);
            wfSquare.EmitPointSource(squareFig.Source, 120, 320, true, 0);
            for (int i = 0; i < 60; i++) wfSquare.Step(1.0 / 60.0);
            if (wfSquare.DiffractionCount != 0)
                return $"на углах квадрата возникла дифракция ({wfSquare.DiffractionCount} шт.), хотя угол <= 180° должен давать только зеркальное отражение";

            // В фигуре с тонкой перегородкой («КРУГ+ПАЛКА») дифракция возникает на свободных концах
            // и уходит строго в полуплоскость тени (Y <= 300), не распыляясь назад к источнику на 360°
            var stickFig = figs.First(f => f.Level.Name == "КРУГ+ПАЛКА");
            var wfStick = new WaveFrontSim();
            wfStick.ResetFrom(stickFig.Level, stickFig.Source);
            wfStick.EmitPointSource(stickFig.Source, 120, 320, true, 0);
            for (int i = 0; i < 68; i++) wfStick.Step(1.0 / 60.0);
            if (wfStick.DiffractionCount < 2)
                return $"на концах перегородки не возникли дифракционные волны: {wfStick.DiffractionCount}";

            for (int i = 0; i < wfStick.NodeCount; i++)
            {
                ref var nd = ref wfStick.Nodes[i];
                if (nd.Alive == 0 || nd.Gen == 0) continue;
                // Все лучи первого поколения дифракции за перегородкой должны распространяться в область тени (Y <= 305)
                if (nd.Pos.Y > 305 && nd.Vel.Y > 50)
                    return $"дифракционный луч летит обратно в освещённую зону (Y={nd.Pos.Y:0.0}, vy={nd.Vel.Y:0.0})";
            }
            return null;
        });

        failed += Test("меню-оверлей слева: открывается поверх игры и шапки без смещения кнопок UI", () =>
        {
            using var form = new UI.MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();

            var hostField = typeof(UI.MainForm).GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var sidebarField = typeof(UI.MainForm).GetField("sidebar", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var host = (Panel)hostField.GetValue(form)!;
            var sidebar = (Panel)sidebarField.GetValue(form)!;
            var toolbar = form.ActiveView.Controls.OfType<UI.Toolbar>().First();
            var btnPositionsBefore = toolbar.Controls.Cast<Control>().Select(c => c.Bounds).ToArray();
            var hostBoundsBefore = host.Bounds;

            if (form.IsMenuOpen) return "меню должно быть скрыто при старте";

            form.SetMenuOpen(true);
            Application.DoEvents();
            if (!form.IsMenuOpen) return "меню не открылось";
            if (form.Controls.GetChildIndex(sidebar) != 0) return "оверлей меню не поверх остальных контролов (Z-index != 0)";
            if (host.Bounds != hostBoundsBefore)
                return $"игровое поле сдвинулось при открытии меню: {hostBoundsBefore} -> {host.Bounds}";

            var btnPositionsAfter = toolbar.Controls.Cast<Control>().Select(c => c.Bounds).ToArray();
            for (int i = 0; i < btnPositionsBefore.Length; i++)
                if (btnPositionsBefore[i] != btnPositionsAfter[i])
                    return $"кнопка {i} в верхней шапке сдвинулась: {btnPositionsBefore[i]} -> {btnPositionsAfter[i]}";

            form.SetMenuOpen(false);
            Application.DoEvents();
            if (form.IsMenuOpen) return "меню не закрылось";
            return null;
        });

        failed += Test("редактор: примитивы всех режимов, выделение, копирование, удаление и Undo/Redo", () =>
        {
            using var ev = new UI.EditorView { Size = new Size(1360, 780) };
            var c = ev.Canvas;

            // Генератор колец Ротора в разных режимах вращения
            ev.GenerateSpiral(5, 110, 20, 60, 22, SpiralSpinPattern.Alternating, replaceExisting: true, enableRotorRules: true);
            if (c.Design.Arcs.Count != 5) return $"ожидалось 5 колец спирали, получено {c.Design.Arcs.Count}";
            if (Math.Abs(c.Design.Arcs[0].SpinMul - 1.0) > 1e-6 || Math.Abs(c.Design.Arcs[1].SpinMul + 1.0) > 1e-6)
                return "неверные множители встречного вращения (Alternating)";

            ev.GenerateSpiral(4, 100, 18, 55, 15, SpiralSpinPattern.Vortex, replaceExisting: true, enableRotorRules: true);
            if (c.Design.Arcs.Count != 4) return $"ожидалось 4 кольца вихря, получено {c.Design.Arcs.Count}";

            // Добавляем все виды геометрических объектов
            var circle = new CircleData { Center = new Vec2(300, 360), Radius = 75 };
            c.PushUndo();
            c.Design.Circles.Add(circle);

            var hexWall = new PolyData { Closed = true, Pts = LevelShapes.RegularPolygonPts(new Vec2(960, 220), 60, 6, 0) };
            var starWall = new PolyData { Closed = true, Pts = LevelShapes.StarPolygonPts(new Vec2(960, 500), 65, 5, 0.42, 0), Pivot = new Vec2(960, 500) };
            c.PushUndo();
            c.Design.Walls.Add(hexWall);
            c.Design.Walls.Add(starWall);
            if (hexWall.Pts.Count != 6 || starWall.Pts.Count != 10)
                return $"неверное число вершин многоугольника ({hexWall.Pts.Count}) или звезды ({starWall.Pts.Count})";

            var goal = new GoalData { Pts = LevelShapes.RegularPolygonPts(new Vec2(220, 200), 48, 5, 0) };
            c.PushUndo();
            c.Design.Goals.Add(goal);

            var ball = new BallData { Pos = new Vec2(500, 360), R = 12, HasVel = true, Vel = new Vec2(450, -150) };
            c.PushUndo();
            c.Design.Balls.Add(ball);

            // Дублирование, центрирование и удаление
            c.SelectObject(c.Design.Arcs[0]);
            if (!c.DuplicateSelected() || c.Design.Arcs.Count != 5) return "кольцо Ротора не продублировалось";
            c.SelectObject(circle);
            if (!c.CenterSelected() || (circle.Center - new Vec2(640, 360)).Len > 1e-6) return "круг не отцентрировался";
            if (!c.DeleteSelected() || c.Design.Circles.Count != 0) return "круг не удалился";

            // Undo восстанавливает удалённый круг, Redo снова удаляет
            c.DoUndo();
            if (c.Design.Circles.Count != 1) return "Undo не восстановил удалённый круг";
            c.DoRedo();
            if (c.Design.Circles.Count != 0) return "Redo не повторил удаление круга";

            // Полная очистка и её отмена
            c.ClearAll();
            if (c.Design.Arcs.Count != 0 || c.Design.Walls.Count != 0 || c.Design.Goals.Count != 0 || c.Design.Balls.Count != 0)
                return "ClearAll оставил объекты на поле";
            c.DoUndo();
            if (c.Design.Arcs.Count != 5 || c.Design.Walls.Count != 2 || c.Design.Goals.Count != 1 || c.Design.Balls.Count != 1)
                return "Undo после ClearAll не вернул сцену целиком";
            return null;
        });

        failed += Test("универсальный JSON уровня: полное сохранение и загрузка всех механик Ротора и Волнового фронта", () =>
        {
            var src = new LevelData
            {
                Name = "Полный тест JSON",
                Hint = "Проверка сериализации всех режимов",
                Border = false,
                Gravity = true,
                GravityScale = 0.62,
                ElasticBounce = true,
                NoBallCollisions = true,
                SpinSpeed = -1.35,
                FreezeSpinInGap = false,
                RotorRules = true,
                InstantDrop = true,
                ContractRings = false,
                ReverseOnBreak = true,
                AccelOnBreak = 1.08,
                RotorBaseR0 = 132.0,
                WaveEmitter = true,
                WaveSourcePos = new Vec2(512, 404),
                WaveContinuous = false,
                WaveRays = 1440,
                WaveSpeed = 410,
                WaveRayRadius = 4,
                WaveArgb = unchecked((int)0xFF40E18C),
            };
            src.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 290 });
            src.Arcs.Add(new ArcData { Center = new Vec2(640, 360), Radius = 132, StartDeg = 42, SweepDeg = 295, HalfWidth = 3.2, SpinMul = -1.5 });
            src.Walls.Add(new PolyData { Closed = true, Pts = LevelShapes.StarPolygonPts(new Vec2(900, 360), 50, 5, 0.45, 0), Pivot = new Vec2(900, 360) });
            src.Goals.Add(new GoalData { Pts = LevelShapes.RegularPolygonPts(new Vec2(320, 360), 40, 4, 0) });
            src.Balls.Add(new BallData { Pos = new Vec2(640, 340), R = 10, Argb = unchecked((int)0xFFFFB058), HasVel = true, Vel = new Vec2(300, -200) });

            string tmp = Path.Combine(Path.GetTempPath(), $"ricochet_universal_{Guid.NewGuid():N}.json");
            try
            {
                LevelStore.Save(src, tmp);
                var dst = LevelStore.Load(tmp);
                if (dst.Name != src.Name || dst.Hint != src.Hint) return "не совпали Name/Hint";
                if (dst.Border != src.Border || dst.Gravity != src.Gravity || Math.Abs(dst.GravityScale - src.GravityScale) > 1e-6)
                    return "не совпали флаги физики";
                if (dst.ElasticBounce != src.ElasticBounce || dst.NoBallCollisions != src.NoBallCollisions)
                    return "не совпали ElasticBounce/NoBallCollisions";
                if (Math.Abs(dst.SpinSpeed - src.SpinSpeed) > 1e-6 || dst.FreezeSpinInGap != src.FreezeSpinInGap)
                    return "не совпали SpinSpeed/FreezeSpinInGap";
                if (!dst.RotorRules || !dst.InstantDrop || dst.ContractRings != src.ContractRings || !dst.ReverseOnBreak || Math.Abs(dst.AccelOnBreak - 1.08) > 1e-6)
                    return "не совпали параметры Ротора";
                if (!dst.WaveEmitter || (dst.WaveSourcePos - src.WaveSourcePos).Len > 1e-6 || dst.WaveContinuous != src.WaveContinuous ||
                    dst.WaveRays != 1440 || Math.Abs(dst.WaveSpeed - 410) > 1e-6 || Math.Abs(dst.WaveRayRadius - 4) > 1e-6 || dst.WaveArgb != src.WaveArgb)
                    return "не совпали параметры Волнового фронта";
                if (dst.Arcs.Count != 1 || Math.Abs(dst.Arcs[0].SpinMul + 1.5) > 1e-6 || Math.Abs(dst.Arcs[0].HalfWidth - 3.2) > 1e-6)
                    return "не совпали параметры ArcData";
                if (dst.Walls.Count != 1 || !dst.Walls[0].Pivot.HasValue || (dst.Walls[0].Pivot.Value - new Vec2(900, 360)).Len > 1e-6)
                    return "не сохранился локальный Pivot стены";
                return null;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        });

        failed += Test("редактор ТЕСТ + УРОВНИ (PlayView): авторский уровень Ротора (v=0, реверс, разгон, сжатие)", () =>
        {
            var customRotor = new LevelData
            {
                Name = "Авторский Ротор",
                Border = false,
                Gravity = true,
                GravityScale = 0.72,
                ElasticBounce = true,
                SpinSpeed = 1.25,
                FreezeSpinInGap = true,
                RotorRules = true,
                InstantDrop = true,
                ContractRings = true,
                ReverseOnBreak = true,
                AccelOnBreak = 1.06,
                RotorBaseR0 = 110.0,
            };
            // Вырез первого кольца внизу (90° — биссектриса щели): StartDeg = 135°, SweepDeg = 270° => щель [45°..135°] внизу!
            customRotor.Arcs.Add(new ArcData { Center = new Vec2(640, 360), Radius = 110, StartDeg = 135, SweepDeg = 270, HalfWidth = 2.25, SpinMul = 1.0 });
            customRotor.Arcs.Add(new ArcData { Center = new Vec2(640, 360), Radius = 140, StartDeg = 200, SweepDeg = 290, HalfWidth = 2.25, SpinMul = 1.0 });
            customRotor.Arcs.Add(new ArcData { Center = new Vec2(640, 360), Radius = 170, StartDeg = 260, SweepDeg = 290, HalfWidth = 2.25, SpinMul = 1.0 });

            static string VerifyRotorRun(UI.GameCanvas canvas, string where)
            {
                if (!canvas.Paused || string.IsNullOrEmpty(canvas.PromptTitle))
                    return $"{where}: уровень с InstantDrop должен стартовать на паузе с подсказкой";
                if (canvas.HubText?.Invoke() != "3")
                    return $"{where}: ожидался счётчик колец '3', получено '{canvas.HubText?.Invoke()}'";

                var b = canvas.PlaceInstantBall(new Vec2(640, 360));
                if (canvas.Paused || canvas.PromptTitle != null)
                    return $"{where}: клик шара не снял стартовую паузу";
                if (b.Vel.Len > 1e-6)
                    return $"{where}: начальная скорость шара должна быть 0, получено {b.Vel.Len}";

                bool brokeFirst = false;
                for (int i = 0; i < 240; i++)
                {
                    canvas.AdvanceFixedStep(1.0 / 60.0);
                    if (canvas.Sim.Arcs.Count == 2)
                    {
                        brokeFirst = true;
                        break;
                    }
                }
                if (!brokeFirst) return $"{where}: шар не пробил внутреннее кольцо через нижний вырез";
                if (canvas.ActiveShardCount <= 0) return $"{where}: распад кольца не породил осколки";
                if (canvas.HubText?.Invoke() != "2") return $"{where}: счётчик в центре не обновился до '2' ({canvas.HubText?.Invoke()})";
                if (canvas.SpinSpeed >= 0 || Math.Abs(canvas.SpinSpeed + 1.25 * 1.06) > 0.02)
                    return $"{where}: не сработали ReverseOnBreak + AccelOnBreak (SpinSpeed={canvas.SpinSpeed:0.000})";

                // Докручиваем ещё несколько кадров, проверяя плавное сжатие оставшихся колец к базовому радиусу 110
                double rBefore = canvas.Sim.Arcs[0].Radius;
                for (int i = 0; i < 20; i++) canvas.AdvanceFixedStep(1.0 / 60.0);
                if (canvas.Sim.Arcs[0].Radius >= rBefore - 0.5)
                    return $"{where}: внешнее кольцо не сжимается внутрь после пробоя ({rBefore:0.1} -> {canvas.Sim.Arcs[0].Radius:0.1})";
                return null;
            }

            // 1. Запуск через «ТЕСТ» в редакторе и возврат в редактирование
            using (var ev = new UI.EditorView { Size = new Size(1360, 780) })
            {
                ev.LoadTemplate(customRotor);
                ev.ToggleTest();
                var err = VerifyRotorRun(ev.Canvas, "EditorView.ТЕСТ");
                if (err != null) return err;
                ev.ToggleTest(); // СТОП
                if (ev.Canvas.Testing) return "EditorView не вышел из режима ТЕСТ";
                if (ev.Canvas.Design.Arcs.Count != 3) return "после ТЕСТа в редакторе потерялись исходные кольца Design.Arcs";
            }

            // 2. Тот же самый пользовательский уровень в режиме «УРОВНИ» (PlayView)
            using (var pv = new UI.PlayView { Size = new Size(1360, 780) })
            {
                pv.LoadCustomLevel(customRotor.Clone());
                var err = VerifyRotorRun(pv.Canvas, "PlayView.УРОВНИ");
                if (err != null) return err;
            }
            return null;
        });

        failed += Test("редактор ТЕСТ + УРОВНИ (PlayView): авторский бильярд Волнового фронта с дугой и дифракцией", () =>
        {
            var customWave = new LevelData
            {
                Name = "Авторский дифракционный бильярд",
                Border = false,
                WaveEmitter = true,
                WaveContinuous = true,
                WaveSourcePos = new Vec2(640, 410),
                WaveRays = 180,
                WaveSpeed = 320,
                WaveRayRadius = 3,
                NoBallCollisions = true,
            };
            customWave.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 285 });
            customWave.Arcs.Add(new ArcData { Center = new Vec2(640, 315), Radius = 95, StartDeg = 200, SweepDeg = 140, HalfWidth = 2.5 });
            customWave.Walls.Add(new PolyData { Closed = false, Pts = new() { new Vec2(545, 370), new Vec2(735, 370) } });

            using var bmp = new Bitmap(640, 360);
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;

            static string VerifyWaveRun(UI.GameCanvas canvas, Bitmap bmp, MethodInfo onPaint, string where)
            {
                if (!canvas.WaveMode || !canvas.UseWaveFront)
                    return $"{where}: не активировался режим непрерывного волнового фронта";
                for (int i = 0; i < 72; i++)
                    canvas.AdvanceFixedStep(1.0 / 60.0);

                using (var g = Graphics.FromImage(bmp))
                using (var pe = new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)))
                    onPaint.Invoke(canvas, new object[] { pe });

                if (canvas.WaveFront.NodeCount <= 180)
                    return $"{where}: адаптивное уплотнение фронта не добавило узлы ({canvas.WaveFront.NodeCount})";
                if (canvas.WaveFront.DiffractionCount <= 0)
                    return $"{where}: не возникли дифракционные волны Келлера на краях преграды и дуги";
                return null;
            }

            using (var ev = new UI.EditorView { Size = new Size(1360, 780) })
            {
                ev.LoadTemplate(customWave);
                ev.ToggleTest();
                var err = VerifyWaveRun(ev.Canvas, bmp, onPaint, "EditorView.ТЕСТ");
                if (err != null) return err;
                ev.ToggleTest();
                if (ev.Canvas.WaveMode || ev.Canvas.UseWaveFront)
                    return "после выхода из ТЕСТа в редакторе остались флаги WaveMode/UseWaveFront";
            }

            using (var pv = new UI.PlayView { Size = new Size(1360, 780) })
            {
                pv.LoadCustomLevel(customWave.Clone());
                var err = VerifyWaveRun(pv.Canvas, bmp, onPaint, "PlayView.УРОВНИ");
                if (err != null) return err;
            }
            return null;
        });

        failed += Test("вращающиеся стены в ТЕСТе редактора: координаты Design.Walls не искажаются вращением", () =>
        {
            using var ev = new UI.EditorView { Size = new Size(1360, 780) };
            var lvl = new LevelData { Name = "Вращающаяся звезда", SpinSpeed = 2.4 };
            lvl.Walls.Add(new PolyData
            {
                Closed = true,
                Pts = LevelShapes.StarPolygonPts(new Vec2(640, 360), 180, 5, 0.45, 0),
                Pivot = new Vec2(640, 360),
            });
            ev.LoadTemplate(lvl);
            var origPt0 = ev.Canvas.Design.Walls[0].Pts[0];

            ev.ToggleTest();
            for (int i = 0; i < 60; i++)
                ev.Canvas.AdvanceFixedStep(1.0 / 60.0);
            var rotatedSimPt0 = ev.Canvas.Sim.Walls[0].Pts[0];
            if ((rotatedSimPt0 - origPt0).Len < 10.0)
                return "стена в симуляции не вращалась при SpinSpeed=2.4";

            ev.ToggleTest(); // СТОП
            var afterPt0 = ev.Canvas.Design.Walls[0].Pts[0];
            if ((afterPt0 - origPt0).Len > 1e-9)
                return $"исходная вершина Design.Walls сместилась после ТЕСТа: {origPt0} -> {afterPt0}";
            return null;
        });

        failed += Test("вёрстка обновлённого редактора (100% и 150% DPI, все 4 темы): нет наложений и обрезки", () =>
        {
            static string CheckPanelLayout(Control container, string panelName)
            {
                var vis = container.Controls.Cast<Control>().Where(c => c.Visible).ToList();
                var hostRect = new Rectangle(0, 0, container.ClientSize.Width, container.ClientSize.Height);
                foreach (var c in vis)
                {
                    if (c.Left < 0 || c.Top < 0 || c.Right > hostRect.Width + 1 || c.Bottom > hostRect.Height + 1)
                        return $"{panelName}: контрол '{c.Text}' ({c.GetType().Name} {c.Bounds}) выходит за пределы панели {hostRect.Size}";
                }
                for (int i = 0; i < vis.Count; i++)
                {
                    for (int j = i + 1; j < vis.Count; j++)
                    {
                        var intersection = Rectangle.Intersect(vis[i].Bounds, vis[j].Bounds);
                        if (intersection.Width > 1 && intersection.Height > 1)
                            return $"{panelName}: перекрытие '{vis[i].Text}' ({vis[i].Bounds}) и '{vis[j].Text}' ({vis[j].Bounds})";
                    }
                }
                return null;
            }

            int prevPreset = UI.Theme.CurrentPreset;
            try
            {
                using var form = new UI.MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Show();
                form.ShowView(1);
                Application.DoEvents();

                var ev = (UI.EditorView)form.ActiveView;
                ev.GenerateSpiral(4, 120, 20, 60, 20, SpiralSpinPattern.Sync);
                var toolbar = ev.Controls.OfType<UI.Toolbar>().First();
                var palField = typeof(UI.EditorView).GetField("palettePanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var insField = typeof(UI.EditorView).GetField("inspectorPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var pal = (Control)palField.GetValue(ev)!;
                var ins = (Control)insField.GetValue(ev)!;

                foreach (string scaleTag in new[] { "100% DPI", "150% DPI" })
                {
                    if (scaleTag == "150% DPI")
                    {
                        form.Scale(new SizeF(1.5f, 1.5f));
                        Application.DoEvents();
                    }

                    for (int preset = 0; preset < UI.Theme.Presets.Length; preset++)
                    {
                        UI.Theme.SetPreset(preset);
                        Application.DoEvents();

                        ev.SetInspectorTab(0);
                        string err = CheckPanelLayout(toolbar, $"{scaleTag}/тема {preset}/Toolbar")
                                  ?? CheckPanelLayout(pal, $"{scaleTag}/тема {preset}/LeftPalette")
                                  ?? CheckPanelLayout(ins, $"{scaleTag}/тема {preset}/Inspector(СЦЕНА)");
                        if (err != null) return err;

                        ev.Canvas.SelectObject(ev.Canvas.Design.Arcs[0]);
                        ev.SetInspectorTab(1);
                        err = CheckPanelLayout(ins, $"{scaleTag}/тема {preset}/Inspector(ОБЪЕКТ:Arc)");
                        if (err != null) return err;

                        ev.Canvas.SelectObject(null);
                        ev.SelectTool(UI.Tool.Select);
                        err = CheckPanelLayout(ins, $"{scaleTag}/тема {preset}/Inspector(ОБЪЕКТ:Default)");
                        if (err != null) return err;
                    }
                }
                return null;
            }
            finally
            {
                UI.Theme.SetPreset(prevPreset);
            }
        });

        failed += Test("FX свечения шара: плавный градиент без ступенчатых колец 4×4", () =>
        {
            using var canvas = new UI.GameCanvas { Size = new Size(1280, 720), FxOn = true };
            var empty = new LevelData { Name = "GlowTest" };
            canvas.LoadLevel(empty);
            canvas.Sim.AddBall(new Vec2(640, 360), new Vec2(0, 0), 14, -1, unchecked((int)0xFF5EC5FF));

            bool prevHalo = UI.GameCanvas.HaloOn;
            bool prevBloom = UI.Fx.BloomOn;
            try
            {
                UI.GameCanvas.HaloOn = true;
                UI.Fx.BloomOn = true;
                var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                using var dummy = new Bitmap(1280, 720);
                using var dg = Graphics.FromImage(dummy);
                using var pe = new PaintEventArgs(dg, new Rectangle(0, 0, 1280, 720));
                onPaint.Invoke(canvas, new object[] { pe });

                var frameBmp = (Bitmap)typeof(UI.GameCanvas).GetField("frameBmp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
                int prevLum = -1;
                int maxStepJump = 0;
                for (int x = 660; x <= 708; x++)
                {
                    var px = frameBmp.GetPixel(x, 360);
                    int lum = px.R + px.G + px.B;
                    if (prevLum >= 0)
                        maxStepJump = Math.Max(maxStepJump, Math.Abs(lum - prevLum));
                    prevLum = lum;
                }
                if (maxStepJump > 42)
                    return $"слишком резкий переход в свечении шара (скачок яркости соседних пикселей {maxStepJump})";
                return null;
            }
            finally
            {
                UI.GameCanvas.HaloOn = prevHalo;
                UI.Fx.BloomOn = prevBloom;
            }
        });

        failed += Test("перенос уровней в Редактор из всех режимов + менеджер неограниченных сохранений и импорт файла", () =>
        {
            var createdFiles = new List<string>();
            string extTemp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ricochet_ext_{Guid.NewGuid():N}.json");
            try
            {
                using var form = new UI.MainForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Show();
                Application.DoEvents();

                // 1. Из «Уровней» -> в Редактор
                var pv = (UI.PlayView)form.ActiveView;
                UI.MainForm.RequestOpenInEditor(pv.CurrentLevel.Clone());
                if (form.ActiveView is not UI.EditorView ev)
                    return "RequestOpenInEditor не переключил главное окно в Редактор";
                if (ev.Canvas.Design.Name != pv.CurrentLevel.Name)
                    return $"не перенёсся уровень из PlayView: {ev.Canvas.Design.Name}";

                // 2. Из «Волнового фронта» -> в Редактор
                form.ShowView(2);
                var wv = (UI.WaveView)form.ActiveView;
                var exportedWave = wv.ExportCurrentLevel();
                UI.MainForm.RequestOpenInEditor(exportedWave);
                if (!ReferenceEquals(form.ActiveView, ev) || !ev.Canvas.Design.WaveEmitter)
                    return "не перенёсся волновой уровень из WaveView в Редактор";

                // 3. Из «Ротора» (включая уровень «50 КРУГОВ · ПРОСТОР») -> в Редактор
                form.ShowView(3);
                var rv = (UI.RotorView)form.ActiveView;
                rv.ShowLevel(6);
                var rotor50 = rv.ExportCurrentLevel();
                if (rotor50.Arcs.Count != 50 || rotor50.Arcs[0].Radius < 244.0)
                    return $"уровень '50 КРУГОВ · ПРОСТОР' имеет неверную геометрию: колец={rotor50.Arcs.Count}, R0={rotor50.Arcs[0].Radius}";
                UI.MainForm.RequestOpenInEditor(rotor50);
                if (ev.Canvas.Design.Arcs.Count != 50)
                    return "не перенеслись все 50 колец Ротора в Редактор";

                // 4. Неограниченные сохранения прямо в игре (несколько слотов с одним именем + перезапись)
                ev.Canvas.Design.Name = "ТестСлотАвто";
                string s1 = ev.SaveCurrentInGame(asNewSlot: true);
                createdFiles.Add(s1);
                string s2 = ev.SaveCurrentInGame(asNewSlot: true);
                createdFiles.Add(s2);
                string s3 = ev.SaveCurrentInGame(asNewSlot: true);
                createdFiles.Add(s3);
                if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s2, s3, StringComparison.OrdinalIgnoreCase))
                    return "новые слоты сохранения перезаписали друг друга вместо создания отдельных сохранений";

                ev.Canvas.Design.Hint = "Обновлённая подсказка";
                string s3Over = ev.SaveCurrentInGame(asNewSlot: false);
                if (!string.Equals(s3, s3Over, StringComparison.OrdinalIgnoreCase))
                    return "обычное сохранение должно обновлять текущий активный слот";

                // 5. Импорт внешнего .json файла сохранения в библиотеку игры
                var extLvl = new LevelData { Name = "Импортированный уровень", Gravity = true };
                extLvl.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 250 });
                LevelStore.Save(extLvl, extTemp);
                string importedPath = ev.ImportSaveFile(extTemp);
                createdFiles.Add(importedPath);
                if (!System.IO.File.Exists(importedPath) || ev.Canvas.Design.Name != "Импортированный уровень")
                    return "импорт внешнего .json файла в игру не загрузил уровень";

                // Проверяем и удаление через LevelStore.DeleteSave
                foreach (var f in createdFiles)
                {
                    if (!LevelStore.DeleteSave(f) || System.IO.File.Exists(f))
                        return "DeleteSave не удалил слот сохранения: " + f;
                }
                createdFiles.Clear();
                return null;
            }
            finally
            {
                foreach (var f in createdFiles) LevelStore.DeleteSave(f);
                try { if (System.IO.File.Exists(extTemp)) System.IO.File.Delete(extTemp); } catch { }
            }
        });

        failed += Test("кнопки ВОЛНА / ЛУЧИ: нет зелёной обводки в выкл. состоянии, ЛУЧИ серые при включённой ВОЛНЕ", () =>
        {
            using var wv = new UI.WaveView { Size = new Size(1360, 780) };
            var chipWave = wv.WaveToggleButton;
            var raysBtn = wv.RaysSliderButton;

            // По умолчанию ВОЛНА включена -> ЛУЧИ должны быть неактивны (серые)
            if (!chipWave.IsOn) return "по умолчанию ВОЛНА должна быть включена";
            if (raysBtn.Enabled) return "при включённой ВОЛНЕ кнопка ЛУЧИ должна быть серой (Enabled=false)";

            // Выключаем ВОЛНУ кликом -> ЛУЧИ становятся активными, а на кнопке ВОЛНА нет зелёного контура
            var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            onClick.Invoke(chipWave, new object[] { EventArgs.Empty });
            if (chipWave.IsOn) return "клик по ВОЛНЕ не выключил переключатель";
            if (!raysBtn.Enabled) return "при выключенной ВОЛНЕ кнопка ЛУЧИ должна становиться активной";

            using var bmp = new Bitmap(chipWave.Width, chipWave.Height);
            chipWave.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    // В теме КИБЕР нейтральные тона слегка синеватые (B >= G >= R); зелёного доминирования быть не должно!
                    if (c.G > c.B + 6 && c.G > c.R + 12)
                        return $"на выключенной кнопке ВОЛНА осталась зелёная обводка в пикселе ({x},{y}): RGB=({c.R},{c.G},{c.B})";
                }
            }

            // Проверяем и все шапки режимов на отсутствие перекрытий при 1360 px
            using var form = new UI.MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            for (int mode = 0; mode < 4; mode++)
            {
                form.ShowView(mode);
                Application.DoEvents();
                var tb = form.ActiveView.Controls.OfType<UI.Toolbar>().First();
                var vis = tb.Controls.Cast<Control>().Where(k => k.Visible).ToList();
                for (int i = 0; i < vis.Count; i++)
                {
                    for (int j = i + 1; j < vis.Count; j++)
                    {
                        var inter = Rectangle.Intersect(vis[i].Bounds, vis[j].Bounds);
                        if (inter.Width > 1 && inter.Height > 1)
                            return $"режим {mode}: кнопки в верхней шапке перекрываются: '{vis[i].Text}' и '{vis[j].Text}'";
                    }
                }
            }
            return null;
        });

        failed += Test("непрерывная ВОЛНА не исчезает после 15 секунд + быстрый сглаженный рендер + вечный СЛЕД", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            var stadium = figs.First(f => f.Level.Name == "СТАДИОН");

            using var canvas = new UI.GameCanvas
            {
                Size = new Size(1280, 720),
                WaveMode = true,
                UseWaveFront = true,
                FxOn = false,
                NewBallR = 3,
                WaveSource = stadium.Source,
            };
            canvas.LoadLevel(stadium.Level);
            canvas.WaveFront.ResetFrom(stadium.Level, stadium.Source);
            canvas.WaveFront.EmitPointSource(stadium.Source, 120, 320, spectrum: true, fixedArgb: 0);

            // Шагаем 15 полных секунд (900 кадров по 1/60 с) — раньше волна исчезала после ~10 с!
            for (int i = 0; i < 60 * 15; i++)
                canvas.WaveFront.Step(1.0 / 60.0);

            int alive = 0;
            float maxIntensity = 0f;
            for (int i = 0; i < canvas.WaveFront.NodeCount; i++)
            {
                ref readonly var nd = ref canvas.WaveFront.Nodes[i];
                if (nd.Alive == 0) continue;
                alive++;
                if (nd.Intensity > maxIntensity) maxIntensity = nd.Intensity;
            }
            if (alive < 250)
                return $"непрерывная волна деградировала после 15 с: осталось живых узлов {alive}";
            if (maxIntensity < 0.85f)
                return $"яркость непрерывной волны затухла после 15 с: maxIntensity={maxIntensity:0.00}";

            // Проверяем скорость рендера со включённым «Сглаживанием» на плотном 15-секундном фронте
            bool prevSmooth = UI.GameCanvas.SmoothOn;
            UI.GameCanvas.SmoothOn = true;
            try
            {
                var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
                using var bmp = new Bitmap(1280, 720);
                using var g = Graphics.FromImage(bmp);
                using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));
                onPaint.Invoke(canvas, new object[] { pe }); // прогрев JIT
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int frames = 12;
                for (int f = 0; f < frames; f++)
                {
                    canvas.WaveFront.Step(1.0 / 60.0);
                    onPaint.Invoke(canvas, new object[] { pe });
                }
                double msPerFrame = sw.Elapsed.TotalMilliseconds / frames;
                Console.WriteLine($"      15с плотная волна ({alive} узлов, Smooth=ON): {msPerFrame:0.1} мс/кадр");
                if (msPerFrame > 35.0)
                    return $"слишком медленный рендер плотной волны со сглаживанием: {msPerFrame:0.1} мс/кадр";

                // Проверяем вечный СЛЕД в «ЛАБИРИНТЕ ТЕНИ»
                var shadowFig = figs.First(f => f.Level.Name == "ЛАБИРИНТ ТЕНИ");
                canvas.PersistentTrace = true;
                canvas.LoadLevel(shadowFig.Level);
                canvas.WaveSource = shadowFig.Source;
                canvas.WaveFront.ResetFrom(shadowFig.Level, shadowFig.Source);
                canvas.WaveFront.EmitPointSource(shadowFig.Source, 120, 320, spectrum: true, fixedArgb: 0);

                int earlyTrace = 0;
                for (int i = 0; i < 90; i++)
                {
                    canvas.WaveFront.Step(1.0 / 60.0);
                    onPaint.Invoke(canvas, new object[] { pe });
                    if (i == 20)
                        earlyTrace = canvas.GetPersistentTraceSum(305, 360);
                }
                int lateTrace = canvas.GetPersistentTraceSum(305, 360);
                int deepShadowTrace = canvas.GetPersistentTraceSum(1050, 560);
                if (earlyTrace <= 0 || lateTrace < earlyTrace)
                    return $"вечный СЛЕД исчезает со временем: было {earlyTrace}, стало {lateTrace}";
                if (deepShadowTrace != 0)
                    return $"зона глубокой тени за перегородками не должна быть закрашена за 1.5 с: {deepShadowTrace}";
            }
            finally
            {
                UI.GameCanvas.SmoothOn = prevSmooth;
            }
            return null;
        });

        failed += Test("режим УРОВНИ: сильное замедление (slow-mo) и неоновое конфетти при достижении зелёного круга", () =>
        {
            using var pv = new UI.PlayView { Size = new Size(1360, 780) };
            var goalLevel = BuiltinLevels.All().First(l => l.Name == "Круг");
            pv.LoadCustomLevel(goalLevel.Clone());

            var canvas = pv.Canvas;
            canvas.Size = new Size(1280, 720);
            // Зелёная цель в «Круге» находится в (860, 480, R=38) — пускаем шар прямо в неё
            canvas.Sim.AddBall(new Vec2(780, 480), new Vec2(700, 0), 12);

            bool hitGoal = false;
            for (int i = 0; i < 30; i++)
            {
                canvas.AdvanceFixedStep(1.0 / 60.0);
                if (canvas.Sim.Finished)
                {
                    hitGoal = true;
                    break;
                }
            }
            if (!hitGoal) return "шар не достиг зелёной цели";
            if (canvas.GoalTimeScale > 0.12)
                return $"не включилось сильное замедление при входе в зелёный круг: GoalTimeScale={canvas.GoalTimeScale:0.00}";
            if (canvas.ActiveConfettiCount < 60)
                return $"не появилось праздничное конфетти: частиц={canvas.ActiveConfettiCount}";

            // Рендерим кадр конфетти и проверяем плавный возврат скорости к 1.0×
            var onPaint = typeof(UI.GameCanvas).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var bmp = new Bitmap(1280, 720);
            using var g = Graphics.FromImage(bmp);
            using var pe = new PaintEventArgs(g, new Rectangle(0, 0, 1280, 720));
            onPaint.Invoke(canvas, new object[] { pe });

            for (int i = 0; i < 130; i++)
                canvas.StepVisuals(1.0 / 60.0);
            if (canvas.GoalTimeScale < 0.99)
                return $"скорость не восстановилась после замедления: GoalTimeScale={canvas.GoalTimeScale:0.00}";
            return null;
        });

        Console.WriteLine(failed == 0 ? "SELFTEST OK" : $"SELFTEST FAILED: {failed}");
        return failed;
    }

    /// <summary>Полигонные фигуры волнового фронта (как в WaveView) для теста удержания лучей.</summary>
    private static List<(string, LevelData)> WaveTestFigures()
    {
        var res = new List<(string, LevelData)>();

        var ellipse = new LevelData { Name = "ЭЛЛИПС" };
        ellipse.Walls.Add(Poly(96, (i, n) =>
        {
            double a = i * 2 * Math.PI / n;
            return new Vec2(640 + Math.Cos(a) * 430, 360 + Math.Sin(a) * 270);
        }));
        res.Add(("эллипс", ellipse));

        var stadium = new LevelData { Name = "СТАДИОН" };
        double cx1 = 490, cx2 = 790, cy = 360, r = 260;
        var pts = new List<Vec2> { new(cx1, cy - r), new(cx2, cy - r) };
        for (int a = -84; a <= 84; a += 6) { double rad = a * Math.PI / 180; pts.Add(new(cx2 + r * Math.Cos(rad), cy + r * Math.Sin(rad))); }
        pts.Add(new Vec2(cx1, cy + r));
        for (int a = 96; a <= 264; a += 6) { double rad = a * Math.PI / 180; pts.Add(new(cx1 + r * Math.Cos(rad), cy + r * Math.Sin(rad))); }
        var st = new PolyData { Closed = true, Pts = pts };
        stadium.Walls.Add(st);
        res.Add(("стадион", stadium));

        var square = new LevelData { Name = "КВАДРАТ" };
        var sq = new PolyData { Closed = true };
        sq.Pts.Add(new Vec2(310, 80)); sq.Pts.Add(new Vec2(970, 80));
        sq.Pts.Add(new Vec2(970, 640)); sq.Pts.Add(new Vec2(310, 640));
        square.Walls.Add(sq);
        res.Add(("квадрат", square));

        return res;
    }

    private static PolyData Poly(int n, Func<int, int, Vec2> pt)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < n; i++) p.Pts.Add(pt(i, n));
        return p;
    }

    private static int Test(string name, Func<string> body)    {
        try
        {
            var err = body();
            Console.WriteLine(err == null ? $"  [OK] {name}" : $"  [FAIL] {name}: {err}");
            return err == null ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}: исключение {ex.Message}");
            return 1;
        }
    }
}

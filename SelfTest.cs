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

        failed += Test("волновой фронт: 24 фигуры, источник внутри каждой, шаг без ошибок", () =>
        {
            var figs = UI.WaveView.BuildFigures();
            if (figs.Count != 24) return "ожидалось 24 фигуры, есть " + figs.Count;
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

using System;
using System.Collections.Generic;

namespace Ricochet.Core;

/// <summary>
/// Встроенные уровни из простых фигур: круг, квадрат, прямоугольник, треугольник.
/// Не во всех уровнях есть зелёная цель — «свободные» арены просто для игры с рикошетами.
/// Границы поля нет нигде: замкнутая фигура сама держит шары, из открытой — улетают.
/// </summary>
public static class BuiltinLevels
{
    // ---- помощники-конструкторы простых фигур ----
    private static PolyData RectWall(double x, double y, double w, double h)
    {
        var p = new PolyData { Closed = true };
        p.Pts.Add(new Vec2(x, y));
        p.Pts.Add(new Vec2(x + w, y));
        p.Pts.Add(new Vec2(x + w, y + h));
        p.Pts.Add(new Vec2(x, y + h));
        return p;
    }

    private static PolyData CircleWall(double cx, double cy, double r, int n = 48)
    {
        var p = new PolyData { Closed = true };
        for (int i = 0; i < n; i++)
        {
            double a = i * 2 * Math.PI / n;
            p.Pts.Add(new Vec2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return p;
    }

    private static GoalData GoalRect(double x, double y, double w, double h)
    {
        var g = new GoalData();
        g.Pts.Add(new Vec2(x, y));
        g.Pts.Add(new Vec2(x + w, y));
        g.Pts.Add(new Vec2(x + w, y + h));
        g.Pts.Add(new Vec2(x, y + h));
        return g;
    }

    private static GoalData GoalCircle(double cx, double cy, double r, int n = 28)
    {
        var g = new GoalData();
        for (int i = 0; i < n; i++)
        {
            double a = i * 2 * Math.PI / n;
            g.Pts.Add(new Vec2(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
        }
        return g;
    }

    public static List<LevelData> All()
    {
        return new List<LevelData>
        {
            FreeCircle(), FreeSquare(), Circle(), Square(), RectangleLevel(), Triangle(), GravityLevel(),
            Zigzag(), Windmill(), Basketball(), DoubleGates(), PinballCascade(), Hourglass(), Kaleidoscope(),
        };
    }

    // 3. круглая арена (аналитический круг — точные отражения)
    private static LevelData Circle()
    {
        var l = new LevelData
        {
            Name = "Круг",
            Hint = "Круглая арена. Попади в зелёный круг — напрямую или отскоком от стенки. Колесо мыши меняет размер шара.",
        };
        l.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 310 });
        l.Goals.Add(GoalCircle(860, 480, 38));
        return l;
    }

    // 2. квадратная арена
    private static LevelData Square()
    {
        var l = new LevelData
        {
            Name = "Квадрат",
            Hint = "Квадратная арена. Зелёная цель — в верхнем правом углу: удобно добивать отскоком от стенок.",
        };
        l.Walls.Add(RectWall(360, 80, 560, 560));
        l.Goals.Add(GoalCircle(850, 150, 40));
        return l;
    }

    // 3. прямоугольная арена с круглой преградой
    private static LevelData RectangleLevel()
    {
        var l = new LevelData
        {
            Name = "Прямоугольник",
            Hint = "Прямоугольная арена. Круг в центре перекрывает прямую траекторию — сыграй от стенок.",
        };
        l.Walls.Add(RectWall(160, 160, 960, 400));
        l.Walls.Add(CircleWall(640, 360, 70));
        l.Goals.Add(GoalCircle(280, 240, 34));
        return l;
    }

    // 4. треугольная арена
    private static LevelData Triangle()
    {
        var l = new LevelData
        {
            Name = "Треугольник",
            Hint = "Треугольная арена. Острые углы дают неожиданные отскоки — цель у нижней грани.",
        };
        var t = new PolyData { Closed = true };
        t.Pts.Add(new Vec2(640, 70));
        t.Pts.Add(new Vec2(1180, 645));
        t.Pts.Add(new Vec2(100, 645));
        l.Walls.Add(t);
        l.Goals.Add(GoalCircle(640, 500, 40));
        return l;
    }

    // 5. гравитация, границы нет — открытая сцена с полом
    private static LevelData GravityLevel()
    {
        var l = new LevelData
        {
            Name = "Полёт с гравитацией",
            Gravity = true,
            Hint = "Гравитация включена, границы поля НЕТ: промахнулся — шар улетел навсегда. Пол — узкий прямоугольник, цель на нём справа.",
        };
        l.Walls.Add(RectWall(140, 600, 1000, 26));       // пол
        l.Goals.Add(GoalCircle(1000, 560, 38));          // цель над правым концом пола
        return l;
    }

    // 1. свободная круглая арена — без цели (аналитический круг)
    private static LevelData FreeCircle()
    {
        var l = new LevelData
        {
            Name = "Свободный круг",
            Hint = "Свободный уровень: зелёной цели нет — просто играй с рикошетами. Попробуй гравитацию (G) и разные размеры шаров.",
        };
        l.Circles.Add(new CircleData { Center = new Vec2(640, 360), Radius = 310 });
        return l;
    }

    // 7. свободная квадратная арена с двумя кругами — без цели
    private static LevelData FreeSquare()
    {
        var l = new LevelData
        {
            Name = "Свободный квадрат",
            Hint = "Свободный уровень: цели нет. Два круга-препятствия для каскадов из отскоков.",
        };
        l.Walls.Add(RectWall(220, 100, 840, 520));
        l.Walls.Add(CircleWall(640, 240, 60));
        l.Walls.Add(CircleWall(640, 480, 60));
        return l;
    }

    // 8. зеркальный коридор-зигзаг
    private static LevelData Zigzag()
    {
        var l = new LevelData
        {
            Name = "Зигзаг",
            Hint = "Зеркальный слалом из трёх перегородок: запусти шар из левого отсека под углом, чтобы он прошил весь коридор.",
        };
        l.Walls.Add(RectWall(160, 120, 960, 480));
        l.Walls.Add(RectWall(400, 120, 20, 320));
        l.Walls.Add(RectWall(640, 280, 20, 320));
        l.Walls.Add(RectWall(880, 120, 20, 320));
        l.Goals.Add(GoalCircle(1020, 235, 38));
        return l;
    }

    // 9. круглая арена с вращающейся крестовиной в центре
    private static LevelData Windmill()
    {
        var c = new Vec2(640, 360);
        var l = new LevelData
        {
            Name = "Мельница",
            SpinSpeed = 1.05,
            Hint = "Лопасти в центре непрерывно вращаются — поймай окно или используй отскок от подвижной грани!",
        };
        l.Circles.Add(new CircleData { Center = c, Radius = 315 });

        const double w = 18, arm = 165;
        var cross = new PolyData { Closed = true, Pivot = c };
        cross.Pts.AddRange(new[]
        {
            new Vec2(c.X - w, c.Y - arm), new Vec2(c.X + w, c.Y - arm), new Vec2(c.X + w, c.Y - w),
            new Vec2(c.X + arm, c.Y - w), new Vec2(c.X + arm, c.Y + w), new Vec2(c.X + w, c.Y + w),
            new Vec2(c.X + w, c.Y + arm), new Vec2(c.X - w, c.Y + arm), new Vec2(c.X - w, c.Y + w),
            new Vec2(c.X - arm, c.Y + w), new Vec2(c.X - arm, c.Y - w), new Vec2(c.X - w, c.Y - w),
        });
        l.Walls.Add(cross);
        l.Goals.Add(GoalCircle(900, 360, 38));
        return l;
    }

    // 10. баскетбольный бросок с гравитацией и щитом
    private static LevelData Basketball()
    {
        var l = new LevelData
        {
            Name = "Баскетбол",
            Gravity = true,
            Hint = "Бросок по навесной дуге или от щита справа прямо в корзину! Границ нет — рассчитывай траекторию.",
        };
        l.Walls.Add(RectWall(140, 610, 480, 22));  // стартовый помост
        l.Walls.Add(RectWall(1060, 145, 22, 296)); // высокий щит
        l.Walls.Add(RectWall(900, 332, 18, 93));   // передняя дужка кольца
        l.Walls.Add(RectWall(900, 425, 160, 16));  // дно корзины
        l.Goals.Add(GoalCircle(986, 378, 34));
        return l;
    }

    // 11. встречные вращающиеся щиты вокруг центральной цели
    private static LevelData DoubleGates()
    {
        var c = new Vec2(640, 360);
        var l = new LevelData
        {
            Name = "Двойные врата",
            SpinSpeed = 1.15,
            Hint = "Два встречных кольца защищают зелёное ядро в центре. Подгадай момент, когда прорези совпадут!",
        };
        l.Circles.Add(new CircleData { Center = c, Radius = 320 });
        l.Arcs.Add(new ArcData { Center = c, Radius = 158, StartDeg = 20, SweepDeg = 275, HalfWidth = 3.0, SpinMul = 1.0 });
        l.Arcs.Add(new ArcData { Center = c, Radius = 96, StartDeg = 200, SweepDeg = 265, HalfWidth = 3.0, SpinMul = -1.25 });
        l.Goals.Add(GoalCircle(640, 360, 35));
        return l;
    }

    // 12. пинбол-каскад с бамперами и гравитацией
    private static LevelData PinballCascade()
    {
        var l = new LevelData
        {
            Name = "Пинбол-каскад",
            Gravity = true,
            GravityScale = 0.72,
            ElasticBounce = true,
            Hint = "Запусти шар вверх под купол бамперов — упругий каскад отскоков проведёт его к нижней лузе.",
        };
        var arena = new PolyData { Closed = true };
        arena.Pts.AddRange(new[]
        {
            new Vec2(320, 75), new Vec2(960, 75), new Vec2(1120, 290),
            new Vec2(960, 645), new Vec2(320, 645), new Vec2(160, 290),
        });
        l.Walls.Add(arena);
        l.Walls.Add(CircleWall(480, 240, 34, 32));
        l.Walls.Add(CircleWall(800, 240, 34, 32));
        l.Walls.Add(CircleWall(640, 325, 38, 32));
        l.Walls.Add(CircleWall(515, 425, 32, 32));
        l.Walls.Add(CircleWall(765, 425, 32, 32));
        l.Goals.Add(GoalCircle(640, 565, 38));
        return l;
    }

    // 13. песочные часы с ромбом-отражателем перед целью
    private static LevelData Hourglass()
    {
        var l = new LevelData
        {
            Name = "Песочные часы",
            Hint = "Узкая горловина соединяет две залы, а ромб закрывает прямой прострел к цели.",
        };
        var body = new PolyData { Closed = true };
        body.Pts.AddRange(new[]
        {
            new Vec2(170, 110), new Vec2(500, 110), new Vec2(640, 295), new Vec2(780, 110), new Vec2(1110, 110),
            new Vec2(1110, 610), new Vec2(780, 610), new Vec2(640, 425), new Vec2(500, 610), new Vec2(170, 610),
        });
        l.Walls.Add(body);
        l.Walls.Add(CircleWall(885, 360, 44, 4));
        l.Goals.Add(GoalCircle(1025, 360, 36));
        return l;
    }

    // 14. свободный калейдоскоп (8-лучевая звезда + вращающийся кристалл)
    private static LevelData Kaleidoscope()
    {
        var c = new Vec2(640, 360);
        var l = new LevelData
        {
            Name = "Калейдоскоп",
            SpinSpeed = 0.65,
            Hint = "Свободная звёздная арена с вращающимся кристаллом в центре. Попробуй цвет «СПЕКТР» и треки!",
        };
        var star = new PolyData { Closed = true };
        for (int i = 0; i < 16; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 8.0;
            double r = (i % 2 == 0) ? 328.0 : 218.0;
            star.Pts.Add(new Vec2(c.X + Math.Cos(a) * r * 1.25, c.Y + Math.Sin(a) * r * 0.96));
        }
        l.Walls.Add(star);
        var gem = CircleWall(c.X, c.Y, 58, 4);
        gem.Pivot = c;
        l.Walls.Add(gem);
        return l;
    }
}

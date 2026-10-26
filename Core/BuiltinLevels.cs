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
}

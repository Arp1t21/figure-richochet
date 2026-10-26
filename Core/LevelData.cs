using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ricochet.Core;

/// <summary>Стенка-полилиния. Незамкнутая — «дорожка», замкнутая (Closed) — контур фигуры.
/// Pivot — центр вращения стенки (режим «Ротор»); null — центр поля.</summary>
public class PolyData
{
    public List<Vec2> Pts = new();
    public bool Closed;
    public Vec2? Pivot;
}

/// <summary>Целевая зона — замкнутый многоугольник. Касание шаром = пройдено.</summary>
public class GoalData
{
    public List<Vec2> Pts = new();
}

/// <summary>Шар, заданный на этапе дизайна (в редакторе). Позиция, радиус, начальная скорость, цвет.</summary>
public class BallData
{
    public Vec2 Pos;
    public double R = 12;
    public Vec2 Vel;
    public bool HasVel;
    public int Argb;   // цвет (0 = белый по умолчанию)
}

/// <summary>Аналитическая круговая арена: шары внутри, отражение строго по радиусу —
/// точный «математический бильярд» без погрешностей многоугольной аппроксимации.</summary>
public class CircleData
{
    public Vec2 Center;
    public double Radius;
}

/// <summary>Неполный вращающийся круг (дуга): аналитическая геометрия без многоугольной
/// аппроксимации. StartDeg — начало сплошной дуги (градусы, по часовой от оси +X),
/// SweepDeg — угловая длина сплошной части (например, 300° при вырезе 60°).</summary>
public class ArcData
{
    public Vec2 Center;
    public double Radius;
    public double StartDeg;
    public double SweepDeg = 300;
    public double HalfWidth = 2.3;
    public double SpinMul = 1.0; // индивидуальный множитель скорости/направления вращения кольца
}

/// <summary>Полное описание уровня / сцены редактора. Сериализуется в JSON как есть.</summary>
public class LevelData
{
    public const double W = 1280;
    public const double H = 720;

    public string Name = "Новый уровень";
    public bool Gravity;
    public double GravityScale = 1.0;      // множитель ускорения свободного падения
    public bool ElasticBounce;             // без гашения микро-отскоков при гравитации (режим «Ротор»)
    public bool Border;                    // упругая рамка поля; по умолчанию выключена
    public bool NoBallCollisions;          // шары не сталкиваются друг с другом
    public string Hint = "";
    public List<PolyData> Walls = new();
    public List<CircleData> Circles = new();
    public List<ArcData> Arcs = new();
    public List<GoalData> Goals = new();
    public List<BallData> Balls = new();

    public LevelData Clone()
    {
        var c = new LevelData
        {
            Name = Name,
            Gravity = Gravity,
            GravityScale = GravityScale,
            ElasticBounce = ElasticBounce,
            Border = Border,
            NoBallCollisions = NoBallCollisions,
            Hint = Hint,
            Walls = new List<PolyData>(Walls.Count),
            Circles = new List<CircleData>(Circles.Count),
            Arcs = new List<ArcData>(Arcs.Count),
            Goals = new List<GoalData>(Goals.Count),
            Balls = new List<BallData>(Balls.Count),
        };
        for (int i = 0; i < Walls.Count; i++)
        {
            var w = Walls[i];
            c.Walls.Add(new PolyData { Closed = w.Closed, Pivot = w.Pivot, Pts = new List<Vec2>(w.Pts) });
        }
        for (int i = 0; i < Circles.Count; i++)
        {
            var ci = Circles[i];
            c.Circles.Add(new CircleData { Center = ci.Center, Radius = ci.Radius });
        }
        for (int i = 0; i < Arcs.Count; i++)
        {
            var a = Arcs[i];
            c.Arcs.Add(new ArcData
            {
                Center = a.Center, Radius = a.Radius, StartDeg = a.StartDeg,
                SweepDeg = a.SweepDeg, HalfWidth = a.HalfWidth, SpinMul = a.SpinMul,
            });
        }
        for (int i = 0; i < Goals.Count; i++)
            c.Goals.Add(new GoalData { Pts = new List<Vec2>(Goals[i].Pts) });
        for (int i = 0; i < Balls.Count; i++)
        {
            var b = Balls[i];
            c.Balls.Add(new BallData { Pos = b.Pos, R = b.R, Vel = b.Vel, HasVel = b.HasVel, Argb = b.Argb });
        }
        return c;
    }
}

/// <summary>Загрузка/сохранение уровней. Свои уровни лежат в папке levels/ рядом с exe.</summary>
public static class LevelStore
{
    public static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };

    public static string LevelsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "levels");

    public static void Save(LevelData lvl, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(lvl, JsonOpts));
    }

    public static LevelData Load(string path)
    {
        var lvl = JsonSerializer.Deserialize<LevelData>(File.ReadAllText(path), JsonOpts);
        return lvl ?? new LevelData();
    }

    /// <summary>Пользовательские уровни из папки levels/ (созданные в редакторе).</summary>
    public static List<LevelData> UserLevels()
    {
        var result = new List<LevelData>();
        try
        {
            if (!Directory.Exists(LevelsDir)) return result;
            foreach (var f in Directory.GetFiles(LevelsDir, "*.json"))
            {
                try { result.Add(Load(f)); }
                catch { /* битый файл — пропускаем */ }
            }
        }
        catch { /* нет папки — нет проблем */ }
        return result;
    }
}

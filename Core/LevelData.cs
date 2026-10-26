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

    public LevelData Clone() =>
        JsonSerializer.Deserialize<LevelData>(JsonSerializer.Serialize(this, LevelStore.JsonOpts), LevelStore.JsonOpts)
        ?? new LevelData();
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

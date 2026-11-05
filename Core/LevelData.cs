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

    // --- параметры вращения и механик «Ротора» ---
    public double SpinSpeed;               // угловая скорость вращения дуг и стенок (рад/с; 0 — статично)
    public bool FreezeSpinInGap = true;    // пауза вращения кругов, пока шар проходит через вырез
    public bool RotorRules;                // рассыпание пройденных колец, космический скин и счётчик в центре
    public bool InstantDrop;               // клик ЛКМ ставит шар с v=0 и запускает вращение с паузы
    public bool ContractRings = true;      // плавное сжатие внешних колец к центру по мере пробоя
    public bool ReverseOnBreak;            // «РЕВЕРС»: смена направления вращения при каждом пробое кольца
    public double AccelOnBreak = 1.0;      // «РАЗГОН»: множитель ускорения вращения при пробое кольца
    public double RotorBaseR0;             // целевой внутренний радиус сжатия спирали (0 = авто по первому кольцу)

    // --- параметры режима «Волновой фронт / Математический бильярд» ---
    public bool WaveEmitter;               // уровень работает как волновой бильярд с точечным источником
    public Vec2 WaveSourcePos = new(640, 360);
    public bool WaveContinuous = true;     // true — непрерывный фронт + дифракция Келлера; false — частицы-лучи
    public int WaveRays = 180;             // начальное число лучей фронта
    public double WaveSpeed = 320.0;       // скорость распространения волны (px/s)
    public double WaveRayRadius = 3.0;     // толщина гребня / радиус луча-частицы
    public int WaveArgb;                   // цвет волны (0 = радужный СПЕКТР)

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
            SpinSpeed = SpinSpeed,
            FreezeSpinInGap = FreezeSpinInGap,
            RotorRules = RotorRules,
            InstantDrop = InstantDrop,
            ContractRings = ContractRings,
            ReverseOnBreak = ReverseOnBreak,
            AccelOnBreak = AccelOnBreak,
            RotorBaseR0 = RotorBaseR0,
            WaveEmitter = WaveEmitter,
            WaveSourcePos = WaveSourcePos,
            WaveContinuous = WaveContinuous,
            WaveRays = WaveRays,
            WaveSpeed = WaveSpeed,
            WaveRayRadius = WaveRayRadius,
            WaveArgb = WaveArgb,
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

/// <summary>Метаданные внутриигрового слота сохранения в папке levels/.</summary>
public sealed class SaveSlotInfo
{
    public string FilePath = "";
    public string FileName = "";
    public System.DateTime SavedLocal;
    public LevelData Level = new();
}

/// <summary>Загрузка/сохранение уровней и внутриигровая библиотека слотов (levels/ рядом с exe).</summary>
public static class LevelStore
{
    public static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };

    public static string LevelsDir => Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "levels");

    public static void Save(LevelData lvl, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(lvl, JsonOpts));
    }

    public static LevelData Load(string path)
    {
        var lvl = JsonSerializer.Deserialize<LevelData>(File.ReadAllText(path), JsonOpts) ?? new LevelData();
        lvl.Sanitize();
        return lvl;
    }

    public static string SanitizeFileStem(string rawName)
    {
        string s = string.IsNullOrWhiteSpace(rawName) ? "Уровень" : rawName.Trim();
        foreach (char bad in Path.GetInvalidFileNameChars())
            s = s.Replace(bad, '_');
        s = s.Trim('.', ' ');
        if (s.Length == 0) s = "Уровень";
        if (s.Length > 64) s = s[..64].Trim();
        return s;
    }

    /// <summary>Сохранить уровень прямо во внутриигровую библиотеку levels/.
    /// Если overwritePath == null, создаёт новый уникальный слот (неограниченное число сохранений без затирания).</summary>
    public static string SaveToLibrary(LevelData lvl, string overwritePath = null)
    {
        Directory.CreateDirectory(LevelsDir);
        if (!string.IsNullOrWhiteSpace(overwritePath))
        {
            Save(lvl, overwritePath);
            return overwritePath;
        }

        string stem = SanitizeFileStem(lvl.Name);
        string candidate = Path.Combine(LevelsDir, stem + ".json");
        int n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(LevelsDir, $"{stem} ({n}).json");
            n++;
        }
        Save(lvl, candidate);
        return candidate;
    }

    /// <summary>Импортировать внешний JSON-файл в библиотеку игры levels/ как новое сохранение.</summary>
    public static SaveSlotInfo ImportToLibrary(string externalFilePath)
    {
        var lvl = Load(externalFilePath);
        if (string.IsNullOrWhiteSpace(lvl.Name) || lvl.Name == "Новый уровень")
            lvl.Name = Path.GetFileNameWithoutExtension(externalFilePath);

        string fullExt = Path.GetFullPath(externalFilePath);
        string fullLib = Path.GetFullPath(LevelsDir);
        string savedPath = fullExt.StartsWith(fullLib, System.StringComparison.OrdinalIgnoreCase) && File.Exists(fullExt)
            ? fullExt
            : SaveToLibrary(lvl, overwritePath: null);

        return new SaveSlotInfo
        {
            FilePath = savedPath,
            FileName = Path.GetFileName(savedPath),
            SavedLocal = File.GetLastWriteTime(savedPath),
            Level = lvl,
        };
    }

    public static bool DeleteSave(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Полный список внутриигровых сохранений (новые сверху).</summary>
    public static List<SaveSlotInfo> ListSaves()
    {
        var result = new List<SaveSlotInfo>();
        try
        {
            if (!Directory.Exists(LevelsDir)) return result;
            var files = Directory.GetFiles(LevelsDir, "*.json");
            System.Array.Sort(files, static (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            foreach (var f in files)
            {
                try
                {
                    result.Add(new SaveSlotInfo
                    {
                        FilePath = f,
                        FileName = Path.GetFileName(f),
                        SavedLocal = File.GetLastWriteTime(f),
                        Level = Load(f),
                    });
                }
                catch { /* битый файл — пропускаем */ }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Пользовательские уровни из папки levels/ (созданные в редакторе).</summary>
    public static List<LevelData> UserLevels()
    {
        var slots = ListSaves();
        var result = new List<LevelData>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
            result.Add(slots[i].Level);
        return result;
    }
}

public enum SpiralSpinPattern
{
    Sync = 0,       // все кольца вращаются синхронно (+1)
    Alternating = 1,// чётные и нечётные навстречу (+1 / -1)
    Vortex = 2,     // дифференциальный вихрь (разные скорости и направления)
}

/// <summary>Генераторы аналитических и полигональных примитивов для редактора и шаблонов.</summary>
public static class LevelShapes
{
    public static void Sanitize(this LevelData lvl)
    {
        if (lvl.GravityScale <= 0) lvl.GravityScale = 1.0;
        if (lvl.AccelOnBreak <= 0) lvl.AccelOnBreak = 1.0;
        if (lvl.WaveRays <= 0) lvl.WaveRays = 180;
        if (lvl.WaveSpeed <= 0) lvl.WaveSpeed = 320.0;
        if (lvl.WaveRayRadius <= 0) lvl.WaveRayRadius = 3.0;
        if (lvl.WaveSourcePos.X == 0 && lvl.WaveSourcePos.Y == 0)
            lvl.WaveSourcePos = new Vec2(LevelData.W / 2, LevelData.H / 2);
    }

    public static List<Vec2> RegularPolygonPts(Vec2 center, double radius, int sides, double angleRad)
    {
        sides = System.Math.Clamp(sides, 3, 64);
        var pts = new List<Vec2>(sides);
        for (int i = 0; i < sides; i++)
        {
            double a = angleRad + i * 2.0 * System.Math.PI / sides;
            pts.Add(new Vec2(center.X + System.Math.Cos(a) * radius, center.Y + System.Math.Sin(a) * radius));
        }
        return pts;
    }

    public static List<Vec2> StarPolygonPts(Vec2 center, double outerRadius, int rays, double innerRatio, double angleRad)
    {
        rays = System.Math.Clamp(rays, 3, 32);
        innerRatio = System.Math.Clamp(innerRatio, 0.15, 0.90);
        double innerR = outerRadius * innerRatio;
        var pts = new List<Vec2>(rays * 2);
        for (int i = 0; i < rays * 2; i++)
        {
            double a = angleRad + i * System.Math.PI / rays;
            double r = (i & 1) == 0 ? outerRadius : innerR;
            pts.Add(new Vec2(center.X + System.Math.Cos(a) * r, center.Y + System.Math.Sin(a) * r));
        }
        return pts;
    }

    /// <summary>Создаёт серию концентрических колец с вырезом («Ротор» / спираль).</summary>
    public static void AddConcentricSpiral(
        LevelData lvl, Vec2 center, int ringCount, double innerR0, double step,
        double gapDeg, double startDeg0, double twistDeg, SpiralSpinPattern pattern,
        double halfWidth = 2.25, bool clearExisting = false)
    {
        if (clearExisting) lvl.Arcs.Clear();
        ringCount = System.Math.Clamp(ringCount, 1, 200);
        innerR0 = System.Math.Clamp(innerR0, 32.0, 500.0);
        step = System.Math.Clamp(step, 4.0, 80.0);
        gapDeg = System.Math.Clamp(gapDeg, 12.0, 260.0);
        double sweep = 360.0 - gapDeg;
        lvl.RotorBaseR0 = innerR0;

        for (int k = 0; k < ringCount; k++)
        {
            double mul = pattern switch
            {
                SpiralSpinPattern.Alternating => (k % 2 == 0) ? 1.0 : -1.0,
                SpiralSpinPattern.Vortex => ((k % 2 == 0) ? 1.0 : -1.0) * (0.65 + (k % 5) * 0.22),
                _ => 1.0,
            };
            double st = (startDeg0 + k * twistDeg) % 360.0;
            if (st < 0) st += 360.0;
            lvl.Arcs.Add(new ArcData
            {
                Center = center,
                Radius = innerR0 + k * step,
                StartDeg = st,
                SweepDeg = sweep,
                HalfWidth = halfWidth,
                SpinMul = mul,
            });
        }
    }
}

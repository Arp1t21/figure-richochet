using System;
using System.Collections.Generic;
using System.Linq;

namespace Ricochet.Core;

/// <summary>Стена-полилиния с рамкой для быстрого отсева дальних шаров.</summary>
public class Wall
{
    public List<Vec2> Pts;
    public bool Closed;
    public Vec2 Pivot;                   // центр вращения (режим «Ротор»)
    public PolyData Src;                 // исходная стенка дизайна (для удаления из симуляции)
    public double MinX, MinY, MaxX, MaxY;

    public Wall(List<Vec2> pts, bool closed, Vec2? pivot = null, PolyData src = null)
    {
        Pts = pts;
        Closed = closed;
        Pivot = pivot ?? new Vec2(LevelData.W / 2, LevelData.H / 2);
        Src = src;
        UpdateBounds();
    }

    public void UpdateBounds()
    {
        MinX = MinY = double.MaxValue;
        MaxX = MaxY = double.MinValue;
        foreach (var p in Pts)
        {
            if (p.X < MinX) MinX = p.X;
            if (p.Y < MinY) MinY = p.Y;
            if (p.X > MaxX) MaxX = p.X;
            if (p.Y > MaxY) MaxY = p.Y;
        }
    }
}

public class SimBall
{
    public Vec2 Pos, Vel;
    public double R;
    public int Hue = -1;               // цветовой тон 0..360 (-1 = обычный шар)
    public int Argb;                   // цвет шара (0 = белый по умолчанию)
    public double MinCenterDist = double.MaxValue; // мин. расстояние до центра дуг («Ротор»)
}

/// <summary>Живая аналитическая дуга в симуляции: вращается и сжимается без потери точности.</summary>
public class SimArc
{
    public Vec2 Center;
    public double Radius;
    public double StartRad;
    public double SweepRad;
    public double HalfWidth;
    public ArcData Src;

    public bool IsInSolid(double angleRad)
    {
        const double TwoPi = Math.PI * 2;
        double rel = (angleRad - StartRad) % TwoPi;
        if (rel < 0) rel += TwoPi;
        return rel <= SweepRad;
    }
}

/// <summary>
/// Физический мир: шары, отрезки стенок (собранные из всех полилиний уровня),
/// целевые зоны. Границы поля включаются уровнем (Border); без них улетевший
/// шар просто удаляется — «улетел, так улетел».
/// </summary>
public class Simulation
{
    public const double G = 1400;          // ускорение свободного падения, px/s^2
    public const double Restitution = 1.0; // упругость: 1 = вечный рикошет без потерь
    public const double MaxSpeed = 2600;
    public const double OutMargin = 400;   // насколько дальше поля шар считается улетевшим

    public List<SimBall> Balls = new();
    public bool GravityOn;
    public double GravityScale = 1.0;
    public bool ElasticBounce;
    public double SpinSpeed;               // рад/с: вращение стенок и дуг прямо внутри подшагов
    public bool HasBorder;
    public bool BallCollisions = true;     // можно отключить уровнем (NoBallCollisions)
    public bool Finished;                  // какой-то шар коснулся цели

    private readonly List<Wall> walls = new();
    private readonly List<CircleData> circles = new();
    private readonly List<SimArc> arcs = new();
    private readonly List<List<Vec2>> goals = new();

    // --- пространственная сетка стен: ячейка 64px, в каждой — сегменты, её ---
    // --- пересекающие. Проверяются только сегменты рядом с шаром:          ---
    // --- веер из тысяч лучей не сканирует все сегменты фигуры.             ---
    private struct SegRef { public Wall W; public int I; }
    private List<SegRef>[] gridCells;
    private const int Cell = 64, GW = 20, GH = 12;

    private static int CellX(double x) { int c = (int)Math.Floor(x / Cell); return c < 0 ? 0 : c > GW - 1 ? GW - 1 : c; }
    private static int CellY(double y) { int c = (int)Math.Floor(y / Cell); return c < 0 ? 0 : c > GH - 1 ? GH - 1 : c; }

    private void BuildGrid()
    {
        gridCells ??= new List<SegRef>[GW * GH];
        foreach (var cell in gridCells)
            cell?.Clear(); // ячейки переиспользуются — поворот стенок не аллоцирует
        foreach (var w in walls)
        {
            int n = w.Pts.Count;
            int segCount = w.Closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a = w.Pts[i];
                var b = w.Pts[(i + 1) % n];
                int cx0 = CellX(Math.Min(a.X, b.X)), cx1 = CellX(Math.Max(a.X, b.X));
                int cy0 = CellY(Math.Min(a.Y, b.Y)), cy1 = CellY(Math.Max(a.Y, b.Y));
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        int k = cy * GW + cx;
                        (gridCells[k] ??= new List<SegRef>(4)).Add(new SegRef { W = w, I = i });
                    }
            }
        }
    }

    /// <summary>Повернуть все стенки и дуги вокруг их центров вращения (режим «Ротор»);
    /// пространственная сетка перестраивается с переиспользованием ячеек.</summary>
    public void RotateWalls(double dAngle)
    {
        if (walls.Count > 0)
        {
            double cos = Math.Cos(dAngle), sin = Math.Sin(dAngle);
            foreach (var w in walls)
            {
                var pv = w.Pivot;
                for (int i = 0; i < w.Pts.Count; i++)
                {
                    double dx = w.Pts[i].X - pv.X, dy = w.Pts[i].Y - pv.Y;
                    w.Pts[i] = new Vec2(pv.X + dx * cos - dy * sin, pv.Y + dx * sin + dy * cos);
                }
                w.UpdateBounds();
            }
            BuildGrid();
        }
        RotateArcs(dAngle);
    }

    public void RotateArcs(double dAngle)
    {
        const double TwoPi = Math.PI * 2;
        foreach (var a in arcs)
        {
            a.StartRad = (a.StartRad + dAngle) % TwoPi;
            if (a.StartRad < 0) a.StartRad += TwoPi;
        }
    }

    /// <summary>Стенки симуляции (для рендера вращающихся/удаляемых стен).</summary>
    public IReadOnlyList<Wall> Walls => walls;
    /// <summary>Живые аналитические дуги симуляции (режим «Ротор»).</summary>
    public List<SimArc> Arcs => arcs;

    public Wall FindWall(PolyData src)
    {
        foreach (var w in walls)
            if (ReferenceEquals(w.Src, src)) return w;
        return null;
    }

    /// <summary>Убрать стенку из симуляции (например, круг рассыпался) — сетка перестраивается.</summary>
    public bool RemoveWall(PolyData src)
    {
        for (int i = 0; i < walls.Count; i++)
            if (ReferenceEquals(walls[i].Src, src))
            {
                walls.RemoveAt(i);
                BuildGrid();
                return true;
            }
        return false;
    }

    /// <summary>Убрать дугу из симуляции (пройденное кольцо рассыпалось).</summary>
    public bool RemoveArc(SimArc target) => arcs.Remove(target);

    public void ResetFrom(LevelData lvl)
    {
        GravityOn = lvl.Gravity;
        GravityScale = lvl.GravityScale <= 0 ? 1.0 : lvl.GravityScale;
        ElasticBounce = lvl.ElasticBounce;
        HasBorder = lvl.Border;
        BallCollisions = !lvl.NoBallCollisions;
        Finished = false;
        walls.Clear();
        foreach (var w in lvl.Walls)
            if (w.Pts.Count >= 2) walls.Add(new Wall(w.Pts, w.Closed, w.Pivot, w));
        BuildGrid();
        circles.Clear();
        circles.AddRange(lvl.Circles);
        arcs.Clear();
        foreach (var a in lvl.Arcs)
        {
            double s = (a.StartDeg * Math.PI / 180.0) % (Math.PI * 2);
            if (s < 0) s += Math.PI * 2;
            arcs.Add(new SimArc
            {
                Center = a.Center,
                Radius = a.Radius,
                StartRad = s,
                SweepRad = Math.Clamp(a.SweepDeg, 1, 359.9) * Math.PI / 180.0,
                HalfWidth = a.HalfWidth,
                Src = a,
            });
        }
        goals.Clear();
        foreach (var g in lvl.Goals)
            if (g.Pts.Count >= 3) goals.Add(g.Pts);

        Balls = lvl.Balls
            .Select(b =>
            {
                var sb = new SimBall { Pos = b.Pos, Vel = b.HasVel ? b.Vel : new Vec2(), R = b.R, Argb = b.Argb };
                if (arcs.Count > 0) sb.MinCenterDist = Vec2.Dist(sb.Pos, arcs[0].Center);
                return sb;
            })
            .ToList();
    }

    public SimBall AddBall(Vec2 pos, Vec2 vel, double r, int hue = -1, int argb = 0)
    {
        var b = new SimBall
        {
            Pos = pos,
            Vel = ClampSpeed(vel),
            R = r,
            Hue = hue,
            Argb = argb,
            MinCenterDist = arcs.Count > 0 ? Vec2.Dist(pos, arcs[0].Center) : double.MaxValue,
        };
        Balls.Add(b);
        return b;
    }

    public static Vec2 ClampSpeed(Vec2 v)
    {
        var l = v.Len;
        return l > MaxSpeed ? v * (MaxSpeed / l) : v;
    }

    public void Step(double dt)
    {
        if (dt <= 0) return;
        if (dt > 0.3) dt = 0.3; // запас на множитель скорости (сырой кадр уже ограничен в полотне)

        // адаптивные подшаги, чтобы быстрые маленькие шары не проскакивали сквозь стенки.
        // Предел 2 (не 4): за подшаг шар смещается не более чем на 0.6·R и не может
        // пересечь линию стенки насквозь (при R=2 и пределе 4 смещение 2.4px выкидывало
        // лучи за фигуру — нормаль отражения разворачивалась наружу)
        double minR = 12, maxV = 0;
        foreach (var bb in Balls)
        {
            if (bb.R < minR) minR = bb.R;
            var vl = bb.Vel.Len;
            if (vl > maxV) maxV = vl;
        }
        if (minR < 2) minR = 2;
        int sub = (int)Math.Clamp(Math.Ceiling(maxV * dt / (minR * 0.6)), 1, 40);
        double h = dt / sub;
        double grav = G * GravityScale;

        for (int s = 0; s < sub; s++)
        {
            if (SpinSpeed != 0) RotateWalls(SpinSpeed * h);
            foreach (var b in Balls)
            {
                if (GravityOn) b.Vel.Y += grav * h;
                b.Pos += b.Vel * h;
                if (HasBorder) CollideBounds(b);
                if (walls.Count > 0) CollideWalls(b);
                foreach (var c in circles) CollideCircle(b, c);
                if (arcs.Count > 0)
                {
                    for (int ai = 0; ai < arcs.Count; ai++) CollideArc(b, arcs[ai]);
                    double dc = Vec2.Dist(b.Pos, arcs[0].Center);
                    if (dc < b.MinCenterDist) b.MinCenterDist = dc;
                }
            }
            if (BallCollisions) CollideBalls();
        }

        if (!HasBorder)
        {
            // Пока вокруг центра остаются внешние дуги («Ротор»), шар внутри радиуса внешней дуги не удаляется
            double safeR = arcs.Count > 0 ? arcs[^1].Radius + OutMargin : 0;
            Vec2 ac = arcs.Count > 0 ? arcs[0].Center : new Vec2();
            Balls.RemoveAll(b =>
                (arcs.Count == 0 || Vec2.Dist(b.Pos, ac) > safeR) &&
                (b.Pos.X < -OutMargin || b.Pos.X > LevelData.W + OutMargin ||
                 b.Pos.Y < -OutMargin || b.Pos.Y > LevelData.H + OutMargin));
        }

        if (!Finished)
            foreach (var b in Balls)
                if (HitGoal(b.Pos, b.R)) { Finished = true; break; }
    }

    private void CollideBounds(SimBall b)
    {
        if (b.Pos.X < b.R)               { b.Pos.X = b.R;               if (b.Vel.X < 0) b.Vel.X = ReflectV(-b.Vel.X); }
        if (b.Pos.X > LevelData.W - b.R) { b.Pos.X = LevelData.W - b.R; if (b.Vel.X > 0) b.Vel.X = -ReflectV(b.Vel.X); }
        if (b.Pos.Y < b.R)               { b.Pos.Y = b.R;               if (b.Vel.Y < 0) b.Vel.Y = ReflectV(-b.Vel.Y); }
        if (b.Pos.Y > LevelData.H - b.R) { b.Pos.Y = LevelData.H - b.R; if (b.Vel.Y > 0) b.Vel.Y = -ReflectV(b.Vel.Y); }
    }

    // при включённой гравитации микро-отскоки глушим, чтобы шар «улёгся», а не дрожал
    private double ReflectV(double speed) =>
        GravityOn && !ElasticBounce && speed < 40 ? speed * 0.4 : speed * Restitution;

    /// <summary>
    /// Столкновение с фигурой: по ячейкам сетки рядом с шаром собирается ОДНА
    /// ближайшая точка по всем попадающим в них сегментам (для замкнутых —
    /// включая стык последняя-первая) и выполняется одно отражение.
    /// Так корректно обрабатываются вершины: шар у стыка двух сегментов отражается
    /// один раз о нормаль (шар − вершина), а не дважды о нормали каждого сегмента.
    /// Сегмент ближе радиуса шара обязан лежать в соседних ячейках, поэтому
    /// отсечение сеткой не меняет результат полного перебора.
    /// </summary>
    private void CollideWalls(SimBall b)
    {
        if (gridCells == null) BuildGrid(); // симуляция без ResetFrom — строим по текущим стенам
        int cx0 = CellX(b.Pos.X - b.R - 1), cx1 = CellX(b.Pos.X + b.R + 1);
        int cy0 = CellY(b.Pos.Y - b.R - 1), cy1 = CellY(b.Pos.Y + b.R + 1);

        double best = double.MaxValue;
        var bestPt = new Vec2();
        bool found = false;
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                var cell = gridCells[cy * GW + cx];
                if (cell == null) continue;
                foreach (var s in cell)
                {
                    var cp = ClosestPtSeg(b.Pos, s.W.Pts[s.I], s.W.Pts[(s.I + 1) % s.W.Pts.Count]);
                    double d = Vec2.Dist(b.Pos, cp);
                    if (d < best) { best = d; bestPt = cp; found = true; }
                }
            }
        if (!found || best >= b.R) return;

        var nrm = b.Pos - bestPt;
        double len = nrm.Len;
        var nn = len > 1e-9 ? nrm * (1.0 / len) : new Vec2(0, -1);
        b.Pos = bestPt + nn * (b.R + 0.01);
        double vn = Vec2.Dot(b.Vel, nn);
        if (vn < 0)
        {
            double e = GravityOn && !ElasticBounce && -vn < 40 ? 0.4 : Restitution;
            b.Vel -= nn * ((1 + e) * vn);
        }
    }

    /// <summary>
    /// Аналитическая круговая арена: отражение строго по радиальной нормали.
    /// Луч из центра всегда возвращается точно в центр (свойство диаметров) —
    /// погрешностей многоугольной аппроксимации нет совсем.
    /// </summary>
    private void CollideCircle(SimBall b, CircleData c)
    {
        var d = b.Pos - c.Center;
        double dist = d.Len;
        double lim = c.Radius - b.R;
        if (dist <= lim) return;
        if (dist < 1e-9) { b.Vel = -b.Vel; return; } // редкий случай: центр точно в центре арены
        var n = d * (1.0 / dist);                    // наружная нормаль
        b.Pos = c.Center + n * (lim - 0.01);
        double vn = Vec2.Dot(b.Vel, n);
        if (vn > 0)
        {
            double e = GravityOn && !ElasticBounce && vn < 40 ? 0.4 : Restitution;
            b.Vel -= n * ((1 + e) * vn);
        }
    }

    /// <summary>
    /// Аналитическая неполная окружность (дуга с разрывом): мгновенный отсев по радиусу,
    /// точное радиальное отражение на теле дуги и отражение от движущихся краёв разрыва.
    /// </summary>
    private void CollideArc(SimBall b, SimArc arc)
    {
        double dx = b.Pos.X - arc.Center.X, dy = b.Pos.Y - arc.Center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double reach = b.R + arc.HalfWidth;
        if (Math.Abs(dist - arc.Radius) >= reach) return;

        bool fromInside = b.MinCenterDist < arc.Radius - arc.HalfWidth;
        double ang = Math.Atan2(dy, dx);
        if (arc.IsInSolid(ang))
        {
            if (dist < 1e-9) { b.Vel = -b.Vel; return; }
            double inv = 1.0 / dist;
            double rx = dx * inv, ry = dy * inv;
            Vec2 nn;
            if (dist <= arc.Radius)
            {
                nn = new Vec2(-rx, -ry);
                b.Pos = new Vec2(arc.Center.X + rx * (arc.Radius - reach - 0.01),
                                 arc.Center.Y + ry * (arc.Radius - reach - 0.01));
            }
            else if (!fromInside)
            {
                nn = new Vec2(rx, ry);
                b.Pos = new Vec2(arc.Center.X + rx * (arc.Radius + reach + 0.01),
                                 arc.Center.Y + ry * (arc.Radius + reach + 0.01));
            }
            else
            {
                // Шар изнутри на границе разрыва: проверяем край дуги, но никогда не выталкиваем наружу сквозь внешние кольца
                CollideArcLip(b, arc, reach, clampOutward: true);
                return;
            }
            double vn = Vec2.Dot(b.Vel, nn);
            if (vn < 0)
            {
                double e = GravityOn && !ElasticBounce && -vn < 40 ? 0.4 : Restitution;
                b.Vel -= nn * ((1 + e) * vn);
            }
        }
        else
        {
            CollideArcLip(b, arc, reach, clampOutward: false);
        }
    }

    private void CollideArcLip(SimBall b, SimArc arc, double reach, bool clampOutward)
    {
        double a1 = arc.StartRad, a2 = arc.StartRad + arc.SweepRad;
        var e1 = new Vec2(arc.Center.X + Math.Cos(a1) * arc.Radius, arc.Center.Y + Math.Sin(a1) * arc.Radius);
        var e2 = new Vec2(arc.Center.X + Math.Cos(a2) * arc.Radius, arc.Center.Y + Math.Sin(a2) * arc.Radius);
        double d1 = Vec2.Dist(b.Pos, e1), d2 = Vec2.Dist(b.Pos, e2);
        bool first = d1 <= d2;
        var lip = first ? e1 : e2;
        double dLip = first ? d1 : d2;
        if (dLip >= reach || dLip < 1e-9) return;

        Vec2 nn;
        if (clampOutward)
        {
            // Толкаем касательно в сторону открытого выреза, не увеличивая радиус
            var radDir = (lip - arc.Center) * (1.0 / Math.Max(1.0, arc.Radius));
            nn = first ? -Vec2.Perp(radDir) : Vec2.Perp(radDir);
        }
        else
        {
            nn = (b.Pos - lip) * (1.0 / dLip);
        }
        b.Pos = lip + nn * (reach + 0.01);
        var vLip = Vec2.Perp(lip - arc.Center) * SpinSpeed;
        double vn = Vec2.Dot(b.Vel - vLip, nn);
        if (vn < 0)
        {
            double e = GravityOn && !ElasticBounce && -vn < 40 ? 0.4 : Restitution;
            b.Vel = ClampSpeed(b.Vel - nn * ((1 + e) * vn));
        }
    }

    public static Vec2 ClosestPtSeg(Vec2 p, Vec2 a, Vec2 b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 1e-9) return a;
        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        return new Vec2(a.X + abx * t, a.Y + aby * t);
    }

    private void CollideBalls()
    {
        for (int i = 0; i < Balls.Count; i++)
            for (int j = i + 1; j < Balls.Count; j++)
            {
                var a = Balls[i];
                var bb = Balls[j];
                var d = bb.Pos - a.Pos;
                double dist = d.Len;
                double sum = a.R + bb.R;
                if (dist >= sum || dist < 1e-9) continue;

                var n = d * (1.0 / dist);
                double overlap = sum - dist;
                a.Pos -= n * (overlap * 0.5);
                bb.Pos += n * (overlap * 0.5);
                double vn = Vec2.Dot(bb.Vel - a.Vel, n);
                if (vn < 0)
                {
                    a.Vel += n * vn;   // упругий обмен при равных массах, e = 1
                    bb.Vel -= n * vn;
                }
            }
    }

    public bool HitGoal(Vec2 p, double r)
    {
        foreach (var poly in goals)
        {
            if (PointInPoly(p, poly)) return true;
            int n = poly.Count;
            double md = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double d = DistPtSeg(p, poly[i], poly[(i + 1) % n]);
                if (d < md) md = d;
            }
            if (md < r * 0.9) return true;
        }
        return false;
    }

    public static bool PointInPoly(Vec2 p, List<Vec2> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    public static double DistPtSeg(Vec2 p, Vec2 a, Vec2 b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 1e-9) return Vec2.Dist(p, a);
        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        return Vec2.Dist(p, new Vec2(a.X + abx * t, a.Y + aby * t));
    }
}

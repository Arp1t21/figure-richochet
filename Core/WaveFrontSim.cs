using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ricochet.Core;

/// <summary>
/// Реалистичная волновая модель для математических бильярдов:
/// 1) Волновой фронт хранится как связные цепочки/петли лучей и адаптивно уплотняется
///    (вставляет новые лучи с эрмитовой интерполяцией кривизны там, где фронт растягивается),
///    поэтому волна остаётся непрерывной и не рвётся на отдельные точки.
/// 2) На острых углах многоугольников и свободных концах перегородок рождаются вторичные
///    цилиндрические дифракционные волны (геометрическая теория дифракции Келлера).
/// </summary>
public sealed class WaveFrontSim
{
    public const double DefaultWaveSpeed = 320.0;
    public const int MaxNodes = 9600;
    private const double SplitDist = 5.5;          // базовый порог растяжения сегмента фронта, px
    private const double SplitDist2 = SplitDist * SplitDist;
    private const double MaxTearDist = 15.0;       // порог разрыва фронта при расхождении на угле/препятствии, px
    private const double MaxTearDist2 = MaxTearDist * MaxTearDist;
    private const double SmoothCosMin = 0.88;      // cos(~28°): условие гладкости участка для вставки луча
    private const double TearCosMax = 0.84;        // при расхождении направлений и длине > MaxTearDist фронт рвётся
    private const double VertexCaptureR = 4.6;     // радиус захвата вершины проходящим фронтом, px
    private const double VertexCooldown = 0.22;    // мин. интервал повторного излучения одной вершины, с
    private const float MinIntensity = 0.045f;     // порог затухания тонких третичных нитей

    public struct WaveNode
    {
        public Vec2 Pos;
        public Vec2 PrevPos;
        public Vec2 FrameStartPos;  // позиция в начале кадра (для сплошного вечного следа без разрывов)
        public Vec2 Vel;
        public int Next;            // индекс следующего узла в цепочке фронта (-1 = разрыв/конец)
        public int BounceId;        // идентификатор последней отразившей грани/окружности
        public int Hue;             // цветовой тон 0..359 (-1 = фиксированный Argb)
        public int Argb;            // явный цвет (0 = по Hue/белый)
        public float Intensity;     // амплитуда участка фронта (0..1)
        public byte Gen;            // поколение волны (0 — первичная, 1..2 — дифракционные)
        public byte Alive;
    }

    private struct Seg
    {
        public Vec2 A, B, E, N;     // концы, вектор A->B и единичная нормаль
        public double Len;
        public int Id;
    }

    private struct DiffVertex
    {
        public Vec2 Pos;
        public double StartRad;     // начало допустимого внутреннего сектора излучения
        public double SweepRad;     // раствор сектора внутри области бильярда
        public bool FullTip;        // свободный конец разреза/перегородки (~360°)
        public double LastEmitTime;
    }

    public WaveNode[] Nodes = new WaveNode[4096];
    public int NodeCount { get; private set; }
    public int DiffractionCount { get; private set; }

    public bool GravityOn;
    public double GravityScale = 1.0;
    public bool HasBorder;
    public double WaveSpeed = DefaultWaveSpeed;

    private readonly List<Seg> segs = new();
    private readonly List<CircleData> circles = new();
    private readonly List<PolyData> closedPolys = new();
    private readonly List<DiffVertex> vertices = new();
    private List<int>[] grid;
    private const int Cell = 64, GW = 20, GH = 12;

    private double simTime;
    private int domainParity = 1;

    private struct PendingDiff
    {
        public int VertexIdx;
        public int Hue;
        public int Argb;
        public float Intensity;
        public byte Gen;
    }

    private readonly List<PendingDiff> pendingDiffs = new(32);
    private int[] compactMap = new int[4096];
    private bool[] hasIncoming = new bool[4096];

    private static int CellX(double x) { int c = (int)Math.Floor(x / Cell); return c < 0 ? 0 : c > GW - 1 ? GW - 1 : c; }
    private static int CellY(double y) { int c = (int)Math.Floor(y / Cell); return c < 0 ? 0 : c > GH - 1 ? GH - 1 : c; }

    /// <summary>Загрузить геометрию фигуры, построить сетку сегментов и найти дифракционные вершины.</summary>
    public void ResetFrom(LevelData lvl, Vec2 source)
    {
        GravityOn = lvl.Gravity;
        GravityScale = lvl.GravityScale <= 0 ? 1.0 : lvl.GravityScale;
        HasBorder = lvl.Border;
        simTime = 0;
        NodeCount = 0;
        DiffractionCount = 0;

        segs.Clear();
        circles.Clear();
        closedPolys.Clear();
        vertices.Clear();

        circles.AddRange(lvl.Circles);
        int nextId = 1;
        foreach (var w in lvl.Walls)
        {
            if (w.Pts.Count < 2) continue;
            if (w.Closed && w.Pts.Count >= 3) closedPolys.Add(w);
            int n = w.Pts.Count;
            int cnt = w.Closed ? n : n - 1;
            for (int i = 0; i < cnt; i++)
            {
                var a = w.Pts[i];
                var b = w.Pts[(i + 1) % n];
                var e = b - a;
                double len = e.Len;
                if (len < 1e-6) continue;
                var inv = 1.0 / len;
                segs.Add(new Seg
                {
                    A = a,
                    B = b,
                    E = e,
                    N = new Vec2(-e.Y * inv, e.X * inv),
                    Len = len,
                    Id = nextId++,
                });
            }
        }

        foreach (var arc in lvl.Arcs)
        {
            if (arc.Radius < 2 || arc.Radius > 820 || arc.SweepDeg < 2) continue;
            int steps = Math.Clamp((int)Math.Ceiling(arc.SweepDeg / 4.0), 6, 96);
            double s0 = arc.StartDeg * Math.PI / 180.0;
            double sw = Math.Min(360.0, arc.SweepDeg) * Math.PI / 180.0;
            var prev = new Vec2(arc.Center.X + Math.Cos(s0) * arc.Radius, arc.Center.Y + Math.Sin(s0) * arc.Radius);
            for (int i = 1; i <= steps; i++)
            {
                double ang = s0 + sw * i / steps;
                var cur = new Vec2(arc.Center.X + Math.Cos(ang) * arc.Radius, arc.Center.Y + Math.Sin(ang) * arc.Radius);
                var e = cur - prev;
                double len = e.Len;
                if (len >= 1e-6)
                {
                    double inv = 1.0 / len;
                    segs.Add(new Seg
                    {
                        A = prev,
                        B = cur,
                        E = e,
                        N = new Vec2(-e.Y * inv, e.X * inv),
                        Len = len,
                        Id = nextId++,
                    });
                }
                prev = cur;
            }
        }

        BuildGrid();
        domainParity = ComputeDepth(source) & 1;
        ExtractDiffractingVertices(lvl);
    }

    private void BuildGrid()
    {
        grid ??= new List<int>[GW * GH];
        for (int i = 0; i < grid.Length; i++) grid[i]?.Clear();

        for (int si = 0; si < segs.Count; si++)
        {
            var s = segs[si];
            int cx0 = CellX(Math.Min(s.A.X, s.B.X) - 1), cx1 = CellX(Math.Max(s.A.X, s.B.X) + 1);
            int cy0 = CellY(Math.Min(s.A.Y, s.B.Y) - 1), cy1 = CellY(Math.Max(s.A.Y, s.B.Y) + 1);
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    (grid[cy * GW + cx] ??= new List<int>(4)).Add(si);
        }
    }

    private int ComputeDepth(Vec2 p)
    {
        int d = 0;
        for (int i = 0; i < circles.Count; i++)
            if (Vec2.Dist(p, circles[i].Center) < circles[i].Radius) d++;
        for (int i = 0; i < closedPolys.Count; i++)
            if (Simulation.PointInPoly(p, closedPolys[i].Pts)) d++;
        return d;
    }

    private bool IsInsideDomain(Vec2 p)
    {
        if (p.X < 0 || p.X > LevelData.W || p.Y < 0 || p.Y > LevelData.H) return false;
        if (circles.Count == 0 && closedPolys.Count == 0) return true;
        return (ComputeDepth(p) & 1) == domainParity;
    }

    private void ExtractDiffractingVertices(LevelData lvl)
    {
        const double TwoPi = Math.PI * 2.0;
        const double MinCornerTurnRad = 26.0 * Math.PI / 180.0; // гладкие аппроксимации эллипса/сердца не дробят фронт

        foreach (var w in lvl.Walls)
        {
            int n = w.Pts.Count;
            if (n < 2) continue;

            if (!w.Closed)
            {
                // Свободные концы разомкнутых перегородок (дифракция Келлера в секторе ~360°)
                AddOpenEndpoint(w.Pts[0], w.Pts[1]);
                AddOpenEndpoint(w.Pts[n - 1], w.Pts[n - 2]);

                for (int i = 1; i < n - 1; i++)
                    TryAddCornerVertex(w.Pts[i - 1], w.Pts[i], w.Pts[i + 1], MinCornerTurnRad, TwoPi);
            }
            else if (n >= 3)
            {
                for (int i = 0; i < n; i++)
                {
                    var prev = w.Pts[(i - 1 + n) % n];
                    var cur = w.Pts[i];
                    var next = w.Pts[(i + 1) % n];
                    TryAddCornerVertex(prev, cur, next, MinCornerTurnRad, TwoPi);
                }
            }
        }

        foreach (var arc in lvl.Arcs)
        {
            if (arc.Radius < 4 || arc.Radius > 820 || arc.SweepDeg < 2 || arc.SweepDeg >= 359) continue;
            double s0 = arc.StartDeg * Math.PI / 180.0;
            double sw = arc.SweepDeg * Math.PI / 180.0;
            double step = Math.Max(0.03, sw / Math.Clamp((int)Math.Ceiling(arc.SweepDeg / 4.0), 6, 96));
            var tip0 = new Vec2(arc.Center.X + Math.Cos(s0) * arc.Radius, arc.Center.Y + Math.Sin(s0) * arc.Radius);
            var nb0 = new Vec2(arc.Center.X + Math.Cos(s0 + step) * arc.Radius, arc.Center.Y + Math.Sin(s0 + step) * arc.Radius);
            var tip1 = new Vec2(arc.Center.X + Math.Cos(s0 + sw) * arc.Radius, arc.Center.Y + Math.Sin(s0 + sw) * arc.Radius);
            var nb1 = new Vec2(arc.Center.X + Math.Cos(s0 + sw - step) * arc.Radius, arc.Center.Y + Math.Sin(s0 + sw - step) * arc.Radius);
            AddOpenEndpoint(tip0, nb0);
            AddOpenEndpoint(tip1, nb1);
        }
    }

    private void AddOpenEndpoint(Vec2 tip, Vec2 neighbor)
    {
        var away = (tip - neighbor).Norm();
        if (away.Len2 < 0.5) return;
        // Проверяем, что конец перегородки действительно лежит внутри игровой области
        if (!IsInsideDomain(tip + away * 2.2)) return;

        double wallAng = Math.Atan2(neighbor.Y - tip.Y, neighbor.X - tip.X);
        const double eps = 0.035;
        vertices.Add(new DiffVertex
        {
            Pos = tip,
            StartRad = wallAng + eps,
            SweepRad = Math.PI * 2.0 - 2.0 * eps,
            FullTip = true,
            LastEmitTime = -10.0,
        });
    }

    private void TryAddCornerVertex(Vec2 prev, Vec2 cur, Vec2 next, double minTurn, double twoPi)
    {
        var eIn = (cur - prev).Norm();
        var eOut = (next - cur).Norm();
        if (eIn.Len2 < 0.5 || eOut.Len2 < 0.5) return;

        double dot = Math.Clamp(Vec2.Dot(eIn, eOut), -1.0, 1.0);
        double turn = Math.Acos(dot);
        if (turn < minTurn) return;

        // Лучи вдоль обеих граней от вершины делят плоскость на два угловых сектора
        double a1 = Math.Atan2(prev.Y - cur.Y, prev.X - cur.X);
        double a2 = Math.Atan2(next.Y - cur.Y, next.X - cur.X);
        if (a1 < 0) a1 += twoPi;
        if (a2 < 0) a2 += twoPi;

        double sweep1 = a2 - a1;
        if (sweep1 < 0) sweep1 += twoPi;
        double sweep2 = twoPi - sweep1;

        TryAddSector(cur, a1, sweep1);
        TryAddSector(cur, a2, sweep2);
    }

    private void TryAddSector(Vec2 v, double startRad, double sweepRad)
    {
        if (sweepRad < 0.12) return;
        double mid = startRad + sweepRad * 0.5;
        var probe = v + new Vec2(Math.Cos(mid), Math.Sin(mid)) * 2.5;
        if (!IsInsideDomain(probe)) return;
        if (SegmentHitsAnyWall(v + new Vec2(Math.Cos(mid), Math.Sin(mid)) * 0.25, probe)) return;

        const double eps = 0.03;
        if (sweepRad <= 2.0 * eps) return;
        vertices.Add(new DiffVertex
        {
            Pos = v,
            StartRad = startRad + eps,
            SweepRad = sweepRad - 2.0 * eps,
            FullTip = false,
            LastEmitTime = -10.0,
        });
    }

    /// <summary>Испустить первичный замкнутый круговой волновой фронт из источника.</summary>
    public void EmitPointSource(Vec2 source, int initialRays, double speed, bool spectrum, int fixedArgb)
    {
        WaveSpeed = speed;
        NodeCount = 0;
        DiffractionCount = 0;
        simTime = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            v.LastEmitTime = -10.0;
            vertices[i] = v;
        }

        int count = Math.Clamp(initialRays, 24, 8192);
        EnsureCapacity(count + 256);

        const double TwoPi = Math.PI * 2.0;
        for (int i = 0; i < count; i++)
        {
            double a = i * TwoPi / count;
            var dir = new Vec2(Math.Cos(a), Math.Sin(a));
            var pos = source + dir * 0.5;
            Nodes[i] = new WaveNode
            {
                Pos = pos,
                PrevPos = source,
                FrameStartPos = source,
                Vel = dir * speed,
                Next = (i + 1) % count,
                BounceId = 0,
                Hue = spectrum ? (int)(i * 360.0 / count) : -1,
                Argb = spectrum ? 0 : fixedArgb,
                Intensity = 1.0f,
                Gen = 0,
                Alive = 1,
            };
        }
        NodeCount = count;
    }

    private void EnsureCapacity(int needed)
    {
        if (Nodes.Length >= needed) return;
        int cap = Nodes.Length;
        while (cap < needed) cap *= 2;
        if (cap > MaxNodes + 2048) cap = MaxNodes + 2048;
        Array.Resize(ref Nodes, cap);
    }

    /// <summary>Шаг эволюции волнового фронта: перенос лучей, дифракция на вершинах и адаптивная вставка лучей.</summary>
    public void Step(double dt)
    {
        if (dt <= 0 || NodeCount == 0) return;
        if (dt > 0.08) dt = 0.08;

        for (int i = 0; i < NodeCount; i++)
            if (Nodes[i].Alive != 0)
                Nodes[i].FrameStartPos = Nodes[i].Pos;

        // Разбиваем шаг так, чтобы за один подшаг фронт сдвигался не более чем на ~4.5 px —
        // тогда ни одна вершина не пропускается, а вставка лучей идёт плавно.
        int sub = Math.Clamp((int)Math.Ceiling(WaveSpeed * dt / 4.5), 1, 8);
        double h = dt / sub;
        double gEff = GravityOn ? Simulation.G * GravityScale : 0.0;

        for (int s = 0; s < sub; s++)
        {
            simTime += h;
            AdvanceAllNodes(h, gEff);
            DetectAndSpawnDiffraction();
            RefineAndSplitFront();
            if (NodeCount > MaxNodes - 384)
                CompactDeadOrFaintNodes();
        }
    }

    private void AdvanceAllNodes(double h, double gEff)
    {
        int n = NodeCount;
        if (n >= 768)
        {
            const int chunks = 12;
            Parallel.For(0, chunks, ch =>
            {
                int i0 = ch * n / chunks;
                int i1 = (ch + 1) * n / chunks;
                for (int i = i0; i < i1; i++)
                    if (Nodes[i].Alive != 0)
                        AdvanceOneNode(ref Nodes[i], h, gEff);
            });
        }
        else
        {
            for (int i = 0; i < n; i++)
                if (Nodes[i].Alive != 0)
                    AdvanceOneNode(ref Nodes[i], h, gEff);
        }
    }

    private void AdvanceOneNode(ref WaveNode node, double h, double gEff)
    {
        node.PrevPos = node.Pos;
        if (gEff != 0)
        {
            node.Vel.Y += 0.5 * gEff * h;
            TraceSegment(ref node, h);
            node.Vel.Y += 0.5 * gEff * h;
            node.Vel = Simulation.ClampSpeed(node.Vel);
        }
        else
        {
            TraceSegment(ref node, h);
        }

        // Если границы отключены и луч улетел далеко за сцену — гасим его
        if (!HasBorder &&
            (node.Pos.X < -Simulation.OutMargin || node.Pos.X > LevelData.W + Simulation.OutMargin ||
             node.Pos.Y < -Simulation.OutMargin || node.Pos.Y > LevelData.H + Simulation.OutMargin))
        {
            node.Alive = 0;
            node.Next = -1;
        }
    }

    private void TraceSegment(ref WaveNode node, double dt)
    {
        double rem = dt;
        int ignoreId = 0;
        for (int bounce = 0; bounce < 6 && rem > 1e-8; bounce++)
        {
            var p = node.Pos;
            var d = node.Vel * rem;
            double dLen = d.Len;
            if (dLen < 1e-9) break;

            double bestS = 2.0;
            Vec2 bestN = default;
            int bestId = 0;

            // 1. Рамка поля (если включена)
            if (HasBorder)
            {
                CheckBound(p.X, d.X, 1.0, new Vec2(1, 0), -1001, ignoreId, ref bestS, ref bestN, ref bestId);
                CheckBound(p.X, d.X, LevelData.W - 1.0, new Vec2(-1, 0), -1002, ignoreId, ref bestS, ref bestN, ref bestId);
                CheckBound(p.Y, d.Y, 1.0, new Vec2(0, 1), -1003, ignoreId, ref bestS, ref bestN, ref bestId);
                CheckBound(p.Y, d.Y, LevelData.H - 1.0, new Vec2(0, -1), -1004, ignoreId, ref bestS, ref bestN, ref bestId);
            }

            // 2. Аналитические окружности
            for (int ci = 0; ci < circles.Count; ci++)
            {
                int cid = -(ci + 1);
                if (cid == ignoreId) continue;
                var c = circles[ci];
                var oc = p - c.Center;
                double r = Math.Max(1.0, c.Radius - 0.5);
                double a = d.Len2;
                double bh = Vec2.Dot(oc, d);
                double cv = oc.Len2 - r * r;
                double disc = bh * bh - a * cv;
                if (disc < 0) continue;
                double sq = Math.Sqrt(disc);

                if (cv <= 1e-3)
                {
                    // Луч внутри круга: ищем выход наружу
                    double s = (-bh + sq) / a;
                    if (s >= 0.0 && s < bestS && s <= 1.0)
                    {
                        var hit = oc + d * s;
                        if (Vec2.Dot(d, hit) > 0)
                        {
                            bestS = s;
                            bestN = (-hit).Norm();
                            bestId = cid;
                        }
                    }
                }
                else
                {
                    // Луч снаружи круга-препятствия: ищем вход
                    double s = (-bh - sq) / a;
                    if (s >= 0.0 && s < bestS && s <= 1.0)
                    {
                        var hit = oc + d * s;
                        if (Vec2.Dot(d, hit) < 0)
                        {
                            bestS = s;
                            bestN = hit.Norm();
                            bestId = cid;
                        }
                    }
                }
            }

            // 3. Отрезки стен по пространственной сетке
            if (segs.Count > 0)
            {
                var pEnd = p + d;
                int cx0 = CellX(Math.Min(p.X, pEnd.X) - 1), cx1 = CellX(Math.Max(p.X, pEnd.X) + 1);
                int cy0 = CellY(Math.Min(p.Y, pEnd.Y) - 1), cy1 = CellY(Math.Max(p.Y, pEnd.Y) + 1);
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    int row = cy * GW;
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        var cell = grid[row + cx];
                        if (cell == null) continue;
                        for (int k = 0; k < cell.Count; k++)
                        {
                            var seg = segs[cell[k]];
                            if (seg.Id == ignoreId) continue;
                            double denom = d.X * seg.E.Y - d.Y * seg.E.X;
                            if (Math.Abs(denom) < 1e-11) continue;
                            double apx = seg.A.X - p.X, apy = seg.A.Y - p.Y;
                            double s = (apx * seg.E.Y - apy * seg.E.X) / denom;
                            if (s < -1e-7 || s > 1.0 || s >= bestS) continue;
                            double u = (apx * d.Y - apy * d.X) / denom;
                            if (u < -1e-6 || u > 1.0 + 1e-6) continue;

                            var nrm = Vec2.Dot(d, seg.N) < 0 ? seg.N : -seg.N;
                            bestS = Math.Max(0.0, s);
                            bestN = nrm;
                            bestId = seg.Id;
                        }
                    }
                }
            }

            if (bestS > 1.0)
            {
                node.Pos = p + d;
                break;
            }

            // Останавливаемся на микро-отступе вдоль проверенного свободного луча до стенки —
            // это исключает просачивание даже в сколь угодно острых углах звезды
            double backOff = Math.Min(bestS * 0.5, 2e-3 / Math.Max(1.0, dLen));
            double sSafe = Math.Max(0.0, bestS - backOff);
            node.Pos = p + d * sSafe;

            double vn = Vec2.Dot(node.Vel, bestN);
            if (vn < 0)
                node.Vel -= bestN * (2.0 * vn);

            node.BounceId = bestId;
            ignoreId = bestId;
            rem *= (1.0 - bestS);
        }
    }

    private static void CheckBound(double p, double d, double wall, Vec2 n, int id, int ignoreId,
        ref double bestS, ref Vec2 bestN, ref int bestId)
    {
        if (id == ignoreId) return;
        if (d * (n.X + n.Y) >= 0) return; // летит внутрь поля, а не в границу
        double s = (wall - p) / d;
        if (s >= -1e-7 && s <= 1.0 && s < bestS)
        {
            bestS = Math.Max(0.0, s);
            bestN = n;
            bestId = id;
        }
    }

    private bool SegmentHitsAnyWall(Vec2 a, Vec2 b)
    {
        if (segs.Count == 0) return false;
        var d = b - a;
        if (d.Len2 < 1e-10) return false;
        int cx0 = CellX(Math.Min(a.X, b.X)), cx1 = CellX(Math.Max(a.X, b.X));
        int cy0 = CellY(Math.Min(a.Y, b.Y)), cy1 = CellY(Math.Max(a.Y, b.Y));
        for (int cy = cy0; cy <= cy1; cy++)
        {
            int row = cy * GW;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                var cell = grid[row + cx];
                if (cell == null) continue;
                for (int k = 0; k < cell.Count; k++)
                {
                    var seg = segs[cell[k]];
                    double denom = d.X * seg.E.Y - d.Y * seg.E.X;
                    if (Math.Abs(denom) < 1e-11) continue;
                    double apx = seg.A.X - a.X, apy = seg.A.Y - a.Y;
                    double s = (apx * seg.E.Y - apy * seg.E.X) / denom;
                    if (s <= 1e-4 || s >= 1.0 - 1e-4) continue;
                    double u = (apx * d.Y - apy * d.X) / denom;
                    if (u >= -1e-4 && u <= 1.0 + 1e-4) return true;
                }
            }
        }
        return false;
    }

    /// <summary>Порождение цилиндрических дифракционных волн при прохождении фронта через вершины.</summary>
    private void DetectAndSpawnDiffraction()
    {
        if (vertices.Count == 0 || NodeCount >= MaxNodes - 128) return;
        pendingDiffs.Clear();

        int vCount = vertices.Count;
        int nCount = NodeCount;
        const double capR2 = VertexCaptureR * VertexCaptureR;

        for (int vi = 0; vi < vCount; vi++)
        {
            var v = vertices[vi];
            if (simTime - v.LastEmitTime < VertexCooldown) continue;

            int bestNode = -1;
            double bestD2 = capR2;

            int maxAllowedGen = NodeCount > MaxNodes / 2 ? 1 : 2;
            for (int ni = 0; ni < nCount; ni++)
            {
                ref var nd = ref Nodes[ni];
                if (nd.Alive == 0 || nd.Gen >= maxAllowedGen || nd.Intensity < 0.18f) continue;

                double dx = nd.Pos.X - v.Pos.X, dy = nd.Pos.Y - v.Pos.Y;
                if (dx * dx + dy * dy > 100.0) continue; // быстрый отсев дальше 10 px

                double d = Simulation.DistPtSeg(v.Pos, nd.PrevPos, nd.Pos);
                double d2 = d * d;
                if (d2 < bestD2)
                {
                    // Исключаем самовозбуждение лучами, только что родившимися у этой же вершины
                    if ((nd.PrevPos - v.Pos).Len2 < 9.0) continue;
                    bestD2 = d2;
                    bestNode = ni;
                }
            }

            if (bestNode >= 0)
            {
                v.LastEmitTime = simTime;
                vertices[vi] = v;
                ref var src = ref Nodes[bestNode];
                float gain = (v.FullTip || v.SweepRad > Math.PI + 0.05) ? 0.68f : 0.44f;
                float amp = src.Intensity * gain;
                if (amp >= MinIntensity * 1.4f)
                {
                    pendingDiffs.Add(new PendingDiff
                    {
                        VertexIdx = vi,
                        Hue = src.Hue,
                        Argb = src.Argb,
                        Intensity = amp,
                        Gen = (byte)(src.Gen + 1),
                    });
                }
            }
        }

        for (int k = 0; k < pendingDiffs.Count; k++)
            SpawnDiffractionArc(pendingDiffs[k]);
    }

    private void SpawnDiffractionArc(PendingDiff pd)
    {
        var v = vertices[pd.VertexIdx];
        int count = Math.Clamp((int)Math.Ceiling(v.SweepRad / 0.14), 10, 48);
        if (NodeCount + count > MaxNodes) return;
        EnsureCapacity(NodeCount + count + 64);

        int baseIdx = NodeCount;
        double step = count > 1 ? v.SweepRad / (v.FullTip ? count : count - 1) : 0.0;
        const double r0 = 1.6;

        for (int i = 0; i < count; i++)
        {
            double a = v.StartRad + step * i;
            var dir = new Vec2(Math.Cos(a), Math.Sin(a));
            var p = v.Pos + dir * r0;
            int hue = pd.Hue >= 0 ? (pd.Hue + (int)((i - count * 0.5) * 1.5) + 360) % 360 : -1;
            int next = (i < count - 1) ? (baseIdx + i + 1) : (v.FullTip ? baseIdx : -1);

            Nodes[baseIdx + i] = new WaveNode
            {
                Pos = p,
                PrevPos = v.Pos,
                FrameStartPos = v.Pos,
                Vel = dir * WaveSpeed,
                Next = next,
                BounceId = 0,
                Hue = hue,
                Argb = pd.Argb,
                Intensity = pd.Intensity,
                Gen = pd.Gen,
                Alive = 1,
            };
        }

        NodeCount += count;
        DiffractionCount++;
    }

    /// <summary>
    /// Адаптивная вставка лучей на растянутых гладких участках фронта (эрмитова дуга 4-го порядка),
    /// слияние избыточно сжавшихся коллинеарных узлов и разрыв связей на углах без потери яркости волны.
    /// </summary>
    private void RefineAndSplitFront()
    {
        int origCount = NodeCount;
        // Плавное адаптивное масштабирование шага разбиения при высокой плотности фронта
        double load = Math.Clamp((origCount - 3200.0) / (MaxNodes - 3200.0), 0.0, 1.0);
        double dynSplit = SplitDist + load * 6.0; // 5.5 px .. 11.5 px
        double dynSplit2 = dynSplit * dynSplit;
        double wallCheckDist2 = Math.Max(49.0, dynSplit2 * 0.9);

        for (int i = 0; i < origCount; i++)
        {
            ref var a = ref Nodes[i];
            if (a.Alive == 0) continue;
            int j = a.Next;
            if (j < 0 || j == i) { if (j == i) a.Next = -1; continue; }

            if (Nodes[j].Alive == 0)
            {
                a.Next = -1;
                continue;
            }

            var b = Nodes[j];
            var chord = b.Pos - a.Pos;
            double d2 = chord.Len2;

            double vaLen = a.Vel.Len, vbLen = b.Vel.Len;
            if (vaLen < 1e-6 || vbLen < 1e-6) continue;
            var ua = a.Vel * (1.0 / vaLen);
            var ub = b.Vel * (1.0 / vbLen);
            double cosAng = Vec2.Dot(ua, ub);

            // Если два соседних узла сильно сблизились на фокусе и летят в одном направлении — сливаем лишний узел
            if (d2 < 2.25 && cosAng > 0.996 && b.Next >= 0 && b.Next != i && Nodes[b.Next].Alive != 0)
            {
                if (!SegmentHitsAnyWall(a.Pos, Nodes[b.Next].Pos))
                {
                    Nodes[i].Next = b.Next;
                    Nodes[j].Alive = 0;
                    Nodes[j].Next = -1;
                    continue;
                }
            }

            // Если соседние лучи оказались по разные стороны стенки — разрываем ложную хорду
            if (d2 > 4.0 && (a.BounceId != b.BounceId || d2 > wallCheckDist2) && SegmentHitsAnyWall(a.Pos, b.Pos))
            {
                Nodes[i].Next = -1;
                continue;
            }

            // Если лучи разошлись на угле/крае преграды на заметное расстояние под разными углами —
            // разрываем связь (а разрыв заполняет круговая дифракционная волна из вершины!)
            if (d2 > MaxTearDist2 && cosAng < TearCosMax)
            {
                Nodes[i].Next = -1;
                continue;
            }
            if (d2 > 484.0) // > 22 px — гарантированный разрыв фронта
            {
                Nodes[i].Next = -1;
                continue;
            }

            // На гладком растянувшемся участке фронта вставляем новый промежуточный луч
            if (d2 > dynSplit2 && cosAng >= SmoothCosMin && NodeCount < MaxNodes)
            {
                byte childGen = Math.Max(a.Gen, b.Gen);
                float avgAmp = (a.Intensity + b.Intensity) * 0.5f;
                float childAmp = childGen == 0
                    ? Math.Max(0.88f, avgAmp) // первичная волна никогда не тускнеет и не исчезает!
                    : childGen == 1
                        ? Math.Max(0.32f, avgAmp * 0.994f)
                        : avgAmp * 0.975f;
                if (childAmp < MinIntensity)
                {
                    Nodes[i].Next = -1;
                    continue;
                }

                var um = (ua + ub).Norm();
                if (um.Len2 < 0.5) um = ua;

                // Эрмитова поправка по нормалям фронта: сохраняет идеальную окружность с точностью O(θ^4)
                var mid = (a.Pos + b.Pos) * 0.5;
                double bow = 0.125 * (Vec2.Dot(chord, ub) - Vec2.Dot(chord, ua));
                bow = Math.Clamp(bow, -2.5, 2.5);
                var posM = mid + um * bow;
                if (SegmentHitsAnyWall(mid, posM))
                    posM = mid;

                double speedM = (vaLen + vbLen) * 0.5;
                int hueM = -1;
                if (a.Hue >= 0 && b.Hue >= 0)
                {
                    int dh = b.Hue - a.Hue;
                    if (dh > 180) dh -= 360;
                    else if (dh < -180) dh += 360;
                    hueM = ((a.Hue + dh / 2) % 360 + 360) % 360;
                }

                EnsureCapacity(NodeCount + 16);
                int mIdx = NodeCount++;
                Nodes[mIdx] = new WaveNode
                {
                    Pos = posM,
                    PrevPos = (a.PrevPos + b.PrevPos) * 0.5,
                    FrameStartPos = (a.FrameStartPos + b.FrameStartPos) * 0.5,
                    Vel = um * speedM,
                    Next = j,
                    BounceId = a.BounceId == b.BounceId ? a.BounceId : 0,
                    Hue = hueM,
                    Argb = a.Argb != 0 ? a.Argb : b.Argb,
                    Intensity = childAmp,
                    Gen = childGen,
                    Alive = 1,
                };
                Nodes[i].Next = mIdx;
                Nodes[i].Intensity = childAmp;
            }
        }
    }

    private void CompactDeadOrFaintNodes()
    {
        int n = NodeCount;
        if (n == 0) return;
        if (compactMap.Length < n)
        {
            int cap = Math.Max(n, compactMap.Length * 2);
            compactMap = new int[cap];
            hasIncoming = new bool[cap];
        }
        Array.Clear(hasIncoming, 0, n);

        for (int i = 0; i < n; i++)
        {
            if (Nodes[i].Alive == 0) continue;
            int nxt = Nodes[i].Next;
            if (nxt >= 0 && nxt < n && Nodes[nxt].Alive != 0)
                hasIncoming[nxt] = true;
            else
                Nodes[i].Next = -1;
        }

        // 1. Удаляем одиночные оторванные узлы-сироты (у них нет ни входящего, ни исходящего сегмента)
        int aliveCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (Nodes[i].Alive == 0) continue;
            if (!hasIncoming[i] && Nodes[i].Next < 0)
            {
                Nodes[i].Alive = 0;
                continue;
            }
            aliveCount++;
        }

        int targetMax = MaxNodes * 3 / 4;

        // 2. Если буфер всё ещё плотный — сначала убираем третичные осколки (Gen >= 2)
        if (aliveCount > targetMax)
        {
            for (int i = 0; i < n && aliveCount > targetMax; i++)
            {
                if (Nodes[i].Alive != 0 && Nodes[i].Gen >= 2)
                {
                    Nodes[i].Alive = 0;
                    Nodes[i].Next = -1;
                    aliveCount--;
                }
            }
        }

        // 3. Если связный фронт занял более 75% пула — аккуратно прореживаем каждый 2-й внутренний узел на гладких участках,
        // полностью сохраняя непрерывность цепочек и 100% яркости первичной волны!
        if (aliveCount > targetMax)
        {
            for (int i = 0; i < n && aliveCount > targetMax; i++)
            {
                if (Nodes[i].Alive == 0) continue;
                int j = Nodes[i].Next;
                if (j < 0 || j >= n || j == i || Nodes[j].Alive == 0) continue;
                int k = Nodes[j].Next;
                if (k < 0 || k >= n || k == i || k == j || Nodes[k].Alive == 0) continue;

                var chord = Nodes[k].Pos - Nodes[i].Pos;
                if (chord.Len2 < 361.0 && !SegmentHitsAnyWall(Nodes[i].Pos, Nodes[k].Pos))
                {
                    Nodes[i].Next = k;
                    Nodes[j].Alive = 0;
                    Nodes[j].Next = -1;
                    aliveCount--;
                }
            }
        }

        // 4. В хаотических бильярдах (стадион, Синай) со временем скапливаются мелкие оторванные 2-узловые щепки (i -> j -> -1):
        // убираем их в первую очередь, чтобы основной непрерывный фронт всегда имел запас узлов для гладкого деления.
        if (aliveCount > targetMax)
        {
            for (int i = 0; i < n && aliveCount > targetMax; i++)
            {
                if (Nodes[i].Alive == 0 || hasIncoming[i]) continue;
                int j = Nodes[i].Next;
                if (j < 0 || j >= n || j == i || Nodes[j].Alive == 0) continue;
                if (Nodes[j].Next < 0)
                {
                    Nodes[i].Alive = 0;
                    Nodes[i].Next = -1;
                    Nodes[j].Alive = 0;
                    aliveCount -= 2;
                }
            }
        }

        // 5. Если даже после очистки щепок пул перегружен, мягко подрезаем вторичные дифракционные дуги (Gen >= 1)
        if (aliveCount > targetMax)
        {
            for (int i = 0; i < n && aliveCount > targetMax; i++)
            {
                if (Nodes[i].Alive != 0 && Nodes[i].Gen >= 1)
                {
                    Nodes[i].Alive = 0;
                    Nodes[i].Next = -1;
                    aliveCount--;
                }
            }
        }

        // 6. Страховочная подрезка открытых концов цепочек при экстремальном экспоненциальном растяжении в хаосе
        if (aliveCount > targetMax)
        {
            for (int i = 0; i < n && aliveCount > targetMax; i++)
            {
                if (Nodes[i].Alive != 0 && !hasIncoming[i])
                {
                    Nodes[i].Alive = 0;
                    Nodes[i].Next = -1;
                    aliveCount--;
                }
            }
        }

        int write = 0;
        for (int i = 0; i < n; i++)
        {
            if (Nodes[i].Alive != 0)
            {
                compactMap[i] = write;
                if (write != i) Nodes[write] = Nodes[i];
                write++;
            }
            else
            {
                compactMap[i] = -1;
            }
        }
        for (int i = 0; i < write; i++)
        {
            int nxt = Nodes[i].Next;
            Nodes[i].Next = (nxt >= 0 && nxt < n) ? compactMap[nxt] : -1;
        }
        NodeCount = write;
    }
}

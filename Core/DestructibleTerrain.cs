using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Ricochet.Core;

/// <summary>
/// Попиксельная разрушаемая радужная масса с яркой белой кромкой на границе выработки
/// (сетка 640×360, масштаб 2× до мировых координат 1280×720).
/// </summary>
public sealed class DestructibleBitmapTerrain
{
    public const int GridW = 640;
    public const int GridH = 360;
    public const double Scale = 2.0; // 1 ячейка = 2×2 мировых пикселя

    // 0 = пусто, 1 = радужное тело, 2 = белая эродированная кромка
    private readonly byte[] state = new byte[GridW * GridH];
    private readonly byte[] hueMap = new byte[GridW * GridH];
    private static readonly int[] RainbowLut = BuildRainbowLut();

    private int minX = GridW, maxX = -1, minY = GridH, maxY = -1;
    private int minHitY = GridH, maxHitY = -1;

    public int InitialSolidCount { get; private set; }
    public int SolidCount { get; private set; }
    public double ClearedRatio => InitialSolidCount > 0 ? Math.Clamp(1.0 - (double)SolidCount / InitialSolidCount, 0.0, 1.0) : 1.0;

    private static int[] BuildRainbowLut()
    {
        var lut = new int[256];
        for (int i = 0; i < 256; i++)
        {
            var c = HsvColor(i * (360.0 / 256.0), 0.72, 0.78);
            lut[i] = unchecked((int)(0xEA000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        }
        return lut;
    }

    public static Color HsvColor(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(
            Math.Clamp((int)Math.Round((r + m) * 255), 0, 255),
            Math.Clamp((int)Math.Round((g + m) * 255), 0, 255),
            Math.Clamp((int)Math.Round((b + m) * 255), 0, 255));
    }

    public void Clear()
    {
        Array.Clear(state, 0, state.Length);
        Array.Clear(hueMap, 0, hueMap.Length);
        InitialSolidCount = 0;
        SolidCount = 0;
        minX = GridW; maxX = -1;
        minY = GridH; maxY = -1;
    }

    /// <summary>Заполнить сетку по произвольному предикату в мировых координатах (1280×720).</summary>
    public void Build(Vec2 center, Func<double, double, bool> isSolidWorld)
    {
        Clear();
        for (int gy = 0; gy < GridH; gy++)
        {
            double wy = (gy + 0.5) * Scale;
            int row = gy * GridW;
            for (int gx = 0; gx < GridW; gx++)
            {
                double wx = (gx + 0.5) * Scale;
                if (!isSolidWorld(wx, wy)) continue;

                state[row + gx] = 1;
                SolidCount++;
                // плавная спирально-радиальная радуга без тригонометрии в кадре
                double dx = wx - center.X, dy = wy - center.Y;
                double ang = Math.Atan2(dy, dx) / (Math.PI * 2.0) + 0.5;
                double rad = Math.Sqrt(dx * dx + dy * dy) / 360.0;
                int hByte = (int)Math.Round((ang * 1.15 + rad * 0.65) * 255.0) & 255;
                hueMap[row + gx] = (byte)hByte;

                if (gx < minX) minX = gx;
                if (gx > maxX) maxX = gx;
                if (gy < minY) minY = gy;
                if (gy > maxY) maxY = gy;
            }
        }
        InitialSolidCount = SolidCount;
        if (SolidCount > 0)
            RebuildEdges(minX, minY, maxX, maxY);
    }

    /// <summary>Вырезать пустой круг (в мировых координатах), вернуть число стёртых ячеек и средний тон радуги.</summary>
    public int CarveCircle(double worldX, double worldY, double worldRadius, out Color avgColor)
    {
        avgColor = Color.FromArgb(94, 197, 255);
        if (SolidCount == 0 || maxX < minX) return 0;

        double gcx = worldX / Scale, gcy = worldY / Scale, gr = worldRadius / Scale;
        int x0 = Math.Max(minX, (int)Math.Floor(gcx - gr));
        int x1 = Math.Min(maxX, (int)Math.Ceiling(gcx + gr));
        int y0 = Math.Max(minY, (int)Math.Floor(gcy - gr));
        int y1 = Math.Min(maxY, (int)Math.Ceiling(gcy + gr));
        if (x0 > x1 || y0 > y1) return 0;

        double gr2 = gr * gr;
        int carved = 0;
        int hueAcc = 0;
        for (int gy = y0; gy <= y1; gy++)
        {
            double dy = (gy + 0.5) - gcy;
            double dy2 = dy * dy;
            int row = gy * GridW;
            for (int gx = x0; gx <= x1; gx++)
            {
                int idx = row + gx;
                if (state[idx] == 0) continue;
                double dx = (gx + 0.5) - gcx;
                if (dx * dx + dy2 <= gr2)
                {
                    state[idx] = 0;
                    SolidCount--;
                    carved++;
                    hueAcc += hueMap[idx];
                }
            }
        }

        if (carved > 0)
        {
            int argb = RainbowLut[(hueAcc / carved) & 255];
            avgColor = Color.FromArgb(255, Color.FromArgb(argb));
            RebuildEdges(Math.Max(0, x0 - 2), Math.Max(0, y0 - 2), Math.Min(GridW - 1, x1 + 2), Math.Min(GridH - 1, y1 + 2));
        }
        return carved;
    }

    private void RebuildEdges(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, GridW - 1);
        x1 = Math.Clamp(x1, 0, GridW - 1);
        y0 = Math.Clamp(y0, 0, GridH - 1);
        y1 = Math.Clamp(y1, 0, GridH - 1);

        for (int gy = y0; gy <= y1; gy++)
        {
            int row = gy * GridW;
            for (int gx = x0; gx <= x1; gx++)
            {
                int idx = row + gx;
                if (state[idx] == 0) continue;
                bool border =
                    gx == 0 || gx == GridW - 1 || gy == 0 || gy == GridH - 1 ||
                    state[idx - 1] == 0 || state[idx + 1] == 0 ||
                    state[idx - GridW] == 0 || state[idx + GridW] == 0;
                state[idx] = (byte)(border ? 2 : 1);
            }
        }
    }

    /// <summary>Проверить столкновение шара с массой: отразить импульс с заданной упругостью и выгрызть кратер.</summary>
    public bool CollideBall(SimBall b, double carveExtra, out int carved, out Vec2 hitPos, out Color avgColor, double restitution = 1.0)
    {
        carved = 0;
        hitPos = b.Pos;
        avgColor = Color.FromArgb(94, 197, 255);
        if (SolidCount == 0 || maxX < minX) return false;

        double gcx = b.Pos.X / Scale, gcy = b.Pos.Y / Scale, gr = b.R / Scale;
        int x0 = Math.Max(minX, (int)Math.Floor(gcx - gr - 0.5));
        int x1 = Math.Min(maxX, (int)Math.Ceiling(gcx + gr + 0.5));
        int y0 = Math.Max(minY, (int)Math.Floor(gcy - gr - 0.5));
        int y1 = Math.Min(maxY, (int)Math.Ceiling(gcy + gr + 0.5));
        if (x0 > x1 || y0 > y1) return false;

        double gr2 = (gr + 0.6) * (gr + 0.6);
        double sumDx = 0, sumDy = 0, sumHx = 0, sumHy = 0;
        int hits = 0;
        minHitY = GridH; maxHitY = -1;

        for (int gy = y0; gy <= y1; gy++)
        {
            double wy = (gy + 0.5) * Scale;
            double dy = (gy + 0.5) - gcy;
            double dy2 = dy * dy;
            int row = gy * GridW;
            for (int gx = x0; gx <= x1; gx++)
            {
                if (state[row + gx] == 0) continue;
                double dx = (gx + 0.5) - gcx;
                if (dx * dx + dy2 <= gr2)
                {
                    sumDx += dx;
                    sumDy += dy;
                    sumHx += (gx + 0.5) * Scale;
                    sumHy += wy;
                    hits++;
                    if (gy < minHitY) minHitY = gy;
                    if (gy > maxHitY) maxHitY = gy;
                }
            }
        }
        if (hits == 0) return false;

        hitPos = new Vec2(sumHx / hits, sumHy / hits);
        var away = new Vec2(-sumDx, -sumDy);
        Vec2 n = away.Len > 1e-6 ? away.Norm()
               : (b.Vel.Len > 1e-6 ? -b.Vel.Norm() : new Vec2(0, -1));

        // Выталкиваем на пару пикселей по нормали, чтобы шар не вяз в массе
        b.Pos += n * Math.Clamp(b.R * 0.28, 1.5, 4.0);
        double vn = Vec2.Dot(b.Vel, n);
        if (vn < 0)
            b.Vel -= n * ((1.0 + restitution) * vn);

        carved = CarveCircle(hitPos.X, hitPos.Y, b.R + carveExtra, out avgColor);
        return true;
    }

    /// <summary>Быстрая unsafe-отрисовка радужного тела + белой кромки в 32bpp кеш-битмап.</summary>
    public unsafe void RenderToBitmap(Bitmap bmp, double phaseTurns)
    {
        int phaseShift = ((int)Math.Round(phaseTurns * 255.0)) & 255;
        const int borderArgb = unchecked((int)0xFFF4FAFF);

        var bd = bmp.LockBits(new Rectangle(0, 0, GridW, GridH), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            byte* b0 = (byte*)bd.Scan0;
            int stride = bd.Stride;
            for (int y = 0; y < GridH; y++)
            {
                int* dst = (int*)(b0 + y * stride);
                if (y < minY || y > maxY || maxX < minX)
                {
                    new Span<int>(dst, GridW).Clear();
                    continue;
                }
                if (minX > 0) new Span<int>(dst, minX).Clear();
                if (maxX < GridW - 1) new Span<int>(dst + maxX + 1, GridW - 1 - maxX).Clear();

                int row = y * GridW;
                for (int x = minX; x <= maxX; x++)
                {
                    byte st = state[row + x];
                    if (st == 0) dst[x] = 0;
                    else if (st == 2) dst[x] = borderArgb;
                    else dst[x] = RainbowLut[(hueMap[row + x] + phaseShift) & 255];
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
    }
}

/// <summary>
/// Полярная сетка разрушаемых блоков внутри спирали Архимеда (Clip 4A).
/// </summary>
public sealed class PolarSpiralGrid
{
    public struct Tile
    {
        public bool Alive;
        public PointF[] Poly; // 4 вершины в мировых координатах (для быстрой заливки и обводки)
        public Vec2 Center;
        public double MinR, MaxR;
        public double MinX, MaxX, MinY, MaxY;
        public double MidAngle;
        public byte HueIdx;
    }

    public readonly Tile[] Tiles;
    public readonly Vec2 Center;
    public readonly double RMin, RMax, Pitch;
    public int AliveCount { get; private set; }
    public int TotalCount => Tiles.Length;

    public PolarSpiralGrid(Vec2 center, double rMin, double rMax, int turns, int segsPerTurn, double startSkipRad = 0.0)
    {
        Center = center;
        RMin = rMin;
        RMax = rMax;
        // Спираль делает turns полных витков; внутри образуется (turns - 1) закрытых каналов между соседними витками
        turns = Math.Max(2, turns);
        Pitch = (rMax - rMin) / turns;

        int channels = turns - 1;
        double da = (Math.PI * 2.0) / segsPerTurn;
        var list = new List<Tile>(channels * segsPerTurn);
        double maxChannelTheta = channels * Math.PI * 2.0;

        for (int t = 0; t < channels; t++)
        {
            for (int s = 0; s < segsPerTurn; s++)
            {
                double a0 = s * da;
                double a1 = (s + 1) * da;
                double theta0 = t * Math.PI * 2.0 + a0;
                double theta1 = t * Math.PI * 2.0 + a1;
                double thetaM = (theta0 + theta1) * 0.5;

                // Оставляем свободный входной карман у внешнего края последнего канала
                if (thetaM >= maxChannelTheta - startSkipRad) continue;

                double r00 = rMin + Pitch * (theta0 / (Math.PI * 2.0)) + 2.0;
                double r01 = rMin + Pitch * (theta1 / (Math.PI * 2.0)) + 2.0;
                double r10 = r00 + Pitch - 4.0;
                double r11 = r01 + Pitch - 4.0;
                if (r10 <= r00 + 3 || r11 <= r01 + 3) continue;

                var p00 = new Vec2(center.X + Math.Cos(a0) * r00, center.Y + Math.Sin(a0) * r00);
                var p01 = new Vec2(center.X + Math.Cos(a1) * r01, center.Y + Math.Sin(a1) * r01);
                var p11 = new Vec2(center.X + Math.Cos(a1) * r11, center.Y + Math.Sin(a1) * r11);
                var p10 = new Vec2(center.X + Math.Cos(a0) * r10, center.Y + Math.Sin(a0) * r10);

                var c = (p00 + p01 + p11 + p10) * 0.25;
                double minR = Math.Min(Math.Min(r00, r01), Math.Min(r10, r11));
                double maxR = Math.Max(Math.Max(r00, r01), Math.Max(r10, r11));
                var pf0 = new PointF((float)p00.X, (float)p00.Y);
                var pf1 = new PointF((float)p01.X, (float)p01.Y);
                var pf2 = new PointF((float)p11.X, (float)p11.Y);
                var pf3 = new PointF((float)p10.X, (float)p10.Y);
                double minX = Math.Min(Math.Min(pf0.X, pf1.X), Math.Min(pf2.X, pf3.X));
                double maxX = Math.Max(Math.Max(pf0.X, pf1.X), Math.Max(pf2.X, pf3.X));
                double minY = Math.Min(Math.Min(pf0.Y, pf1.Y), Math.Min(pf2.Y, pf3.Y));
                double maxY = Math.Max(Math.Max(pf0.Y, pf1.Y), Math.Max(pf2.Y, pf3.Y));
                byte hue = (byte)(((thetaM / maxChannelTheta) * 24.0) % 24);

                list.Add(new Tile
                {
                    Alive = true,
                    Poly = new[] { pf0, pf1, pf2, pf3 },
                    Center = c,
                    MinR = minR,
                    MaxR = maxR,
                    MinX = minX,
                    MaxX = maxX,
                    MinY = minY,
                    MaxY = maxY,
                    MidAngle = (a0 + a1) * 0.5,
                    HueIdx = hue,
                });
            }
        }
        Tiles = list.ToArray();
        AliveCount = Tiles.Length;
    }

    /// <summary>Создать полилинию самой спиральной стенки + торцевую заглушку внешнего входа.</summary>
    public static List<PolyData> BuildSpiralWalls(Vec2 center, double rMin, double rMax, int turns)
    {
        turns = Math.Max(2, turns);
        double pitch = (rMax - rMin) / turns;
        int stepsPerTurn = 64;
        int totalSteps = turns * stepsPerTurn;
        var spiralPts = new List<Vec2>(totalSteps + 2);
        for (int i = 0; i <= totalSteps; i++)
        {
            double u = (double)i / stepsPerTurn; // 0 .. turns
            double a = u * Math.PI * 2.0;
            double r = rMin + pitch * u;
            spiralPts.Add(new Vec2(center.X + Math.Cos(a) * r, center.Y + Math.Sin(a) * r));
        }

        // Закрываем внешний торец на α = 0 между последним и предпоследним витком, чтобы шар шёл внутрь к ядру
        var capPts = new List<Vec2>
        {
            new Vec2(center.X + (rMax - pitch), center.Y),
            new Vec2(center.X + rMax, center.Y),
        };

        return new List<PolyData>
        {
            new() { Closed = false, Pts = spiralPts, Pivot = center },
            new() { Closed = false, Pts = capPts, Pivot = center },
        };
    }

    /// <summary>Столкновение шара с ближайшим целым блоком спирали.</summary>
    public bool CollideBall(SimBall b, out Tile broken)
        => CollideBallEx(b, restitution: 1.0, splashRadius: 0.0, out broken, splashTiles: null);

    /// <summary>Расширенное столкновение шара с блоками: задаёт упругость и радиус осколочного пролома соседних блоков.</summary>
    public bool CollideBallEx(SimBall b, double restitution, double splashRadius, out Tile broken, List<Tile> splashTiles = null)
    {
        broken = default;
        if (AliveCount == 0) return false;

        double bx = b.Pos.X, by = b.Pos.Y;
        double dx = bx - Center.X, dy = by - Center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double reach = b.R + 1.5;

        double bestD2 = reach * reach;
        int bestIdx = -1;
        var bestPt = b.Pos;

        for (int i = 0; i < Tiles.Length; i++)
        {
            ref readonly var t = ref Tiles[i];
            if (!t.Alive) continue;
            if (dist + reach < t.MinR || dist - reach > t.MaxR) continue;
            if (bx + reach < t.MinX || bx - reach > t.MaxX || by + reach < t.MinY || by - reach > t.MaxY) continue;

            // Точка ближайшая к четырёхугольнику блока
            for (int e = 0; e < 4; e++)
            {
                var p0 = new Vec2(t.Poly[e].X, t.Poly[e].Y);
                var p1 = new Vec2(t.Poly[(e + 1) & 3].X, t.Poly[(e + 1) & 3].Y);
                var q = Simulation.ClosestPtSeg(b.Pos, p0, p1);
                double d2 = (b.Pos - q).Len2;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestIdx = i;
                    bestPt = q;
                }
            }
        }
        if (bestIdx < 0) return false;

        Tiles[bestIdx].Alive = false;
        AliveCount--;
        broken = Tiles[bestIdx];

        if (splashRadius > 0.5)
        {
            double sr2 = splashRadius * splashRadius;
            double brokenR = (broken.Center - Center).Len;
            for (int i = 0; i < Tiles.Length; i++)
            {
                if (!Tiles[i].Alive) continue;
                var dd = Tiles[i].Center - bestPt;
                double dAng = Math.Abs(Tiles[i].MidAngle - broken.MidAngle);
                if (dAng > Math.PI) dAng = Math.PI * 2.0 - dAng;
                double tileR = (Tiles[i].Center - Center).Len;
                // Не перескакиваем сквозь соседние стенки спирали: ограничиваем угол и радиус в пределах своего витка
                if (dd.Len2 <= sr2 && dAng < 0.7 && Math.Abs(tileR - brokenR) <= Pitch * 0.55)
                {
                    Tiles[i].Alive = false;
                    AliveCount--;
                    splashTiles?.Add(Tiles[i]);
                }
            }
        }

        var away = b.Pos - bestPt;
        Vec2 n = away.Len > 1e-6 ? away.Norm() : (b.Pos - broken.Center).Norm();
        if (n.Len < 1e-6) n = -b.Vel.Norm();
        b.Pos = bestPt + n * (b.R + 0.5);
        double vn = Vec2.Dot(b.Vel, n);
        if (vn < 0)
            b.Vel -= n * ((1.0 + restitution) * vn);
        return true;
    }
}

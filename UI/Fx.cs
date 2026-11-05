using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace Ricochet.UI;

/// <summary>
/// Программная пост-обработка кадра — «шейдерный» конвейер на CPU:
/// двухмасштабное гауссово свечение (bloom) в 16-битной точности с билинейной
/// интерполяцией и субпиксельным дизерингом (без ступенчатых колец вокруг шаров),
/// хроматическая аберрация к краям, мягкая виньетка и едва заметные скан-линии.
/// Весь конвейер работает за один LockBits без вызовов GDI+.
/// </summary>
public static unsafe class Fx
{
    /// <summary>Свечение ярких элементов (bloom) — настройка из боковой панели.</summary>
    public static bool BloomOn = true;

    private const int W = (int)Core.LevelData.W;   // 1280
    private const int H = (int)Core.LevelData.H;   // 720
    private const int SW = W / 4, SH = H / 4;      // буфер свечения (320×180)
    private const int SC = SW * 3;                 // каналов в одной строке буфера свечения

    // --- интенсивности эффектов ---
    private const float BloomK   = 0.90f;  // сила двухмасштабного свечения
    private const int   BloomCut = 112;    // мягкий порог неоновой энергии
    private const float CaMax    = 1.25f;  // субпиксельный сдвиг каналов в дальних углах, px
    private const float Vignette = 0.40f;  // затемнение к углам
    private const float ScanAmp  = 0.024f; // деликатные скан-линии

    private static readonly byte[] lut;       // виньетка × скан-линия (0..255)
    private static readonly ushort[] caMapQ8; // субпиксельный сдвиг аберрации (px × 256)
    private static readonly ushort[] txIdx0;  // левый тексель билинейной выборки по X (*3)
    private static readonly ushort[] txIdx1;  // правый тексель билинейной выборки по X (*3)
    private static readonly byte[] txW0;      // вес левого текселя (0..8)
    private static readonly byte[] txW1;      // вес правого текселя (0..8)

    private static readonly ushort[] glowCore; // ближний ореол (16-бит 0..16320)
    private static readonly ushort[] glowWide; // широкий мягкий ореол
    private static readonly ushort[] glowTmp;  // промежуточный буфер размытия

    static Fx()
    {
        lut = new byte[W * H];
        caMapQ8 = new ushort[W * H];
        double cx = (W - 1) / 2.0, cy = (H - 1) / 2.0;
        double maxD = Math.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < H; y++)
        {
            float scan = 1f - ScanAmp * (0.5f + 0.5f * MathF.Cos(y * (2f * MathF.PI / 3f)));
            for (int x = 0; x < W; x++)
            {
                double dx = (x - cx) / maxD, dy = (y - cy) / maxD;
                double r = Math.Sqrt(dx * dx + dy * dy);
                double v = (1 - Vignette * Math.Min(1, r * r * 1.32)) * scan;
                lut[y * W + x] = (byte)Math.Clamp(v * 255 + 0.5, 0, 255);
                double e = Math.Max(0, r * 1.22 - 0.34);
                caMapQ8[y * W + x] = (ushort)Math.Clamp(CaMax * e * e * 256.0 + 0.5, 0, 768);
            }
        }

        // Предрасчёт точных центрированных весов билинейной выборки (центр текселя = tx*4 + 1.5)
        txIdx0 = new ushort[W];
        txIdx1 = new ushort[W];
        txW0 = new byte[W];
        txW1 = new byte[W];
        for (int x = 0; x < W; x++)
        {
            int sx8 = x * 2 - 3; // в восьмых долях шага 4 px
            int i0, w1;
            if (sx8 <= 0)
            {
                i0 = 0;
                w1 = 0;
            }
            else
            {
                i0 = sx8 >> 3;
                w1 = sx8 & 7;
                if (i0 >= SW - 1) { i0 = SW - 1; w1 = 0; }
            }
            int i1 = Math.Min(i0 + 1, SW - 1);
            txIdx0[x] = (ushort)(i0 * 3);
            txIdx1[x] = (ushort)(i1 * 3);
            txW1[x] = (byte)w1;
            txW0[x] = (byte)(8 - w1);
        }

        glowCore = new ushort[SW * SH * 3];
        glowWide = new ushort[SW * SH * 3];
        glowTmp  = new ushort[SW * SH * 3];
    }

    /// <summary>Применить эффекты к кадру 1280×720 (модифицирует битмап на месте за один LockBits).</summary>
    public static void Apply(Bitmap frame, int tick)
    {
        if (frame.Width != W || frame.Height != H) return;

        var fd = frame.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = fd.Stride;
        nint b0Ptr = fd.Scan0;
        try
        {
            fixed (ushort* gCore = glowCore, gWide = glowWide, gTmp = glowTmp, pTx0 = txIdx0, pTx1 = txIdx1, pCa = caMapQ8)
            fixed (byte* pLut = lut, pWx0 = txW0, pWx1 = txW1)
            {
                if (BloomOn)
                {
                    BrightPass16((byte*)b0Ptr, stride, gCore);
                    // Двойной проход Гаусса [1,4,6,4,1]/16 — шелковистое ядро без резкого перегиба у края шара
                    BlurPass16(gCore, gTmp, gCore, step: 1);
                    BlurPass16(gCore, gTmp, gCore, step: 1);
                    // Широкий многомасштабный ореол (à-trous шаги 2 → 3 → 1) для плавного растворения света в фон
                    BlurPass16(gCore, gTmp, gWide, step: 2);
                    BlurPass16(gWide, gTmp, gWide, step: 3);
                    BlurPass16(gWide, gTmp, gWide, step: 1);
                    CombineDualRadius(gCore, gWide);
                }

                nint glowPtr = (nint)gCore;
                nint lutPtr = (nint)pLut;
                nint caPtr = (nint)pCa;
                nint tx0Ptr = (nint)pTx0;
                nint tx1Ptr = (nint)pTx1;
                nint wx0Ptr = (nint)pWx0;
                nint wx1Ptr = (nint)pWx1;

                const int parts = 16;
                int bk = BloomOn ? (int)(BloomK * 256) : 0;
                Parallel.For(0, parts, p =>
                {
                    int y0 = p * H / parts, y1 = (p + 1) * H / parts;
                    Rows((byte*)b0Ptr, (ushort*)glowPtr, (byte*)lutPtr, (ushort*)caPtr,
                        (ushort*)tx0Ptr, (ushort*)tx1Ptr, (byte*)wx0Ptr, (byte*)wx1Ptr,
                        stride, y0, y1, tick, bk);
                });
            }
        }
        finally
        {
            frame.UnlockBits(fd);
        }
    }

    // Полная выборка всех 16 пикселей блока 4×4 + мягкая S-кривая яркости в 16-бит (0..16320)
    private static void BrightPass16(byte* b0, int stride, ushort* dst)
    {
        const int invCut = (256 << 8) / (255 - BloomCut);
        int s1 = stride, s2 = stride * 2, s3 = stride * 3;

        Parallel.For(0, SH, ty =>
        {
            byte* r0 = b0 + (ty * 4) * stride;
            byte* r1 = r0 + s1;
            byte* r2 = r0 + s2;
            byte* r3 = r0 + s3;
            ushort* d = dst + ty * SC;

            for (int tx = 0; tx < SW; tx++)
            {
                int ox = tx * 16;
                int sb = r0[ox] + r0[ox + 4] + r0[ox + 8] + r0[ox + 12]
                       + r1[ox] + r1[ox + 4] + r1[ox + 8] + r1[ox + 12]
                       + r2[ox] + r2[ox + 4] + r2[ox + 8] + r2[ox + 12]
                       + r3[ox] + r3[ox + 4] + r3[ox + 8] + r3[ox + 12];

                int sg = r0[ox + 1] + r0[ox + 5] + r0[ox + 9]  + r0[ox + 13]
                       + r1[ox + 1] + r1[ox + 5] + r1[ox + 9]  + r1[ox + 13]
                       + r2[ox + 1] + r2[ox + 5] + r2[ox + 9]  + r2[ox + 13]
                       + r3[ox + 1] + r3[ox + 5] + r3[ox + 9]  + r3[ox + 13];

                int sr = r0[ox + 2] + r0[ox + 6] + r0[ox + 10] + r0[ox + 14]
                       + r1[ox + 2] + r1[ox + 6] + r1[ox + 10] + r1[ox + 14]
                       + r2[ox + 2] + r2[ox + 6] + r2[ox + 10] + r2[ox + 14]
                       + r3[ox + 2] + r3[ox + 6] + r3[ox + 10] + r3[ox + 14];

                int b = (sb + 8) >> 4;
                int g = (sg + 8) >> 4;
                int r = (sr + 8) >> 4;

                int lum = (r * 54 + g * 183 + b * 19) >> 8;
                int peak = r > g ? (r > b ? r : b) : (g > b ? g : b);
                int energy = (lum + peak) >> 1;

                int di = tx * 3;
                if (energy <= BloomCut)
                {
                    d[di] = 0;
                    d[di + 1] = 0;
                    d[di + 2] = 0;
                    continue;
                }

                // Гладкая эрмитова кривая smoothstep (0..256) без излома производной на пороге
                int t = ((energy - BloomCut) * invCut) >> 8;
                if (t > 256) t = 256;
                int smooth = (t * t * (768 - (t << 1))) >> 16; // 0..256

                // Выходной диапазон 0..16320 (в 64 раза точнее byte)
                d[di]     = (ushort)((sb * smooth + 32) >> 6);
                d[di + 1] = (ushort)((sg * smooth + 32) >> 6);
                d[di + 2] = (ushort)((sr * smooth + 32) >> 6);
            }
        });
    }

    // Разделяемый 5-точечный фильтр Гаусса [1, 4, 6, 4, 1] / 16 с произвольным шагом (à-trous)
    private static void BlurPass16(ushort* src, ushort* tmp, ushort* dst, int step)
    {
        int dx1 = step * 3, dx2 = step * 6;
        int marginX = step * 2;

        // Горизонтальный проход
        for (int y = 0; y < SH; y++)
        {
            ushort* s = src + y * SC;
            ushort* d = tmp + y * SC;

            for (int x = 0; x < SW; x++)
            {
                int i = x * 3;
                if (x >= marginX && x < SW - marginX)
                {
                    d[i]     = (ushort)((s[i - dx2]     + (s[i - dx1]     << 2) + s[i]     * 6 + (s[i + dx1]     << 2) + s[i + dx2]     + 8) >> 4);
                    d[i + 1] = (ushort)((s[i - dx2 + 1] + (s[i - dx1 + 1] << 2) + s[i + 1] * 6 + (s[i + dx1 + 1] << 2) + s[i + dx2 + 1] + 8) >> 4);
                    d[i + 2] = (ushort)((s[i - dx2 + 2] + (s[i - dx1 + 2] << 2) + s[i + 2] * 6 + (s[i + dx1 + 2] << 2) + s[i + dx2 + 2] + 8) >> 4);
                }
                else
                {
                    int xL2 = Math.Max(0, x - step * 2) * 3;
                    int xL1 = Math.Max(0, x - step) * 3;
                    int xR1 = Math.Min(SW - 1, x + step) * 3;
                    int xR2 = Math.Min(SW - 1, x + step * 2) * 3;
                    for (int c = 0; c < 3; c++)
                        d[i + c] = (ushort)((s[xL2 + c] + (s[xL1 + c] << 2) + s[i + c] * 6 + (s[xR1 + c] << 2) + s[xR2 + c] + 8) >> 4);
                }
            }
        }

        // Вертикальный проход
        int dy1 = step * SC, dy2 = step * 2 * SC;
        int marginY = step * 2;
        for (int y = 0; y < SH; y++)
        {
            ushort* d = dst + y * SC;
            if (y >= marginY && y < SH - marginY)
            {
                ushort* r2L = tmp + (y - step * 2) * SC;
                ushort* r1L = tmp + (y - step) * SC;
                ushort* r0  = tmp + y * SC;
                ushort* r1R = tmp + (y + step) * SC;
                ushort* r2R = tmp + (y + step * 2) * SC;
                for (int i = 0; i < SC; i++)
                    d[i] = (ushort)((r2L[i] + (r1L[i] << 2) + r0[i] * 6 + (r1R[i] << 2) + r2R[i] + 8) >> 4);
            }
            else
            {
                ushort* r2L = tmp + Math.Max(0, y - step * 2) * SC;
                ushort* r1L = tmp + Math.Max(0, y - step) * SC;
                ushort* r0  = tmp + y * SC;
                ushort* r1R = tmp + Math.Min(SH - 1, y + step) * SC;
                ushort* r2R = tmp + Math.Min(SH - 1, y + step * 2) * SC;
                for (int i = 0; i < SC; i++)
                    d[i] = (ushort)((r2L[i] + (r1L[i] << 2) + r0[i] * 6 + (r1R[i] << 2) + r2R[i] + 8) >> 4);
            }
        }
    }

    // Смешивание ближнего свечения (28%) и широкого атмосферного ореола (72%)
    private static void CombineDualRadius(ushort* core, ushort* wide)
    {
        int total = SW * SH * 3;
        for (int i = 0; i < total; i++)
        {
            int v = (core[i] * 72 + wide[i] * 188) >> 8;
            core[i] = (ushort)(v > 32767 ? 32767 : v);
        }
    }

    private static byte SoftShoulder(int c)
    {
        if (c <= 0) return 0;
        if (c <= 188) return (byte)c;
        int ex = c - 188;
        // Мягкое плёночное плечо (188..255): исключает жёсткий обрыв и резкую смену оттенка в ореоле шара
        int comp = 188 + (ex * 67) / (67 + ex);
        return (byte)(comp > 255 ? 255 : comp);
    }

    private static void Rows(byte* b0, ushort* bloom, byte* pLut, ushort* pCaQ8,
        ushort* pTx0, ushort* pTx1, byte* pWx0, byte* pWx1,
        int stride, int y0, int y1, int tick, int bloomK256)
    {
        byte* rowCopy = stackalloc byte[W * 4];
        int* vGlow = stackalloc int[SC];
        int tickSeed = tick * 1274126177;

        for (int y = y0; y < y1; y++)
        {
            byte* row = b0 + y * stride;
            Buffer.MemoryCopy(row, rowCopy, W * 4, W * 4);
            byte* lrow = pLut + y * W;
            ushort* crow = pCaQ8 + y * W;

            if (bloomK256 != 0)
            {
                int sy8 = y * 2 - 3;
                int ty0, wy1;
                if (sy8 <= 0)
                {
                    ty0 = 0;
                    wy1 = 0;
                }
                else
                {
                    ty0 = sy8 >> 3;
                    wy1 = sy8 & 7;
                    if (ty0 >= SH - 1) { ty0 = SH - 1; wy1 = 0; }
                }
                int ty1 = Math.Min(ty0 + 1, SH - 1);
                int wy0 = 8 - wy1;
                ushort* gr0 = bloom + ty0 * SC;
                ushort* gr1 = bloom + ty1 * SC;
                for (int i = 0; i < SC; i++)
                    vGlow[i] = gr0[i] * wy0 + gr1[i] * wy1; // 0..262144 (масштаб ×512)
            }

            int rowSeed = y * 668265263 + tickSeed;

            for (int x = 0; x < W; x++)
            {
                int caQ8 = crow[x];
                int v = lrow[x];

                // Субпиксельный треугольный дизеринг (устраняет 8-битную ступенчатость градиентов)
                uint h1 = (uint)(x * 374761393 + rowSeed);
                h1 ^= h1 >> 13;
                h1 *= 1274126177u;
                int triDith = (int)(h1 & 255) + (int)((h1 >> 8) & 255) - 255; // -255..+255 (~±1 LSB)

                int r8, g8, b8;
                int px4 = x * 4;
                if (caQ8 == 0)
                {
                    b8 = rowCopy[px4]     * v + triDith;
                    g8 = rowCopy[px4 + 1] * v + triDith;
                    r8 = rowCopy[px4 + 2] * v + triDith;
                }
                else
                {
                    int shift = caQ8 >> 8;
                    int frac = caQ8 & 255;
                    int xr0 = Math.Max(0, x - shift);
                    int xr1 = Math.Max(0, xr0 - 1);
                    int xb0 = Math.Min(W - 1, x + shift);
                    int xb1 = Math.Min(W - 1, xb0 + 1);
                    int rSamp = (rowCopy[xr0 * 4 + 2] * (256 - frac) + rowCopy[xr1 * 4 + 2] * frac) >> 8;
                    int bSamp = (rowCopy[xb0 * 4]     * (256 - frac) + rowCopy[xb1 * 4]     * frac) >> 8;
                    r8 = rSamp * v + triDith;
                    g8 = rowCopy[px4 + 1] * v + triDith;
                    b8 = bSamp * v + triDith;
                }

                if (bloomK256 != 0)
                {
                    int i0 = pTx0[x], i1 = pTx1[x];
                    int wx0 = pWx0[x], wx1 = pWx1[x];
                    int gb8 = (int)(((long)(vGlow[i0]     * wx0 + vGlow[i1]     * wx1) * bloomK256) >> 12);
                    int gg8 = (int)(((long)(vGlow[i0 + 1] * wx0 + vGlow[i1 + 1] * wx1) * bloomK256) >> 12);
                    int gr8 = (int)(((long)(vGlow[i0 + 2] * wx0 + vGlow[i1 + 2] * wx1) * bloomK256) >> 12);

                    // Мягкий перелив избыточной энергии между каналами (cross-channel bloom spill):
                    // цветное свечение вблизи ядра плавно теплеет к белому без резких цветовых колец насыщения
                    int gPeak = gr8 > gg8 ? (gr8 > gb8 ? gr8 : gb8) : (gg8 > gb8 ? gg8 : gb8);
                    if (gPeak > (96 << 8))
                    {
                        int spill = (gPeak - (96 << 8)) >> 2;
                        gb8 += spill;
                        gg8 += spill;
                        gr8 += spill;
                    }

                    b8 += gb8;
                    g8 += gg8;
                    r8 += gr8;
                }

                int b = (b8 + 128) >> 8;
                int g = (g8 + 128) >> 8;
                int r = (r8 + 128) >> 8;

                int peak = r > g ? (r > b ? r : b) : (g > b ? g : b);
                if (peak > 196)
                {
                    int whiteSpill = ((peak - 196) * 72) >> 8;
                    b += whiteSpill;
                    g += whiteSpill;
                    r += whiteSpill;
                }

                row[px4]     = SoftShoulder(b);
                row[px4 + 1] = SoftShoulder(g);
                row[px4 + 2] = SoftShoulder(r);
            }
        }
    }
}

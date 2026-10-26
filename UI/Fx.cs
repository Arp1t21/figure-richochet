using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace Ricochet.UI;

/// <summary>
/// Программная пост-обработка кадра — «шейдерный» конвейер на CPU:
/// свечение ярких элементов (bloom), хроматическая аберрация к краям,
/// виньетка, скан-линии и лёгкое зерно. Кадр всегда 1280×720 (размер мира),
/// поэтому таблицы и буферы создаются один раз; весь конвейер (включая
/// даунскейл и размытие свечения) работает за один LockBits без вызовов GDI+.
/// </summary>
public static unsafe class Fx
{
    /// <summary>Свечение ярких элементов (bloom) — настройка из боковой панели.</summary>
    public static bool BloomOn = true;

    private const int W = (int)Core.LevelData.W;   // 1280
    private const int H = (int)Core.LevelData.H;   // 720
    private const int SW = W / 4, SH = H / 4;      // буфер свечения (1/4 разрешения)
    private const int SB = SW * 3;                 // строка буфера свечения

    // --- интенсивности эффектов ---
    private const float BloomK   = 0.85f;  // сила свечения
    private const int   BloomCut = 128;    // порог яркости для свечения
    private const float CaMax    = 2.3f;   // сдвиг каналов в углах, px
    private const float Vignette = 0.44f;  // затемнение к углам
    private const float ScanAmp  = 0.045f; // глубина скан-линий

    private static readonly byte[] lut;    // виньетка × скан-линия (0..255)
    private static readonly byte[] caMap;  // сдвиг аберрации, px
    private static readonly byte[] glowA;  // буфер свечения RGB
    private static readonly byte[] glowB;  // временный буфер размытия

    static Fx()
    {
        lut = new byte[W * H];
        caMap = new byte[W * H];
        double cx = (W - 1) / 2.0, cy = (H - 1) / 2.0;
        double maxD = Math.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < H; y++)
        {
            // скан-линии с периодом 3 px
            float scan = 1f - ScanAmp * (0.5f + 0.5f * MathF.Cos(y * (2f * MathF.PI / 3f)));
            for (int x = 0; x < W; x++)
            {
                double dx = (x - cx) / maxD, dy = (y - cy) / maxD;
                double r = Math.Sqrt(dx * dx + dy * dy);        // 0 в центре → ~1 в углу
                double v = (1 - Vignette * Math.Min(1, r * r * 1.35)) * scan;
                lut[y * W + x] = (byte)Math.Clamp(v * 255 + 0.5, 0, 255);
                double e = Math.Max(0, r * 1.25 - 0.25);        // аберрация только к краям
                caMap[y * W + x] = (byte)Math.Min(6, CaMax * e * e + 0.5);
            }
        }
        glowA = new byte[SW * SH * 3];
        glowB = new byte[SW * SH * 3];
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
            fixed (byte* ga = glowA, gb = glowB, pLut = lut, pCa = caMap)
            {
                nint gaPtr = (nint)ga;
                nint lutPtr = (nint)pLut;
                nint caPtr = (nint)pCa;

                if (BloomOn)
                {
                    BrightPassDirect((byte*)b0Ptr, stride, ga);
                    Blur(ga, gb);
                }

                const int parts = 16;
                int bk = BloomOn ? (int)(BloomK * 256) : 0;
                Parallel.For(0, parts, p =>
                {
                    int y0 = p * H / parts, y1 = (p + 1) * H / parts;
                    Rows((byte*)b0Ptr, (byte*)gaPtr, (byte*)lutPtr, (byte*)caPtr, stride, y0, y1, tick, bk);
                });
            }
        }
        finally
        {
            frame.UnlockBits(fd);
        }
    }

    // прямой даунскейл 4×4 + яркий проход прямо по заблокированному кадру (без GDI+ DrawImage)
    private static void BrightPassDirect(byte* b0, int stride, byte* dst)
    {
        const int invCut = (255 << 8) / (255 - BloomCut);
        int stride2 = stride * 2;
        for (int ty = 0; ty < SH; ty++)
        {
            byte* r0 = b0 + (ty * 4) * stride;
            byte* r2 = r0 + stride2;
            byte* d = dst + ty * SB;
            for (int tx = 0; tx < SW; tx++)
            {
                int ox = tx * 16;
                int b = (r0[ox] + r0[ox + 8] + r2[ox] + r2[ox + 8]) >> 2;
                int g = (r0[ox + 1] + r0[ox + 9] + r2[ox + 1] + r2[ox + 9]) >> 2;
                int r = (r0[ox + 2] + r0[ox + 10] + r2[ox + 2] + r2[ox + 10]) >> 2;
                int lum = (r * 54 + g * 183 + b * 19) >> 8;
                if (lum <= BloomCut)
                {
                    d[tx * 3] = 0;
                    d[tx * 3 + 1] = 0;
                    d[tx * 3 + 2] = 0;
                    continue;
                }
                int t = ((lum - BloomCut) * invCut) >> 8;
                int k = t * t >> 6;
                k += k >> 1;
                if (k > 255) k = 255;
                d[tx * 3]     = (byte)(b * k >> 8);
                d[tx * 3 + 1] = (byte)(g * k >> 8);
                d[tx * 3 + 2] = (byte)(r * k >> 8);
            }
        }
    }

    // кэш-дружелюбное разделяемое box-размытие радиуса 2 (без целочисленного деления во внутреннем цикле)
    private static void Blur(byte* a, byte* b)
    {
        // горизонтальный проход
        for (int y = 0; y < SH; y++)
        {
            byte* s = a + y * SB;
            byte* d = b + y * SB;
            for (int c = 0; c < 3; c++)
            {
                d[c] = (byte)((s[c] + s[3 + c] + s[6 + c]) * 341 >> 10);
                d[3 + c] = (byte)((s[c] + s[3 + c] + s[6 + c] + s[9 + c]) >> 2);
                int e1 = (SW - 2) * 3 + c, e0 = (SW - 1) * 3 + c;
                d[e1] = (byte)((s[e1 - 6] + s[e1 - 3] + s[e1] + s[e0]) >> 2);
                d[e0] = (byte)((s[e0 - 6] + s[e0 - 3] + s[e0]) * 341 >> 10);
            }
            for (int i = 6; i < SB - 6; i++)
                d[i] = (byte)(((s[i - 6] + s[i - 3] + s[i] + s[i + 3] + s[i + 6]) * 3277) >> 14);
        }

        // вертикальный проход по строкам (последовательный доступ к памяти)
        for (int i = 0; i < SB; i++)
        {
            a[i] = (byte)((b[i] + b[SB + i] + b[SB * 2 + i]) * 341 >> 10);
            a[SB + i] = (byte)((b[i] + b[SB + i] + b[SB * 2 + i] + b[SB * 3 + i]) >> 2);
            int bLast = (SH - 1) * SB + i, bPrev = (SH - 2) * SB + i;
            a[bPrev] = (byte)((b[bPrev - SB * 2] + b[bPrev - SB] + b[bPrev] + b[bLast]) >> 2);
            a[bLast] = (byte)((b[bLast - SB * 2] + b[bLast - SB] + b[bLast]) * 341 >> 10);
        }
        for (int y = 2; y < SH - 2; y++)
        {
            byte* r0 = b + (y - 2) * SB;
            byte* r1 = r0 + SB;
            byte* r2 = r1 + SB;
            byte* r3 = r2 + SB;
            byte* r4 = r3 + SB;
            byte* dst = a + y * SB;
            for (int i = 0; i < SB; i++)
                dst[i] = (byte)(((r0[i] + r1[i] + r2[i] + r3[i] + r4[i]) * 3277) >> 14);
        }
    }

    private static void Rows(byte* b0, byte* bloom, byte* pLut, byte* pCa, int stride, int y0, int y1, int tick, int bloomK256)
    {
        byte* rowCopy = stackalloc byte[W * 4];
        int tickSeed = tick * 1274126177;
        for (int y = y0; y < y1; y++)
        {
            byte* row = b0 + y * stride;
            Buffer.MemoryCopy(row, rowCopy, W * 4, W * 4);
            byte* lrow = pLut + y * W;
            byte* crow = pCa + y * W;
            byte* grow = bloom + (y >> 2) * SB;
            int rowSeed = y * 668265263 + tickSeed;

            for (int x = 0; x < W; x++)
            {
                int d = crow[x];
                int v = lrow[x];
                int n = ((x * 374761393 + rowSeed) >> 14 & 7) - 3;

                int r, g, b;
                int px4 = x * 4;
                if (d == 0)
                {
                    b = (rowCopy[px4]     * v >> 8) + n;
                    g = (rowCopy[px4 + 1] * v >> 8) + n;
                    r = (rowCopy[px4 + 2] * v >> 8) + n;
                }
                else
                {
                    int xr = x - d; if (xr < 0) xr = 0;
                    int xb = x + d; if (xb > W - 1) xb = W - 1;
                    r = (rowCopy[xr * 4 + 2] * v >> 8) + n;
                    g = (rowCopy[px4 + 1]    * v >> 8) + n;
                    b = (rowCopy[xb * 4]     * v >> 8) + n;
                }

                if (bloomK256 != 0)
                {
                    int gi = (x >> 2) * 3;
                    b += grow[gi]     * bloomK256 >> 8;
                    g += grow[gi + 1] * bloomK256 >> 8;
                    r += grow[gi + 2] * bloomK256 >> 8;
                }

                row[px4]     = (uint)b <= 255 ? (byte)b : (byte)(b < 0 ? 0 : 255);
                row[px4 + 1] = (uint)g <= 255 ? (byte)g : (byte)(g < 0 ? 0 : 255);
                row[px4 + 2] = (uint)r <= 255 ? (byte)r : (byte)(r < 0 ? 0 : 255);
            }
        }
    }
}

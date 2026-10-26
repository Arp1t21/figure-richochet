using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Tasks;

namespace Ricochet.UI;

/// <summary>
/// Программная пост-обработка кадра — «шейдерный» конвейер на CPU:
/// свечение ярких элементов (bloom), хроматическая аберрация к краям,
/// виньетка, скан-линии и лёгкое зерно. Кадр всегда 1280×720 (размер мира),
/// поэтому таблицы и буферы создаются один раз; строки кадра обрабатываются
/// параллельно, аллокаций на кадр нет.
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
    private const int   Grain    = 3;      // амплитуда зерна

    private static readonly byte[] lut;    // виньетка × скан-линия (0..255)
    private static readonly byte[] caMap;  // сдвиг аберрации, px
    private static readonly byte[] glowA;  // буфер свечения RGB
    private static readonly byte[] glowB;  // временный буфер размытия
    private static readonly Bitmap small;
    private static readonly Graphics smallG;

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
        small = new Bitmap(SW, SH, PixelFormat.Format32bppArgb);
        smallG = Graphics.FromImage(small);
        smallG.InterpolationMode = InterpolationMode.Bilinear;
    }

    /// <summary>Применить эффекты к кадру 1280×720 (модифицирует битмап на месте).</summary>
    public static void Apply(Bitmap frame, int tick)
    {
        if (frame.Width != W || frame.Height != H) return;

        // --- свечение: даунскейл → яркий проход → размытие ---
        if (BloomOn)
        {
            smallG.DrawImage(frame, new Rectangle(0, 0, SW, SH));
            var bd = small.LockBits(new Rectangle(0, 0, SW, SH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte* sb = (byte*)bd.Scan0;
            fixed (byte* ga = glowA, gb = glowB)
            {
                BrightPass(sb, bd.Stride, ga);
                Blur(ga, gb);
            }
            small.UnlockBits(bd);
        }

        // --- основной проход: аберрация + виньетка/скан + свечение + зерно ---
        var fd = frame.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = fd.Stride;
        byte* b0 = (byte*)fd.Scan0;
        int parts = 16;
        int bk = BloomOn ? (int)(BloomK * 256) : 0;
        Parallel.For(0, parts, p =>
        {
            int y0 = p * H / parts, y1 = (p + 1) * H / parts;
            Rows(b0, glowA, stride, y0, y1, tick, bk);
        });
        frame.UnlockBits(fd);
    }

    // яркий проход: оставляем только светящиеся элементы (шары, стены, цели)
    private static void BrightPass(byte* src, int srcStride, byte* dst)
    {
        for (int y = 0; y < SH; y++)
        {
            byte* s = src + y * srcStride;
            byte* d = dst + y * SB;
            for (int x = 0; x < SW; x++)
            {
                int b = s[x * 4], g = s[x * 4 + 1], r = s[x * 4 + 2];
                int lum = (r * 54 + g * 183 + b * 19) >> 8;
                int t = lum > BloomCut ? (lum - BloomCut) * 255 / (255 - BloomCut) : 0;
                int k = t * t >> 6;                 // квадратичное затухание
                k = k + (k >> 1);                   // ×1.5 — яркое светит сильнее
                if (k > 255) k = 255;
                d[x * 3]     = (byte)(b * k >> 8);
                d[x * 3 + 1] = (byte)(g * k >> 8);
                d[x * 3 + 2] = (byte)(r * k >> 8);
            }
        }
    }

    // разделяемое box-размытие радиуса 2 (горизонталь, затем вертикаль)
    private static void Blur(byte* a, byte* b)
    {
        for (int y = 0; y < SH; y++)
        {
            byte* s = a + y * SB;
            byte* d = b + y * SB;
            for (int x = 0; x < SW; x++)
                for (int c = 0; c < 3; c++)
                {
                    int xs = x < 2 ? 0 : x - 2, xe = x > SW - 3 ? SW - 1 : x + 2;
                    int sum = 0, n = 0;
                    for (int xx = xs; xx <= xe; xx++) { sum += s[xx * 3 + c]; n++; }
                    d[x * 3 + c] = (byte)(sum / n);
                }
        }
        for (int x = 0; x < SW; x++)
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < SH; y++)
                {
                    int ys = y < 2 ? 0 : y - 2, ye = y > SH - 3 ? SH - 1 : y + 2;
                    int sum = 0, n = 0;
                    for (int yy = ys; yy <= ye; yy++) { sum += b[yy * SB + x * 3 + c]; n++; }
                    a[y * SB + x * 3 + c] = (byte)(sum / n);
                }
            }
    }

    private static void Rows(byte* b0, byte[] bloomBuf, int stride, int y0, int y1, int tick, int bloomK256)
    {
        // аберрация читает соседние пиксели строки — работаем по копии, чтобы не
        // подмешивать уже записанные значения
        byte* rowCopy = stackalloc byte[W * 4];
        fixed (byte* bloom = bloomBuf)
        {
            for (int y = y0; y < y1; y++)
            {
                byte* row = b0 + y * stride;
                Buffer.MemoryCopy(row, rowCopy, W * 4, W * 4);
                int lrow = y * W;
                int ty = y >> 2;
                byte* grow = bloom + ty * SB;
                for (int x = 0; x < W; x++)
                {
                    int d = caMap[lrow + x];
                    int xr = x - d; if (xr < 0) xr = 0;
                    int xb = x + d; if (xb > W - 1) xb = W - 1;
                    int v = lut[lrow + x];
                    int gi = (x >> 2) * 3;

                    int n = (x * 374761393 + y * 668265263 + tick * 1274126177) >> 14 & 7; // зерно -3..3
                    n -= 3;

                    int r = (rowCopy[xr * 4 + 2] * v >> 8) + (grow[gi + 2] * bloomK256 >> 8) + n;
                    int g = (rowCopy[x * 4 + 1] * v >> 8) + (grow[gi + 1] * bloomK256 >> 8) + n;
                    int b = (rowCopy[xb * 4]     * v >> 8) + (grow[gi]     * bloomK256 >> 8) + n;

                    if (r < 0) r = 0; else if (r > 255) r = 255;
                    if (g < 0) g = 0; else if (g > 255) g = 255;
                    if (b < 0) b = 0; else if (b > 255) b = 255;
                    row[x * 4] = (byte)b;
                    row[x * 4 + 1] = (byte)g;
                    row[x * 4 + 2] = (byte)r;
                }
            }
        }
    }
}

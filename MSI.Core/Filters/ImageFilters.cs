using System.Drawing;
using System.Drawing.Imaging;

namespace MSI.Core.Filters;

public sealed class InvertFilter : IImageFilter
{
    public string Name => "invert";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        return BitmapHelper.ProcessPixels(source, (r, g, b) =>
            ((byte)(255 - r), (byte)(255 - g), (byte)(255 - b)));
    }
}

public sealed class ContrastFilter : IImageFilter
{
    public string Name => "contrast";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        float factor = parameters.GetFloat("factor", 1.5f);
        factor = Math.Max(0.1f, Math.Min(10f, factor));

        return BitmapHelper.ProcessPixels(source, (r, g, b) =>
        {
            byte nr = Clamp((r - 128f) * factor + 128f);
            byte ng = Clamp((g - 128f) * factor + 128f);
            byte nb = Clamp((b - 128f) * factor + 128f);
            return (nr, ng, nb);
        });
    }

    private static byte Clamp(float v) => (byte)Math.Max(0, Math.Min(255, (int)v));
}

public sealed class MeanRemovalFilter : IImageFilter
{
    public string Name => "mean_removal";

    private static readonly float[,] Kernel =
    {
        { -1, -1, -1 },
        { -1,  9, -1 },
        { -1, -1, -1 }
    };

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        float strength = Math.Clamp(parameters.GetFloat("strength", 1.0f), 0f, 2f);
        return BitmapHelper.Convolve(source, Kernel, strength);
    }
}

public sealed class EdgeEnhanceFilter : IImageFilter
{
    public string Name => "edge_enhance";

    private static readonly float[,] Kernel =
    {
        {  0, -1,  0 },
        { -1,  5, -1 },
        {  0, -1,  0 }
    };

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        float strength = Math.Clamp(parameters.GetFloat("strength", 1.0f), 0f, 3f);
        return BitmapHelper.Convolve(source, Kernel, strength);
    }
}

public sealed class SphereFilter : IImageFilter
{
    public string Name => "sphere";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        float radius = Math.Clamp(parameters.GetFloat("radius", 1.0f), 0.01f, 2.0f);
        int w = source.Width, h = source.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        var srcData = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        float cx = w / 2f, cy = h / 2f;
        float r = Math.Min(w, h) / 2f * radius;

        unsafe
        {
            byte* srcPtr = (byte*)srcData.Scan0;
            byte* dstPtr = (byte*)dstData.Scan0;
            int stride = srcData.Stride;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - cx) / r;
                    float dy = (y - cy) / r;
                    float dist = dx * dx + dy * dy;

                    int sx, sy;
                    if (dist > 1.0f)
                    {
                        sx = x; sy = y;
                    }
                    else
                    {
                        float dz = MathF.Sqrt(1.0f - dist);
                        float theta = MathF.Atan2(MathF.Sqrt(dist), dz) / MathF.PI * 2f;
                        float phi = MathF.Atan2(dy, dx);
                        float newR = theta * r;
                        sx = (int)(cx + newR * MathF.Cos(phi));
                        sy = (int)(cy + newR * MathF.Sin(phi));
                        sx = Math.Clamp(sx, 0, w - 1);
                        sy = Math.Clamp(sy, 0, h - 1);
                    }

                    byte* sp = srcPtr + sy * stride + sx * 3;
                    byte* dp = dstPtr + y * stride + x * 3;
                    dp[0] = sp[0]; dp[1] = sp[1]; dp[2] = sp[2];
                }
        }

        source.UnlockBits(srcData);
        dst.UnlockBits(dstData);
        return dst;
    }
}

public sealed class PixelateFilter : IImageFilter
{
    public string Name => "pixelate";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        int blockSize = Math.Clamp(parameters.GetInt("block_size", 10), 1, 200);
        int w = source.Width, h = source.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        var srcData = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        unsafe
        {
            byte* srcPtr = (byte*)srcData.Scan0;
            byte* dstPtr = (byte*)dstData.Scan0;
            int stride = srcData.Stride;

            for (int by = 0; by < h; by += blockSize)
                for (int bx = 0; bx < w; bx += blockSize)
                {
                    long sumR = 0, sumG = 0, sumB = 0, count = 0;
                    int maxY = Math.Min(by + blockSize, h);
                    int maxX = Math.Min(bx + blockSize, w);

                    for (int y = by; y < maxY; y++)
                        for (int x = bx; x < maxX; x++)
                        {
                            byte* p = srcPtr + y * stride + x * 3;
                            sumB += p[0]; sumG += p[1]; sumR += p[2]; count++;
                        }

                    byte avgR = (byte)(sumR / count), avgG = (byte)(sumG / count), avgB = (byte)(sumB / count);

                    for (int y = by; y < maxY; y++)
                        for (int x = bx; x < maxX; x++)
                        {
                            byte* p = dstPtr + y * stride + x * 3;
                            p[0] = avgB; p[1] = avgG; p[2] = avgR;
                        }
                }
        }

        source.UnlockBits(srcData);
        dst.UnlockBits(dstData);
        return dst;
    }
}

public sealed class SierraFilter : IImageFilter
{
    public string Name => "sierra";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        int levels = Math.Clamp(parameters.GetInt("levels", 2), 2, 16);
        int w = source.Width, h = source.Height;

        float[] gray = new float[w * h];
        var srcData = source.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)srcData.Scan0;
                int stride = srcData.Stride;
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    int off = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte b = row[x * 3];
                        byte g = row[x * 3 + 1];
                        byte r = row[x * 3 + 2];
                        gray[off + x] = 0.299f * r + 0.587f * g + 0.114f * b;
                    }
                }
            }
        }
        finally { source.UnlockBits(srcData); }

        float step = 255f / (levels - 1);

        for (int y = 0; y < h; y++)
        {
            int row0 = y * w;
            int row1 = (y + 1 < h) ? (y + 1) * w : -1;
            int row2 = (y + 2 < h) ? (y + 2) * w : -1;

            for (int x = 0; x < w; x++)
            {
                float oldVal = gray[row0 + x];
                float newVal = Math.Clamp(MathF.Round(oldVal / step) * step, 0f, 255f);
                gray[row0 + x] = newVal;
                float err = oldVal - newVal;
                if (MathF.Abs(err) < 0.001f) continue;

                if (x + 1 < w) gray[row0 + x + 1] = Math.Clamp(gray[row0 + x + 1] + err * (5f / 32f), 0f, 255f);
                if (x + 2 < w) gray[row0 + x + 2] = Math.Clamp(gray[row0 + x + 2] + err * (3f / 32f), 0f, 255f);
                if (row1 >= 0)
                {
                    if (x - 2 >= 0) gray[row1 + x - 2] = Math.Clamp(gray[row1 + x - 2] + err * (2f / 32f), 0f, 255f);
                    if (x - 1 >= 0) gray[row1 + x - 1] = Math.Clamp(gray[row1 + x - 1] + err * (4f / 32f), 0f, 255f);
                    gray[row1 + x] = Math.Clamp(gray[row1 + x] + err * (5f / 32f), 0f, 255f);
                    if (x + 1 < w) gray[row1 + x + 1] = Math.Clamp(gray[row1 + x + 1] + err * (4f / 32f), 0f, 255f);
                    if (x + 2 < w) gray[row1 + x + 2] = Math.Clamp(gray[row1 + x + 2] + err * (2f / 32f), 0f, 255f);
                }
                if (row2 >= 0)
                {
                    if (x - 1 >= 0) gray[row2 + x - 1] = Math.Clamp(gray[row2 + x - 1] + err * (2f / 32f), 0f, 255f);
                    gray[row2 + x] = Math.Clamp(gray[row2 + x] + err * (3f / 32f), 0f, 255f);
                    if (x + 1 < w) gray[row2 + x + 1] = Math.Clamp(gray[row2 + x + 1] + err * (2f / 32f), 0f, 255f);
                }
            }
        }

        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)dstData.Scan0;
                int stride = dstData.Stride;
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    int off = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte v = (byte)gray[off + x];
                        row[x * 3] = v;
                        row[x * 3 + 1] = v;
                        row[x * 3 + 2] = v;
                    }
                }
            }
        }
        finally { dst.UnlockBits(dstData); }

        return dst;
    }
}

public sealed class CrossDomainColorizeFilter : IImageFilter
{
    public string Name => "cross_domain_colorize";

    public Bitmap Apply(Bitmap source, FilterParameters parameters)
    {
        float hueShift = Math.Clamp(parameters.GetFloat("hue_shift", 0.0f), 0f, 1f);
        float saturation = Math.Clamp(parameters.GetFloat("saturation", 0.9f), 0f, 1f);

        return BitmapHelper.ProcessPixels(source, (r, g, b) =>
        {
            float lum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
            float hue = (lum + hueShift) % 1.0f;
            HsvToRgb(hue, saturation, 1.0f, out byte nr, out byte ng, out byte nb);
            return (nr, ng, nb);
        });
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        float C = v * s;
        float X = C * (1 - MathF.Abs(h * 6 % 2 - 1));
        float m = v - C;
        float r1, g1, b1;
        int seg = (int)(h * 6);

        (r1, g1, b1) = seg switch
        {
            0 => (C, X, 0f),
            1 => (X, C, 0f),
            2 => (0f, C, X),
            3 => (0f, X, C),
            4 => (X, 0f, C),
            _ => (C, 0f, X)
        };

        r = (byte)Math.Round((r1 + m) * 255);
        g = (byte)Math.Round((g1 + m) * 255);
        b = (byte)Math.Round((b1 + m) * 255);
    }
}

internal static class BitmapHelper
{
    public static Bitmap ProcessPixels(Bitmap src, Func<byte, byte, byte, (byte, byte, byte)> transform)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        var srcData = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                byte* sp = (byte*)srcData.Scan0;
                byte* dp = (byte*)dstData.Scan0;
                int stride = srcData.Stride;

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        byte* s = sp + y * stride + x * 3;
                        byte* d = dp + y * stride + x * 3;
                        var (nr, ng, nb) = transform(s[2], s[1], s[0]);
                        d[2] = nr; d[1] = ng; d[0] = nb;
                    }
            }
        }
        finally
        {
            src.UnlockBits(srcData);
            dst.UnlockBits(dstData);
        }
        return dst;
    }

    public static Bitmap Convolve(Bitmap src, float[,] kernel, float strength)
    {
        int w = src.Width, h = src.Height;
        var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        var srcData = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                byte* sp = (byte*)srcData.Scan0;
                byte* dp = (byte*)dstData.Scan0;
                int stride = srcData.Stride;
                int kh = kernel.GetLength(0), kw = kernel.GetLength(1);
                int halfH = kh / 2, halfW = kw / 2;

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float sumR = 0, sumG = 0, sumB = 0;
                        for (int ky = 0; ky < kh; ky++)
                            for (int kx = 0; kx < kw; kx++)
                            {
                                int sy = Math.Clamp(y + ky - halfH, 0, h - 1);
                                int sx = Math.Clamp(x + kx - halfW, 0, w - 1);
                                byte* s = sp + sy * stride + sx * 3;
                                float k = kernel[ky, kx];
                                sumB += s[0] * k; sumG += s[1] * k; sumR += s[2] * k;
                            }
                        byte* orig = sp + y * stride + x * 3;
                        byte* d = dp + y * stride + x * 3;
                        d[2] = ClampByte(sumR * strength + orig[2] * (1 - strength));
                        d[1] = ClampByte(sumG * strength + orig[1] * (1 - strength));
                        d[0] = ClampByte(sumB * strength + orig[0] * (1 - strength));
                    }
            }
        }
        finally
        {
            src.UnlockBits(srcData);
            dst.UnlockBits(dstData);
        }
        return dst;
    }

    private static byte ClampByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)v));
}
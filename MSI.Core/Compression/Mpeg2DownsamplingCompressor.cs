namespace MSI.Core.Compression;

// Lossy 4:2:0 chroma-subsampling kompresija – MPEG-2 varijanta.
// MPEG-2: levo-poravnata pozicija chroma uzorkovanja.
//
// Format kompresovanih podataka:
//   [4B] width      (uint32 LE)
//   [4B] height     (uint32 LE)
//   [w*h] Y kanali
//   [cbW*cbH] Cb  gdje cbW=(w+1)/2, cbH=(h+1)/2
//   [cbW*cbH] Cr

public sealed class Mpeg2DownsamplingCompressor : ICompressor
{
    private int _width;
    private int _height;

    public void SetDimensions(int w, int h) { _width = w; _height = h; }

    public Dictionary<string, string>? MetaInfo =>
        new() { ["compression_detail"] = "downsampling_4:2:0_mpeg2" };

    public byte[] Compress(byte[] data)
    {
        if (data.Length % 3 != 0)
            throw new InvalidOperationException("MPEG-2 zahteva RGB podatke (3 bajta/piksel).");

        int w = _width, h = _height;

        if (w == 0 || h == 0)
        {
            int pixelCount = data.Length / 3;
            w = (int)Math.Ceiling(Math.Sqrt(pixelCount));
            h = (pixelCount + w - 1) / w;
        }

        int totalPixels = w * h;
        byte[] padded = data;
        if (data.Length < totalPixels * 3)
        {
            padded = new byte[totalPixels * 3];
            Array.Copy(data, padded, data.Length);
        }

        byte[] yArr = new byte[totalPixels];
        byte[] cbArr = new byte[totalPixels];
        byte[] crArr = new byte[totalPixels];

        for (int i = 0; i < totalPixels; i++)
        {
            RgbToYcbcr(padded[i * 3], padded[i * 3 + 1], padded[i * 3 + 2],
                out yArr[i], out cbArr[i], out crArr[i]);
        }

        int cbW = (w + 1) / 2, cbH = (h + 1) / 2;
        byte[] cbDown = Downsample(cbArr, w, h, cbW, cbH);
        byte[] crDown = Downsample(crArr, w, h, cbW, cbH);

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)w));
        ms.Write(BitConverter.GetBytes((uint)h));
        ms.Write(yArr);
        ms.Write(cbDown);
        ms.Write(crDown);
        return ms.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var ms = new MemoryStream(data);
        int w = (int)ReadU32(ms);
        int h = (int)ReadU32(ms);

        int totalPixels = w * h;
        byte[] yArr = new byte[totalPixels];
        ms.ReadExactly(yArr);

        int cbW = (w + 1) / 2, cbH = (h + 1) / 2;
        byte[] cbDown = new byte[cbW * cbH];
        byte[] crDown = new byte[cbW * cbH];
        ms.ReadExactly(cbDown);
        ms.ReadExactly(crDown);

        byte[] cb = Upsample(cbDown, cbW, cbH, w, h);
        byte[] cr = Upsample(crDown, cbW, cbH, w, h);

        byte[] result = new byte[totalPixels * 3];
        for (int i = 0; i < totalPixels; i++)
        {
            YcbcrToRgb(yArr[i], cb[i], cr[i],
                out result[i * 3], out result[i * 3 + 1], out result[i * 3 + 2]);
        }
        return result;
    }

    private static void RgbToYcbcr(byte r, byte g, byte b,
        out byte yv, out byte cbv, out byte crv)
    {
        double Y = 16 + 65.481 * r / 255.0 + 128.553 * g / 255.0 + 24.966 * b / 255.0;
        double Cb = 128 - 37.797 * r / 255.0 - 74.203 * g / 255.0 + 112.0 * b / 255.0;
        double Cr = 128 + 112.0 * r / 255.0 - 93.786 * g / 255.0 - 18.214 * b / 255.0;
        yv = Clamp(Y); cbv = Clamp(Cb); crv = Clamp(Cr);
    }

    private static void YcbcrToRgb(byte yv, byte cbv, byte crv,
        out byte r, out byte g, out byte b)
    {
        double Y = yv - 16, Cb = cbv - 128, Cr = crv - 128;
        r = Clamp(1.164 * Y + 1.596 * Cr);
        g = Clamp(1.164 * Y - 0.392 * Cb - 0.813 * Cr);
        b = Clamp(1.164 * Y + 2.017 * Cb);
    }

    private static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

    private static byte[] Downsample(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        byte[] dst = new byte[dstW * dstH];
        for (int dy = 0; dy < dstH; dy++)
            for (int dx = 0; dx < dstW; dx++)
            {
                int sum = 0, count = 0;
                for (int ky = 0; ky < 2; ky++)
                    for (int kx = 0; kx < 2; kx++)
                    {
                        int sy = dy * 2 + ky, sx = dx * 2 + kx;
                        if (sy < srcH && sx < srcW)
                        {
                            int idx = sy * srcW + sx;
                            if (idx < src.Length) { sum += src[idx]; count++; }
                        }
                    }
                dst[dy * dstW + dx] = count > 0 ? (byte)(sum / count) : (byte)0;
            }
        return dst;
    }

    private static byte[] Upsample(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        byte[] dst = new byte[dstW * dstH];
        for (int dy = 0; dy < dstH; dy++)
            for (int dx = 0; dx < dstW; dx++)
            {
                double srcXd = dx / 2.0, srcYd = dy / 2.0;
                int x0 = Math.Clamp((int)srcXd, 0, srcW - 1);
                int x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                int y0 = Math.Clamp((int)srcYd, 0, srcH - 1);
                int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
                double wx = srcXd - x0, wy = srcYd - y0;
                double v = src[y0 * srcW + x0] * (1 - wx) * (1 - wy)
                          + src[y0 * srcW + x1] * wx * (1 - wy)
                          + src[y1 * srcW + x0] * (1 - wx) * wy
                          + src[y1 * srcW + x1] * wx * wy;
                dst[dy * dstW + dx] = (byte)Math.Round(v);
            }
        return dst;
    }

    private static uint ReadU32(Stream s)
    {
        byte[] b = new byte[4]; s.ReadExactly(b);
        return BitConverter.ToUInt32(b);
    }
}
namespace MSI.Core.Compression;

// format 
// [4B] origW (uint32 LE)
// [4B] origH (uint32 LE)
// [1B] 0 = MPEG1, 1 = MPEG2
// [origW * origH] Y kanali
// [cbW * cbH] Cb kanali   (cbW = (origW+1)/2, cbH = (origH+1)/2)
// [cbW * cbH] Cr kanali

public sealed class DownsamplingCompressor : ICompressor
{
    private readonly bool _mpeg2;

    public DownsamplingCompressor(bool mpeg2 = false) => _mpeg2 = mpeg2;

    public Dictionary<string, string>? MetaInfo => new()
    {
        ["compression_detail"] = _mpeg2 ? "downsampling_420_mpeg2" : "downsampling_420_mpeg1"
    };


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
                        if (sy < srcH && sx < srcW) { sum += src[sy * srcW + sx]; count++; }
                    }
                dst[dy * dstW + dx] = (byte)(sum / count);
            }
        return dst;
    }

    private static byte[] Upsample(byte[] src, int srcW, int srcH, int dstW, int dstH, bool mpeg2)
    {
        byte[] dst = new byte[dstW * dstH];
        for (int dy = 0; dy < dstH; dy++)
            for (int dx = 0; dx < dstW; dx++)
            {
                double srcXd = mpeg2 ? dx / 2.0 : (dx + 0.5) / 2.0 - 0.5;
                double srcYd = mpeg2 ? dy / 2.0 : (dy + 0.5) / 2.0 - 0.5;
                int x0 = Math.Max(0, (int)srcXd), x1 = Math.Min(srcW - 1, x0 + 1);
                int y0 = Math.Max(0, (int)srcYd), y1 = Math.Min(srcH - 1, y0 + 1);
                double wx = srcXd - x0, wy = srcYd - y0;
                double v = src[y0 * srcW + x0] * (1 - wx) * (1 - wy)
                          + src[y0 * srcW + x1] * wx * (1 - wy)
                          + src[y1 * srcW + x0] * (1 - wx) * wy
                          + src[y1 * srcW + x1] * wx * wy;
                dst[dy * dstW + dx] = (byte)Math.Round(v);
            }
        return dst;
    }

    public byte[] Compress(byte[] data)
    {
        throw new NotImplementedException();
    }

    public byte[] Decompress(byte[] data)
    {
        throw new NotImplementedException();
    }

}

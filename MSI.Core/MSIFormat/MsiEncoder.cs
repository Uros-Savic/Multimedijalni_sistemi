using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using MSI.Core.Compression;

namespace MSI.Core.MsiFormat;

public sealed class MsiEncoder
{
    public MsiEncoder() { }

    public void Encode(Bitmap bmp, Stream output,
        byte colorspace = MsiConstants.CS_RGB,
        byte compression = MsiConstants.COMP_NONE,
        Dictionary<string, string>? extraMeta = null)
    {
        ArgumentNullException.ThrowIfNull(bmp);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
            throw new ArgumentException("Stream mora biti writable.", nameof(output));

        ValidateColorspace(colorspace);

        byte[] rawPixels = ExtractPixels(bmp, colorspace, out int channels);

        ICompressor comp = CompressorFactory.Create(compression, bmp.Width, bmp.Height);
        byte[] compressedPixels = comp.Compress(rawPixels);

        var meta = extraMeta != null
            ? new Dictionary<string, string>(extraMeta)
            : new Dictionary<string, string>();
        meta["encoder"] = "MSI.Core v1.0";
        meta["created"] = DateTime.UtcNow.ToString("O");
        meta["orig_width"] = bmp.Width.ToString();
        meta["orig_height"] = bmp.Height.ToString();
        meta["colorspace"] = colorspace.ToString();
        if (comp.MetaInfo != null)
            foreach (var kv in comp.MetaInfo) meta[kv.Key] = kv.Value;

        byte[] metaBytes = SerializeMeta(meta);

        var header = new MsiHeader
        {
            Width = (uint)bmp.Width,
            Height = (uint)bmp.Height,
            Channels = (byte)channels,
            Colorspace = colorspace,
            Compression = compression,
            MetaLen = (uint)metaBytes.Length,
            PixelLen = (uint)compressedPixels.Length
        };

        using var ms = new MemoryStream();
        WriteHeader(ms, header);
        ms.Write(metaBytes);
        ms.Write(compressedPixels);

        byte[] content = ms.ToArray();
        uint crc = Crc32.Compute(content);

        output.Write(content);
        output.Write(BitConverter.GetBytes(crc));
    }

    public byte[] EncodeToBytes(Bitmap bmp,
        byte colorspace = MsiConstants.CS_RGB,
        byte compression = MsiConstants.COMP_NONE,
        Dictionary<string, string>? extraMeta = null)
    {
        using var ms = new MemoryStream();
        Encode(bmp, ms, colorspace, compression, extraMeta);
        return ms.ToArray();
    }

    private static byte[] ExtractPixels(Bitmap bmp, byte colorspace, out int channels)
    {
        channels = colorspace == MsiConstants.CS_LINEAR ? 1 : 3;
        int w = bmp.Width, h = bmp.Height;
        byte[] pixels = new byte[w * h * channels];

        var rect = new Rectangle(0, 0, w, h);
        var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;
                int idx = 0;

                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte r = row[x * 3 + 2];
                        byte g = row[x * 3 + 1];
                        byte b = row[x * 3 + 0];

                        switch (colorspace)
                        {
                            case MsiConstants.CS_LINEAR:
                                pixels[idx++] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                                break;

                            case MsiConstants.CS_HSV:
                                RgbToHsv(r, g, b,
                                    out pixels[idx], out pixels[idx + 1], out pixels[idx + 2]);
                                idx += 3;
                                break;

                            default:
                                pixels[idx++] = r;
                                pixels[idx++] = g;
                                pixels[idx++] = b;
                                break;
                        }
                    }
                }
            }
        }
        finally { bmp.UnlockBits(bmpData); }

        return pixels;
    }

    internal static void RgbToHsv(byte r, byte g, byte b,
        out byte hOut, out byte sOut, out byte vOut)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        vOut = (byte)Math.Round(max * 255);
        sOut = (byte)(max < 1e-10 ? 0 : Math.Round((delta / max) * 255));
        double h = 0;

        if (delta > 1e-10)
        {
            if (Math.Abs(max - rd) < 1e-10)
                h = 60 * (((gd - bd) / delta) % 6);
            else if (Math.Abs(max - gd) < 1e-10)
                h = 60 * ((bd - rd) / delta + 2);
            else
                h = 60 * ((rd - gd) / delta + 4);
        }
        if (h < 0) h += 360;
        hOut = (byte)Math.Round(h / 360.0 * 255);
    }

    private static void WriteHeader(Stream s, MsiHeader h)
    {
        s.Write(MsiConstants.Magic);
        WriteU16(s, h.Version);
        WriteU16(s, h.HeaderLen);
        WriteU32(s, h.Width);
        WriteU32(s, h.Height);
        s.WriteByte(h.Channels);
        s.WriteByte(h.Colorspace);
        s.WriteByte(h.Compression);
        s.WriteByte(h.Reserved);
        WriteU32(s, h.MetaLen);
        WriteU32(s, h.PixelLen);
    }

    internal static byte[] SerializeMeta(Dictionary<string, string> meta)
    {
        using var ms = new MemoryStream();
        WriteU16ToStream(ms, (ushort)meta.Count);
        foreach (var kv in meta)
        {
            byte[] kBytes = Encoding.UTF8.GetBytes(kv.Key);
            byte[] vBytes = Encoding.UTF8.GetBytes(kv.Value);
            if (kBytes.Length > 255)
                throw new InvalidOperationException($"Meta kljuc predugacak: {kv.Key}");
            ms.WriteByte((byte)kBytes.Length);
            ms.Write(kBytes);
            WriteU16ToStream(ms, (ushort)vBytes.Length);
            ms.Write(vBytes);
        }
        return ms.ToArray();
    }

    internal static uint ComputeCrc32(byte[] data) => Crc32.Compute(data);

    private static void ValidateColorspace(byte cs)
    {
        if (cs != MsiConstants.CS_LINEAR && cs != MsiConstants.CS_RGB && cs != MsiConstants.CS_HSV)
            throw new ArgumentException(
                $"Colorspace {cs} nije podrzan. Dozvoljeni: 0=Linear, 1=RGB, 3=HSV.");
    }

    private static void WriteU16(Stream s, ushort v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));
    private static void WriteU16ToStream(MemoryStream s, ushort v) => s.Write(BitConverter.GetBytes(v));
}
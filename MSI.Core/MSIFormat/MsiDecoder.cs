using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using MSI.Core.Compression;

namespace MSI.Core.MsiFormat;

// dekodira MSI u bitmap

public sealed class MsiDecoder
{
    public (Bitmap Bitmap, MsiHeader Header) Decode(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
            throw new ArgumentException("Stream mora biti readable.", nameof(input));

        byte[] allBytes = ReadAllBytes(input);

        if (allBytes.Length < MsiConstants.HeaderLength + 4)
            throw new MsiFormatException("Fajl je previse mali da bi bio validan MSI.");

        uint storedCrc = BitConverter.ToUInt32(allBytes, allBytes.Length - 4);
        byte[] content = allBytes[..^4];
        uint calcCrc = Crc32.Compute(content);

        if (storedCrc != calcCrc)
            throw new MsiFormatException(
                $"CRC32 mismatch: ocekivano 0x{calcCrc:X8}, pronadjeno 0x{storedCrc:X8}. Fajl je ostecen.");

        using var ms = new MemoryStream(content);

        MsiHeader header = ReadHeader(ms);

        byte[] metaBytes = new byte[header.MetaLen];
        ms.ReadExactly(metaBytes);
        header.Meta = DeserializeMeta(metaBytes);

        byte[] pixelBytes = new byte[header.PixelLen];
        ms.ReadExactly(pixelBytes);

        ICompressor comp = CompressorFactory.Create(header.Compression);
        byte[] pixels = comp.Decompress(pixelBytes);

        long expectedRaw = (long)header.Width * header.Height * header.Channels;
        if (pixels.Length != expectedRaw)
            throw new MsiFormatException(
                $"Piksel segment ne odgovara dimenzijama: ocekivano {expectedRaw}, dobijeno {pixels.Length}.");

        Bitmap bmp = PixelsToBitmap(pixels, (int)header.Width, (int)header.Height,
                                    header.Channels, header.Colorspace);
        return (bmp, header);
    }

    public (Bitmap Bitmap, MsiHeader Header) Decode(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Decode(ms);
    }

    private static Bitmap PixelsToBitmap(byte[] pixels, int w, int h,
        byte channels, byte colorspace)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
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
                        byte r, g, b;
                        if (channels == 1)
                        {
                            byte v = pixels[idx++];
                            r = g = b = v;
                        }
                        else if (colorspace == MsiConstants.CS_HSV)
                        {
                            HsvToRgb(pixels[idx], pixels[idx + 1], pixels[idx + 2],
                                out r, out g, out b);
                            idx += 3;
                        }
                        else
                        {
                            r = pixels[idx++];
                            g = pixels[idx++];
                            b = pixels[idx++];
                        }
                        row[x * 3 + 2] = r;
                        row[x * 3 + 1] = g;
                        row[x * 3 + 0] = b;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(bmpData); }
        return bmp;
    }

    internal static void HsvToRgb(byte hByte, byte sByte, byte vByte,
        out byte r, out byte g, out byte b)
    {
        double h = hByte / 255.0 * 360.0;
        double s = sByte / 255.0;
        double v = vByte / 255.0;

        if (s < 1e-10) { byte vi = (byte)Math.Round(v * 255); r = g = b = vi; return; }

        double C = v * s;
        double X = C * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - C;

        double r1, g1, b1;
        int seg = (int)(h / 60) % 6;
        (r1, g1, b1) = seg switch
        {
            0 => (C, X, 0.0),
            1 => (X, C, 0.0),
            2 => (0.0, C, X),
            3 => (0.0, X, C),
            4 => (X, 0.0, C),
            _ => (C, 0.0, X)
        };
        r = (byte)Math.Round((r1 + m) * 255);
        g = (byte)Math.Round((g1 + m) * 255);
        b = (byte)Math.Round((b1 + m) * 255);
    }

    private static MsiHeader ReadHeader(Stream s)
    {
        var header = new MsiHeader();
        header.Magic = new byte[4];
        s.ReadExactly(header.Magic);

        if (!header.IsMagicValid())
            throw new MsiFormatException(
                $"Nevazeca magija: [{string.Join(", ", header.Magic.Select(b => $"0x{b:X2}"))}]");

        header.Version = ReadU16(s);
        if (header.Version != MsiConstants.Version)
            throw new MsiFormatException($"Nepodrzana verzija: 0x{header.Version:X4}");

        header.HeaderLen = ReadU16(s);
        header.Width = ReadU32(s);
        header.Height = ReadU32(s);
        header.Channels = (byte)s.ReadByte();
        header.Colorspace = (byte)s.ReadByte();
        header.Compression = (byte)s.ReadByte();
        header.Reserved = (byte)s.ReadByte();
        header.MetaLen = ReadU32(s);
        header.PixelLen = ReadU32(s);

        if (header.Channels != 1 && header.Channels != 3)
            throw new MsiFormatException($"Nevazeci broj kanala: {header.Channels}");
        if (header.Width < MsiConstants.MinDimension || header.Width > MsiConstants.MaxDimension)
            throw new MsiFormatException($"Nevazeca sirina: {header.Width}");
        if (header.Height < MsiConstants.MinDimension || header.Height > MsiConstants.MaxDimension)
            throw new MsiFormatException($"Nevazeca visina: {header.Height}");

        return header;
    }

    internal static Dictionary<string, string> DeserializeMeta(byte[] metaBytes)
    {
        var dict = new Dictionary<string, string>();
        if (metaBytes.Length == 0) return dict;
        using var ms = new MemoryStream(metaBytes);
        ushort cnt = ReadU16(ms);
        for (int i = 0; i < cnt; i++)
        {
            int kLen = ms.ReadByte();
            byte[] kBuf = new byte[kLen]; ms.ReadExactly(kBuf);
            ushort vLen = ReadU16(ms);
            byte[] vBuf = new byte[vLen]; ms.ReadExactly(vBuf);
            dict[Encoding.UTF8.GetString(kBuf)] = Encoding.UTF8.GetString(vBuf);
        }
        return dict;
    }

    private static byte[] ReadAllBytes(Stream s)
    {
        if (s.CanSeek) s.Position = 0;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static ushort ReadU16(Stream s)
    { byte[] b = new byte[2]; s.ReadExactly(b); return BitConverter.ToUInt16(b); }

    private static uint ReadU32(Stream s)
    { byte[] b = new byte[4]; s.ReadExactly(b); return BitConverter.ToUInt32(b); }
}
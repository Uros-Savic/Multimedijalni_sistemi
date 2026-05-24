using System.Drawing;
using System.Drawing.Imaging;
using MSI.Core.Compression;
using MSI.Core.Filters;
using MSI.Core.MsiFormat;
using Xunit;

namespace MSI.Tests;

public class MsiFormatTests
{
    private static Bitmap MakeSolid(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp); g.Clear(color);
        return bmp;
    }

    private static Bitmap MakeGradient(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, Color.FromArgb(x * 255 / w, y * 255 / h, 128));
        return bmp;
    }

    [Fact]
    public void RoundTrip_None_RGB_PixelsMatch()
    {
        using var orig = MakeGradient(32, 32);
        byte[] bytes = new MsiEncoder().EncodeToBytes(orig,
            MsiConstants.CS_RGB, MsiConstants.COMP_NONE);
        var (decoded, header) = new MsiDecoder().Decode(bytes);

        Assert.Equal(MsiConstants.CS_RGB, header.Colorspace);
        Assert.Equal(MsiConstants.COMP_NONE, header.Compression);
        Assert.Equal(32u, header.Width);
        Assert.Equal(32u, header.Height);

        for (int i = 0; i < 20; i++)
        {
            var o = orig.GetPixel(i, i);
            var d = decoded.GetPixel(i, i);
            Assert.Equal(o.R, d.R); Assert.Equal(o.G, d.G); Assert.Equal(o.B, d.B);
        }
        decoded.Dispose();
    }

    [Fact]
    public void RoundTrip_Huffman_RGB_Lossless()
    {
        using var orig = MakeSolid(16, 16, Color.DodgerBlue);
        byte[] bytes = new MsiEncoder().EncodeToBytes(orig,
            MsiConstants.CS_RGB, MsiConstants.COMP_HUFFMAN);
        var (decoded, header) = new MsiDecoder().Decode(bytes);

        Assert.Equal(MsiConstants.COMP_HUFFMAN, header.Compression);
        var o = orig.GetPixel(8, 8); var d = decoded.GetPixel(8, 8);
        Assert.Equal(o.R, d.R); Assert.Equal(o.G, d.G); Assert.Equal(o.B, d.B);
        decoded.Dispose();
    }

    [Fact]
    public void RoundTrip_Mpeg2_Lossy_WithTolerance()
    {
        using var orig = MakeSolid(32, 32, Color.Tomato);
        byte[] bytes = new MsiEncoder().EncodeToBytes(orig,
            MsiConstants.CS_RGB, MsiConstants.COMP_MPEG2);
        var (decoded, header) = new MsiDecoder().Decode(bytes);

        Assert.Equal(MsiConstants.COMP_MPEG2, header.Compression);
        var o = orig.GetPixel(16, 16); var d = decoded.GetPixel(16, 16);
        Assert.InRange(Math.Abs(o.R - d.R), 0, 15);
        Assert.InRange(Math.Abs(o.G - d.G), 0, 15);
        Assert.InRange(Math.Abs(o.B - d.B), 0, 15);
        decoded.Dispose();
    }

    [Fact]
    public void RoundTrip_HSV_Colorspace()
    {
        using var orig = MakeSolid(16, 16, Color.ForestGreen);
        byte[] bytes = new MsiEncoder().EncodeToBytes(orig,
            MsiConstants.CS_HSV, MsiConstants.COMP_NONE);
        var (decoded, header) = new MsiDecoder().Decode(bytes);

        Assert.Equal(MsiConstants.CS_HSV, header.Colorspace);
        Assert.Equal(3, header.Channels);
        var o = orig.GetPixel(8, 8); var d = decoded.GetPixel(8, 8);
        Assert.InRange(Math.Abs(o.R - d.R), 0, 3);
        Assert.InRange(Math.Abs(o.G - d.G), 0, 3);
        Assert.InRange(Math.Abs(o.B - d.B), 0, 3);
        decoded.Dispose();
    }

    [Fact]
    public void RoundTrip_Linear_Grayscale()
    {
        using var orig = MakeSolid(8, 8, Color.FromArgb(100, 100, 100));
        byte[] bytes = new MsiEncoder().EncodeToBytes(orig,
            MsiConstants.CS_LINEAR, MsiConstants.COMP_NONE);
        var (decoded, header) = new MsiDecoder().Decode(bytes);

        Assert.Equal(MsiConstants.CS_LINEAR, header.Colorspace);
        Assert.Equal(1, header.Channels);
        decoded.Dispose();
    }

    [Fact]
    public void Header_MagicBytes_AreCorrect()
    {
        using var bmp = MakeSolid(4, 4, Color.Red);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        Assert.Equal((byte)'M', bytes[0]); Assert.Equal((byte)'S', bytes[1]);
        Assert.Equal((byte)'I', bytes[2]); Assert.Equal((byte)'0', bytes[3]);
    }

    [Fact]
    public void Decode_InvalidMagic_Throws()
    {
        using var bmp = MakeSolid(4, 4, Color.Blue);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        bytes[0] = 0xFF;
        Assert.Throws<MsiFormatException>(() => new MsiDecoder().Decode(bytes));
    }

    [Fact]
    public void Decode_CorruptedCrc_Throws()
    {
        using var bmp = MakeSolid(4, 4, Color.Green);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        bytes[^1] ^= 0xFF;
        Assert.Throws<MsiFormatException>(() => new MsiDecoder().Decode(bytes));
    }

    [Fact]
    public void Fuzz_RandomBytes_NeverCrashes()
    {
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            byte[] garbage = new byte[rng.Next(1, 512)];
            rng.NextBytes(garbage);
            try { new MsiDecoder().Decode(garbage); }
            catch (MsiFormatException) { /* ocekivano */ }
            catch (Exception ex)
            { Assert.Fail($"Fuzz {i}: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    [Fact]
    public void Header_Dimensions_StoredCorrectly()
    {
        using var bmp = MakeSolid(77, 43, Color.Cyan);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        Assert.Equal(77u, BitConverter.ToUInt32(bytes, 8));
        Assert.Equal(43u, BitConverter.ToUInt32(bytes, 12));
    }

    [Fact]
    public void Meta_CustomValues_Preserved()
    {
        using var bmp = MakeSolid(4, 4, Color.Purple);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp,
            extraMeta: new Dictionary<string, string> { ["autor"] = "test", ["ver"] = "42" });
        var (_, header) = new MsiDecoder().Decode(bytes);
        Assert.Equal("test", header.Meta["autor"]);
        Assert.Equal("42", header.Meta["ver"]);
    }

    [Fact]
    public void CompressorFactory_ShannonFano_Throws()
        => Assert.Throws<NotSupportedException>(() => CompressorFactory.Create(1));

    [Fact]
    public void CompressorFactory_Mpeg1_Throws()
        => Assert.Throws<NotSupportedException>(() => CompressorFactory.Create(3));

    [Fact]
    public void Crc32_SameInput_SameOutput()
    {
        byte[] data = { 10, 20, 30, 40, 50 };
        uint c1 = Crc32.Compute(data);
        Assert.Equal(c1, Crc32.Compute(data));
        data[0] = 99;
        Assert.NotEqual(c1, Crc32.Compute(data));
    }

    [Fact]
    public void Decode_TooSmallFile_Throws()
        => Assert.Throws<MsiFormatException>(() => new MsiDecoder().Decode(new byte[3]));
}

public class FilterTests
{
    private static Bitmap MakeRgb(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var gr = Graphics.FromImage(bmp); gr.Clear(Color.FromArgb(r, g, b));
        return bmp;
    }

    [Fact]
    public void Invert_White_BecomesBlack()
    {
        using var src = MakeRgb(8, 8, 255, 255, 255);
        using var res = new InvertFilter().Apply(src, new FilterParameters());
        var px = res.GetPixel(4, 4);
        Assert.Equal(0, px.R); Assert.Equal(0, px.G); Assert.Equal(0, px.B);
    }

    [Fact]
    public void Invert_Black_BecomesWhite()
    {
        using var src = MakeRgb(8, 8, 0, 0, 0);
        using var res = new InvertFilter().Apply(src, new FilterParameters());
        var px = res.GetPixel(4, 4);
        Assert.Equal(255, px.R); Assert.Equal(255, px.G); Assert.Equal(255, px.B);
    }

    [Fact]
    public void Pixelate_PreservesDimensions()
    {
        using var src = new Bitmap(100, 80, PixelFormat.Format24bppRgb);
        var prms = new FilterParameters(new Dictionary<string, string> { ["block_size"] = "10" });
        using var res = new PixelateFilter().Apply(src, prms);
        Assert.Equal(100, res.Width); Assert.Equal(80, res.Height);
    }

    [Fact]
    public void Sphere_PreservesDimensions()
    {
        using var src = new Bitmap(50, 50, PixelFormat.Format24bppRgb);
        using var res = new SphereFilter().Apply(src, new FilterParameters());
        Assert.Equal(50, res.Width); Assert.Equal(50, res.Height);
    }

    [Theory]
    [InlineData("invert")]
    [InlineData("contrast")]
    [InlineData("mean_removal")]
    [InlineData("edge_enhance")]
    [InlineData("sphere")]
    [InlineData("pixelate")]
    [InlineData("sierra")]
    [InlineData("cross_domain_colorize")]
    public void FilterFactory_AllFiltersExist(string name)
    {
        Assert.True(FilterFactory.Exists(name));
        Assert.Equal(name, FilterFactory.Get(name).Name);
    }

    [Fact]
    public void FilterFactory_UnknownName_Throws()
        => Assert.Throws<KeyNotFoundException>(() => FilterFactory.Get("nepostoji_xyz"));
}

public class HuffmanTests
{
    [Fact]
    public void Huffman_Empty_Roundtrip()
    {
        var comp = new HuffmanCompressor();
        Assert.Empty(comp.Decompress(comp.Compress(Array.Empty<byte>())));
    }

    [Fact]
    public void Huffman_SingleByte_Roundtrip()
    {
        var comp = new HuffmanCompressor();
        byte[] data = { 77 };
        Assert.Equal(data, comp.Decompress(comp.Compress(data)));
    }

    [Fact]
    public void Huffman_AllSameBytes_Roundtrip()
    {
        var comp = new HuffmanCompressor();
        byte[] data = Enumerable.Repeat((byte)42, 1000).ToArray();
        Assert.Equal(data, comp.Decompress(comp.Compress(data)));
    }

    [Fact]
    public void Huffman_RandomData_Roundtrip()
    {
        var comp = new HuffmanCompressor();
        byte[] data = new byte[5000];
        new Random(7).NextBytes(data);
        Assert.Equal(data, comp.Decompress(comp.Compress(data)));
    }

    [Fact]
    public void Huffman_LowEntropy_CompressesBetter()
    {
        var comp = new HuffmanCompressor();
        byte[] data = Enumerable.Range(0, 10000).Select(i => (byte)(i % 4)).ToArray();
        byte[] compressed = comp.Compress(data);
        Assert.True(compressed.Length < data.Length,
            $"Kompresovani ({compressed.Length}B) >= originalni ({data.Length}B)");
    }
}

public class Mpeg2Tests
{
    [Fact]
    public void Mpeg2_Roundtrip_DimensionsPreserved()
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp); g.Clear(Color.CornflowerBlue);
        byte[] enc = new MsiEncoder().EncodeToBytes(bmp,
            MsiConstants.CS_RGB, MsiConstants.COMP_MPEG2);
        var (decoded, header) = new MsiDecoder().Decode(enc);
        Assert.Equal(64u, header.Width); Assert.Equal(64u, header.Height);
        decoded.Dispose();
    }

    [Fact]
    public void Mpeg2_RedColor_RemainsRed()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp); g.Clear(Color.Red);
        byte[] enc = new MsiEncoder().EncodeToBytes(bmp,
            MsiConstants.CS_RGB, MsiConstants.COMP_MPEG2);
        var (decoded, _) = new MsiDecoder().Decode(enc);
        var px = decoded.GetPixel(16, 16);
        Assert.True(px.R > 200, $"Ocekivano R>200, dobijeno R={px.R}");
        decoded.Dispose();
    }
}

public class HsvConversionTests
{
    private static void RgbToHsv(byte r, byte g, byte b,
        out byte h, out byte s, out byte v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;
        v = (byte)Math.Round(max * 255);
        s = (byte)(max < 1e-10 ? 0 : Math.Round(delta / max * 255));
        double hd = 0;
        if (delta > 1e-10)
        {
            if (Math.Abs(max - rd) < 1e-10) hd = 60 * ((gd - bd) / delta % 6);
            else if (Math.Abs(max - gd) < 1e-10) hd = 60 * ((bd - rd) / delta + 2);
            else hd = 60 * ((rd - gd) / delta + 4);
        }
        if (hd < 0) hd += 360;
        h = (byte)Math.Round(hd / 360.0 * 255);
    }

    private static void HsvToRgb(byte hb, byte sb, byte vb,
        out byte r, out byte g, out byte b)
    {
        double h = hb / 255.0 * 360.0, s = sb / 255.0, v = vb / 255.0;
        if (s < 1e-10) { byte vi = (byte)Math.Round(v * 255); r = g = b = vi; return; }
        double C = v * s, X = C * (1 - Math.Abs(h / 60.0 % 2 - 1)), m = v - C;
        double r1, g1, b1;
        (r1, g1, b1) = ((int)(h / 60) % 6) switch
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

    [Fact]
    public void White_HasZeroSaturation()
    {
        RgbToHsv(255, 255, 255, out _, out byte s, out byte v);
        Assert.Equal(0, s);
        Assert.Equal(255, v);
    }

    [Fact]
    public void Black_HasZeroValue()
    {
        RgbToHsv(0, 0, 0, out _, out _, out byte v);
        Assert.Equal(0, v);
    }

    [Fact]
    public void RgbHsv_Roundtrip_WithinTolerance()
    {
        var cases = new[] { (255, 0, 0), (0, 255, 0), (0, 0, 255), (128, 64, 32) };
        foreach (var (ri, gi, bi) in cases)
        {
            RgbToHsv((byte)ri, (byte)gi, (byte)bi, out byte h, out byte s, out byte v);
            HsvToRgb(h, s, v, out byte r2, out byte g2, out byte b2);
            Assert.InRange(Math.Abs(ri - r2), 0, 3);
            Assert.InRange(Math.Abs(gi - g2), 0, 3);
            Assert.InRange(Math.Abs(bi - b2), 0, 3);
        }
    }
}

public class UiLogicTests
{
    private static Bitmap MakeBmp(Color c, int w = 10, int h = 10)
    {
        var b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(b); g.Clear(c);
        return b;
    }

    [Fact]
    public void Export_ToMsi_RGB_CanReDecode()
    {
        using var bmp = MakeBmp(Color.Teal, 32, 32);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp,
            MsiConstants.CS_RGB, MsiConstants.COMP_NONE);
        var (decoded, header) = new MsiDecoder().Decode(bytes);
        Assert.Equal(32u, header.Width);
        Assert.Equal(MsiConstants.CS_RGB, header.Colorspace);
        decoded.Dispose();
    }

    [Fact]
    public void Export_ToMsi_HSV_CanReDecode()
    {
        using var bmp = MakeBmp(Color.Orange, 16, 16);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp,
            MsiConstants.CS_HSV, MsiConstants.COMP_NONE);
        var (decoded, header) = new MsiDecoder().Decode(bytes);
        Assert.Equal(MsiConstants.CS_HSV, header.Colorspace);
        decoded.Dispose();
    }

    [Fact]
    public void Export_ToMsi_Huffman_CanReDecode()
    {
        using var bmp = MakeBmp(Color.Navy, 16, 16);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp,
            MsiConstants.CS_RGB, MsiConstants.COMP_HUFFMAN);
        var (decoded, header) = new MsiDecoder().Decode(bytes);
        Assert.Equal(MsiConstants.COMP_HUFFMAN, header.Compression);
        decoded.Dispose();
    }

    [Fact]
    public void MsiValidator_ValidFile_IsValid()
    {
        using var bmp = MakeBmp(Color.SteelBlue, 16, 16);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        var result = MsiValidator.Validate(bytes);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(16u, result.Width);
    }

    [Fact]
    public void MsiValidator_CorruptedCrc_IsInvalid()
    {
        using var bmp = MakeBmp(Color.Coral, 8, 8);
        byte[] bytes = new MsiEncoder().EncodeToBytes(bmp);
        bytes[^1] ^= 0xFF;
        var result = MsiValidator.Validate(bytes);
        Assert.False(result.IsValid);
        Assert.Contains("CRC", result.Error);
    }
}

public class PerformanceTests
{
    private static Bitmap MakeNoisy(int w = 1024, int h = 768)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rng = new Random(42);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
        return bmp;
    }

    [Theory]
    [InlineData("invert", 100)]
    [InlineData("contrast", 100)]
    [InlineData("mean_removal", 100)]
    [InlineData("edge_enhance", 100)]
    [InlineData("sphere", 100)]
    [InlineData("pixelate", 100)]
    [InlineData("sierra", 2000)]
    [InlineData("cross_domain_colorize", 100)]
    public void Filter_1024x768_UnderLimit(string filterName, int limitMs)
    {
        using var src = MakeNoisy();
        var prms = new FilterParameters(new Dictionary<string, string>
        {
            ["factor"] = "1.5",
            ["block_size"] = "10",
            ["levels"] = "4",
            ["strength"] = "1.0",
            ["radius"] = "1.0",
            ["hue_shift"] = "0.0",
            ["saturation"] = "0.8"
        });
        using var w0 = FilterFactory.Get(filterName).Apply(src, prms);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var result = FilterFactory.Get(filterName).Apply(src, prms);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < limitMs,
            $"{filterName}: {sw.ElapsedMilliseconds}ms > {limitMs}ms");
    }

    [Fact]
    public void ThreeFilters_1024x768_Under500ms()
    {
        using var src = MakeNoisy();
        var prms = new FilterParameters(new Dictionary<string, string>
        { ["factor"] = "1.5", ["block_size"] = "10" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var f1 = FilterFactory.Get("invert").Apply(src, prms);
        var f2 = FilterFactory.Get("contrast").Apply(f1, prms); f1.Dispose();
        var f3 = FilterFactory.Get("pixelate").Apply(f2, prms); f2.Dispose();
        sw.Stop(); f3.Dispose();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"3 filtera: {sw.ElapsedMilliseconds}ms > 500ms");
    }

    [Fact]
    public async Task TenParallelFilters_Under4250ms()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var prms = new FilterParameters(new Dictionary<string, string>
        { ["factor"] = "1.5", ["block_size"] = "10" });

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            using var img = MakeNoisy();
            var f1 = FilterFactory.Get("invert").Apply(img, prms);
            var f2 = FilterFactory.Get("contrast").Apply(f1, prms); f1.Dispose();
            var f3 = FilterFactory.Get("pixelate").Apply(f2, prms); f2.Dispose();
            f3.Dispose();
        }));

        await Task.WhenAll(tasks);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 4250,
            $"10 paralelnih: {sw.ElapsedMilliseconds}ms > 4250ms");
    }
}
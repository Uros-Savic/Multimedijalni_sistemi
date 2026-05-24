using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using MSI.Core.Filters;
using MSI.Core.MsiFormat;
using Xunit;
using Xunit.Abstractions;

namespace MSI.Tests;

public class BenchmarkRunner
{
    private readonly ITestOutputHelper _out;
    private const string DefaultImagePath = @"C:\Users\Uros\Desktop\Screenshot_1.png";
    public BenchmarkRunner(ITestOutputHelper output) => _out = output;
    private const int W = 1024;
    private const int H = 768;
    private const int Reps = 10;

    [Fact]
    public void RunAllFilters_10Reps_PrintChartAndCsv()
    {
        using var img = LoadOrCreateTestImage();

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

        var results = new List<(string Name, double[] Times)>();

        foreach (string name in FilterFactory.AllFilterNames)
        {
            double[] times = new double[Reps];

            for (int i = 0; i < 2; i++)
                FilterFactory.Get(name).Apply(img, prms).Dispose();

            for (int r = 0; r < Reps; r++)
            {
                var sw = Stopwatch.StartNew();
                FilterFactory.Get(name).Apply(img, prms).Dispose();
                sw.Stop();
                times[r] = sw.Elapsed.TotalMilliseconds;
            }
            results.Add((name, times));
        }

        _out.WriteLine("");
        _out.WriteLine($"{W}×{H} {Reps} ponavljanja");
        _out.WriteLine($"Slika: {GetImageInfo()}\n");

        _out.WriteLine($"{"Filter",-28} {"Min",6} {"Avg",7} {"Max",6} {"P95",6}   {"Grafikon (avg ms)"}");
        _out.WriteLine(new string('─', 85));

        double globalMax = results.Max(r => r.Times.Average());
        const int BarWidth = 30;

        var csvRows = new List<string> { "filter,avg_ms,r1,r2,r3,r4,r5,r6,r7,r8,r9,r10" };

        foreach (var (name, times) in results)
        {
            double min = times.Min();
            double avg = times.Average();
            double max = times.Max();
            double p95 = Percentile(times, 95);
            bool ok = avg < 100;

            int barLen = Math.Min(BarWidth, (int)(avg / globalMax * BarWidth));
            string bar = new string('█', barLen) + new string('░', BarWidth - barLen);

            _out.WriteLine($"{name,-28} {min,5:F1} {avg,6:F1} {max,5:F1} {p95,5:F1}   [{bar}] {avg:F1}ms {(ok ? "✓" : "✗")}");

            var culture = CultureInfo.InvariantCulture;
            string rawTimes = string.Join(",", times.Select(t => t.ToString("F2", culture)));
            csvRows.Add(string.Format(culture, "{0},{1:F2},{2}", name, avg, rawTimes));
        }

        _out.WriteLine(new string('─', 85));
        BenchmarkMsi(img, csvRows);
        _out.WriteLine(new string('─', 85));

        int passed = results.Count(r => r.Times.Average() < 100);
        _out.WriteLine($"Proslo ≤100ms: {passed}/{results.Count} filtera\n");

        try
        {
            string csvPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "benchmark_results.csv");
            File.WriteAllLines(csvPath, csvRows);
            _out.WriteLine($"CSV sacuvan: {Path.GetFullPath(csvPath)}");
        }
        catch (Exception ex) { _out.WriteLine($"CSV nije sacuvan: {ex.Message}"); }

        Assert.True(true);
    }

    private void BenchmarkMsi(Bitmap img, List<string> csvRows)
    {
        var encoder = new MsiEncoder();
        var decoder = new MsiDecoder();

        double[] encTimes = new double[Reps];
        byte[]? encoded = null;
        for (int r = 0; r < Reps; r++)
        {
            var sw = Stopwatch.StartNew();
            encoded = encoder.EncodeToBytes(img, MsiConstants.CS_RGB, MsiConstants.COMP_NONE);
            sw.Stop(); encTimes[r] = sw.Elapsed.TotalMilliseconds;
        }
        PrintMsiRow("MSI encode (None)", encTimes, csvRows);
        _out.WriteLine($"   -> velicina: {encoded!.Length / 1024} KB");

        double[] encHuff = new double[Reps];
        byte[]? encH = null;
        for (int r = 0; r < Reps; r++)
        {
            var sw = Stopwatch.StartNew();
            encH = encoder.EncodeToBytes(img, MsiConstants.CS_RGB, MsiConstants.COMP_HUFFMAN);
            sw.Stop(); encHuff[r] = sw.Elapsed.TotalMilliseconds;
        }
        PrintMsiRow("MSI encode (Huffman)", encHuff, csvRows);
        double hRatio = (encH!.Length * 100.0) / encoded!.Length;
        _out.WriteLine($"   -> velicina: {encH!.Length / 1024} KB  ({hRatio:F2}% od None)");

        double[] encMpeg = new double[Reps];
        byte[]? encM = null;
        for (int r = 0; r < Reps; r++)
        {
            var sw = Stopwatch.StartNew();
            encM = encoder.EncodeToBytes(img, MsiConstants.CS_RGB, MsiConstants.COMP_MPEG2);
            sw.Stop(); encMpeg[r] = sw.Elapsed.TotalMilliseconds;
        }
        PrintMsiRow("MSI encode (MPEG-2)", encMpeg, csvRows);
        double mRatio = (encM!.Length * 100.0) / encoded!.Length;
        _out.WriteLine($"   -> velicina: {encM!.Length / 1024} KB  ({mRatio:F2}% od None)");

        double[] decTimes = new double[Reps];
        for (int r = 0; r < Reps; r++)
        {
            var sw = Stopwatch.StartNew();
            var (bmp, _) = decoder.Decode(encoded!); bmp.Dispose();
            sw.Stop(); decTimes[r] = sw.Elapsed.TotalMilliseconds;
        }
        PrintMsiRow("MSI decode (None)", decTimes, csvRows);
    }

    private void PrintMsiRow(string label, double[] times, List<string> csvRows)
    {
        var culture = CultureInfo.InvariantCulture;
        double min = times.Min(), avg = times.Average(), max = times.Max(), p95 = Percentile(times, 95);
        _out.WriteLine($"{label,-28} {min,5:F1} {avg,6:F1} {max,5:F1} {p95,5:F1}");

        string rawTimes = string.Join(",", times.Select(t => t.ToString("F2", culture)));
        csvRows.Add(string.Format(culture, "{0},{1:F2},{2}", label.Replace(",", ""), avg, rawTimes));
    }

    private Bitmap LoadOrCreateTestImage()
    {
        string? imagePath = Environment.GetEnvironmentVariable(@"C:\Users\Uros\Desktop\output_20260330_134055.png");
        if (string.IsNullOrEmpty(imagePath)) imagePath = DefaultImagePath;

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                _out.WriteLine($"Ucitavanje slike: {imagePath}");
                return LoadAndResizeImage(imagePath, W, H);
            }
            catch (Exception ex)
            {
                _out.WriteLine($"Greska: {ex.Message}. Koristi se Noise generated img");
            }
        }
        return MakeNoisyImage();
    }

    private static Bitmap LoadAndResizeImage(string path, int targetWidth, int targetHeight)
    {
        using (var original = Image.FromFile(path))
        {
            if (original.Width == targetWidth && original.Height == targetHeight) return new Bitmap(original);
            var resized = new Bitmap(targetWidth, targetHeight);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, targetWidth, targetHeight);
            }
            return resized;
        }
    }

    private static Bitmap MakeNoisyImage()
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        var rng = new Random(42);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
        return bmp;
    }

    private string GetImageInfo()
    {
        string? imagePath = DefaultImagePath;
        if (File.Exists(imagePath))
        {
            var fileInfo = new FileInfo(imagePath);
            return $"{fileInfo.Name} ({fileInfo.Length / 1024} KB)";
        }
        return "Generisana sumska slika (fallback)";
    }

    private static double Percentile(double[] data, int pct)
    {
        var sorted = data.OrderBy(x => x).ToArray();
        int idx = (int)Math.Ceiling(pct / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }
}
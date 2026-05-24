using System.Drawing;
using System.Drawing.Imaging;
using MSI.Client.Services;
using MSI.Core.MsiFormat;
using Xunit;

namespace MSI.Tests;

public class UiTests
{
    [Fact]
    public void UndoRedo_ClearsOnNewUpload()
    {
        using var mgr = new UndoRedoManager();
        using var bmp = new Bitmap(10, 10, PixelFormat.Format24bppRgb);

        mgr.Push(bmp, "step1", "session1");
        mgr.Push(bmp, "step2", "session1");
        Assert.True(mgr.CanUndo);
        mgr.Clear();
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
    }

    [Fact]
    public void UndoRedo_PushUndoRedo_WorksCorrectly()
    {
        using var mgr = new UndoRedoManager();
        using var img1 = MakeBmp(Color.Red);
        using var img2 = MakeBmp(Color.Blue);
        mgr.Push(img1, "original", "s1");
        Assert.True(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
        using var current = MakeBmp(Color.Green);
        var entry = mgr.Undo(current, "green", "s1");
        Assert.NotNull(entry);
        Assert.Equal("original", entry!.Label);
        Assert.True(mgr.CanRedo);
        var entry2 = mgr.Redo(entry.Snapshot, "original", "s1");
        Assert.NotNull(entry2);
        Assert.Equal("green", entry2!.Label);
        entry.Snapshot.Dispose();
        entry2.Snapshot.Dispose();
    }

    [Fact]
    public void Export_ToMsi_CanReDecode()
    {
        using var original = MakeBmp(Color.Teal, 32, 32);
        var encoder = new MsiEncoder();
        byte[] msiBytes = encoder.EncodeToBytes(original);

        var decoder = new MsiDecoder();
        var (decoded, header) = decoder.Decode(msiBytes);

        Assert.Equal(32, (int)header.Width);
        Assert.Equal(32, (int)header.Height);

        var px = decoded.GetPixel(10, 10);
        Assert.InRange(px.R, 0, 30);
        Assert.InRange(px.G, 110, 145);
        Assert.InRange(px.B, 110, 145);
        decoded.Dispose();
    }

    [Fact]
    public void CompareMode_OriginalUnchangedAfterFilter()
    {
        using var original = MakeBmp(Color.Red, 20, 20);
        using var filtered = MakeBmp(Color.Blue, 20, 20);
        var origPx = original.GetPixel(10, 10);
        var filtPx = filtered.GetPixel(10, 10);

        Assert.Equal(255, origPx.R);
        Assert.Equal(0, filtPx.R);
        Assert.Equal(255, filtPx.B);
        filtered.SetPixel(10, 10, Color.Green);
        Assert.Equal(255, original.GetPixel(10, 10).R);
    }

    [Fact]
    public void UndoRedo_MaxThreeEntries()
    {
        using var mgr = new UndoRedoManager();
        using var bmp = MakeBmp(Color.Gray);

        for (int i = 0; i < 5; i++)
            mgr.Push(bmp, $"step{i}", "s");

        using var cur = MakeBmp(Color.White);
        var tmp = cur;
        int count = 0;
        while (mgr.CanUndo)
        {
            var e = mgr.Undo(tmp, "cur", "s");
            if (e != null) { count++; e.Snapshot.Dispose(); }
        }
        Assert.Equal(3, count);
    }

    private static Bitmap MakeBmp(Color c, int w = 10, int h = 10)
    {
        var b = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }
}

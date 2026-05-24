using System.Drawing;
using System.Drawing.Imaging;
using MSI.Core.MsiFormat;
using Xunit;

namespace MSI.Tests;

public class ValidatorTests
{
    private static byte[] MakeValidMsi()
    {
        using var bmp = new Bitmap(16, 16, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.DodgerBlue);
        return new MsiEncoder().EncodeToBytes(bmp);
    }

    [Fact]
    public void Validate_ValidMsi_ReturnsTrue()
    {
        var result = MsiValidator.Validate(MakeValidMsi());
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(16u, result.Width);
        Assert.Equal(16u, result.Height);
    }

    [Fact]
    public void Validate_CorruptedCrc_ReturnsFalse()
    {
        byte[] data = MakeValidMsi();
        data[^1] ^= 0xFF;
        var result = MsiValidator.Validate(data);
        Assert.False(result.IsValid);
        Assert.Contains("CRC", result.Error);
    }

    [Fact]
    public void Validate_WrongMagic_ReturnsFalse()
    {
        byte[] data = MakeValidMsi();
        data[0] = 0x00;
        var result = MsiValidator.Validate(data);
        Assert.False(result.IsValid);
        Assert.Contains("magija", result.Error);
    }

    [Fact]
    public void Validate_EmptyData_ReturnsFalse()
    {
        var result = MsiValidator.Validate(Array.Empty<byte>());
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RandomGarbage_ReturnsFalse()
    {
        var rng = new Random(55);
        var data = new byte[500];
        rng.NextBytes(data);
        var result = MsiValidator.Validate(data);
        Assert.False(result.IsValid);
    }
}

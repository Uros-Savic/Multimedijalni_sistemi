namespace MSI.Core.MsiFormat;

// provera strukture i integriteta

public static class MsiValidator
{
    public sealed record ValidationResult(
        bool IsValid,
        string Error = "",
        uint StoredCrc = 0,
        uint CalcCrc = 0,
        uint Width = 0,
        uint Height = 0,
        byte Channels = 0,
        byte Compression = 0
    );

    public static ValidationResult Validate(byte[] data)
    {
        try
        {
            if (data == null || data.Length < MsiConstants.HeaderLength + 4)
                return new ValidationResult(false, "Fajl je previse mali.");

            if (data[0] != 'M' || data[1] != 'S' || data[2] != 'I' || data[3] != '0')
                return new ValidationResult(false,
                    $"Nevazeca magija: {data[0]:X2}{data[1]:X2}{data[2]:X2}{data[3]:X2}");

            ushort ver = BitConverter.ToUInt16(data, 4);
            if (ver != MsiConstants.Version)
                return new ValidationResult(false, $"Nepodrzana verzija: 0x{ver:X4}");

            uint storedCrc = BitConverter.ToUInt32(data, data.Length - 4);
            byte[] content = data[..^4];
            uint calcCrc = Crc32.Compute(content);

            if (storedCrc != calcCrc)
                return new ValidationResult(false,
                    $"CRC nepodudaranje: ocekivano 0x{calcCrc:X8}, pronadjeno 0x{storedCrc:X8}",
                    storedCrc, calcCrc);

            uint w = BitConverter.ToUInt32(data, 8);
            uint h = BitConverter.ToUInt32(data, 12);
            if (w == 0 || h == 0 || w > MsiConstants.MaxDimension || h > MsiConstants.MaxDimension)
                return new ValidationResult(false, $"Nevazece dimenzije: {w}×{h}");

            byte channels = data[16];
            byte compression = data[18];
            if (channels != 1 && channels != 3)
                return new ValidationResult(false, $"Nevazeci broj kanala: {channels}");
            if (compression > 4)
                return new ValidationResult(false, $"Nevazeci tip kompresije: {compression}");

            return new ValidationResult(true, "", storedCrc, calcCrc, w, h, channels, compression);
        }
        catch (Exception ex)
        {
            return new ValidationResult(false, $"Izuzetak: {ex.Message}");
        }
    }

    public static ValidationResult Validate(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Validate(ms.ToArray());
    }

    public static ValidationResult ValidateFile(string path)
    {
        if (!File.Exists(path))
            return new ValidationResult(false, $"Fajl ne postoji: {path}");
        return Validate(File.ReadAllBytes(path));
    }
}
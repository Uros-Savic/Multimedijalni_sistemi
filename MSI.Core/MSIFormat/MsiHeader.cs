namespace MSI.Core.MsiFormat;

// zaglavlje MSI fajla (28 bajta fiksnog dela + META + PIXELS + CRC32).
// vrednosti su little-endian.

//  Offset  Size  Polje
//  0       4     MAGIC      'MSI0'
//  4       2     VERSION    0x0001
//  6       2     HEADER_LEN duzina zaglavlja (28)
//  8       4     WIDTH      uint32
//  12      4     HEIGHT     uint32
//  16      1     CHANNELS   1 ili 3
//  17      1     COLORSPACE 0=Linear,1=RGB,3=HSV
//  18      1     COMPRESSION 0-4
//  19      1     RESERVED   poravnanje
//  20      4     META_LEN   duzina metapodataka
//  24      4     PIXEL_LEN  duzina piksel-segmenta
//  28      N     META
//  28+N    M     PIXELS
//  ...     4     CRC32

public sealed class MsiHeader
{
    public byte[] Magic { get; set; } = MsiConstants.Magic.ToArray();
    public ushort Version { get; set; } = MsiConstants.Version;
    public ushort HeaderLen { get; set; } = MsiConstants.HeaderLength;
    public uint Width { get; set; }
    public uint Height { get; set; }
    public byte Channels { get; set; }
    public byte Colorspace { get; set; }
    public byte Compression { get; set; }
    public byte Reserved { get; set; } = 0;
    public uint MetaLen { get; set; }
    public uint PixelLen { get; set; }
    public Dictionary<string, string> Meta { get; set; } = new();
    public bool IsGrayscale => Channels == 1;
    public bool IsColor => Channels == 3;
    public long TotalPixels => (long)Width * Height;
    public long RawPixelBytes => TotalPixels * Channels;

    public bool IsMagicValid()
        => Magic is { Length: 4 }
        && Magic[0] == MsiConstants.Magic[0]
        && Magic[1] == MsiConstants.Magic[1]
        && Magic[2] == MsiConstants.Magic[2]
        && Magic[3] == MsiConstants.Magic[3];
}

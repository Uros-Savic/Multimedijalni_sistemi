namespace MSI.Core.MsiFormat;

public static class MsiConstants
{
    public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'I', (byte)'0' };
    public const ushort Version = 0x0001;
    public const ushort HeaderLength = 28;
    public const byte CS_LINEAR = 0;
    public const byte CS_RGB = 1;
    public const byte CS_HSV = 3;
    public const byte COMP_NONE = 0;
    public const byte COMP_HUFFMAN = 2;
    public const byte COMP_MPEG2 = 4;
    public const int MaxImageSizeBytes = 20 * 1024 * 1024;
    public const int MaxDimension = 16384;
    public const int MinDimension = 1;
}
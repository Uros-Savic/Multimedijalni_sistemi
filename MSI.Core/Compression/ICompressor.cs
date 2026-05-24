namespace MSI.Core.Compression;

public interface ICompressor
{
    byte[] Compress(byte[] data);
    byte[] Decompress(byte[] data);
    Dictionary<string, string>? MetaInfo { get; }
}

public static class CompressorFactory
{
    public static ICompressor Create(byte compressionType) => compressionType switch
    {
        0 => new NoCompressor(),
        2 => new HuffmanCompressor(),
        4 => new Mpeg2DownsamplingCompressor(),
        1 => throw new NotSupportedException("Shannon-Fano (tip 1) nije implementiran."),
        3 => throw new NotSupportedException("MPEG-1 (tip 3) nije implementiran."),
        _ => throw new NotSupportedException($"Nepoznati tip kompresije: {compressionType}.")
    };

    public static ICompressor Create(byte compressionType, int width, int height)
    {
        var comp = Create(compressionType);
        if (comp is Mpeg2DownsamplingCompressor mpeg2)
            mpeg2.SetDimensions(width, height);
        return comp;
    }
}

public sealed class NoCompressor : ICompressor
{
    public Dictionary<string, string>? MetaInfo => null;
    public byte[] Compress(byte[] data) => data;
    public byte[] Decompress(byte[] data) => data;
}
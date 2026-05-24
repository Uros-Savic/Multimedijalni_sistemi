namespace MSI.Core.Compression;

// format 
// [4B] origW (uint32 LE)
// [4B] origH (uint32 LE)
// [1B] 1 = MPEG2
// [origW * origH] Y kanali
// [cbW * cbH] Cb kanali   (cbW = (origW+1)/2, cbH = (origH+1)/2)
// [cbW * cbH] Cr kanali

public sealed class DownsamplingCompressor : ICompressor
{
    private readonly bool _mpeg2;

    public DownsamplingCompressor(bool mpeg2 = false) => _mpeg2 = mpeg2;

    public Dictionary<string, string>? MetaInfo => new()
    {
        ["compression_detail"] = _mpeg2 ? "downsampling_420_mpeg2" : "downsampling_420_mpeg1"
    };

    public byte[] Compress(byte[] data)
    {
        throw new NotImplementedException();
    }

    public byte[] Decompress(byte[] data)
    {
        throw new NotImplementedException();
    }

}

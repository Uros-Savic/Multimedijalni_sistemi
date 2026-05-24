using System.Collections;

namespace MSI.Core.Compression;

// Lossless Huffman kompresija.

// Format kompresovanih podataka:
//   [4B] origLen    – broj originalnih bajtova (uint32 LE)
//   [4B] tableLen   – broj bajtova tabele kodiranja (uint32 LE)
//   [N]  tabela     – za svaki simbol: 1B simbol + 1B duzina koda + ceil(duzina/8) B kod
//   [M]  bitovi     – kompresovani podaci, padded na ceo bajt

public sealed class HuffmanCompressor : ICompressor
{
    public Dictionary<string, string>? MetaInfo =>
        new() { ["compression_detail"] = "huffman" };

    public byte[] Compress(byte[] data)
    {
        if (data.Length == 0) return Array.Empty<byte>();

        var freq = new long[256];

        foreach (byte b in data) freq[b]++;

        var codes = BuildCodes(freq);

        var bits = new List<bool>(data.Length * 4);
        byte[] encodedBits = BitsToBytes(bits);
        byte[] tableBytes = SerializeTable(codes);
        foreach (byte b in data)
            AppendBits(bits, codes[b]);
        encodedBits = BitsToBytes(bits);

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)data.Length));
        ms.Write(BitConverter.GetBytes((uint)tableBytes.Length));
        ms.Write(tableBytes);
        ms.Write(encodedBits);
        return ms.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        if (data.Length == 0) return Array.Empty<byte>();

        using var ms = new MemoryStream(data);
        uint origLen = ReadU32(ms);
        uint tableLen = ReadU32(ms);

        byte[] tableBytes = new byte[tableLen];
        ms.ReadExactly(tableBytes);
        var codes = DeserializeTable(tableBytes);

        byte[] encodedBits = new byte[ms.Length - ms.Position];
        ms.ReadExactly(encodedBits);

        if (codes.Count == 1)
        {
            var onlySymbol = codes.Keys.First();
            return Enumerable.Repeat(onlySymbol, (int)origLen).ToArray();
        }

        var root = RebuildTrie(codes);
        var result = new byte[origLen];
        int outIdx = 0;
        var cur = root;

        foreach (byte eByte in encodedBits)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                bool goRight = (eByte >> bit & 1) == 1;
                cur = goRight ? cur.Right : cur.Left;

                if (cur == null) { cur = root; continue; }

                if (cur.IsLeaf)
                {
                    if (outIdx < (int)origLen)
                        result[outIdx++] = cur.Symbol;
                    cur = root;
                }
                if (outIdx >= (int)origLen) goto done;
            }
        }
    done:
        return result;
    }

    private static Dictionary<byte, bool[]> BuildCodes(long[] freq)
    {
        var nodes = new List<Node>();
        for (int i = 0; i < 256; i++)
            if (freq[i] > 0)
                nodes.Add(new Node { Symbol = (byte)i, Freq = freq[i] });

        if (nodes.Count == 1)
            return new Dictionary<byte, bool[]> { [nodes[0].Symbol] = new[] { false } };

        while (nodes.Count > 1)
        {
            nodes.Sort((a, b) => a.Freq != b.Freq
                ? a.Freq.CompareTo(b.Freq)
                : a.Symbol.CompareTo(b.Symbol));
            var left = nodes[0]; nodes.RemoveAt(0);
            var right = nodes[0]; nodes.RemoveAt(0);
            nodes.Add(new Node { Freq = left.Freq + right.Freq, Left = left, Right = right });
        }

        var codes = new Dictionary<byte, bool[]>();
        Traverse(nodes[0], Array.Empty<bool>(), codes);
        return codes;
    }

    private static void Traverse(Node node, bool[] prefix, Dictionary<byte, bool[]> codes)
    {
        if (node.IsLeaf) { codes[node.Symbol] = prefix; return; }
        Traverse(node.Left!, Append(prefix, false), codes);
        Traverse(node.Right!, Append(prefix, true), codes);
    }

    private static bool[] Append(bool[] arr, bool val)
    {
        var next = new bool[arr.Length + 1];
        arr.CopyTo(next, 0);
        next[^1] = val;
        return next;
    }

    private sealed class Node
    {
        public byte Symbol { get; set; }
        public long Freq { get; set; }
        public Node? Left { get; set; }
        public Node? Right { get; set; }
        public bool IsLeaf => Left == null && Right == null;
    }

    private static Node RebuildTrie(Dictionary<byte, bool[]> codes)
    {
        var root = new Node();
        foreach (var kv in codes)
        {
            var cur = root;
            foreach (bool bit in kv.Value)
            {
                if (bit) { cur.Right ??= new Node(); cur = cur.Right; }
                else { cur.Left ??= new Node(); cur = cur.Left; }
            }
            cur.Symbol = kv.Key;
        }
        return root;
    }

    private static byte[] SerializeTable(Dictionary<byte, bool[]> codes)
    {
        using var ms = new MemoryStream();

        foreach (var kv in codes)
        {
            byte[] codeBytes = BitsToBytes(kv.Value);
            ms.WriteByte(kv.Key);
            ms.WriteByte((byte)kv.Value.Length);
            ms.Write(codeBytes);
        }
        return ms.ToArray();
    }

    private static Dictionary<byte, bool[]> DeserializeTable(byte[] tableBytes)
    {
        var dict = new Dictionary<byte, bool[]>();
        using var ms = new MemoryStream(tableBytes);

        while (ms.Position < ms.Length)
        {
            byte sym = (byte)ms.ReadByte();
            int bitLen = ms.ReadByte();
            int byteLen = (bitLen + 7) / 8;
            byte[] raw = new byte[byteLen];
            ms.ReadExactly(raw);

            var bits = new bool[bitLen];
            for (int b = 0; b < bitLen; b++)
                bits[b] = (raw[b / 8] >> (7 - b % 8) & 1) == 1;
            dict[sym] = bits;
        }
        return dict;
    }

    private static byte[] BitsToBytes(IList<bool> bits)
    {
        int len = (bits.Count + 7) / 8;
        byte[] res = new byte[len];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) res[i / 8] |= (byte)(1 << (7 - i % 8));
        return res;
    }

    private static byte[] BitsToBytes(bool[] bits)
    {
        int len = (bits.Length + 7) / 8;
        byte[] res = new byte[len];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i]) res[i / 8] |= (byte)(1 << (7 - i % 8));
        return res;
    }

    private static void AppendBits(List<bool> list, bool[] bits)
    {
        foreach (bool b in bits) list.Add(b);
    }

    private static uint ReadU32(Stream s)
    {
        byte[] buf = new byte[4]; s.ReadExactly(buf);
        return BitConverter.ToUInt32(buf);
    }
}
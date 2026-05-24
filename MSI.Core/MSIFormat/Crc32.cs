using System;

namespace MSI.Core.MsiFormat;

public static class Crc32
{
    private static readonly uint[] Table;

    static Crc32()
    {
        const uint polynomial = 0x82F63B78;
        Table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
            }
            Table[i] = crc;
        }
    }


    public static uint Compute(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 0;
        uint crc = 0xFFFFFFFF;

        foreach (byte b in data)
        {
            byte index = (byte)((crc ^ b) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }
        return crc ^ 0xFFFFFFFF;
    }

    public static uint Compute(byte[] data, int offset, int length)
    {
        if (data == null || length == 0)
            return 0;

        uint crc = 0xFFFFFFFF;
        int end = offset + length;

        for (int i = offset; i < end; i++)
        {
            byte index = (byte)((crc ^ data[i]) & 0xFF);
            crc = (crc >> 8) ^ Table[index];
        }

        return crc ^ 0xFFFFFFFF;
    }
}
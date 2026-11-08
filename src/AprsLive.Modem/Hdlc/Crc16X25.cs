namespace AprsLive.Modem.Hdlc;

/// <summary>
/// CRC-16/X-25 (poly 0x8408 reflected, init 0xFFFF, final XOR 0xFFFF) — the
/// HDLC frame check sequence AX.25 transmits over the air. KISS frames never
/// carry it (the TNC strips/adds it), so it lives here rather than in
/// AprsLive.Core.Ax25: it's purely an HDLC/modem-layer concern.
/// </summary>
public static class Crc16X25
{
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0x8408) : (ushort)(crc >> 1);
            table[i] = crc;
        }
        return table;
    }

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        return (ushort)(crc ^ 0xFFFF);
    }

    /// <summary>Appends the 2-byte FCS (LSB first, as transmitted on air) to <paramref name="data"/>.</summary>
    public static byte[] Append(ReadOnlySpan<byte> data)
    {
        ushort crc = Compute(data);
        var result = new byte[data.Length + 2];
        data.CopyTo(result);
        result[^2] = (byte)(crc & 0xFF);
        result[^1] = (byte)(crc >> 8);
        return result;
    }

    /// <summary>True if the trailing 2 bytes of <paramref name="frameWithFcs"/> are a valid FCS for the rest.</summary>
    public static bool Check(ReadOnlySpan<byte> frameWithFcs)
    {
        if (frameWithFcs.Length < 2) return false;
        ushort crc = Compute(frameWithFcs[..^2]);
        return frameWithFcs[^2] == (byte)(crc & 0xFF) && frameWithFcs[^1] == (byte)(crc >> 8);
    }
}

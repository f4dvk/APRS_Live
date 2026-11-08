namespace AprsLive.Modem.Hdlc;

/// <summary>Turns AX.25 frame bytes into the raw bit stream to transmit: preamble flags, bit-stuffed FCS-appended frame, closing flag(s), trailing flags.</summary>
public static class HdlcFramer
{
    private const byte Flag = 0x7E;

    public static List<bool> BuildBitStream(byte[] ax25Frame, int txDelayFlags, int txTailFlags)
    {
        var withFcs = Crc16X25.Append(ax25Frame);

        var bits = new List<bool>();
        for (int i = 0; i < Math.Max(1, txDelayFlags); i++)
            AppendByteLsbFirst(bits, Flag);

        var frameBits = new List<bool>(withFcs.Length * 8);
        foreach (byte b in withFcs)
            AppendByteLsbFirst(frameBits, b);
        bits.AddRange(BitStuffer.Stuff(frameBits));

        // the closing flag doubles as the first of the trailing flags
        for (int i = 0; i < Math.Max(1, txTailFlags); i++)
            AppendByteLsbFirst(bits, Flag);

        return bits;
    }

    private static void AppendByteLsbFirst(List<bool> bits, byte b)
    {
        for (int i = 0; i < 8; i++)
            bits.Add(((b >> i) & 1) != 0);
    }
}

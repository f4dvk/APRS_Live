namespace AprsLive.Modem.Afsk;

/// <summary>NRZI line coding: a 0 bit is a transition, a 1 bit is no transition. Levels here map true=mark tone, false=space tone.</summary>
public static class NrziCodec
{
    public static List<bool> Encode(IReadOnlyList<bool> bits)
    {
        var output = new List<bool>(bits.Count);
        bool level = true;
        foreach (bool bit in bits)
        {
            if (!bit) level = !level;
            output.Add(level);
        }
        return output;
    }

    public static bool DecodeNext(bool level, ref bool previousLevel, ref bool hasPrevious)
    {
        bool bit = !hasPrevious || level == previousLevel;
        previousLevel = level;
        hasPrevious = true;
        return bit;
    }
}

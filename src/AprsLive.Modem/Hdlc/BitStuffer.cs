namespace AprsLive.Modem.Hdlc;

/// <summary>HDLC bit stuffing: insert a 0 after five consecutive 1-bits, so the data never contains six 1s in a row (which would look like a flag or abort).</summary>
public static class BitStuffer
{
    public static List<bool> Stuff(IReadOnlyList<bool> bits)
    {
        var output = new List<bool>(bits.Count + bits.Count / 5 + 1);
        int ones = 0;
        foreach (bool bit in bits)
        {
            output.Add(bit);
            if (bit)
            {
                ones++;
                if (ones == 5)
                {
                    output.Add(false);
                    ones = 0;
                }
            }
            else
            {
                ones = 0;
            }
        }
        return output;
    }

    /// <summary>Removes stuffed 0-bits from a span known to contain no flags/aborts (i.e. already split at flag boundaries).</summary>
    public static List<bool>? Unstuff(IReadOnlyList<bool> raw)
    {
        var output = new List<bool>(raw.Count);
        int ones = 0;
        foreach (bool bit in raw)
        {
            if (ones == 5)
            {
                ones = 0;
                if (bit)
                    return null; // a correctly-stuffed sender never sends 6 raw ones outside a flag/abort
                continue; // drop the stuffed 0
            }

            output.Add(bit);
            ones = bit ? ones + 1 : 0;
        }
        return output;
    }
}

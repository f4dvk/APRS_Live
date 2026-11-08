namespace AprsLive.Core.Tnc;

/// <summary>
/// The standard APRS-IS login passcode algorithm — a public, well-known hash
/// of a callsign (documented at aprs-is.net and reproduced in essentially
/// every APRS client); it exists to keep casual noise off the network, not
/// as a real secret, and every station computes its own from its own
/// callsign the same way.
/// </summary>
public static class AprsIsPasscode
{
    public static int Compute(string callsign)
    {
        string call = callsign.Split('-')[0].ToUpperInvariant();
        int hash = 0x73E2;
        for (int i = 0; i < call.Length; i += 2)
        {
            hash ^= call[i] << 8;
            if (i + 1 < call.Length)
                hash ^= call[i + 1];
        }
        return hash & 0x7FFF;
    }
}

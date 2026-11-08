namespace AprsLive.Core.Ax25;

/// <summary>
/// Standard APRS "New N-Paradigm" WIDEn-N digipeating: finds the first
/// not-yet-repeated path entry, and if it's a recognized generic alias
/// (<c>WIDE1-N</c>/<c>WIDE2-N</c>/<c>WIDE3-N</c>, per which of those are
/// enabled) either decrements its hop count in place (N &gt; 1) or replaces
/// it with the digipeating station's own callsign, marked repeated (N == 1
/// — "ajout indicatif", the last hop). Pure logic, no I/O: what channel the
/// result goes out on is a separate policy decision made by the caller.
/// </summary>
public static class DigipeaterProcessor
{
    /// <summary>
    /// Returns the re-encoded AX.25 UI frame to repeat, or <c>null</c> if
    /// <paramref name="ax25"/> doesn't match an enabled alias (nothing to
    /// digipeat — including frames from our own callsign, so a station
    /// never re-digipeats its own transmissions).
    /// </summary>
    public static byte[]? TryDigipeat(byte[] ax25, string ownCallsign, bool wide1, bool wide2, bool wide3)
    {
        if (!Ax25Frame.TryParse(ax25, out var frame) || frame is null)
            return null;

        if (string.Equals(frame.Source.Base, BaseCallOf(ownCallsign), StringComparison.OrdinalIgnoreCase))
            return null;

        if (frame.HasInternetPath)
            return null; // APRS-IS traffic handed over by a bridging TNC must not go on the air


        var path = frame.Path;
        int idx = -1;
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].HasBeenRepeated)
                continue;

            var a = path[i];
            bool isWideAlias = a.Callsign is "WIDE1" or "WIDE2" or "WIDE3" && a.Ssid is >= 1 and <= 7;
            if (!isWideAlias)
                return null; // the next hop to service isn't a recognized alias — leave it alone

            bool enabled = a.Callsign switch
            {
                "WIDE1" => wide1,
                "WIDE2" => wide2,
                "WIDE3" => wide3,
                _ => false,
            };
            if (!enabled)
                return null;

            idx = i;
            break;
        }

        if (idx < 0)
            return null;

        var entries = new List<string>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            if (i != idx)
            {
                entries.Add(path[i].ToStringWithFlag());
                continue;
            }

            var alias = path[i];
            entries.Add(alias.Ssid > 1
                ? $"{alias.Callsign}-{alias.Ssid - 1}"
                : $"{ownCallsign}*");
        }

        return Ax25Builder.BuildUi(frame.Source.ToString(), frame.Destination.ToString(), entries, frame.InfoText);
    }

    private static string BaseCallOf(string callsign)
    {
        int dash = callsign.IndexOf('-');
        return (dash >= 0 ? callsign[..dash] : callsign).ToUpperInvariant();
    }
}

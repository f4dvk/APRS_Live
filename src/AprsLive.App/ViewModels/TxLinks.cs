namespace AprsLive.App.ViewModels;

/// <summary>
/// Shared send policy for the station beacon, messages and objects: go out
/// every RF channel with "Émission" on — not just a single "primary" one,
/// so both can carry the same traffic when both are enabled.
/// </summary>
internal static class TxLinks
{
    /// <summary>Sends the frame on every TX-enabled, connected link. Returns the names of the links it actually went out on.</summary>
    public static async Task<IReadOnlyList<string>> SendAsync(byte[] frame, params LinkViewModel[] links)
    {
        var sentOn = new List<string>();
        foreach (var link in links)
        {
            if (!link.TxEnabled)
                continue;
            if (await link.SendRawFrameAsync(frame))
                sentOn.Add(link.Name);
        }
        return sentOn;
    }
}

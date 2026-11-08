namespace AprsLive.Core.Tnc;

/// <summary>
/// The standard aprs2.net round-robin APRS-IS access points — publicly
/// documented at aprs-is.net, the same ones every mainstream APRS client
/// (Xastir, YAAC, APRSIS32...) offers. <see cref="AprsIsClient"/> tries the
/// preferred one first, then falls back through the rest of this list.
/// </summary>
public static class AprsIsServers
{
    public static readonly IReadOnlyList<string> Known =
    [
        "rotate.aprs2.net",
        "rotate.aprs.net",
        "euro.aprs2.net",
        "noam.aprs2.net",
        "soam.aprs2.net",
        "asia.aprs2.net",
        "aunz.aprs2.net",
        "africa.aprs2.net",
    ];
}

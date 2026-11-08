using System.Text;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Tnc;

/// <summary>
/// RF → APRS-IS gating rules (aprs-is.net "IGate details"): which frames heard
/// on RF may go to the Internet, and the TNC2 line to send — the RF header
/// followed by <c>,qAR,IGATECALL</c>. The same packet (source, destination
/// and payload, whatever path it came by) is sent once per
/// <see cref="DupeWindow"/>, like the servers' own duplicate check, so the
/// direct copy and its digipeated echoes don't all go out.
/// </summary>
public sealed class IGate
{
    public static readonly TimeSpan DupeWindow = TimeSpan.FromSeconds(30);
    public const string DuplicateReason = "doublon";

    private static readonly string[] NoGateAliases = ["NOGATE", "RFONLY"];

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, DateTime> _recent = new();

    public IGate(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>The line to send to APRS-IS, or <c>null</c> with the reason the frame must not be gated.</summary>
    public string? TryGate(Ax25Frame frame, string igateCallsign, out string? reason)
    {
        string info = frame.InfoText;
        int eol = info.AsSpan().IndexOfAny('\r', '\n');
        if (eol >= 0)
            info = info[..eol];          // APRS-IS is line based: keep the first line only

        reason = Reject(frame, info, igateCallsign);
        if (reason is not null)
            return null;

        var now = _clock();
        if (_recent.Count > 512)
            foreach (var stale in _recent.Where(kv => now - kv.Value >= DupeWindow).Select(kv => kv.Key).ToList())
                _recent.Remove(stale);

        string key = $"{frame.Source}>{frame.Destination}:{info}";
        if (_recent.TryGetValue(key, out var last) && now - last < DupeWindow)
        {
            reason = DuplicateReason;
            return null;
        }
        _recent[key] = now;

        return BuildLine(frame, info, igateCallsign);
    }

    private static string? Reject(Ax25Frame frame, string info, string igateCallsign)
    {
        if (info.Length == 0)
            return "trame vide";
        if (frame.HasInternetPath)
            return "venu d'Internet (TCPIP/TCPXX/qA)";
        if (frame.Path.Any(a => NoGateAliases.Contains(a.Callsign, StringComparer.OrdinalIgnoreCase)))
            return "chemin NOGATE/RFONLY";
        if (string.Equals(frame.Source.ToString(), igateCallsign, StringComparison.OrdinalIgnoreCase))
            return "notre propre trame";
        return info[0] switch
        {
            '}' => "paquet tiers",
            '?' => "requête générale",
            _ => null,
        };
    }

    private static string BuildLine(Ax25Frame frame, string info, string igateCallsign)
    {
        var sb = new StringBuilder();
        sb.Append(frame.Source).Append('>').Append(frame.Destination);

        // TNC2 convention: only the last digipeater that has repeated it carries the '*'.
        int lastRepeated = -1;
        for (int i = 0; i < frame.Path.Count; i++)
            if (frame.Path[i].HasBeenRepeated)
                lastRepeated = i;
        for (int i = 0; i < frame.Path.Count; i++)
        {
            sb.Append(',').Append(frame.Path[i]);
            if (i == lastRepeated)
                sb.Append('*');
        }

        sb.Append(",qAR,").Append(igateCallsign.ToUpperInvariant());
        sb.Append(':').Append(info);
        return sb.ToString();
    }
}

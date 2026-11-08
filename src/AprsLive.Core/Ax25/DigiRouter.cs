using AprsLive.Core.Settings;

namespace AprsLive.Core.Ax25;

/// <summary>What the digipeater does with one heard frame.</summary>
/// <param name="Source">Callsign of the original sender, for the log.</param>
/// <param name="Frame">The re-encoded frame to transmit.</param>
/// <param name="SendOn">Channels (1, 2) to transmit it on.</param>
/// <param name="AlreadyOnAir">Routed channels skipped: this packet was already heard or sent there within <see cref="DigiRouter.DuplicateWindow"/>.</param>
public sealed record DigiDecision(string Source, byte[] Frame, IReadOnlyList<int> SendOn, IReadOnlyList<int> AlreadyOnAir);

/// <summary>
/// Channel policy on top of <see cref="DigipeaterProcessor"/>: picks the
/// destination channels from the routes in <see cref="DigiSettings"/> (only
/// those with TX on), and doesn't send a packet onto a channel where it was
/// already on the air in the last <see cref="DuplicateWindow"/> — sent there
/// by us, or heard there. Packets are matched on source, destination and
/// info, ignoring the path, so the same packet coming back through another
/// digi, or heard on both channels, is caught. Thread-safe: each link
/// delivers frames on its own thread.
/// </summary>
public sealed class DigiRouter
{
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);

    /// <summary>When each packet was last on the air on each channel (heard or sent), for the duplicate check.</summary>
    private readonly Dictionary<(string Packet, int Channel), DateTime> _onAir = new();

    /// <summary>
    /// Returns <c>null</c> when nothing is to be repeated: no route from
    /// <paramref name="fromChannel"/> to a channel with TX on, or the frame
    /// doesn't need digipeating (see <see cref="DigipeaterProcessor.TryDigipeat"/>).
    /// </summary>
    public DigiDecision? Route(byte[] ax25, int fromChannel, DigiSettings settings, string ownCallsign,
        bool txOn1, bool txOn2, DateTime nowUtc)
    {
        var repeated = DigipeaterProcessor.TryDigipeat(ax25, ownCallsign, settings.Wide1, settings.Wide2, settings.Wide3);
        if (repeated is null || !Ax25Frame.TryParse(ax25, out var frame) || frame is null)
            return null;

        var targets = new List<int>(2);
        if (txOn1 && settings.RoutesTo(fromChannel, 1))
            targets.Add(1);
        if (txOn2 && settings.RoutesTo(fromChannel, 2))
            targets.Add(2);

        string packet = $"{frame.Source}>{frame.Destination}:{frame.InfoText}";
        var sendOn = new List<int>(2);
        var alreadyOnAir = new List<int>();
        lock (_onAir)
        {
            Prune(nowUtc);
            foreach (int channel in targets)
            {
                if (IsOnAir(packet, channel, nowUtc))
                {
                    alreadyOnAir.Add(channel);
                    continue;
                }
                _onAir[(packet, channel)] = nowUtc;
                sendOn.Add(channel);
            }

            // Recorded after the routing above, so a 1=1 route still repeats
            // the first copy heard on 1, but no later copy goes back there.
            if (!IsOnAir(packet, fromChannel, nowUtc))
                _onAir[(packet, fromChannel)] = nowUtc;
        }
        return targets.Count == 0 ? null : new DigiDecision(frame.Source.ToString(), repeated, sendOn, alreadyOnAir);
    }

    private bool IsOnAir(string packet, int channel, DateTime nowUtc) =>
        _onAir.TryGetValue((packet, channel), out var at) && nowUtc - at < DuplicateWindow;

    /// <summary>
    /// UI-View notation of the routes, e.g. <c>1=2  2=1,2</c>; a channel
    /// that repeats nowhere is left out.
    /// </summary>
    public static string Describe(DigiSettings settings)
    {
        var lines = new List<string>(2);
        foreach (int from in new[] { 1, 2 })
        {
            var to = new[] { 1, 2 }.Where(t => settings.RoutesTo(from, t)).ToList();
            if (to.Count > 0)
                lines.Add($"{from}={string.Join(",", to)}");
        }
        return string.Join("  ", lines);
    }

    private void Prune(DateTime nowUtc)
    {
        if (_onAir.Count < 256)
            return;
        foreach (var key in _onAir.Where(kv => nowUtc - kv.Value >= DuplicateWindow).Select(kv => kv.Key).ToList())
            _onAir.Remove(key);
    }
}

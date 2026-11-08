using System.Text.Json;
using System.Text.Json.Serialization;
using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Settings;

namespace AprsLive.Core.Stations;

/// <summary>
/// Estimates how far the watched stations (digipeaters) reach, over a
/// sliding 24 h window. A frame counts for a watched station when it is the
/// first entry of the frame's path and the frame has been repeated: that
/// station heard the sender direct, so the distance from it to the sender is
/// one coverage sample. Our own station (<see cref="OwnStation"/>) counts
/// what our TNCs hear direct: radio frames with no hop repeated. Only
/// position reports count, since they carry the sender's own position (an
/// object's position is not where its sender is). The copies of one
/// transmission (heard on both TNCs, then the APRS-IS echo) count once.
/// <para>
/// Links between digis are recorded too: in <c>F1CNH-3*,F5ZBC-3*</c>,
/// F5ZBC-3 heard F1CNH-3, and our own station heard the last digi of a radio
/// frame. The distance is between the digis' known positions, so any frame
/// counts, not only position reports. They are only counted in the summaries
/// with <see cref="IncludeDigiLinks"/>.
/// </para>
/// </summary>
public sealed class DigiCoverage
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly Dictionary<string, CoverageLog> _logs = new(StringComparer.OrdinalIgnoreCase);

    // Every digi seen in the path of a repeated frame within the window,
    // watched or not: the stations offered when picking one to watch.
    private readonly Dictionary<string, DateTimeOffset> _relaysSeen = new(StringComparer.OrdinalIgnoreCase);

    // Where a digi is (station list, our settings), for the links between digis.
    private readonly Func<string, (double Lat, double Lon)?>? _positionOf;

    /// <param name="positionOf">
    /// Where a station is now; without it, only the watched stations' own
    /// beacons place a digi, for the links between digis.
    /// </param>
    public DigiCoverage(Func<string, (double Lat, double Lon)?>? positionOf = null)
    {
        _positionOf = positionOf;
    }

    /// <summary>Raised when a station joins or leaves <see cref="RelaysSeen"/>.</summary>
    public event Action? RelaysSeenChanged;

    public IReadOnlyCollection<string> Watched => _logs.Keys;

    /// <summary>Digis seen in the path of a repeated frame in the last 24 h.</summary>
    public IReadOnlyCollection<string> RelaysSeen => _relaysSeen.Keys;

    /// <summary>Counts the digis heard relaying (links between digis) in the summaries and lists.</summary>
    public bool IncludeDigiLinks { get; set; }

    public bool IsWatched(string callsign) => _logs.ContainsKey(callsign);

    /// <summary>Our own station, also watched: it gets what our TNCs hear direct.</summary>
    public string? OwnStation { get; private set; }

    /// <summary>
    /// Sets our own station and starts recording for it. The previous one is
    /// left watched: the caller unwatches it if it isn't followed as a digi.
    /// </summary>
    public void SetOwnStation(string? callsign)
    {
        OwnStation = string.IsNullOrWhiteSpace(callsign) ? null : callsign;
        if (OwnStation is not null)
            Watch(OwnStation);
    }

    /// <summary>Starts recording for this station. Returns false if it already was.</summary>
    public bool Watch(string callsign) => _logs.TryAdd(callsign, new CoverageLog { Callsign = callsign });

    /// <summary>Stops recording for this station and forgets its samples.</summary>
    public bool Unwatch(string callsign) => _logs.Remove(callsign);

    /// <summary>
    /// Looks at one received packet. Returns true when it added a sample for a
    /// watched station.
    /// </summary>
    /// <param name="origin">Only a radio frame can have been heard direct by our own station.</param>
    public bool Record(AprsPacket packet, PacketOrigin origin)
    {
        bool ownPosition = packet.Kind == AprsPacketKind.Position && packet.HasPosition;

        // A watched relay's own beacon: remember where it is, in case it later
        // drops out of the station list (expiry setting).
        if (ownPosition && _logs.TryGetValue(packet.Source, out var relayLog))
        {
            relayLog.Latitude = packet.Latitude;
            relayLog.Longitude = packet.Longitude;
        }

        var chain = RelayChain(packet.Frame);
        bool relaysChanged = false;
        foreach (string? digi in chain)
        {
            if (digi is null)
                continue;
            if (_relaysSeen.TryAdd(digi, packet.ReceivedAt))
                relaysChanged = true;
            else if (packet.ReceivedAt > _relaysSeen[digi])
                _relaysSeen[digi] = packet.ReceivedAt;
        }
        if (relaysChanged)
            RelaysSeenChanged?.Invoke();

        bool added = false;
        // The first digi heard the sender.
        if (chain.Count > 0 && chain[0] is { } first && ownPosition)
            added |= AddSample(first, packet.Source, packet.Latitude!.Value, packet.Longitude!.Value, packet, isRelay: false);
        // Each digi heard the one before it.
        for (int k = 1; k < chain.Count; k++)
            if (chain[k - 1] is { } heard && chain[k] is { } heardBy)
                added |= AddRelaySample(heardBy, heard, packet);

        // Our TNCs heard the sender direct, or the last digi.
        if (OwnStation is { } own && origin == PacketOrigin.Rf)
        {
            if (chain.Count == 0)
            {
                if (ownPosition)
                    added |= AddSample(own, packet.Source, packet.Latitude!.Value, packet.Longitude!.Value, packet, isRelay: false);
            }
            else if (chain[^1] is { } lastDigi)
                added |= AddRelaySample(own, lastDigi, packet);
        }
        return added;
    }

    // A digi heard relaying: placed where it is known to be, else not counted.
    private bool AddRelaySample(string heardBy, string digi, AprsPacket packet)
    {
        if (!_logs.ContainsKey(heardBy) || PositionOf(digi) is not { } at)
            return false;
        return AddSample(heardBy, digi, at.Lat, at.Lon, packet, isRelay: true);
    }

    private (double Lat, double Lon)? PositionOf(string callsign)
    {
        if (_positionOf?.Invoke(callsign) is { } known)
            return known;
        return _logs.TryGetValue(callsign, out var log) && log.Latitude is { } lat && log.Longitude is { } lon
            ? (lat, lon)
            : null;
    }

    // One sample for this watched station, unless it is a copy of one already counted.
    private bool AddSample(string heardBy, string station, double lat, double lon, AprsPacket packet, bool isRelay)
    {
        if (!_logs.TryGetValue(heardBy, out var log))
            return false;
        if (lat == 0 && lon == 0)
            return false;   // "no fix" placeholder sent by some trackers
        if (string.Equals(station, heardBy, StringComparison.OrdinalIgnoreCase))
            return false;

        // Mic-E carries part of the position in the destination: both make up the payload.
        string payloadKey = $"{packet.Frame.Destination}:{packet.Frame.InfoText}";
        var samples = log.Samples;
        for (int i = samples.Count - 1; i >= 0 && packet.ReceivedAt - samples[i].At <= StationRegistry.DuplicateWindow; i--)
        {
            if (samples[i].PayloadKey == payloadKey
                && string.Equals(samples[i].Station, station, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        samples.Add(new CoverageSample(packet.ReceivedAt, station, lat, lon, isRelay, packet.Frame.ToTnc2()) { PayloadKey = payloadKey });
        return true;
    }

    /// <summary>
    /// The station that repeated the frame first: the first path entry, when
    /// it is a callsign (not an alias such as <c>WIDE1</c>, nor <c>TCPIP</c> or a
    /// q construct) and the frame has been repeated. On APRS-IS only the last
    /// repeated hop carries the <c>*</c> (TNC2 convention), so any marked hop
    /// before the q construct means the first one was repeated too.
    /// </summary>
    public static string? FirstRepeater(Ax25Frame frame) =>
        RelayChain(frame) is [{ } first, ..] ? first : null;

    /// <summary>
    /// The stations that repeated the frame, in order. Every hop up to the
    /// last one marked repeated (before the q construct) was repeated: on
    /// APRS-IS only that last one carries the <c>*</c>. A used alias right
    /// after a callsign was used up by that digi (<c>F1CNH-3*,WIDE1*</c>: one
    /// hop); one with no callsign in front, or <c>TCPIP*</c>, was repeated by a
    /// station we can't name, given as <c>null</c>: who heard whom is unknown
    /// on either side of it.
    /// </summary>
    public static IReadOnlyList<string?> RelayChain(Ax25Frame frame)
    {
        var path = frame.Path;
        int last = -1;
        for (int i = 0; i < path.Count && !ReceptionPath.IsQConstruct(path[i]); i++)
            if (path[i].HasBeenRepeated)
                last = i;

        var chain = new List<string?>();
        bool afterCallsign = false;
        for (int i = 0; i <= last; i++)
        {
            var hop = path[i];
            if (ReceptionPath.IsUsedAlias(hop))
            {
                if (!afterCallsign)
                    chain.Add(null);
                afterCallsign = false;
                continue;
            }
            // Every amateur callsign has a digit; TCPIP, RELAY, qAR... don't.
            bool callsign = hop.Callsign.Any(char.IsAsciiDigit);
            chain.Add(callsign ? hop.ToString() : null);
            afterCallsign = callsign;
        }
        return chain;
    }

    /// <summary>Drops what is older than 24 h. Returns true if a sample went.</summary>
    public bool Prune(DateTimeOffset? now = null)
    {
        var limit = (now ?? DateTimeOffset.Now) - Window;
        bool removed = false;
        foreach (var log in _logs.Values)
            removed |= log.Samples.RemoveAll(s => s.At < limit) > 0;

        var stale = _relaysSeen.Where(kv => kv.Value < limit).Select(kv => kv.Key).ToList();
        foreach (var call in stale)
            _relaysSeen.Remove(call);
        if (stale.Count > 0)
            RelaysSeenChanged?.Invoke();
        return removed;
    }

    /// <summary>
    /// Coverage of a watched station over the last 24 h, or <c>null</c> if it
    /// isn't watched. <paramref name="relayPosition"/> is where the station is
    /// now (station list); without it, the last position remembered here is used.
    /// </summary>
    public CoverageSummary? Summarize(string callsign, (double Lat, double Lon)? relayPosition, DateTimeOffset? now = null)
    {
        if (LogWithPosition(callsign, relayPosition) is not { } log)
            return null;

        var samples = Counted(log, now).ToList();
        int stations = samples.Select(s => s.Station).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        DateTimeOffset? last = samples.Count > 0 ? samples.Max(s => s.At) : null;

        if (log.Latitude is not { } lat || log.Longitude is not { } lon || samples.Count == 0)
            return new CoverageSummary(samples.Count, stations, log.Latitude is not null && log.Longitude is not null,
                null, null, null, null, last);

        double sum = 0;
        CoverageSample farthest = samples[0];
        double maxM = -1;
        foreach (var s in samples)
        {
            double d = GeoMath.DistanceM(lat, lon, s.Latitude, s.Longitude);
            sum += d;
            // On a tie (a fixed station beaconing from the same spot), the latest frame wins.
            if (d > maxM || (d == maxM && s.At > farthest.At))
            {
                maxM = d;
                farthest = s;
            }
        }
        return new CoverageSummary(samples.Count, stations, true,
            sum / samples.Count / 1000.0, maxM / 1000.0, farthest.Station, farthest.At, last);
    }

    /// <summary>
    /// The stations a watched station heard direct over the last 24 h (the
    /// senders of the frames it repeated first, and the digis it heard
    /// relaying with <see cref="IncludeDigiLinks"/>), most recently heard
    /// first. Empty if it isn't watched. The distance is to each one's last position.
    /// </summary>
    public IReadOnlyList<DirectStation> HeardDirect(string callsign, (double Lat, double Lon)? relayPosition, DateTimeOffset? now = null)
    {
        if (LogWithPosition(callsign, relayPosition) is not { } log)
            return [];

        return Counted(log, now)
            .GroupBy(s => s.Station, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var last = g.MaxBy(s => s.At)!;
                double? km = log.Latitude is { } lat && log.Longitude is { } lon
                    ? GeoMath.DistanceM(lat, lon, last.Latitude, last.Longitude) / 1000.0
                    : null;
                // Samples saved before the frame was kept have none.
                var withFrame = g.Where(s => s.Frame is not null).MaxBy(s => s.At);
                return new DirectStation(last.Station, g.Count(), last.At, km, g.Any(s => s.IsRelay),
                    withFrame?.Frame, withFrame?.At);
            })
            .OrderByDescending(d => d.LastAt)
            .ToList();
    }

    // The samples of the last 24 h that count with the current option.
    private IEnumerable<CoverageSample> Counted(CoverageLog log, DateTimeOffset? now)
    {
        var limit = (now ?? DateTimeOffset.Now) - Window;
        return log.Samples.Where(s => s.At >= limit && (IncludeDigiLinks || !s.IsRelay));
    }

    // The log of a watched station, its position updated when the caller knows where it is now.
    private CoverageLog? LogWithPosition(string callsign, (double Lat, double Lon)? relayPosition)
    {
        if (!_logs.TryGetValue(callsign, out var log))
            return null;
        if (relayPosition is { } known)
        {
            log.Latitude = known.Lat;
            log.Longitude = known.Lon;
        }
        return log;
    }

    /// <summary>What to save: the watched stations, their position and samples.</summary>
    public IReadOnlyCollection<CoverageLog> Logs => _logs.Values;

    /// <summary>Puts back saved samples, for the stations watched now only.</summary>
    public void Restore(IEnumerable<CoverageLog> saved, DateTimeOffset? now = null)
    {
        foreach (var entry in saved)
        {
            if (string.IsNullOrWhiteSpace(entry.Callsign) || !_logs.TryGetValue(entry.Callsign, out var log))
                continue;
            log.Latitude ??= entry.Latitude;
            log.Longitude ??= entry.Longitude;
            log.Samples.InsertRange(0, entry.Samples ?? []);
            log.Samples.Sort((a, b) => a.At.CompareTo(b.At));
        }
        Prune(now);
    }
}

/// <summary>One station a watched station heard direct: who, when, and from where.</summary>
/// <param name="IsRelay">
/// A digi heard relaying someone else's frame (link between digis), placed
/// where it is known to be; otherwise the sender of a position report, at the
/// position it sent.
/// </param>
/// <param name="Frame">The frame as received (TNC2 text: <c>SRC&gt;DST,PATH:info</c>), first copy.</param>
public sealed record CoverageSample(DateTimeOffset At, string Station, double Latitude, double Longitude, bool IsRelay = false,
    string? Frame = null)
{
    // Destination + info, to recognise the other copies of the same transmission. Not saved.
    [JsonIgnore]
    internal string? PayloadKey { get; init; }
}

/// <summary>The samples of one watched station, and its last known position.</summary>
public sealed class CoverageLog
{
    public required string Callsign { get; init; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public List<CoverageSample> Samples { get; set; } = new();
}

/// <param name="Frames">Frames this station repeated first (copies counted once).</param>
/// <param name="Stations">Distinct senders among them.</param>
/// <param name="RelayPositionKnown">Without it, no distance can be worked out.</param>
public sealed record CoverageSummary(
    int Frames,
    int Stations,
    bool RelayPositionKnown,
    double? AverageKm,
    double? MaxKm,
    string? FarthestStation,
    DateTimeOffset? FarthestAt,
    DateTimeOffset? LastAt);

/// <summary>A station heard direct by a watched station, over the last 24 h.</summary>
/// <param name="Frames">Frames heard direct from it (position reports, or frames it relayed for a digi).</param>
/// <param name="DistanceKm">From the watched station to its last position; <c>null</c> while the relay position is unknown.</param>
/// <param name="HeardRelaying">Heard as a digi relaying someone else's frame at least once.</param>
/// <param name="LastFrame">The last frame heard from it, as received (TNC2 text), and when.</param>
public sealed record DirectStation(string Station, int Frames, DateTimeOffset LastAt, double? DistanceKm, bool HeardRelaying = false,
    string? LastFrame = null, DateTimeOffset? LastFrameAt = null);

/// <summary>Keeps the coverage samples of the watched stations across restarts in <c>coverage.json</c>, next to <c>settings.json</c>.</summary>
public static class CoverageStore
{
    public static string DefaultPath => Path.Combine(
        Path.GetDirectoryName(AppSettings.DefaultPath)!, "coverage.json");

    public static List<CoverageLog> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return new();
            return JsonSerializer.Deserialize<List<CoverageLog>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Writes a temporary file first, so an interrupted save never leaves a truncated file.</summary>
    public static void Save(IEnumerable<CoverageLog> logs, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(logs.ToList()));
        File.Move(tmp, path, overwrite: true);
    }
}

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
/// one coverage sample. Only position reports count, since they carry the
/// sender's own position (an object's position is not where its sender is).
/// The copies of one transmission (heard on both TNCs, then the APRS-IS
/// echo) count once.
/// </summary>
public sealed class DigiCoverage
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly Dictionary<string, CoverageLog> _logs = new(StringComparer.OrdinalIgnoreCase);

    // Every station seen as first hop within the window, watched or not: the
    // stations offered when picking one to watch.
    private readonly Dictionary<string, DateTimeOffset> _relaysSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a station joins or leaves <see cref="RelaysSeen"/>.</summary>
    public event Action? RelaysSeenChanged;

    public IReadOnlyCollection<string> Watched => _logs.Keys;

    /// <summary>Stations seen as the first hop of a repeated frame in the last 24 h.</summary>
    public IReadOnlyCollection<string> RelaysSeen => _relaysSeen.Keys;

    public bool IsWatched(string callsign) => _logs.ContainsKey(callsign);

    /// <summary>Starts recording for this station. Returns false if it already was.</summary>
    public bool Watch(string callsign) => _logs.TryAdd(callsign, new CoverageLog { Callsign = callsign });

    /// <summary>Stops recording for this station and forgets its samples.</summary>
    public bool Unwatch(string callsign) => _logs.Remove(callsign);

    /// <summary>
    /// Looks at one received packet. Returns true when it added a sample for a
    /// watched station.
    /// </summary>
    public bool Record(AprsPacket packet)
    {
        bool ownPosition = packet.Kind == AprsPacketKind.Position && packet.HasPosition;

        // A watched relay's own beacon: remember where it is, in case it later
        // drops out of the station list (expiry setting).
        if (ownPosition && _logs.TryGetValue(packet.Source, out var relayLog))
        {
            relayLog.Latitude = packet.Latitude;
            relayLog.Longitude = packet.Longitude;
        }

        if (FirstRepeater(packet.Frame) is not { } relay)
            return false;
        if (_relaysSeen.TryAdd(relay, packet.ReceivedAt))
            RelaysSeenChanged?.Invoke();
        else if (packet.ReceivedAt > _relaysSeen[relay])
            _relaysSeen[relay] = packet.ReceivedAt;

        if (!ownPosition || !_logs.TryGetValue(relay, out var log))
            return false;
        double lat = packet.Latitude!.Value, lon = packet.Longitude!.Value;
        if (lat == 0 && lon == 0)
            return false;   // "no fix" placeholder sent by some trackers
        if (string.Equals(packet.Source, relay, StringComparison.OrdinalIgnoreCase))
            return false;

        // Mic-E carries part of the position in the destination: both make up the payload.
        string payloadKey = $"{packet.Frame.Destination}:{packet.Frame.InfoText}";
        var samples = log.Samples;
        for (int i = samples.Count - 1; i >= 0 && packet.ReceivedAt - samples[i].At <= StationRegistry.DuplicateWindow; i--)
        {
            if (samples[i].PayloadKey == payloadKey
                && string.Equals(samples[i].Station, packet.Source, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        samples.Add(new CoverageSample(packet.ReceivedAt, packet.Source, lat, lon) { PayloadKey = payloadKey });
        return true;
    }

    /// <summary>
    /// The station that repeated the frame first: the first path entry, when
    /// it is a callsign (not an alias such as <c>WIDE1</c>, nor <c>TCPIP</c> or a
    /// q construct) and the frame has been repeated. On APRS-IS only the last
    /// repeated hop carries the <c>*</c> (TNC2 convention), so any marked hop
    /// before the q construct means the first one was repeated too.
    /// </summary>
    public static string? FirstRepeater(Ax25Frame frame)
    {
        var path = frame.Path;
        if (path.Count == 0)
            return null;
        var first = path[0];
        // Every amateur callsign has a digit; TCPIP, RELAY, qAR... don't.
        if (!first.Callsign.Any(char.IsAsciiDigit) || ReceptionPath.IsUsedAlias(first))
            return null;

        foreach (var hop in path)
        {
            if (ReceptionPath.IsQConstruct(hop))
                break;
            if (hop.HasBeenRepeated)
                return first.ToString();
        }
        return null;
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
        if (!_logs.TryGetValue(callsign, out var log))
            return null;
        if (relayPosition is { } known)
        {
            log.Latitude = known.Lat;
            log.Longitude = known.Lon;
        }

        var limit = (now ?? DateTimeOffset.Now) - Window;
        var samples = log.Samples.Where(s => s.At >= limit).ToList();
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

/// <summary>One frame a watched station repeated first: who sent it, when, and from where.</summary>
public sealed record CoverageSample(DateTimeOffset At, string Station, double Latitude, double Longitude)
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

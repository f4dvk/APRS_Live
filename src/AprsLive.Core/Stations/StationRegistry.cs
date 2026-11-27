using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Stations;

/// <summary>
/// Tracks the last-known state of every station heard with a position
/// (position/object/item/Mic-E packets), keyed by <see cref="AprsPacket.DisplayName"/>
/// — the object/item name when there is one, else the source callsign — so
/// that several objects sent by the same station (or by our own) each get
/// their own map marker instead of colliding under one entry.
/// </summary>
public sealed class StationRegistry
{
    private readonly Dictionary<string, HeardStation> _stations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised whenever a station is added or updated.</summary>
    public event Action<HeardStation>? StationUpdated;

    /// <summary>
    /// Raised after a bulk change: <see cref="RemoveNotHeardSince"/> dropped at
    /// least one station, or <see cref="Restore"/> brought saved ones back.
    /// </summary>
    public event Action? StationsChanged;

    /// <summary>
    /// Puts back stations saved at the last close (see <see cref="StationStore"/>).
    /// A station already heard since this start keeps its fresh entry.
    /// </summary>
    public void Restore(IEnumerable<HeardStation> stations)
    {
        int added = 0;
        foreach (var station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.Callsign) || !station.HasPosition)
                continue;
            if (_stations.TryAdd(station.Callsign, station))
                added++;
        }
        if (added > 0)
            StationsChanged?.Invoke();
    }

    /// <summary>
    /// Forgets every station not heard for longer than <paramref name="maxAge"/>,
    /// except our own (<see cref="HeardStation.IsLocal"/>). Returns how many were removed.
    /// </summary>
    public int RemoveNotHeardSince(TimeSpan maxAge, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var stale = _stations.Where(kv => !kv.Value.IsLocal && at - kv.Value.LastHeard > maxAge)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in stale)
            _stations.Remove(key);
        if (stale.Count > 0)
            StationsChanged?.Invoke();
        return stale.Count;
    }

    public IReadOnlyCollection<HeardStation> Stations => _stations.Values;

    /// <summary>The station (or object) heard under this name, if any.</summary>
    public HeardStation? Find(string callsign) => _stations.GetValueOrDefault(callsign);

    /// <summary>The same packet again within this delay is a copy of one transmission (as in the APRS-IS dupe check).</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);

    private const int MaxAlsoHeardVia = 5;

    /// <param name="channel">Radio channel the frame came in on (1 = TNC1, 2 = TNC2), for <see cref="PacketOrigin.Rf"/>; 0 when unknown.</param>
    public void Apply(AprsPacket packet, PacketOrigin origin = PacketOrigin.Rf, int channel = 0)
    {
        if (!packet.HasPosition)
            return;

        string call = packet.DisplayName;
        if (!_stations.TryGetValue(call, out var station))
        {
            station = new HeardStation { Callsign = call, FirstHeard = packet.ReceivedAt };
            _stations[call] = station;
        }

        // Several copies of one transmission come in (heard direct, then the
        // digi's repeat a second later, then the APRS-IS echo). The route and
        // path shown are the best copy's — direct, else via digi, else
        // Internet — and the others are only listed in AlsoHeardVia. Our own
        // packet heard back is a real reception though, so it does update the route.
        var route = RouteOf(packet.Frame, origin);
        // Every copy counts here, duplicates included: the digi's repeat heard
        // on the other channel still shows the station is heard there.
        bool channelNoted = origin == PacketOrigin.Rf && NoteChannel(station, channel, route, packet.ReceivedAt);
        string payloadKey = $"{packet.Frame.Destination}:{packet.Frame.InfoText}";
        bool sameTransmission = origin != PacketOrigin.Local
            && station.LastRoute != StationRoute.Local
            && station.LastPayloadKey == payloadKey
            && packet.ReceivedAt - station.LastPayloadAt <= DuplicateWindow;
        if (sameTransmission)
        {
            if (route >= station.LastRoute)
            {
                if (AddAlsoHeard(station, Describe(route, packet.Frame.Path)) | channelNoted)
                    StationUpdated?.Invoke(station);
                return;
            }
            // A better copy than the first one: it takes over, the first is kept as "also".
            var previous = station.LastRoute == StationRoute.Internet ? station.LastInternetPath : station.LastRfPath;
            AddAlsoHeard(station, Describe(station.LastRoute, previous ?? []));
        }
        else
        {
            station.LastPayloadKey = payloadKey;
            station.LastPayloadAt = packet.ReceivedAt;
            station.AlsoHeardVia = new();
        }
        station.Latitude = packet.Latitude;
        station.Longitude = packet.Longitude;
        station.SymbolTable = packet.SymbolTable;
        station.SymbolCode = packet.SymbolCode;
        station.Comment = packet.Comment;
        station.CourseDeg = packet.CourseDeg;
        station.SpeedKnots = packet.SpeedKnots;
        station.AltitudeM = packet.AltitudeM;
        station.MicEStatus = packet.MicEStatus;
        station.IsEmergency = packet.IsEmergency;
        if (packet.Device is not null)
            station.Device = packet.Device;
        station.PathText = FormatPath(packet.Frame);
        station.LastHeard = packet.ReceivedAt;
        station.LastRoute = route;
        if (origin == PacketOrigin.Local)
            station.IsLocal = true;
        if (origin == PacketOrigin.Rf)
        {
            station.LastRfPath = packet.Frame.Path;
            station.LastRfChannel = channel;
        }
        else if (origin == PacketOrigin.Internet)
            station.LastInternetPath = packet.Frame.Path;

        if (station.LastRoute == StationRoute.Direct)
        {
            station.LastHeardDirect = packet.ReceivedAt;
            station.DirectLatitude = packet.Latitude;
            station.DirectLongitude = packet.Longitude;
        }

        StationUpdated?.Invoke(station);
    }

    private static bool NoteChannel(HeardStation station, int channel, StationRoute route, DateTimeOffset at)
    {
        ChannelReception reception;
        if (channel == 1)
            reception = station.Tnc1 ??= new ChannelReception();
        else if (channel == 2)
            reception = station.Tnc2 ??= new ChannelReception();
        else
            return false;

        if (at > reception.LastHeard)
            reception.LastHeard = at;
        if (route == StationRoute.Direct && (reception.LastDirect is not { } direct || at > direct))
            reception.LastDirect = at;
        return true;
    }

    /// <summary>
    /// The farthest station heard direct (RF, no digipeater hop — see
    /// <see cref="HeardStation.LastHeardDirect"/>) from the given position,
    /// measured at the position it had when heard direct, or <c>null</c> if
    /// none qualify. For an outgoing "Best DX" status report.
    /// <paramref name="excludeCallsign"/> is normally the operator's own
    /// callsign, as a safety net should a TNC echo our own beacon back as RX.
    /// With <paramref name="maxAge"/>, a station not heard direct for longer
    /// than that no longer counts, so the next one still in the window takes over.
    /// </summary>
    public HeardStation? FindBestDx(double fromLatitude, double fromLongitude, string? excludeCallsign = null,
        TimeSpan? maxAge = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        HeardStation? best = null;
        double bestDistanceM = -1;
        foreach (var station in _stations.Values)
        {
            if (station.DirectLatitude is not { } lat || station.DirectLongitude is not { } lon
                || station.LastHeardDirect is not { } heardAt)
                continue;
            if (maxAge is { } age && at - heardAt > age)
                continue;
            if (excludeCallsign is not null && string.Equals(station.Callsign, excludeCallsign, StringComparison.OrdinalIgnoreCase))
                continue;

            double distanceM = GeoMath.DistanceM(fromLatitude, fromLongitude, lat, lon);
            if (distanceM > bestDistanceM)
            {
                bestDistanceM = distanceM;
                best = station;
            }
        }
        return best;
    }

    // Declared best first: Direct < Digipeater < Internet is relied on above.
    private static StationRoute RouteOf(Ax25Frame frame, PacketOrigin origin) => origin switch
    {
        PacketOrigin.Internet => StationRoute.Internet,
        PacketOrigin.Local => StationRoute.Local,
        _ => frame.HasBeenDigipeated ? StationRoute.Digipeater : StationRoute.Direct,
    };

    private static string Describe(StationRoute route, IReadOnlyList<Ax25Address> path)
    {
        string hops = string.Join(",", path.Select(a => a.ToStringWithFlag()));
        return route switch
        {
            StationRoute.Internet => $"Internet ({hops})",
            StationRoute.Direct => "direct",
            _ => $"via {hops}",
        };
    }

    private static bool AddAlsoHeard(HeardStation station, string via)
    {
        if (station.AlsoHeardVia.Contains(via) || station.AlsoHeardVia.Count >= MaxAlsoHeardVia)
            return false;
        station.AlsoHeardVia.Add(via);
        return true;
    }

    private static string FormatPath(Ax25Frame frame)
    {
        string dest = frame.Destination.ToString();
        return frame.Path.Count == 0
            ? dest
            : $"{dest} via {string.Join(",", frame.Path.Select(a => a.ToStringWithFlag()))}";
    }
}

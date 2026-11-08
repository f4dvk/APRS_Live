using System.Text.RegularExpressions;
using AprsLive.Core.Ax25;

namespace AprsLive.Core.Stations;

/// <summary>
/// The points of the line showing how a station's last packet reached us:
/// the station, each repeating station (H bit set), then our own station —
/// or, for a packet that only came from APRS-IS, the IGate named after the q
/// construct instead of us. All or nothing: if any station the packet went
/// through isn't on the map (no known position), or a hop was repeated by a
/// digi that didn't sign it (a used alias such as <c>WIDE2*</c> with no
/// callsign in front), there is no line — a shortcut drawn past an unknown
/// relay would misrepresent the route.
/// </summary>
public static partial class ReceptionPath
{
    public sealed record Result(IReadOnlyList<(double Lat, double Lon)> Points, bool ViaInternet);

    public static Result? Compute(HeardStation station, Func<string, HeardStation?> findStation,
        (double Lat, double Lon)? ownPosition)
    {
        bool viaInternet = station.LastRfPath is null;
        var path = station.LastRfPath ?? station.LastInternetPath;
        if (path is null || Position(station) is not { } origin)
            return null;

        var points = new List<(double Lat, double Lon)> { origin };
        string? igate = null;
        bool previousHopSigned = false;
        for (int i = 0; i < path.Count; i++)
        {
            var hop = path[i];
            if (IsQConstruct(hop))
            {
                igate = i + 1 < path.Count ? path[i + 1].ToString() : null;
                break;
            }
            if (!hop.HasBeenRepeated)
            {
                previousHopSigned = false;
                continue;
            }
            if (IsUsedAlias(hop))
            {
                // "F4DIGI-1*,WIDE1*": the alias was used up by the digi that signed just before it.
                if (!previousHopSigned)
                    return null;
                previousHopSigned = false;
                continue;
            }
            if (Position(findStation(hop.ToString())) is not { } relay)
                return null;   // TCPIP*, or a digi never heard with a position
            points.Add(relay);
            previousHopSigned = true;
        }

        var end = viaInternet
            ? (igate is null ? null : Position(findStation(igate)))
            : ownPosition;
        if (end is not { } last)
            return null;
        points.Add(last);

        return new Result(points, viaInternet);
    }

    internal static bool IsQConstruct(Ax25Address hop) =>
        hop.Callsign.Length == 3 && hop.Callsign.StartsWith("qA", StringComparison.OrdinalIgnoreCase);

    internal static bool IsUsedAlias(Ax25Address hop) => GenericAlias().IsMatch(hop.Callsign);

    [GeneratedRegex("^(WIDE|TRACE)[1-7]?$|^(RELAY|GATE|ECHO)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericAlias();

    private static (double Lat, double Lon)? Position(HeardStation? s) =>
        s is { Latitude: { } lat, Longitude: { } lon } ? (lat, lon) : null;
}

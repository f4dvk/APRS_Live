using System.Globalization;
using AprsLive.App.Icons;
using AprsLive.Core;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>One row of the Stations tab — a read-only snapshot of a <see cref="HeardStation"/> plus its icon.</summary>
public sealed class StationRowViewModel
{
    /// <param name="ownPosition">Where our station is (settings), for the distance; <c>null</c> if not set.</param>
    /// <param name="ownCallsign">Our own station shows no distance.</param>
    public StationRowViewModel(HeardStation station, (double Lat, double Lon)? ownPosition = null, string? ownCallsign = null)
    {
        Callsign = station.Callsign;
        DistanceText = ownPosition is { } from
            && station.Latitude is { } lat && station.Longitude is { } lon
            && !string.Equals(station.Callsign, ownCallsign, StringComparison.OrdinalIgnoreCase)
                ? Km(GeoMath.DistanceM(from.Lat, from.Lon, lat, lon) / 1000.0)
                : "";
        Icon = AprsSymbolIcons.CellImage(station.SymbolTable, station.SymbolCode);
        Comment = station.Comment ?? "";
        Device = station.Device ?? "";
        MicEStatus = station.MicEStatus ?? "";
        IsEmergency = station.IsEmergency;
        LastHeardText = station.LastHeard.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss");
        LastHeard = station.LastHeard;

        IsDirect = station.LastHeardDirect is not null;
        RouteText = station.LastHeardDirect is { } direct
            ? $"Direct {direct.LocalDateTime:HH:mm}"
            : station.LastRoute switch
            {
                StationRoute.Digipeater => "Via digi",
                StationRoute.Internet => "Internet",
                StationRoute.Local => "Local",
                _ => "",
            };
        ChannelText = string.Join(" · ", new[] { 1, 2 }.Where(c => station.ReceptionOn(c) is not null).Select(c => $"TNC{c}"));
        var channelLines = ChannelLines(station);
        ChannelTip = channelLines.Count > 0 ? string.Join("\n", channelLines) : null;

        string lastPacket = LastCopyChannel(station) is { } port
            ? $"Dernier paquet ({port}) : {station.PathText}"
            : $"Dernier paquet : {station.PathText}";
        RouteTip = station.AlsoHeardVia.Count == 0
            ? lastPacket
            : $"{lastPacket}\nAussi reçu : {string.Join(" ; ", station.AlsoHeardVia)}";
    }

    /// <summary>One line per radio channel the station was heard on, e.g. "TNC1 (Liaison A) : reçue 12:48:05, en direct 12:48:05".</summary>
    public static IReadOnlyList<string> ChannelLines(HeardStation station)
    {
        var lines = new List<string>(2);
        foreach (int channel in new[] { 1, 2 })
        {
            if (station.ReceptionOn(channel) is not { } rx)
                continue;
            string direct = rx.LastDirect is { } d ? $"en direct {When(d)}" : "jamais en direct";
            lines.Add($"TNC{channel} (Liaison {(channel == 1 ? 'A' : 'B')}) : reçue {When(rx.LastHeard)}, {direct}");
        }
        return lines;
    }

    /// <summary>"TNC1"/"TNC2" for the radio copy whose route and path are shown; <c>null</c> for APRS-IS, our own, or unknown.</summary>
    public static string? LastCopyChannel(HeardStation station) =>
        (station.LastRoute is StationRoute.Direct or StationRoute.Digipeater) && station.LastRfChannel is 1 or 2
            ? $"TNC{station.LastRfChannel}"
            : null;

    // InvariantGlobalization is on: French decimal comma by hand.
    private static string Km(double km) =>
        km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";

    private static string When(DateTimeOffset at)
    {
        var local = at.LocalDateTime;
        return local.Date == DateTime.Today ? $"{local:HH:mm:ss}" : $"{local:dd/MM HH:mm:ss}";
    }

    public string Callsign { get; }

    /// <summary>From our station (settings position) to its last position; empty for ours, or when our position isn't set.</summary>
    public string DistanceText { get; }

    public Avalonia.Media.IImage Icon { get; }
    public string Comment { get; }

    /// <summary>Radio or tracker identified from its Mic-E packets, e.g. "Yaesu FT3D"; empty if unknown.</summary>
    public string Device { get; }

    /// <summary>Mic-E message of the last position ("En route"...); empty if it was not Mic-E.</summary>
    public string MicEStatus { get; }

    public bool IsEmergency { get; }

    public string LastHeardText { get; }
    public DateTimeOffset LastHeard { get; }

    /// <summary>Received on RF with no digipeater hop at least once this session.</summary>
    public bool IsDirect { get; }
    public string RouteText { get; }
    public string RouteTip { get; }

    /// <summary>Radio channels it was heard on, e.g. "TNC1 · TNC2"; empty when only via APRS-IS (or ours).</summary>
    public string ChannelText { get; }

    public string? ChannelTip { get; }
}

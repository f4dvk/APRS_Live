using AprsLive.Core.Ax25;

namespace AprsLive.Core.Aprs;

public enum AprsPacketKind
{
    Unknown,
    Position,
    Object,
    Item,
    Message,
    Status,
    Telemetry
}

/// <summary>Decoded APRS information field, plus the AX.25 envelope it came in.</summary>
public sealed class AprsPacket
{
    public required Ax25Frame Frame { get; init; }
    public AprsPacketKind Kind { get; set; } = AprsPacketKind.Unknown;
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Source callsign-SSID, e.g. <c>F4DVK-14</c>.</summary>
    public string Source => Frame.Source.ToString();

    /// <summary>Base callsign of the source, e.g. <c>F4DVK</c>.</summary>
    public string SourceBase => Frame.Source.Base;

    // --- position (Position / Object / Item) ---
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public char SymbolTable { get; set; } = '/';
    public char SymbolCode { get; set; } = '/';
    public double? CourseDeg { get; set; }
    public double? SpeedKnots { get; set; }
    public double? AltitudeM { get; set; }

    /// <summary>Object / item name (when <see cref="Kind"/> is Object or Item).</summary>
    public string? ObjectName { get; set; }
    public bool ObjectLive { get; set; } = true;

    // --- message (Kind == Message) ---
    public string? MessageAddressee { get; set; }
    public string? MessageText { get; set; }
    public string? MessageNumber { get; set; }
    public bool IsAck { get; set; }
    public bool IsRej { get; set; }

    // --- status / free text ---
    public string? Comment { get; set; }

    public bool HasPosition => Latitude is not null && Longitude is not null;

    /// <summary>Name to show on the map: object/item name, else source callsign.</summary>
    public string DisplayName => ObjectName ?? Source;
}

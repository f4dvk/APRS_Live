using System.Text.Json.Serialization;

namespace AprsLive.Core.Stations;

/// <summary>A station heard over APRS, with its last known position and the frame that last updated it.</summary>
public sealed class HeardStation
{
    /// <summary>Full callsign incl. SSID, e.g. <c>F4DVK-9</c>.</summary>
    public required string Callsign { get; init; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public char SymbolTable { get; set; } = '/';

    public char SymbolCode { get; set; } = '/';

    /// <summary>Free-text comment/status from the last position packet.</summary>
    public string? Comment { get; set; }

    public double? CourseDeg { get; set; }

    public double? SpeedKnots { get; set; }

    public double? AltitudeM { get; set; }

    /// <summary>Mic-E message of the last position packet ("En route"...); <c>null</c> if it was not Mic-E.</summary>
    public string? MicEStatus { get; set; }

    /// <summary>The last position packet was a Mic-E emergency.</summary>
    public bool IsEmergency { get; set; }

    /// <summary>Radio or tracker last identified for this station (kept through packets that don't tell).</summary>
    public string? Device { get; set; }

    /// <summary>The path the last packet was received over, e.g. <c>APLIVE via WIDE1-1*,WIDE2-1</c>.</summary>
    public string PathText { get; set; } = "";

    public DateTimeOffset FirstHeard { get; set; }

    public DateTimeOffset LastHeard { get; set; }

    [JsonIgnore]
    public bool HasPosition => Latitude is not null && Longitude is not null;

    /// <summary>How the last packet from this station reached us.</summary>
    public StationRoute LastRoute { get; set; }

    /// <summary>Ours (our beacon, an object we sent) — kept even when heard back later via a digi, and never expired.</summary>
    public bool IsLocal { get; set; }

    /// <summary>
    /// Last time a packet from this station was received on RF with no
    /// digipeater hop marked as repeated. Kept when later copies arrive via a
    /// digipeater or APRS-IS, so the usual digi/IGate duplicates don't erase it.
    /// </summary>
    public DateTimeOffset? LastHeardDirect { get; set; }

    /// <summary>Position carried by the last direct packet — a mobile may since have moved out of direct range.</summary>
    public double? DirectLatitude { get; set; }

    public double? DirectLongitude { get; set; }

    [JsonIgnore]
    public bool HeardDirect => LastHeardDirect is not null;

    /// <summary>
    /// Digipeater path of the last transmission received on RF, as heard (H bits
    /// included) — for drawing the reception path. Taken from its first copy:
    /// a digi's repeat of a packet already heard direct does not replace it.
    /// </summary>
    public IReadOnlyList<Ax25.Ax25Address>? LastRfPath { get; set; }

    /// <summary>Radio channel the copy behind <see cref="LastRfPath"/> came in on: 1 = TNC1, 2 = TNC2, 0 unknown.</summary>
    public int LastRfChannel { get; set; }

    /// <summary>Same for the last packet received from APRS-IS (q construct and IGate included).</summary>
    public IReadOnlyList<Ax25.Ax25Address>? LastInternetPath { get; set; }

    /// <summary>
    /// Other copies of the last transmission, after the first one (a digi's
    /// repeat, the APRS-IS echo), e.g. <c>via F1PRY-14*,WIDE1*</c> — shown, but
    /// they leave the route and path of the first copy in place.
    /// </summary>
    public List<string> AlsoHeardVia { get; set; } = new();

    /// <summary>Reception on TNC1 (Liaison A), every copy counted; <c>null</c> if never heard there.</summary>
    public ChannelReception? Tnc1 { get; set; }

    /// <summary>Same for TNC2 (Liaison B).</summary>
    public ChannelReception? Tnc2 { get; set; }

    /// <summary><see cref="Tnc1"/> or <see cref="Tnc2"/> for channel 1 or 2.</summary>
    public ChannelReception? ReceptionOn(int channel) => channel switch
    {
        1 => Tnc1,
        2 => Tnc2,
        _ => null,
    };

    // Identifies the last transmission (destination + info) and when its first
    // copy came in, to recognise the later copies above. Not saved.
    internal string? LastPayloadKey { get; set; }

    internal DateTimeOffset LastPayloadAt { get; set; }
}

/// <summary>When a station was last heard on one radio channel (TNC1 or TNC2).</summary>
public sealed class ChannelReception
{
    public DateTimeOffset LastHeard { get; set; }

    /// <summary>Last time heard there with no digipeater hop, if ever.</summary>
    public DateTimeOffset? LastDirect { get; set; }
}

/// <summary>How a packet reached us. Order matters: best reception first (StationRegistry ranks copies of one transmission by it).</summary>
public enum StationRoute
{
    Direct,
    Digipeater,
    Internet,
    /// <summary>Our own transmission (beacon, object) fed back into the registry.</summary>
    Local,
}

/// <summary>Where a packet handed to <see cref="StationRegistry.Apply"/> came from.</summary>
public enum PacketOrigin
{
    Rf,
    Internet,
    Local,
}

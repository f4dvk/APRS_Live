using System.Text.Json;
using System.Text.Json.Serialization;

namespace AprsLive.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// The operator's own station identity: what goes out on every transmitted
/// frame. Mirrors what a UIView32-style "station setup" dialog configures —
/// callsign, icon, digipeater path, destination (tocall), and a free-text
/// beacon comment.
/// </summary>
public sealed class StationSettings
{
    /// <summary>Callsign with optional SSID, e.g. <c>F4DVK-9</c>.</summary>
    public string Callsign { get; set; } = "";

    /// <summary>Fixed station position, in decimal degrees — null until set (form field, or right-click on the map).</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>APRS symbol table: <c>/</c> (primary) or <c>\</c> (alternate).</summary>
    public char SymbolTable { get; set; } = '/';

    /// <summary>APRS symbol code within the chosen table, e.g. <c>&gt;</c> for a car.</summary>
    public char SymbolCode { get; set; } = '>';

    /// <summary>Digipeater path, e.g. <c>["WIDE1-1", "WIDE2-2"]</c>.</summary>
    public List<string> Path { get; set; } = ["WIDE1-1", "WIDE2-2"];

    /// <summary>Destination (tocall), e.g. <c>APLIVE</c>.</summary>
    public string Destination { get; set; } = "APLIVE";

    /// <summary>Free-text comment/status appended to outgoing position beacons.</summary>
    public string Text { get; set; } = "";

    /// <summary>Automatic beacon interval, in minutes. 0 = disabled (manual sending only).</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>Where a status report's text comes from.</summary>
    public StatusMode StatusMode { get; set; } = StatusMode.FreeText;

    /// <summary>Free text sent as the status report when <see cref="StatusMode"/> is <see cref="StatusMode.FreeText"/> — truncated to the APRS-recommended 62 characters on send.</summary>
    public string StatusText { get; set; } = "";

    /// <summary>
    /// Send a status report every Nth beacon (manual or automatic — both
    /// count): 1 = with every beacon, 2 = every other one, etc. 0 disables
    /// status sending entirely.
    /// </summary>
    public int StatusEveryNBeacons { get; set; }

    /// <summary>Best DX validity, in minutes: a station not heard direct for longer is dropped from the Best DX (0 = whole session).</summary>
    public int BestDxWindowMinutes { get; set; }
}

/// <summary>Where an outgoing status report's text comes from.</summary>
public enum StatusMode
{
    /// <summary>The operator's own free text (<see cref="StationSettings.StatusText"/>).</summary>
    FreeText,

    /// <summary>Auto-computed: the farthest station heard direct (no digipeater hop), recomputed at send time.</summary>
    BestDx,
}

/// <summary>
/// One user-defined APRS object: a fixed-position report sent under a made-up
/// name rather than the station's own callsign — used to mark something that
/// isn't itself a transmitting station (an event, a relay, a hazard, a net
/// control point...). Several can be configured and sent independently.
/// </summary>
public sealed class ObjectSettings
{
    /// <summary>Object name, up to 9 characters (space-padded/truncated on send) — this is the object's identity, shown on the map in place of a callsign.</summary>
    public string Name { get; set; } = "";

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public char SymbolTable { get; set; } = '/';

    public char SymbolCode { get; set; } = '>';

    public string Comment { get; set; } = "";

    /// <summary>Automatic resend interval, in minutes, so the object doesn't expire from other stations' views. 0 = disabled (manual sending only).</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>Whether the object is currently live (<c>*</c>) or has been killed (<c>_</c>) — a killed object is sent once more with this flag to retract it, then no longer auto-resent.</summary>
    public bool Live { get; set; } = true;
}

/// <summary>What kind of transport a TNC channel uses.</summary>
public enum ChannelKind
{
    KissSerial,
    KissTcp,
    AudioAfsk,
}

/// <summary>
/// One of the two configurable TNC channels: a hardware KISS TNC (serial or
/// TCP, e.g. Direwolf's KISSPORT), or — once the modem phase lands — a sound
/// card running the built-in AFSK1200 software modem.
/// </summary>
public sealed class ChannelSettings
{
    public bool Enabled { get; set; }

    public ChannelKind Kind { get; set; } = ChannelKind.KissSerial;

    /// <summary>KissSerial: the OS port name, e.g. <c>/dev/ttyUSB0</c> or <c>COM3</c>.</summary>
    public string SerialPort { get; set; } = "";

    public int BaudRate { get; set; } = 9600;

    /// <summary>KissSerial: the kind of TNC (<see cref="Kiss.TncModels"/>), which filled in the commands below.</summary>
    public string TncModel { get; set; } = Kiss.TncModels.NoneId;

    /// <summary>KissSerial: commands that put the TNC into KISS mode once the port is open (see <see cref="Kiss.TncScript"/>).</summary>
    public string KissInitCommands { get; set; } = "";

    /// <summary>KissSerial: commands that take it back out of KISS on disconnect and when the application closes.</summary>
    public string KissExitCommands { get; set; } = "";

    /// <summary>KissTcp: host of the KISS TCP server (e.g. Direwolf's <c>KISSPORT</c>).</summary>
    public string TcpHost { get; set; } = "127.0.0.1";

    public int TcpPort { get; set; } = 8001;

    /// <summary>
    /// Whether this channel transmits: outgoing station beacons, messages and
    /// objects go out every channel with this on (not just one "primary" —
    /// both, if both are on), and the digipeater only repeats onto a channel
    /// with this on (it can still listen to a channel that has it off).
    /// </summary>
    public bool TxEnabled { get; set; }

    /// <summary>Was connected when the application last closed — reconnect on the next start.</summary>
    public bool ReconnectOnStartup { get; set; }

    /// <summary>AudioAfsk: capture device id (empty/null = system default).</summary>
    public string? AudioInputDeviceId { get; set; }

    /// <summary>AudioAfsk: playback device id (empty/null = system default).</summary>
    public string? AudioOutputDeviceId { get; set; }

    public PttMode PttMode { get; set; } = PttMode.None;

    /// <summary>PTT serial port, used when <see cref="PttMode"/> isn't <see cref="PttMode.None"/> — a separate port from any KISS serial link, opened purely for RTS/DTR line control.</summary>
    public string PttPort { get; set; } = "";
}

/// <summary>How an AudioAfsk channel keys the transmitter for the duration of its TX audio burst.</summary>
public enum PttMode
{
    /// <summary>No PTT line — VOX on the radio keys from the audio itself.</summary>
    None,
    Rts,
    Dtr,
}

/// <summary>
/// Digipeater: relays a frame heard on a TNC channel — standard "New
/// N-Paradigm" WIDEn-N alias processing (decrement the hop count, or
/// substitute the station's own callsign on the last hop) — along per-channel
/// routes, UI-View style: <c>1=2</c> "heard on 1, repeat on 2", <c>2=1,2</c>
/// "heard on 2, repeat on 1 and 2". Any channel can be listened to; a route
/// only goes out when its destination channel has
/// <see cref="ChannelSettings.TxEnabled"/> on. Gating to APRS-IS is separate
/// (<see cref="AprsIsSettings.IGateEnabled"/>).
/// </summary>
public sealed class DigiSettings
{
    public bool Enabled { get; set; }

    public bool Wide1 { get; set; } = true;

    public bool Wide2 { get; set; } = true;

    public bool Wide3 { get; set; }

    /// <summary>Heard on TNC1 (Liaison A) → repeated on TNC1.</summary>
    public bool Route1To1 { get; set; } = true;

    /// <summary>Heard on TNC1 (Liaison A) → repeated on TNC2.</summary>
    public bool Route1To2 { get; set; }

    /// <summary>Heard on TNC2 (Liaison B) → repeated on TNC1.</summary>
    public bool Route2To1 { get; set; }

    /// <summary>Heard on TNC2 (Liaison B) → repeated on TNC2.</summary>
    public bool Route2To2 { get; set; } = true;

    /// <summary>Pre-routes setting (one "takes part" flag per channel), read from older files only — see <see cref="UpgradeLegacyChannels"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RepeatOnTnc1 { get; set; }

    /// <inheritdoc cref="RepeatOnTnc1"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RepeatOnTnc2 { get; set; }

    /// <summary>Whether a frame heard on channel <paramref name="from"/> (1 or 2) is to be repeated on channel <paramref name="to"/>.</summary>
    public bool RoutesTo(int from, int to) => (from, to) switch
    {
        (1, 1) => Route1To1,
        (1, 2) => Route1To2,
        (2, 1) => Route2To1,
        (2, 2) => Route2To2,
        _ => false,
    };

    /// <summary>
    /// Older files had one flag per channel: a frame heard on a ticked
    /// channel was repeated on every ticked channel. Same behaviour as routes:
    /// one ticked → that channel onto itself; both → every route.
    /// </summary>
    internal void UpgradeLegacyChannels()
    {
        if (RepeatOnTnc1 is null && RepeatOnTnc2 is null)
            return;

        bool on1 = RepeatOnTnc1 ?? true;   // the old defaults were on
        bool on2 = RepeatOnTnc2 ?? true;
        Route1To1 = on1;
        Route1To2 = on1 && on2;
        Route2To1 = on1 && on2;
        Route2To2 = on2;
        RepeatOnTnc1 = null;
        RepeatOnTnc2 = null;
    }
}

/// <summary>
/// APRS-IS (Internet Service) connection: a text-protocol feed of APRS
/// traffic over TCP, independent from the two RF/audio TNC channels — no
/// "primary TX" concept here, it's about widening reception (and, once
/// logged in, optionally injecting frames) over the internet rather than RF.
/// </summary>
public sealed class AprsIsSettings
{
    /// <summary>
    /// The numeric APRS-IS login passcode for <see cref="StationSettings.Callsign"/>
    /// — "-1" logs in read-only (valid for any callsign, no packets can be
    /// sent), any other value must be the real passcode for that callsign.
    /// </summary>
    public string Passcode { get; set; } = "-1";

    /// <summary>Preferred server hostname, e.g. <c>rotate.aprs2.net</c> — tried first, then the rest of the known rotation on failure.</summary>
    public string Server { get; set; } = "rotate.aprs2.net";

    public int Port { get; set; } = 14580;

    public bool ConnectOnStartup { get; set; }

    /// <summary>Gate frames heard on the radio links to APRS-IS (needs a real passcode).</summary>
    public bool IGateEnabled { get; set; }

    /// <summary>Position beacon period on APRS-IS, in minutes (0 = only right after connecting).</summary>
    public int BeaconIntervalMinutes { get; set; }

    /// <summary>Range filter radius, in km, centred on <see cref="StationSettings.Latitude"/>/<see cref="StationSettings.Longitude"/> — no separate coordinates to configure here.</summary>
    public int FilterRadiusKm { get; set; } = 100;
}

/// <summary>
/// Persisted application settings. Grows over the coming phases (message/
/// track defaults); for now it carries what's wired up so far: the theme
/// choice, the last offline map path, the station identity used to build
/// outgoing frames, the two TNC channel configs, and the APRS-IS connection.
/// </summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool UseOfflineMap { get; set; }

    /// <summary>Remove a station from the map and list once not heard for this many minutes (0 = never). Our own station and objects stay.</summary>
    public int StationExpiryMinutes { get; set; }

    public string? OfflineMbTilesPath { get; set; }

    public StationSettings Station { get; set; } = new();

    public ChannelSettings ChannelA { get; set; } = new() { TxEnabled = true };

    public ChannelSettings ChannelB { get; set; } = new();

    public DigiSettings Digi { get; set; } = new();

    public AprsIsSettings AprsIs { get; set; } = new();

    public List<ObjectSettings> Objects { get; set; } = new();

    /// <summary>Stations (digipeaters) whose coverage is estimated over 24 h — Couverture tab.</summary>
    public List<string> CoverageStations { get; set; } = new();

    /// <summary>Couverture tab: also count the digis heard relaying (in <c>F1CNH-3*,F5ZBC-3*</c>, F5ZBC-3 heard F1CNH-3).</summary>
    public bool CoverageDigiLinks { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "APRS_Live",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Digi ??= new DigiSettings();
            settings.Digi.UpgradeLegacyChannels();
            settings.CoverageStations ??= new();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}

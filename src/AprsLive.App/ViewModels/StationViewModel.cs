using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.App.Icons;
using AprsLive.Core;
using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Links;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "Station" tab: the operator's own identity used to build every
/// outgoing frame (callsign, position, icon, digipeater path,
/// destination/tocall, and a beacon comment) — the fields a UIView32-style
/// station setup dialog has — plus sending the position beacon itself,
/// manually or on a fixed interval.
/// </summary>
public partial class StationViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly LinkViewModel _linkA;
    private readonly LinkViewModel _linkB;
    private readonly StationRegistry _stationRegistry;
    private readonly AprsIsViewModel _aprsIs;
    private System.Timers.Timer? _beaconTimer;
    private System.Timers.Timer? _aprsIsBeaconTimer;
    private int _beaconCount;
    private int _aprsIsBeaconCount;
    /// <summary>Re-evaluates the Best DX as entries age out of its validity window, even with no new traffic.</summary>
    private readonly DispatcherTimer _bestDxRefreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    /// <summary>Shortest APRS-IS beacon period accepted — anything faster is just load on the servers.</summary>
    private const int MinAprsIsBeaconMinutes = 5;
    private readonly Dictionary<object, CancellationTokenSource> _connectBeacons = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private static readonly TimeSpan ConnectBeaconDelay = TimeSpan.FromSeconds(5);
    /// <summary>Path of a packet a client originates on APRS-IS (the RF path means nothing there).</summary>
    private static readonly string[] AprsIsPath = ["TCPIP*"];

    public IReadOnlyList<SymbolChoice> AvailableSymbols { get; } =
        AprsSymbol.KnownSymbols.Select(SymbolChoice.For).ToList();

    public IReadOnlyList<StatusMode> AvailableStatusModes { get; } = [StatusMode.FreeText, StatusMode.BestDx];

    public bool IsStatusFreeText => SelectedStatusMode == StatusMode.FreeText;

    [ObservableProperty]
    private string _callsign;

    [ObservableProperty]
    private string _latitudeText;

    [ObservableProperty]
    private string _longitudeText;

    [ObservableProperty]
    private SymbolChoice _selectedSymbol;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _destination;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private int _intervalMinutes;

    [ObservableProperty]
    private StatusMode _selectedStatusMode;

    /// <summary>Free text sent as the APRS status report when <see cref="SelectedStatusMode"/> is <see cref="Settings.StatusMode.FreeText"/>.</summary>
    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private int _statusEveryNBeacons;

    [ObservableProperty]
    private int _bestDxWindowMinutes;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Live "Best DX" readout — the farthest station currently heard direct, shown above the map regardless of whether status reports are even enabled.</summary>
    [ObservableProperty]
    private string _bestDxDisplay = "Best DX : —";

    [ObservableProperty]
    private string? _bestDxTip;

    /// <summary>Short-lived result of a manual or on-connect beacon, shown over the map.</summary>
    [ObservableProperty]
    private string? _toast;

    public StationViewModel(AppSettings settings, LinkViewModel linkA, LinkViewModel linkB,
        AprsIsViewModel aprsIs, StationRegistry stationRegistry)
    {
        _settings = settings;
        _linkA = linkA;
        _linkB = linkB;
        _aprsIs = aprsIs;
        _stationRegistry = stationRegistry;
        _stationRegistry.StationUpdated += _ => Dispatcher.UIThread.Post(UpdateBestDxDisplay);
        _stationRegistry.StationsChanged += () => Dispatcher.UIThread.Post(UpdateBestDxDisplay);

        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };
        _linkA.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LinkViewModel.State)) OnRfLinkStateChanged(_linkA); };
        _linkB.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LinkViewModel.State)) OnRfLinkStateChanged(_linkB); };
        _aprsIs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AprsIsViewModel.State)) OnAprsIsStateChanged();
            else if (e.PropertyName == nameof(AprsIsViewModel.BeaconIntervalMinutes)) RestartAprsIsBeaconTimer();
        };
        _bestDxRefreshTimer.Tick += (_, _) => UpdateBestDxDisplay();
        _bestDxRefreshTimer.Start();
        var s = settings.Station;

        _callsign = s.Callsign;
        _latitudeText = s.Latitude is { } lat ? GeoCoord.Format(lat, GeoCoord.Notation.Decimal, isLat: true) : "";
        _longitudeText = s.Longitude is { } lon ? GeoCoord.Format(lon, GeoCoord.Notation.Decimal, isLat: false) : "";
        _path = string.Join(",", s.Path);
        _destination = s.Destination;
        _text = s.Text;
        _intervalMinutes = s.IntervalMinutes;
        _selectedStatusMode = s.StatusMode;
        _statusText = s.StatusText;
        _statusEveryNBeacons = s.StatusEveryNBeacons;
        _bestDxWindowMinutes = s.BestDxWindowMinutes;

        _selectedSymbol = AvailableSymbols.FirstOrDefault(
            c => c.Entry.Table == s.SymbolTable && c.Entry.Code == s.SymbolCode)
            ?? SymbolChoice.For(new AprsSymbol.Entry(s.SymbolTable, s.SymbolCode, "Personnalisé"));

        RestartBeaconTimer();
        UpdateBestDxDisplay();
    }

    private void UpdateBestDxDisplay()
    {
        var s = _settings.Station;
        if (s.Latitude is not { } lat || s.Longitude is not { } lon || string.IsNullOrWhiteSpace(s.Callsign))
        {
            BestDxDisplay = "Best DX : —";
            return;
        }

        var best = _stationRegistry.FindBestDx(lat, lon, s.Callsign, BestDxMaxAge);
        if (best is null || best.DirectLatitude is not { } bestLat || best.DirectLongitude is not { } bestLon
            || best.LastHeardDirect is not { } heard)
        {
            BestDxDisplay = "Best DX : —";
            BestDxTip = null;
            return;
        }

        double km = GeoMath.DistanceM(lat, lon, bestLat, bestLon) / 1000.0;
        var at = heard.LocalDateTime;
        // The time of the direct reception that makes it the Best DX (date too when not today).
        string when = at.Date == DateTime.Today ? $"{at:HH:mm}" : $"{at:dd/MM HH:mm}";
        BestDxDisplay = $"Best DX : {best.Callsign} ({km:0} km · {when})";
        BestDxTip = $"Reçue en direct le {at:dd/MM/yyyy} à {at:HH:mm:ss}, à {km:0.0} km";
    }

    private TimeSpan? BestDxMaxAge =>
        _settings.Station.BestDxWindowMinutes > 0 ? TimeSpan.FromMinutes(_settings.Station.BestDxWindowMinutes) : null;

    partial void OnSelectedStatusModeChanged(StatusMode value) => OnPropertyChanged(nameof(IsStatusFreeText));

    /// <summary>Raised once the station settings (callsign, position...) have been saved.</summary>
    public event Action? Saved;

    [RelayCommand]
    private void Save()
    {
        double? lat = null, lon = null;
        if (!string.IsNullOrWhiteSpace(LatitudeText))
        {
            if (!GeoCoord.TryParseLatitude(LatitudeText, out double v))
            {
                StatusMessage = "Latitude invalide.";
                return;
            }
            lat = v;
        }
        if (!string.IsNullOrWhiteSpace(LongitudeText))
        {
            if (!GeoCoord.TryParseLongitude(LongitudeText, out double v))
            {
                StatusMessage = "Longitude invalide.";
                return;
            }
            lon = v;
        }

        var s = _settings.Station;
        s.Callsign = Callsign.Trim().ToUpperInvariant();
        s.Latitude = lat;
        s.Longitude = lon;
        s.SymbolTable = SelectedSymbol.Entry.Table;
        s.SymbolCode = SelectedSymbol.Entry.Code;
        s.Path = Path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        s.Destination = Destination.Trim().ToUpperInvariant();
        s.Text = Text;
        s.IntervalMinutes = Math.Max(0, IntervalMinutes);
        s.StatusMode = SelectedStatusMode;
        s.StatusText = StatusText;
        s.StatusEveryNBeacons = Math.Max(0, StatusEveryNBeacons);
        s.BestDxWindowMinutes = Math.Max(0, BestDxWindowMinutes);

        _settings.Save();
        RestartBeaconTimer();
        UpdateBestDxDisplay();
        StatusMessage = "Enregistré.";
        Saved?.Invoke();
    }

    /// <summary>Called when the user right-clicks the map and chooses "Positionner ma station ici" — applies immediately.</summary>
    public void SetPosition(double latitude, double longitude)
    {
        LatitudeText = latitude.ToString("0.000000", CultureInfo.InvariantCulture);
        LongitudeText = longitude.ToString("0.000000", CultureInfo.InvariantCulture);

        _settings.Station.Latitude = latitude;
        _settings.Station.Longitude = longitude;
        _settings.Save();
        UpdateBestDxDisplay();
        StatusMessage = "Position mise à jour depuis la carte.";
        Saved?.Invoke();
    }

    [RelayCommand]
    private async Task SendNow() => await SendBeaconAsync([_linkA, _linkB], toAprsIs: false, notify: true);

    private void OnRfLinkStateChanged(LinkViewModel link) =>
        ScheduleConnectBeacon(link, link.State == FrameLinkState.Connected, async () =>
        {
            if (link.State == FrameLinkState.Connected && link.TxEnabled)
                await SendBeaconAsync([link], toAprsIs: false, notify: true);
        });

    private void OnAprsIsStateChanged()
    {
        ScheduleConnectBeacon(_aprsIs, _aprsIs.State == FrameLinkState.Connected, async () =>
        {
            if (_aprsIs.State == FrameLinkState.Connected && _aprsIs.IsVerified)
                await SendBeaconAsync([], toAprsIs: true, notify: true);
        });
        RestartAprsIsBeaconTimer();
    }

    /// <summary>Periodic APRS-IS beacon, counted from the connection (whose own beacon goes out 5 s in); runs only while connected.</summary>
    private void RestartAprsIsBeaconTimer()
    {
        _aprsIsBeaconTimer?.Stop();
        _aprsIsBeaconTimer?.Dispose();
        _aprsIsBeaconTimer = null;

        int minutes = _aprsIs.BeaconIntervalMinutes;
        if (minutes <= 0 || _aprsIs.State != FrameLinkState.Connected)
            return;

        _aprsIsBeaconTimer = new System.Timers.Timer(TimeSpan.FromMinutes(Math.Max(minutes, MinAprsIsBeaconMinutes)).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _aprsIsBeaconTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_aprsIs.State == FrameLinkState.Connected && _aprsIs.IsVerified)
                _ = SendBeaconAsync([], toAprsIs: true, notify: false);
        });
        _aprsIsBeaconTimer.Start();
    }

    /// <summary>One pending "beacon a few seconds after connecting" per link; a new state change replaces it, so a flapping link sends once.</summary>
    private void ScheduleConnectBeacon(object link, bool connected, Func<Task> send)
    {
        if (_connectBeacons.Remove(link, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }
        if (!connected)
            return;

        var cts = new CancellationTokenSource();
        _connectBeacons[link] = cts;
        _ = SendAfterDelayAsync(cts.Token, send);
    }

    private static async Task SendAfterDelayAsync(CancellationToken ct, Func<Task> send)
    {
        try { await Task.Delay(ConnectBeaconDelay, ct); }
        catch (OperationCanceledException) { return; }
        await send();
    }

    private void ShowToast(string? text)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private async Task<List<string>> SendAsync(byte[] rfFrame, byte[] aprsIsFrame, LinkViewModel[] links, bool toAprsIs)
    {
        var sentOn = new List<string>(await TxLinks.SendAsync(rfFrame, links));
        if (toAprsIs && await _aprsIs.SendRawFrameAsync(aprsIsFrame))
            sentOn.Add("APRS-IS");
        return sentOn;
    }

    private async Task SendBeaconAsync(LinkViewModel[] links, bool toAprsIs, bool notify)
    {
        var s = _settings.Station;
        if (string.IsNullOrWhiteSpace(s.Callsign))
        {
            StatusMessage = "Indicatif non défini.";
            if (notify) ShowToast(StatusMessage);
            return;
        }
        if (s.Latitude is not { } lat || s.Longitude is not { } lon)
        {
            StatusMessage = "Position GPS non définie.";
            if (notify) ShowToast(StatusMessage);
            return;
        }

        byte[] Build(IEnumerable<string> path) => Ax25Builder.BuildPositionReport(
            s.Callsign, s.Destination, path, lat, lon, s.SymbolTable, s.SymbolCode, s.Text);

        var frame = Build(s.Path);
        var sentOn = await SendAsync(frame, Build(AprsIsPath), links, toAprsIs);

        // Fed through the exact same Apply() as a received packet, so "my
        // station" is just one more entry in the registry — one map marker,
        // clickable with the same popup, instead of a second, separate,
        // non-clickable "MyStation" layer duplicating it (that's what a TNC
        // echoing our own TX back as RX used to produce: two markers for the
        // same callsign, only one of them hit-testable).
        if (Ax25Frame.TryParse(frame, out var parsedFrame) && parsedFrame is not null)
            _stationRegistry.Apply(AprsParser.Parse(parsedFrame), PacketOrigin.Local);

        StatusMessage = sentOn.Count > 0
            ? $"Position envoyée ({string.Join(", ", sentOn)}) à {DateTime.Now:HH:mm:ss}."
            : "Position non transmise : aucune liaison en émission n'est connectée.";
        if (notify)
            ShowToast(StatusMessage);

        // Every beacon (manual, automatic or on connect) counts toward the
        // status cadence, so "toutes les N balises" stays predictable
        // regardless of which one triggered the send. APRS-IS-only beacons
        // keep their own count, so their timer doesn't shift the RF cadence.
        int count = links.Length == 0 ? ++_aprsIsBeaconCount : ++_beaconCount;
        if (s.StatusEveryNBeacons > 0 && count % s.StatusEveryNBeacons == 0)
            await SendStatusAsync(s, lat, lon, links, toAprsIs);
    }

    /// <summary>Sends the APRS status report (type <c>&gt;</c>) — free text, or an auto-computed "Best DX" line — to the same targets as the beacon it follows.</summary>
    private async Task SendStatusAsync(StationSettings s, double ownLatitude, double ownLongitude,
        LinkViewModel[] links, bool toAprsIs)
    {
        string text = s.StatusMode == StatusMode.BestDx
            ? BuildBestDxStatusText(ownLatitude, ownLongitude, s.Callsign)
            : s.StatusText;

        if (string.IsNullOrWhiteSpace(text))
            return;

        await SendAsync(
            Ax25Builder.BuildStatusReport(s.Callsign, s.Destination, s.Path, text),
            Ax25Builder.BuildStatusReport(s.Callsign, s.Destination, AprsIsPath, text),
            links, toAprsIs);
    }

    private string BuildBestDxStatusText(double ownLatitude, double ownLongitude, string ownCallsign)
    {
        var best = _stationRegistry.FindBestDx(ownLatitude, ownLongitude, ownCallsign, BestDxMaxAge);
        if (best is null || best.DirectLatitude is not { } bestLat || best.DirectLongitude is not { } bestLon
            || best.LastHeardDirect is not { } heardAt)
            return "DX: aucune station en direct";

        double km = GeoMath.DistanceM(ownLatitude, ownLongitude, bestLat, bestLon) / 1000.0;
        double bearing = GeoMath.BearingDeg(ownLatitude, ownLongitude, bestLat, bestLon);
        string lat = GeoCoord.ToCompactDdm(bestLat, isLat: true);
        string lon = GeoCoord.ToCompactDdm(bestLon, isLat: false);
        string kmText = km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        return $"DX: {best.Callsign} {lat} {lon} {kmText}km {bearing:0}° {heardAt:HH:mm}";
    }

    private void RestartBeaconTimer()
    {
        _beaconTimer?.Stop();
        _beaconTimer?.Dispose();
        _beaconTimer = null;

        if (IntervalMinutes <= 0)
            return;

        _beaconTimer = new System.Timers.Timer(TimeSpan.FromMinutes(IntervalMinutes).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _beaconTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() =>
            _ = SendBeaconAsync([_linkA, _linkB], toAprsIs: false, notify: false));
        _beaconTimer.Start();
    }
}

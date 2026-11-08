using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.App.Map;
using AprsLive.App.Theme;
using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Links;
using AprsLive.Core.Messaging;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService = new();

    public MapController MapController { get; } = new();

    public StationViewModel Station { get; }

    public LinkViewModel LinkA { get; }

    public LinkViewModel LinkB { get; }

    public DigiViewModel Digi { get; }

    public StationsViewModel Stations { get; }

    public AprsIsViewModel AprsIs { get; }

    public MessagesViewModel Messages { get; }

    public ObjectsViewModel Objects { get; }

    public CoverageViewModel Coverage { get; }

    private readonly StationRegistry _stationRegistry = new();
    private readonly MessageCenter _messageCenter = new();

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _isOfflineMap;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _zoomLevel;

    /// <summary>UI-View32-style "last heard" line under the map: time, port (TNC1, TNC2) and route of the last frame received on RF or APRS-IS.</summary>
    [ObservableProperty]
    private string _lastHeardDisplay = "—";

    /// <summary>That frame, raw, in monitor (TNC2) format — path as heard, repeating stations marked <c>*</c>.</summary>
    [ObservableProperty]
    private string? _lastHeardFrame;

    /// <summary>Links that were connected at the last close and failed to reconnect on this start — shown in a popup.</summary>
    public ObservableCollection<string> StartupConnectErrors { get; } = new();

    [ObservableProperty]
    private bool _showStartupConnectErrors;

    private readonly List<LinkViewModel> _failedStartupLinks = new();
    private readonly Dictionary<LinkViewModel, string> _startupErrorLines = new();

    /// <summary>Paramètres → Général: drop stations not heard for this many minutes (0 = never).</summary>
    [ObservableProperty]
    private int _stationExpiryMinutes;

    private readonly DispatcherTimer _expiryTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    // Heard stations are kept across restarts: saved on close, and every few
    // minutes when something changed so a crash loses little.
    private readonly DispatcherTimer _stationSaveTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _stationsDirty;

    partial void OnStationExpiryMinutesChanged(int value)
    {
        _settings.StationExpiryMinutes = Math.Max(0, value);
        _settings.Save();
        RemoveExpiredStations();
    }

    private void RemoveExpiredStations()
    {
        if (_settings.StationExpiryMinutes > 0)
            _stationRegistry.RemoveNotHeardSince(TimeSpan.FromMinutes(_settings.StationExpiryMinutes));
    }

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        LinkA = new LinkViewModel("Liaison A", 1, _settings, _settings.ChannelA);
        LinkB = new LinkViewModel("Liaison B", 2, _settings, _settings.ChannelB);
        AprsIs = new AprsIsViewModel(_settings);
        Station = new StationViewModel(_settings, LinkA, LinkB, AprsIs, _stationRegistry);
        Stations = new StationsViewModel(_stationRegistry);
        Digi = new DigiViewModel(_settings, LinkA, LinkB);
        Messages = new MessagesViewModel(_settings, LinkA, LinkB, AprsIs, _messageCenter);
        Objects = new ObjectsViewModel(_settings, LinkA, LinkB, _stationRegistry);
        Coverage = new CoverageViewModel(_settings, _stationRegistry);
        MapController.ZoomLevelChanged += z => Dispatcher.UIThread.Post(() => ZoomLevel = z);

        _stationRegistry.StationUpdated += _ => MapController.UpdateHeardStations(_stationRegistry.Stations);
        _stationRegistry.StationsChanged += () => MapController.UpdateHeardStations(_stationRegistry.Stations);
        _stationExpiryMinutes = _settings.StationExpiryMinutes;
        _expiryTimer.Tick += (_, _) => RemoveExpiredStations();
        _expiryTimer.Start();
        RestoreSavedStations();
        _stationRegistry.StationUpdated += _ => _stationsDirty = true;
        _stationRegistry.StationsChanged += () => _stationsDirty = true;
        _stationSaveTimer.Tick += (_, _) =>
        {
            if (_stationsDirty)
                SaveStations();
            SaveCoverage();
        };
        _stationSaveTimer.Start();
        foreach (var link in new[] { LinkA, LinkB })
            link.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LinkViewModel.State) && link.State == FrameLinkState.Connected)
                    OnLinkConnected(link);
            };
        LinkA.RawFrameReceived += ax25 => Dispatcher.UIThread.Post(() => OnRawFrameReceived(ax25, PacketOrigin.Rf, channel: 1));
        LinkB.RawFrameReceived += ax25 => Dispatcher.UIThread.Post(() => OnRawFrameReceived(ax25, PacketOrigin.Rf, channel: 2));
        AprsIs.RawFrameReceived += ax25 => Dispatcher.UIThread.Post(() => OnRawFrameReceived(ax25, PacketOrigin.Internet));

        // Assigning through the generated setter also runs OnSelectedThemeChanged,
        // which is what actually applies the theme on startup.
        SelectedTheme = _settings.Theme;

        if (_settings.UseOfflineMap && !string.IsNullOrWhiteSpace(_settings.OfflineMbTilesPath))
            LoadOfflineMap(_settings.OfflineMbTilesPath);

        if (_settings.Station.Latitude is { } lat && _settings.Station.Longitude is { } lon)
            MapController.CenterOn(lat, lon, 8);

        _ = AprsIs.AutoConnectIfConfiguredAsync();
        _ = ReconnectLinksAsync([LinkA, LinkB]);
    }

    private async Task ReconnectLinksAsync(IEnumerable<LinkViewModel> links)
    {
        var results = await Task.WhenAll(links
            .Where(l => l.ReconnectOnStartup)
            .Select(async l => (Link: l, Error: await l.TryConnectAsync())));

        foreach (var (link, error) in results.Where(r => r.Error is not null))
        {
            if (link.State == FrameLinkState.Connected)
                continue;   // came back on its own in the meantime
            string line = $"{link.Name} ({link.ConnectionSummary}) : {error}";
            _failedStartupLinks.Add(link);
            _startupErrorLines[link] = line;
            StartupConnectErrors.Add(line);
        }
        ShowStartupConnectErrors = StartupConnectErrors.Count > 0;
    }

    /// <summary>A link from the startup popup that reconnected by itself: its line goes, and the popup once empty.</summary>
    private void OnLinkConnected(LinkViewModel link)
    {
        if (!_failedStartupLinks.Remove(link))
            return;
        if (_startupErrorLines.Remove(link, out var line))
            StartupConnectErrors.Remove(line);
        ShowStartupConnectErrors = StartupConnectErrors.Count > 0;
    }

    [RelayCommand]
    private async Task RetryStartupConnections()
    {
        var links = _failedStartupLinks.ToList();
        _failedStartupLinks.Clear();
        _startupErrorLines.Clear();
        StartupConnectErrors.Clear();
        ShowStartupConnectErrors = false;
        await ReconnectLinksAsync(links);
    }

    [RelayCommand]
    private void DismissStartupConnectErrors()
    {
        _failedStartupLinks.Clear();
        _startupErrorLines.Clear();
        StartupConnectErrors.Clear();
        ShowStartupConnectErrors = false;
    }

    /// <summary>
    /// Brings back the stations saved at the last close, then applies the
    /// expiry delay to them. Our own entries come back only for our current
    /// callsign and the objects still defined, so a deleted object (ours never
    /// expire) does not linger forever.
    /// </summary>
    private void RestoreSavedStations()
    {
        var ours = new HashSet<string>(_settings.Objects.Select(o => o.Name), StringComparer.OrdinalIgnoreCase)
        {
            _settings.Station.Callsign,
        };
        _stationRegistry.Restore(StationStore.Load().Where(s => !s.IsLocal || ours.Contains(s.Callsign)));
        RemoveExpiredStations();
    }

    private void SaveStations()
    {
        try
        {
            StationStore.Save(_stationRegistry.Stations);
            _stationsDirty = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stations non enregistrées : {ex.Message}";
        }
    }

    private void SaveCoverage()
    {
        try
        {
            Coverage.Save();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couverture non enregistrée : {ex.Message}";
        }
    }

    /// <summary>Our station's position (Station tab), end point of the drawn reception paths.</summary>
    public (double Lat, double Lon)? OwnPosition =>
        _settings.Station.Latitude is { } lat && _settings.Station.Longitude is { } lon ? (lat, lon) : null;

    /// <summary>Called as the window closes: remembers which links were connected, for the next start.</summary>
    public void SaveSessionState()
    {
        LinkA.RememberStateForNextStart();
        LinkB.RememberStateForNextStart();
        _settings.Save();
        SaveStations();
        SaveCoverage();
    }

    /// <param name="channel">1 for Liaison A (TNC1), 2 for Liaison B (TNC2), 0 for APRS-IS.</param>
    private void OnRawFrameReceived(byte[] ax25, PacketOrigin origin, int channel = 0)
    {
        if (!Ax25Frame.TryParse(ax25, out var frame) || frame is null)
            return;
        if (origin == PacketOrigin.Rf && frame.HasInternetPath)
            origin = PacketOrigin.Internet;   // APRS-IS traffic bridged onto a KISS port

        string route = origin == PacketOrigin.Internet ? "Internet"
            : frame.Path.Any(a => a.HasBeenRepeated) ? "via digi" : "direct";
        string port = channel is 1 or 2 ? $"TNC{channel} · " : "";
        LastHeardDisplay = $"{DateTime.Now:HH:mm:ss} · {port}{route}";
        // One line: control characters (a trailing CR...) shown as dots.
        LastHeardFrame = new string(frame.ToTnc2().Select(c => char.IsControl(c) ? '·' : c).ToArray());

        if (origin == PacketOrigin.Rf)
            AprsIs.GateFromRf(ax25);

        var packet = AprsParser.Parse(frame);
        _stationRegistry.Apply(packet, origin, channel);
        Coverage.Record(packet);
        _messageCenter.ApplyIncoming(packet, _settings.Station.Callsign);
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _themeService.Apply(value);
        _settings.Theme = value;
        _settings.Save();
    }

    [RelayCommand]
    private void ZoomIn() => MapController.ZoomIn();

    [RelayCommand]
    private void ZoomOut() => MapController.ZoomOut();

    [RelayCommand]
    private void UseOnlineMap()
    {
        MapController.UseOnlineOsm();
        IsOfflineMap = false;
        StatusMessage = null;
        _settings.UseOfflineMap = false;
        _settings.Save();
    }

    public void LoadOfflineMap(string path)
    {
        try
        {
            MapController.UseOfflineMbTiles(path);
            MapController.RecenterOnOfflineMap();
            IsOfflineMap = true;
            StatusMessage = null;
            _settings.UseOfflineMap = true;
            _settings.OfflineMbTilesPath = path;
            _settings.Save();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}

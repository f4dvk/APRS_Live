using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Ax25;
using AprsLive.Core.Links;
using AprsLive.Core.Settings;
using AprsLive.Core.Tnc;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "APRS-IS" settings tab: a receive-oriented internet feed of
/// APRS traffic, independent from the two RF/audio TNC channels. Login uses
/// the operator callsign already configured in the Station tab, and the
/// range filter is always centred on that same station's position — no
/// separate coordinates to enter here.
/// </summary>
public partial class AprsIsViewModel : ViewModelBase
{
    private readonly AppSettings _appSettings;
    private readonly AprsIsSettings _settings;
    private AprsIsClient? _link;

    public IReadOnlyList<string> AvailableServers { get; } = AprsIsServers.Known;

    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Raised on every received AX.25 frame (raw bytes), converted from the APRS-IS TNC2 text line.</summary>
    public event Action<byte[]>? RawFrameReceived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerified), nameof(ShowIGatePasscodeWarning))]
    private string _passcode;

    /// <summary>A real passcode (not "-1"): the server accepts what we send instead of silently dropping it.</summary>
    public bool IsVerified => int.TryParse(Passcode, out int code) && code >= 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIGatePasscodeWarning))]
    private bool _iGateEnabled;

    public bool ShowIGatePasscodeWarning => IGateEnabled && !IsVerified;

    private readonly IGate _igate = new();

    /// <summary>Position beacon period on APRS-IS, minutes (0 = only right after connecting). Driven by StationViewModel.</summary>
    [ObservableProperty]
    private int _beaconIntervalMinutes;

    partial void OnBeaconIntervalMinutesChanged(int value) => SaveSettings();

    /// <summary>Flashes on each line received from the server — the feed is alive.</summary>
    [ObservableProperty]
    private bool _rxActive;

    private readonly DispatcherTimer _rxHangTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>Connection indicator colour above the map: grey off, orange connecting, green connected, red failed.</summary>
    public IBrush StateBrush => State switch
    {
        FrameLinkState.Connected => ConnectedBrush,
        FrameLinkState.Connecting => ConnectingBrush,
        _ when NextReconnectAt is not null => ConnectingBrush,
        FrameLinkState.Faulted => FaultedBrush,
        _ => OffBrush,
    };

    public string StateTip => State switch
    {
        FrameLinkState.Connected => $"APRS-IS connecté ({SelectedServer}:{Port}){(IsVerified ? "" : " — lecture seule (passcode -1)")}",
        FrameLinkState.Connecting => "APRS-IS : connexion en cours…",
        _ when NextReconnectAt is { } at => $"APRS-IS : pas de connexion, nouvel essai à {at:HH:mm:ss} (tentative {_reconnectAttempt})",
        FrameLinkState.Faulted => "APRS-IS : échec de connexion (voir Paramètres → APRS-IS)",
        _ => "APRS-IS déconnecté",
    };

    // Automatic reconnection: on as long as the user asked to be connected
    // (Connect button or connect-at-startup) and has not clicked Disconnect.
    private bool _wantConnected;
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private DateTime? _connectedAt;

    /// <summary>When the next automatic attempt runs; null when none is pending.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateBrush), nameof(StateTip))]
    private DateTime? _nextReconnectAt;

    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#FF2ECC40"));
    private static readonly IBrush ConnectingBrush = new SolidColorBrush(Color.Parse("#FFFF9500"));
    private static readonly IBrush FaultedBrush = new SolidColorBrush(Color.Parse("#FFE74C3C"));
    private static readonly IBrush OffBrush = new SolidColorBrush(Color.Parse("#FF555555"));

    partial void OnStateChanged(FrameLinkState value)
    {
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateTip));
    }

    [ObservableProperty]
    private string _selectedServer;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private bool _connectOnStartup;

    [ObservableProperty]
    private int _filterRadiusKm;

    /// <summary>The filter centre, read-only display — always the Station tab's position.</summary>
    public string FilterCentreText =>
        _appSettings.Station.Latitude is { } lat && _appSettings.Station.Longitude is { } lon
            ? $"{lat:0.000}, {lon:0.000} (position de la station)"
            : "Position de la station non définie (onglet Station)";

    [ObservableProperty]
    private FrameLinkState _state = FrameLinkState.Disconnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public AprsIsViewModel(AppSettings appSettings)
    {
        _appSettings = appSettings;
        _settings = appSettings.AprsIs;

        _passcode = _settings.Passcode;
        _selectedServer = _settings.Server;
        _port = _settings.Port;
        _connectOnStartup = _settings.ConnectOnStartup;
        _filterRadiusKm = _settings.FilterRadiusKm;
        _iGateEnabled = _settings.IGateEnabled;
        _beaconIntervalMinutes = _settings.BeaconIntervalMinutes;
        _rxHangTimer.Tick += (_, _) => { _rxHangTimer.Stop(); RxActive = false; };
    }

    // The tab has no "Enregistrer" button: every field is saved as soon as it changes.
    partial void OnIGateEnabledChanged(bool value) => SaveSettings();
    partial void OnPasscodeChanged(string value) => SaveSettings();
    partial void OnSelectedServerChanged(string value) => SaveSettings();
    partial void OnPortChanged(int value) => SaveSettings();
    partial void OnConnectOnStartupChanged(bool value) => SaveSettings();
    partial void OnFilterRadiusKmChanged(int value) => SaveSettings();

    /// <summary>A frame heard on a radio link: relayed to APRS-IS when the IGate is on, unless the gating rules exclude it (see <see cref="IGate"/>).</summary>
    public void GateFromRf(byte[] ax25)
    {
        var link = _link;
        if (!IGateEnabled || !IsVerified || link is null || State != FrameLinkState.Connected)
            return;
        string call = _appSettings.Station.Callsign.Trim();
        if (call.Length == 0 || !Ax25Frame.TryParse(ax25, out var frame) || frame is null)
            return;

        string? line = _igate.TryGate(frame, call, out string? reason);
        if (line is null)
        {
            if (reason != IGate.DuplicateReason)
                AppendLog($"IGate, non relayé ({reason}) : {frame.ToTnc2()}");
            return;
        }
        _ = SendGatedLineAsync(link, line);
    }

    private async Task SendGatedLineAsync(AprsIsClient link, string line)
    {
        try
        {
            await link.SendLineAsync(line);
            AppendLog($"IGate → {line}");
        }
        catch (Exception ex)
        {
            AppendLog($"Erreur IGate : {ex.Message}");
        }
    }

    [RelayCommand]
    private void ComputePasscode()
    {
        var callsign = _appSettings.Station.Callsign;
        if (string.IsNullOrWhiteSpace(callsign))
        {
            StatusMessage = "Indicatif non défini (onglet Station).";
            return;
        }
        Passcode = AprsIsPasscode.Compute(callsign).ToString();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        _wantConnected = true;
        _reconnectAttempt = 0;
        CancelPendingReconnect();
        await ConnectOnceAsync();
    }

    /// <summary>One connection attempt (every known server in turn); schedules the next one if it fails.</summary>
    private async Task ConnectOnceAsync()
    {
        OnPropertyChanged(nameof(FilterCentreText));

        var callsign = _appSettings.Station.Callsign;
        if (string.IsNullOrWhiteSpace(callsign))
        {
            StatusMessage = "Indicatif non défini (onglet Station).";
            _wantConnected = false;
            return;
        }
        if (_appSettings.Station.Latitude is not { } lat || _appSettings.Station.Longitude is not { } lon)
        {
            StatusMessage = "Position de la station non définie (onglet Station).";
            _wantConnected = false;
            return;
        }

        IsBusy = true;
        AprsIsClient? client = null;
        try
        {
            await DropLinkAsync();
            string filter = AprsIsClient.BuildRangeFilter(lat, lon, FilterRadiusKm);
            client = new AprsIsClient(callsign, Passcode, SelectedServer, Port, filter);
            AttachLink(client);
            SaveSettings();
            await client.ConnectAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Erreur : {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }

        if (_wantConnected && client?.State is not (FrameLinkState.Connected or FrameLinkState.Connecting))
            ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (_reconnectCts is not null)
            return;   // one pending attempt at a time
        _reconnectAttempt++;
        var delay = ReconnectSchedule.DelayBefore(_reconnectAttempt);
        NextReconnectAt = DateTime.Now + delay;
        StatusMessage = $"Pas de connexion : nouvel essai automatique à {NextReconnectAt:HH:mm:ss} (tentative {_reconnectAttempt}).";
        AppendLog($"Nouvel essai de connexion dans {delay.TotalSeconds:0} s (tentative {_reconnectAttempt}).");

        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        _ = RunReconnectAsync(delay, cts);
        NotifyCommands();
    }

    private async Task RunReconnectAsync(TimeSpan delay, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(delay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (_reconnectCts != cts)
            return;
        _reconnectCts = null;
        cts.Dispose();
        NextReconnectAt = null;
        if (_wantConnected)
            await ConnectOnceAsync();
    }

    private void CancelPendingReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        NextReconnectAt = null;
    }

    private void AttachLink(AprsIsClient client)
    {
        _link = client;
        client.StateChanged += s => Dispatcher.UIThread.Post(() => OnLinkStateChanged(client, s));
        client.Log += AppendLog;
        client.FrameReceived += OnFrameReceived;
    }

    /// <summary>Closes the current link; its late state events are then ignored (see <see cref="OnLinkStateChanged"/>).</summary>
    private async Task DropLinkAsync()
    {
        var old = _link;
        if (old is null)
            return;
        _link = null;
        old.Log -= AppendLog;
        old.FrameReceived -= OnFrameReceived;
        await old.DisposeAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task Disconnect()
    {
        _wantConnected = false;
        CancelPendingReconnect();
        StatusMessage = null;
        IsBusy = true;
        try
        {
            await DropLinkAsync();
        }
        finally
        {
            State = FrameLinkState.Disconnected;
            _connectedAt = null;
            IsBusy = false;
            NotifyCommands();
        }
    }

    private bool CanConnect() => State is FrameLinkState.Disconnected or FrameLinkState.Faulted && !IsBusy;

    private bool CanDisconnect() => _link is not null || _reconnectCts is not null;

    private void NotifyCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sends a raw AX.25 frame over APRS-IS, if connected (logged either way). Returns whether it was actually sent.</summary>
    public async Task<bool> SendRawFrameAsync(byte[] ax25)
    {
        if (_link is null || State != FrameLinkState.Connected)
        {
            AppendLog("Envoi ignoré : APRS-IS non connecté.");
            return false;
        }

        try
        {
            await _link.SendFrameAsync(ax25);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"Erreur d'émission : {ex.Message}");
            return false;
        }
    }

    /// <summary>Called once at startup if configured — reuses the normal Connect() path.</summary>
    public Task AutoConnectIfConfiguredAsync() => _settings.ConnectOnStartup ? Connect() : Task.CompletedTask;

    private void OnLinkStateChanged(AprsIsClient client, FrameLinkState s)
    {
        if (client != _link)
            return;   // a link already replaced or closed
        State = s;
        if (s == FrameLinkState.Connected)
        {
            _connectedAt = DateTime.Now;
            NextReconnectAt = null;
            StatusMessage = null;
        }
        else if (s is FrameLinkState.Disconnected or FrameLinkState.Faulted && _wantConnected)
        {
            // Back to quick retries only after a connection that held; one that
            // keeps dropping right away carries on with the longer delays.
            if (_connectedAt is { } since && DateTime.Now - since > TimeSpan.FromMinutes(1))
                _reconnectAttempt = 0;
            _connectedAt = null;
            if (!IsBusy)
                ScheduleReconnect();   // otherwise ConnectOnceAsync schedules it when it returns
        }
        NotifyCommands();
    }

    private void OnFrameReceived(byte[] ax25)
    {
        RawFrameReceived?.Invoke(ax25);
        Dispatcher.UIThread.Post(() =>
        {
            RxActive = true;
            _rxHangTimer.Stop();
            _rxHangTimer.Start();
        });
        AppendLog($"{ax25.Length} octets reçus.");
    }

    private void AppendLog(string line) =>
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });

    private void SaveSettings()
    {
        _settings.Passcode = Passcode;
        if (!string.IsNullOrWhiteSpace(SelectedServer))   // the ComboBox pushes null for a server not in its list
            _settings.Server = SelectedServer;
        _settings.Port = Port;
        _settings.ConnectOnStartup = ConnectOnStartup;
        _settings.FilterRadiusKm = FilterRadiusKm;
        _settings.IGateEnabled = IGateEnabled;
        _settings.BeaconIntervalMinutes = Math.Max(0, BeaconIntervalMinutes);
        _appSettings.Save();
    }
}

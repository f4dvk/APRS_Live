using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.App.Audio;
using AprsLive.Audio;
using AprsLive.Audio.Ptt;
using AprsLive.Core.Ax25;
using AprsLive.Core.Kiss;
using AprsLive.Core.Links;
using AprsLive.Core.Settings;
using AprsLive.Core.Tnc;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs one panel of the "Liaisons" tab: configures and connects a single
/// TNC channel (KISS serial, KISS TCP, or the audio AFSK1200 modem), and
/// shows a live log of what it sends/receives.
/// </summary>
public partial class LinkViewModel : ViewModelBase
{
    private static readonly TimeSpan DcdHangTime = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SpectrumUpdateInterval = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan CdPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly AppSettings _appSettings;
    private readonly ChannelSettings _settings;
    private IFrameLink? _link;
    private IPttKeyer? _pttKeyer;
    private AudioModemLink? _audioLink;
    private SerialKissLink? _serialLink;
    private readonly List<float> _spectrumSampleBuffer = new(SpectrumAnalyzer.FftSize);
    private DateTime _lastSpectrumUpdate = DateTime.MinValue;
    private System.Timers.Timer? _dcdHangTimer;
    private System.Timers.Timer? _cdPollTimer;

    public string Name { get; }

    /// <summary>Static layout shared by the spectrum view — same for every link, exposed per-instance just so XAML can bind without <c>x:Static</c>.</summary>
    public int SpectrumBarCount => SpectrumAnalyzer.BarCount;
    public double SpectrumBarWidth => SpectrumAnalyzer.BarWidth;
    public double SpectrumBarMaxHeight => SpectrumAnalyzer.BarMaxHeight;
    public double SpectrumTotalWidth => SpectrumAnalyzer.TotalWidth;
    public double MarkMarkerX => SpectrumAnalyzer.MarkMarkerX;
    public double SpaceMarkerX => SpectrumAnalyzer.SpaceMarkerX;

    [ObservableProperty]
    private IReadOnlyList<double> _spectrumBars = new double[SpectrumAnalyzer.BarCount];

    /// <summary>
    /// Channel activity indicator: on during a short hang time after each
    /// frame actually seen (<see cref="PulseDcd"/>, every link kind) and, for
    /// a serial KISS link, also while the hardware CD line is up
    /// (<see cref="StartCdPolling"/>) — so a TNC that drives CD shows its real
    /// carrier, and one that doesn't (or a TCP KISS link) still blinks on traffic.
    /// </summary>
    [ObservableProperty]
    private bool _carrierActive;

    private bool _framePulse;
    private bool _cdLine;

    private void UpdateCarrier() => CarrierActive = _framePulse || _cdLine;

    /// <summary>1 or 2, as in TNC1/TNC2 — tells the two indicators apart above the map.</summary>
    public int Number { get; }

    public string CarrierLabel => $"{(IsAudioKind ? "DCD" : "CD")} {Number}";

    public bool ShowMeter => State == FrameLinkState.Connected || IsReconnecting;

    // Automatic reconnection (KISS serial/TCP only): on as long as the user
    // asked for this link (Connect, startup reconnect) and has not clicked
    // Disconnect — a TNC unplugged, powered off or rebooted comes back alone.
    private bool _wantConnected;
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private DateTime? _connectedAt;

    private bool SupportsAutoReconnect => Kind is ChannelKind.KissSerial or ChannelKind.KissTcp;

    /// <summary>When the next automatic attempt runs; null when none is pending.</summary>
    [ObservableProperty]
    private DateTime? _nextReconnectAt;

    /// <summary>Lost and waiting to come back: the indicator above the map stays, with an orange dot.</summary>
    public bool IsReconnecting => _wantConnected && SupportsAutoReconnect && State != FrameLinkState.Connected;

    public string ReconnectTip => NextReconnectAt is { } at
        ? $"{Name} ({ConnectionSummary}) : pas de connexion, nouvel essai à {at:HH:mm:ss} (tentative {_reconnectAttempt})"
        : $"{Name} ({ConnectionSummary}) : connexion en cours…";

    /// <summary>Shown next to the state in Paramètres → Radio while retrying.</summary>
    [ObservableProperty]
    private string? _reconnectStatus;

    partial void OnNextReconnectAtChanged(DateTime? value) => RefreshReconnectView();

    private void RefreshReconnectView()
    {
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(ShowMeter));
        OnPropertyChanged(nameof(ReconnectTip));
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Whether the spectrum bars specifically should show — audio only, there's no raw signal to plot for a hardware TNC.</summary>
    public bool ShowSpectrum => IsAudioKind && State == FrameLinkState.Connected;

    public IReadOnlyList<ChannelKind> AvailableKinds { get; } =
        [ChannelKind.KissSerial, ChannelKind.KissTcp, ChannelKind.AudioAfsk];

    public IReadOnlyList<PttMode> AvailablePttModes { get; } = [PttMode.None, PttMode.Rts, PttMode.Dtr];

    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>Raised on every received AX.25 frame (raw bytes) — used by the digipeater to relay across channels.</summary>
    public event Action<byte[]>? RawFrameReceived;

    [ObservableProperty]
    private ChannelKind _kind;

    [ObservableProperty]
    private string _serialPort;

    [ObservableProperty]
    private IReadOnlyList<string> _availableSerialPorts = SerialKissLink.AvailablePorts();

    [ObservableProperty]
    private int _baudRate;

    public IReadOnlyList<TncModel> AvailableTncModels => TncModels.All;

    /// <summary>The kind of serial TNC: picking one fills in its commands, which stay editable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TncModelNotes))]
    private TncModel _selectedTncModel;

    public string TncModelNotes => SelectedTncModel.Notes;

    /// <summary>Sent once the serial port is open, to put the TNC into KISS mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InitCommandsErrors), nameof(HasInitCommandsErrors))]
    private string _kissInitCommands;

    /// <summary>Sent on disconnect and when the application closes, to take the TNC back out of KISS.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExitCommandsErrors), nameof(HasExitCommandsErrors))]
    private string _kissExitCommands;

    public string InitCommandsErrors => string.Join("\n", TncScript.Parse(KissInitCommands).Errors);
    public bool HasInitCommandsErrors => InitCommandsErrors.Length > 0;
    public string ExitCommandsErrors => string.Join("\n", TncScript.Parse(KissExitCommands).Errors);
    public bool HasExitCommandsErrors => ExitCommandsErrors.Length > 0;

    /// <summary>Connected (or connecting) to a serial TNC that has commands to send before the application closes.</summary>
    public bool NeedsExitCommands => _serialLink is not null && !TncScript.Parse(KissExitCommands).IsEmpty;

    [ObservableProperty]
    private string _tcpHost;

    [ObservableProperty]
    private int _tcpPort;

    /// <summary>Whether this channel transmits — station beacons, messages and objects go out every channel with this on, and it's what lets the digipeater use this channel at all.</summary>
    [ObservableProperty]
    private bool _txEnabled;

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> _availableAudioInputDevices = [];

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> _availableAudioOutputDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioInputDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioOutputDevice;

    [ObservableProperty]
    private PttMode _selectedPttMode;

    [ObservableProperty]
    private string _pttPort;

    [ObservableProperty]
    private FrameLinkState _state = FrameLinkState.Disconnected;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsAudioKind => Kind == ChannelKind.AudioAfsk;
    public bool IsSerialKind => Kind == ChannelKind.KissSerial;
    public bool IsTcpKind => Kind == ChannelKind.KissTcp;

    public LinkViewModel(string name, int number, AppSettings appSettings, ChannelSettings settings)
    {
        Name = name;
        Number = number;
        _appSettings = appSettings;
        _settings = settings;

        _kind = settings.Kind;
        _serialPort = settings.SerialPort;
        _baudRate = settings.BaudRate;
        _selectedTncModel = TncModels.Find(settings.TncModel);
        _kissInitCommands = settings.KissInitCommands ?? "";
        _kissExitCommands = settings.KissExitCommands ?? "";
        _tcpHost = settings.TcpHost;
        _tcpPort = settings.TcpPort;
        _txEnabled = settings.TxEnabled;
        _selectedPttMode = settings.PttMode;
        _pttPort = settings.PttPort;

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            RefreshAudioDevices(settings);
    }

    private void RefreshAudioDevices(ChannelSettings settings)
    {
        try
        {
            using var probeIn = AudioBackend.CreateInput();
            AvailableAudioInputDevices = probeIn.Enumerate();
            using var probeOut = AudioBackend.CreateOutput();
            AvailableAudioOutputDevices = probeOut.Enumerate();

            SelectedAudioInputDevice = PickDevice(AvailableAudioInputDevices, settings.AudioInputDeviceId);
            SelectedAudioOutputDevice = PickDevice(AvailableAudioOutputDevices, settings.AudioOutputDeviceId);
        }
        catch
        {
            // no audio subsystem available — Audio stays selectable but Connect() will report the error
        }
    }

    private static AudioDeviceInfo? PickDevice(IReadOnlyList<AudioDeviceInfo> devices, string? preferredId)
    {
        foreach (var d in devices)
            if (d.Id == preferredId) return d;
        return devices.Count > 0 ? devices[0] : null;
    }

    partial void OnKindChanged(ChannelKind value)
    {
        OnPropertyChanged(nameof(IsAudioKind));
        OnPropertyChanged(nameof(IsSerialKind));
        OnPropertyChanged(nameof(IsTcpKind));
        OnPropertyChanged(nameof(ShowMeter));
        OnPropertyChanged(nameof(ShowSpectrum));
        OnPropertyChanged(nameof(CarrierLabel));
    }

    partial void OnStateChanged(FrameLinkState value)
    {
        OnPropertyChanged(nameof(ShowSpectrum));
        RefreshReconnectView();
    }

    // Saved immediately (not just on Connect) since toggling it is also how
    // the digipeater picks up or drops this channel, live.
    partial void OnTxEnabledChanged(bool value)
    {
        _settings.TxEnabled = value;
        _appSettings.Save();
    }

    [RelayCommand]
    private void RefreshSerialPorts() => AvailableSerialPorts = SerialKissLink.AvailablePorts();

    // Picked by the user: its commands replace the current ones, except "Autre" which keeps them.
    partial void OnSelectedTncModelChanged(TncModel value)
    {
        if (value.Id != TncModels.CustomId)
            LoadTncModelCommands(value);
    }

    /// <summary>Puts back the selected model's commands, after edits.</summary>
    [RelayCommand]
    private void RestoreTncModelCommands() => LoadTncModelCommands(SelectedTncModel);

    private void LoadTncModelCommands(TncModel model)
    {
        if (model.Id == TncModels.CustomId)
            return;
        KissInitCommands = model.InitCommands;
        KissExitCommands = model.ExitCommands;
        if (model.BaudRate is { } baud)
            BaudRate = baud;
    }

    [RelayCommand]
    private void RefreshAudioDevices() => RefreshAudioDevices(_settings);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task Connect() => TryConnectAsync();

    public bool ReconnectOnStartup => _settings.ReconnectOnStartup;

    /// <summary>What this link connects to, for messages: <c>KISS série /dev/ttyACM0</c>, <c>KISS TCP 192.168.1.20:8001</c>…</summary>
    public string ConnectionSummary => Kind switch
    {
        ChannelKind.KissTcp => $"KISS TCP {TcpHost}:{TcpPort}",
        ChannelKind.AudioAfsk => "audio",
        _ => $"KISS série {SerialPort}",
    };

    /// <summary>
    /// Records whether this link is connected right now — or lost and being
    /// retried — so the next start reconnects it (saved by the caller).
    /// </summary>
    public void RememberStateForNextStart() =>
        _settings.ReconnectOnStartup = State == FrameLinkState.Connected || _wantConnected;

    /// <summary>
    /// Connects with the current settings; returns <c>null</c> on success, else
    /// the error message. For a KISS link a failure keeps retrying in the
    /// background until it connects or the user clicks Disconnect.
    /// </summary>
    public async Task<string?> TryConnectAsync()
    {
        if (!CanConnect())
            return null;

        _wantConnected = true;
        _reconnectAttempt = 0;
        CancelPendingReconnect();
        RefreshReconnectView();
        return await ConnectOnceAsync();
    }

    private async Task<string?> ConnectOnceAsync()
    {
        IsBusy = true;
        IFrameLink? link = null;
        string? error = null;
        try
        {
            await DropLinkAsync();
            IFrameLink created = Kind switch
            {
                ChannelKind.KissTcp => new TcpKissLink(TcpHost, TcpPort),
                ChannelKind.AudioAfsk => BuildAudioModemLink(),
                _ => _serialLink = new SerialKissLink(SerialPortToOpen, BaudRate,
                    TncScript.Parse(KissInitCommands), TncScript.Parse(KissExitCommands)),
            };
            link = _link = created;

            created.StateChanged += s => Dispatcher.UIThread.Post(() => OnLinkStateChanged(created, s));
            created.Log += AppendLog;
            created.FrameReceived += OnFrameReceived;

            if (_serialLink is not null)
                StartCdPolling();

            SaveSettings();
            await created.ConnectAsync();
            _settings.ReconnectOnStartup = true;
            _appSettings.Save();
        }
        catch (Exception ex)
        {
            AppendLog($"Erreur : {ex.Message}");
            error = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        }

        if (_wantConnected && link?.State is not (FrameLinkState.Connected or FrameLinkState.Connecting))
            ScheduleReconnect();
        return error;
    }

    /// <summary>
    /// The port picked in the list — or the saved one if the list lost it
    /// (refreshed while the TNC was unplugged: the ComboBox then pushes null).
    /// </summary>
    private string SerialPortToOpen => string.IsNullOrWhiteSpace(SerialPort) ? _settings.SerialPort : SerialPort;

    private void ScheduleReconnect()
    {
        if (_reconnectCts is not null || !SupportsAutoReconnect)
            return;   // one pending attempt at a time; never for the audio modem
        _reconnectAttempt++;
        var delay = ReconnectSchedule.DelayBefore(_reconnectAttempt);
        var cts = new CancellationTokenSource();
        _reconnectCts = cts;
        NextReconnectAt = DateTime.Now + delay;
        ReconnectStatus = $"Nouvel essai automatique à {NextReconnectAt:HH:mm:ss} (tentative {_reconnectAttempt})";
        AppendLog($"Nouvel essai de connexion dans {delay.TotalSeconds:0} s (tentative {_reconnectAttempt}).");
        _ = RunReconnectAsync(delay, cts);
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
        ReconnectStatus = null;
    }

    private AudioModemLink BuildAudioModemLink()
    {
        _pttKeyer = SelectedPttMode switch
        {
            PttMode.Rts when !string.IsNullOrWhiteSpace(PttPort) => new SerialPttKeyer(PttPort, PttLine.Rts),
            PttMode.Dtr when !string.IsNullOrWhiteSpace(PttPort) => new SerialPttKeyer(PttPort, PttLine.Dtr),
            _ => new NullPttKeyer(),
        };
        _audioLink = new AudioModemLink(SelectedAudioInputDevice?.Id, SelectedAudioOutputDevice?.Id, _pttKeyer);
        _audioLink.SamplesAvailable += OnAudioSamplesAvailable;
        _audioLink.FrameCrcError += PulseDcd;
        return _audioLink;
    }

    /// <summary>Background capture-thread callback — buffers samples and computes new spectrum bars on the UI thread, throttled so the display doesn't try to repaint faster than it's useful.</summary>
    private void OnAudioSamplesAvailable(float[] samples)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSpectrumUpdate < SpectrumUpdateInterval)
            return;
        _lastSpectrumUpdate = now;

        int sampleRate = _audioLink?.SampleRate ?? 13200;
        lock (_spectrumSampleBuffer)
        {
            _spectrumSampleBuffer.AddRange(samples);
            if (_spectrumSampleBuffer.Count > SpectrumAnalyzer.FftSize)
                _spectrumSampleBuffer.RemoveRange(0, _spectrumSampleBuffer.Count - SpectrumAnalyzer.FftSize);
        }
        var snapshot = _spectrumSampleBuffer.ToArray();

        var bars = SpectrumAnalyzer.ComputeBars(snapshot, sampleRate);
        Dispatcher.UIThread.Post(() => SpectrumBars = bars);
    }

    /// <summary>
    /// Lights the DCD indicator and restarts its hang timer — called for a
    /// frame actually detected: a valid decode (<see cref="OnFrameReceived"/>)
    /// or a frame-shaped block that failed its CRC. Plain HDLC flags (idle
    /// preamble/fill, or noise that happens to look like one) don't trigger
    /// this — only a real frame structure does. Safe to call from any thread.
    /// </summary>
    private void PulseDcd() =>
        Dispatcher.UIThread.Post(() =>
        {
            _framePulse = true;
            UpdateCarrier();
            if (_dcdHangTimer is null)
            {
                _dcdHangTimer = new System.Timers.Timer(DcdHangTime.TotalMilliseconds) { AutoReset = false };
                _dcdHangTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    _framePulse = false;
                    UpdateCarrier();
                });
            }
            _dcdHangTimer.Stop();
            _dcdHangTimer.Start();
        });

    /// <summary>Starts mirroring the serial port's hardware CD line into <see cref="CarrierActive"/> — polled, since cross-platform pin-change events aren't reliable.</summary>
    private void StartCdPolling()
    {
        _cdPollTimer?.Stop();
        _cdPollTimer?.Dispose();
        _cdPollTimer = new System.Timers.Timer(CdPollInterval.TotalMilliseconds) { AutoReset = true };
        _cdPollTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _cdLine = _serialLink?.CdHolding ?? false;
            UpdateCarrier();
        });
        _cdPollTimer.Start();
    }

    private bool CanConnect() => State is FrameLinkState.Disconnected or FrameLinkState.Faulted && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task Disconnect()
    {
        _wantConnected = false;
        CancelPendingReconnect();
        _settings.ReconnectOnStartup = false;
        _appSettings.Save();
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
            RefreshReconnectView();
        }
    }

    /// <summary>Closes the current link and its helpers; its late state events are then ignored (see <see cref="OnLinkStateChanged"/>).</summary>
    private async Task DropLinkAsync()
    {
        var link = _link;
        if (link is null)
            return;
        _link = null;
        link.FrameReceived -= OnFrameReceived;
        try
        {
            // Sends the exit commands first, if any: still logged.
            await link.DisposeAsync();
        }
        catch
        {
            // already gone
        }
        link.Log -= AppendLog;

        _pttKeyer?.Dispose();
        _pttKeyer = null;
        if (_audioLink is not null)
        {
            _audioLink.SamplesAvailable -= OnAudioSamplesAvailable;
            _audioLink.FrameCrcError -= PulseDcd;
            _audioLink = null;
        }
        _serialLink = null;
        _dcdHangTimer?.Stop();
        _dcdHangTimer?.Dispose();
        _dcdHangTimer = null;
        _cdPollTimer?.Stop();
        _cdPollTimer?.Dispose();
        _cdPollTimer = null;
        _framePulse = _cdLine = false;
        CarrierActive = false;
        lock (_spectrumSampleBuffer) _spectrumSampleBuffer.Clear();
        SpectrumBars = new double[SpectrumAnalyzer.BarCount];
    }

    private bool CanDisconnect() => _link is not null || _reconnectCts is not null;

    /// <summary>
    /// The application is closing: closes the link, sending a serial TNC's
    /// exit commands, without changing whether it reconnects on next start
    /// (<see cref="RememberStateForNextStart"/> has run already).
    /// </summary>
    public async Task CloseForExitAsync()
    {
        _wantConnected = false;
        CancelPendingReconnect();
        await DropLinkAsync();
    }

    private void OnLinkStateChanged(IFrameLink link, FrameLinkState s)
    {
        if (link != _link)
            return;   // a link already replaced or closed
        State = s;
        if (s == FrameLinkState.Connected)
        {
            _connectedAt = DateTime.Now;
            NextReconnectAt = null;
            ReconnectStatus = null;
        }
        else if (s is FrameLinkState.Disconnected or FrameLinkState.Faulted && _wantConnected)
        {
            _framePulse = _cdLine = false;
            CarrierActive = false;
            // Back to quick retries only after a connection that held; one that
            // keeps dropping right away carries on with the longer delays.
            if (_connectedAt is { } since && DateTime.Now - since > TimeSpan.FromMinutes(1))
                _reconnectAttempt = 0;
            _connectedAt = null;
            if (!IsBusy)
                ScheduleReconnect();   // otherwise ConnectOnceAsync schedules it when it returns
        }
        RefreshReconnectView();
    }

    private void OnFrameReceived(byte[] ax25)
    {
        RawFrameReceived?.Invoke(ax25);
        PulseDcd();
        Dispatcher.UIThread.Post(() =>
        {
            string line = Ax25Frame.TryParse(ax25, out var frame) && frame is not null
                ? frame.ToTnc2()
                : $"(trame non-UI, {ax25.Length} octets)";
            AppendLog(line);
        });
    }

    /// <summary>Sends a raw AX.25 frame on this channel, if connected (logged either way). Returns whether it was actually sent.</summary>
    public async Task<bool> SendRawFrameAsync(byte[] ax25)
    {
        if (_link is null || State != FrameLinkState.Connected)
        {
            AppendLog("Envoi ignoré : liaison non connectée.");
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

    private void AppendLog(string line) =>
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });

    private void SaveSettings()
    {
        _settings.Kind = Kind;
        if (!string.IsNullOrWhiteSpace(SerialPort))
            _settings.SerialPort = SerialPort;
        _settings.BaudRate = BaudRate;
        _settings.TncModel = SelectedTncModel.Id;
        _settings.KissInitCommands = KissInitCommands;
        _settings.KissExitCommands = KissExitCommands;
        _settings.TcpHost = TcpHost;
        _settings.TcpPort = TcpPort;
        _settings.TxEnabled = TxEnabled;
        _settings.AudioInputDeviceId = SelectedAudioInputDevice?.Id;
        _settings.AudioOutputDeviceId = SelectedAudioOutputDevice?.Id;
        _settings.PttMode = SelectedPttMode;
        _settings.PttPort = PttPort;
        _settings.Enabled = true;
        _appSettings.Save();
    }
}

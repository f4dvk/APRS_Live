using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AprsLive.Core.Ax25;
using AprsLive.Core.Settings;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "Digipeater" panel of Paramètres → Radio. Per-channel routes,
/// UI-View style (<c>1=2</c>: heard on TNC1, repeated on TNC2): any channel
/// can be listened to, a route only goes out when its destination channel
/// has "Émission" on (Liaison A/B). Alias processing and duplicate filtering
/// live in <see cref="DigiRouter"/>. Gating to APRS-IS is separate: see
/// <see cref="AprsIsViewModel.GateFromRf"/>.
/// </summary>
public partial class DigiViewModel : ViewModelBase
{
    private readonly AppSettings _appSettings;
    private readonly DigiSettings _settings;
    private readonly LinkViewModel _linkA;
    private readonly LinkViewModel _linkB;
    private readonly DigiRouter _router = new();

    /// <summary>Exposed so the view can gray out the "repeat on" columns of a channel whose Émission is off.</summary>
    public LinkViewModel LinkA => _linkA;

    public LinkViewModel LinkB => _linkB;

    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _wide1;

    [ObservableProperty]
    private bool _wide2;

    [ObservableProperty]
    private bool _wide3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutesSummary), nameof(RoutesWarning), nameof(HasRoutesWarning))]
    private bool _route1To1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutesSummary), nameof(RoutesWarning), nameof(HasRoutesWarning))]
    private bool _route1To2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutesSummary), nameof(RoutesWarning), nameof(HasRoutesWarning))]
    private bool _route2To1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoutesSummary), nameof(RoutesWarning), nameof(HasRoutesWarning))]
    private bool _route2To2;

    /// <summary>The routes in UI-View notation, e.g. "1=2  2=1,2".</summary>
    public string RoutesSummary
    {
        get
        {
            string routes = DigiRouter.Describe(CurrentRoutes());
            return routes.Length == 0 ? "Aucune route : rien n'est répété." : $"Équivalent UI-View : {routes}";
        }
    }

    /// <summary>Routes that are ticked but can't go out, because their destination channel doesn't transmit.</summary>
    public string RoutesWarning
    {
        get
        {
            var lines = new List<string>(2);
            if ((Route1To1 || Route2To1) && !_linkA.TxEnabled)
                lines.Add("« Émission » est décochée sur la Liaison A : les répétitions vers TNC1 sont ignorées.");
            if ((Route1To2 || Route2To2) && !_linkB.TxEnabled)
                lines.Add("« Émission » est décochée sur la Liaison B : les répétitions vers TNC2 sont ignorées.");
            return string.Join("\n", lines);
        }
    }

    public bool HasRoutesWarning => RoutesWarning.Length > 0;

    public DigiViewModel(AppSettings appSettings, LinkViewModel linkA, LinkViewModel linkB)
    {
        _appSettings = appSettings;
        _settings = appSettings.Digi;
        _linkA = linkA;
        _linkB = linkB;

        _enabled = _settings.Enabled;
        _wide1 = _settings.Wide1;
        _wide2 = _settings.Wide2;
        _wide3 = _settings.Wide3;
        _route1To1 = _settings.Route1To1;
        _route1To2 = _settings.Route1To2;
        _route2To1 = _settings.Route2To1;
        _route2To2 = _settings.Route2To2;

        _linkA.RawFrameReceived += bytes => OnFrameReceived(bytes, fromChannel: 1);
        _linkB.RawFrameReceived += bytes => OnFrameReceived(bytes, fromChannel: 2);
        _linkA.PropertyChanged += OnLinkPropertyChanged;
        _linkB.PropertyChanged += OnLinkPropertyChanged;
    }

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnWide1Changed(bool value) => Save();
    partial void OnWide2Changed(bool value) => Save();
    partial void OnWide3Changed(bool value) => Save();
    partial void OnRoute1To1Changed(bool value) => Save();
    partial void OnRoute1To2Changed(bool value) => Save();
    partial void OnRoute2To1Changed(bool value) => Save();
    partial void OnRoute2To2Changed(bool value) => Save();

    private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LinkViewModel.TxEnabled))
            return;
        OnPropertyChanged(nameof(RoutesWarning));
        OnPropertyChanged(nameof(HasRoutesWarning));
    }

    private DigiSettings CurrentRoutes() => new()
    {
        Route1To1 = Route1To1,
        Route1To2 = Route1To2,
        Route2To1 = Route2To1,
        Route2To2 = Route2To2,
    };

    private void Save()
    {
        _settings.Enabled = Enabled;
        _settings.Wide1 = Wide1;
        _settings.Wide2 = Wide2;
        _settings.Wide3 = Wide3;
        _settings.Route1To1 = Route1To1;
        _settings.Route1To2 = Route1To2;
        _settings.Route2To1 = Route2To1;
        _settings.Route2To2 = Route2To2;
        _appSettings.Save();
    }

    private void OnFrameReceived(byte[] ax25, int fromChannel)
    {
        if (!Enabled)
            return;

        string ownCallsign = _appSettings.Station.Callsign;
        if (string.IsNullOrWhiteSpace(ownCallsign))
            return;

        var decision = _router.Route(ax25, fromChannel, _settings, ownCallsign,
            _linkA.TxEnabled, _linkB.TxEnabled, DateTime.UtcNow);
        if (decision is null)
            return;

        foreach (int channel in decision.SendOn)
            _ = (channel == 1 ? _linkA : _linkB).SendRawFrameAsync(decision.Frame);

        string from = $"TNC{fromChannel}";
        string sent = decision.SendOn.Count > 0
            ? string.Join(", ", decision.SendOn.Select(c => $"TNC{c}"))
            : "rien";
        string skipped = decision.AlreadyOnAir.Count > 0
            ? $" (déjà passée sur {string.Join(", ", decision.AlreadyOnAir.Select(c => $"TNC{c}"))} il y a moins de 30 s)"
            : "";
        AppendLog($"{decision.Source} : {from} → {sent}{skipped}");
    }

    private void AppendLog(string line) =>
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add($"{DateTime.Now:HH:mm:ss} {line}");
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });
}

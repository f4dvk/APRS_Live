using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.App.Icons;
using AprsLive.Core;
using AprsLive.Core.Aprs;
using AprsLive.Core.Ax25;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs one card of the "Objets" tab: a single user-defined APRS object
/// (name, position, icon, comment), sent under its own name rather than the
/// station's callsign — manually, on a fixed interval, or killed (retracted)
/// when no longer relevant.
/// </summary>
public partial class ObjectItemViewModel : ViewModelBase
{
    private readonly AppSettings _appSettings;
    private readonly ObjectSettings _settings;

    /// <summary>The underlying persisted entry — lets the owning list remove it from <see cref="AppSettings.Objects"/> on delete.</summary>
    public ObjectSettings Settings => _settings;

    private readonly LinkViewModel _linkA;
    private readonly LinkViewModel _linkB;
    private readonly StationRegistry _stationRegistry;
    private System.Timers.Timer? _beaconTimer;

    /// <summary>Raised after <see cref="DeleteCommand"/> — the owning list removes this item.</summary>
    public event Action<ObjectItemViewModel>? DeleteRequested;

    public IReadOnlyList<SymbolChoice> AvailableSymbols { get; } =
        AprsSymbol.KnownSymbols.Select(SymbolChoice.For).ToList();

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _latitudeText;

    [ObservableProperty]
    private string _longitudeText;

    [ObservableProperty]
    private SymbolChoice _selectedSymbol;

    [ObservableProperty]
    private string _comment;

    [ObservableProperty]
    private int _intervalMinutes;

    [ObservableProperty]
    private bool _isLive = true;

    public string LiveStateText => IsLive ? "Vivant" : "Tué";

    partial void OnIsLiveChanged(bool value) => OnPropertyChanged(nameof(LiveStateText));

    [ObservableProperty]
    private string? _statusMessage;

    public ObjectItemViewModel(AppSettings appSettings, ObjectSettings settings,
        LinkViewModel linkA, LinkViewModel linkB, StationRegistry stationRegistry)
    {
        _appSettings = appSettings;
        _settings = settings;
        _linkA = linkA;
        _linkB = linkB;
        _stationRegistry = stationRegistry;

        _name = settings.Name;
        _latitudeText = GeoCoord.Format(settings.Latitude, GeoCoord.Notation.Decimal, isLat: true);
        _longitudeText = GeoCoord.Format(settings.Longitude, GeoCoord.Notation.Decimal, isLat: false);
        _comment = settings.Comment;
        _intervalMinutes = settings.IntervalMinutes;
        _isLive = settings.Live;

        _selectedSymbol = AvailableSymbols.FirstOrDefault(
            c => c.Entry.Table == settings.SymbolTable && c.Entry.Code == settings.SymbolCode)
            ?? SymbolChoice.For(new AprsSymbol.Entry(settings.SymbolTable, settings.SymbolCode, "Personnalisé"));

        RestartBeaconTimer();
    }

    [RelayCommand]
    private void Save()
    {
        if (!GeoCoord.TryParseLatitude(LatitudeText, out double lat))
        {
            StatusMessage = "Latitude invalide.";
            return;
        }
        if (!GeoCoord.TryParseLongitude(LongitudeText, out double lon))
        {
            StatusMessage = "Longitude invalide.";
            return;
        }

        _settings.Name = Name.Trim();
        _settings.Latitude = lat;
        _settings.Longitude = lon;
        _settings.SymbolTable = SelectedSymbol.Entry.Table;
        _settings.SymbolCode = SelectedSymbol.Entry.Code;
        _settings.Comment = Comment;
        _settings.IntervalMinutes = Math.Max(0, IntervalMinutes);

        _appSettings.Save();
        RestartBeaconTimer();
        StatusMessage = "Enregistré.";
    }

    [RelayCommand]
    private async Task SendNow()
    {
        Save();
        await SendObjectAsync(live: true);
    }

    [RelayCommand]
    private async Task Kill()
    {
        Save();
        IsLive = false;
        _settings.Live = false;
        _appSettings.Save();
        RestartBeaconTimer();
        await SendObjectAsync(live: false);
    }

    [RelayCommand]
    private void Delete()
    {
        _beaconTimer?.Stop();
        _beaconTimer?.Dispose();
        DeleteRequested?.Invoke(this);
    }

    private async Task SendObjectAsync(bool live)
    {
        if (string.IsNullOrWhiteSpace(_settings.Name))
        {
            StatusMessage = "Nom d'objet non défini.";
            return;
        }

        var station = _appSettings.Station;
        if (string.IsNullOrWhiteSpace(station.Callsign))
        {
            StatusMessage = "Indicatif de la station non défini (onglet Station).";
            return;
        }

        var frame = Ax25Builder.BuildObjectReport(
            station.Callsign, station.Destination, station.Path,
            _settings.Name, live, DateTime.UtcNow,
            _settings.Latitude, _settings.Longitude, _settings.SymbolTable, _settings.SymbolCode, _settings.Comment);

        var sentOn = await TxLinks.SendAsync(frame, _linkA, _linkB);

        if (Ax25Frame.TryParse(frame, out var parsedFrame) && parsedFrame is not null)
            _stationRegistry.Apply(AprsParser.Parse(parsedFrame), PacketOrigin.Local);

        string verb = live ? "envoyé" : "tué";
        StatusMessage = sentOn.Count > 0
            ? $"Objet {verb} ({string.Join(", ", sentOn)}) à {DateTime.Now:HH:mm:ss}."
            : "Objet construit mais non transmis : aucune liaison en émission n'est connectée.";
    }

    private void RestartBeaconTimer()
    {
        _beaconTimer?.Stop();
        _beaconTimer?.Dispose();
        _beaconTimer = null;

        if (IntervalMinutes <= 0 || !IsLive)
            return;

        _beaconTimer = new System.Timers.Timer(TimeSpan.FromMinutes(IntervalMinutes).TotalMilliseconds)
        {
            AutoReset = true,
        };
        _beaconTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() => _ = SendObjectAsync(live: true));
        _beaconTimer.Start();
    }
}

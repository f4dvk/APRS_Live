using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// One watched station in the Couverture tab (our own station on top, or a
/// digipeater), updated in place as frames come in and the 24 h window
/// slides. A click unfolds the stations it heard direct.
/// </summary>
public sealed partial class CoverageRowViewModel : ViewModelBase
{
    private readonly Action<string> _remove;
    private readonly Func<string, IReadOnlyList<DirectStation>> _heardDirect;

    public CoverageRowViewModel(string callsign, Action<string> remove, Func<string, IReadOnlyList<DirectStation>> heardDirect,
        bool isOwnStation = false)
    {
        Callsign = callsign;
        _remove = remove;
        _heardDirect = heardDirect;
        IsOwnStation = isOwnStation;
    }

    public string Callsign { get; }

    /// <summary>Our own station (Paramètres → Station): what our TNCs hear direct. It can't be unwatched.</summary>
    public bool IsOwnStation { get; }

    public string EmptyHeardText => IsOwnStation
        ? "Aucune trame de position reçue en direct par vos TNC sur les 24 dernières heures."
        : "Aucune trame de position relayée en premier par cette station sur les 24 dernières heures.";

    [ObservableProperty]
    private string _framesText = "0";

    [ObservableProperty]
    private string _stationsText = "0";

    [ObservableProperty]
    private string _averageText = "—";

    [ObservableProperty]
    private string _maxText = "—";

    /// <summary>Who the farthest station was and when, or why there is no distance.</summary>
    [ObservableProperty]
    private string _farthestText = "";

    [ObservableProperty]
    private string _lastText = "—";

    /// <summary>Unfolded: the stations heard direct are listed under the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    private bool _isExpanded;

    public string Chevron => IsExpanded ? "▾" : "▸";

    /// <summary>Filled only while unfolded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeardStations))]
    private IReadOnlyList<DirectStationItem> _heardStations = [];

    public bool HasHeardStations => HeardStations.Count > 0;

    public void Update(CoverageSummary? s)
    {
        if (s is null)
            return;
        FramesText = s.Frames.ToString(CultureInfo.InvariantCulture);
        StationsText = s.Stations.ToString(CultureInfo.InvariantCulture);
        LastText = s.LastAt is { } last ? When(last) : "—";
        AverageText = s.AverageKm is { } avg ? Km(avg) : "—";
        MaxText = s.MaxKm is { } max ? Km(max) : "—";
        FarthestText = s.FarthestStation is { } far && s.FarthestAt is { } at
            ? $"{far} ({When(at)})"
            : s.Frames > 0 && !s.RelayPositionKnown ? UnknownPosition(IsOwnStation) : "";
        if (IsExpanded)
            LoadHeardStations();
    }

    [RelayCommand]
    private void Remove() => _remove(Callsign);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            LoadHeardStations();
        else
            HeardStations = [];
    }

    private void LoadHeardStations() =>
        HeardStations = _heardDirect(Callsign)
            .Select(d => new DirectStationItem(
                d.Station,
                LastHeard(d.LastAt),
                d.DistanceKm is { } km ? Km(km) : "—",
                d.Frames.ToString(CultureInfo.InvariantCulture),
                d.HeardRelaying))
            .ToList();

    /// <summary>The line shown in the map popup of a watched station.</summary>
    public static string PopupLine(CoverageSummary s, bool isOwnStation = false)
    {
        if (s.Frames == 0)
            return isOwnStation
                ? "Couverture 24 h : aucune trame reçue en direct pour l'instant"
                : "Couverture 24 h : aucune trame relayée en premier pour l'instant";
        string frames = s.Frames == 1 ? "1 trame" : $"{s.Frames} trames";
        if (s.AverageKm is not { } avg || s.MaxKm is not { } max)
            return $"Couverture 24 h : {frames} · {(isOwnStation ? "position inconnue (Paramètres → Station)" : "position du relais inconnue")}";
        return $"Couverture 24 h : {frames} · moy. {Km(avg)} · max {Km(max)} ({s.FarthestStation})";
    }

    private static string UnknownPosition(bool isOwnStation) =>
        isOwnStation ? "Position inconnue (Paramètres → Station)" : "Position du relais inconnue";

    // InvariantGlobalization is on: French decimal comma by hand, as for the Best DX status.
    private static string Km(double km) =>
        km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";

    private static string When(DateTimeOffset at)
    {
        var local = at.LocalDateTime;
        return local.Date == DateTime.Today ? $"{local:HH:mm}" : $"{local:dd/MM HH:mm}";
    }

    private static string LastHeard(DateTimeOffset at)
    {
        var local = at.LocalDateTime;
        return local.Date == DateTime.Today ? $"{local:HH:mm:ss}" : $"{local:dd/MM HH:mm:ss}";
    }
}

/// <summary>One line of the stations heard direct by a watched station.</summary>
/// <param name="IsDigi">Heard as a digi relaying someone else's frame (links between digis).</param>
public sealed record DirectStationItem(string Callsign, string LastText, string DistanceText, string FramesText, bool IsDigi);

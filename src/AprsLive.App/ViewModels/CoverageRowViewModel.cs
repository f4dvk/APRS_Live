using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>One watched station in the Couverture tab, updated in place as frames come in and the 24 h window slides.</summary>
public sealed partial class CoverageRowViewModel : ViewModelBase
{
    private readonly Action<string> _remove;

    public CoverageRowViewModel(string callsign, Action<string> remove)
    {
        Callsign = callsign;
        _remove = remove;
    }

    public string Callsign { get; }

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
            : s.Frames > 0 && !s.RelayPositionKnown ? "Position du relais inconnue" : "";
    }

    [RelayCommand]
    private void Remove() => _remove(Callsign);

    /// <summary>The line shown in the map popup of a watched station.</summary>
    public static string PopupLine(CoverageSummary s)
    {
        if (s.Frames == 0)
            return "Couverture 24 h : aucune trame relayée en premier pour l'instant";
        string frames = s.Frames == 1 ? "1 trame" : $"{s.Frames} trames";
        if (s.AverageKm is not { } avg || s.MaxKm is not { } max)
            return $"Couverture 24 h : {frames} · position du relais inconnue";
        return $"Couverture 24 h : {frames} · moy. {Km(avg)} · max {Km(max)} ({s.FarthestStation})";
    }

    // InvariantGlobalization is on: French decimal comma by hand, as for the Best DX status.
    private static string Km(double km) =>
        km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";

    private static string When(DateTimeOffset at)
    {
        var local = at.LocalDateTime;
        return local.Date == DateTime.Today ? $"{local:HH:mm}" : $"{local:dd/MM HH:mm}";
    }
}

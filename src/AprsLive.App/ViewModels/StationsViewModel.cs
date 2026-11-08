using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "Stations" tab: every heard station with a position, sorted
/// alphabetically by callsign, filtered by reception channel, with its
/// distance from our own station.
/// </summary>
public sealed partial class StationsViewModel : ViewModelBase
{
    private readonly StationRegistry _registry;
    private readonly StationSettings _ownStation;

    public ObservableCollection<StationRowViewModel> Stations { get; } = new();

    [ObservableProperty]
    private bool _directOnly;

    /// <summary>Index in <see cref="ChannelChoices"/>: 0 every station, 1 heard on TNC1, 2 heard on TNC2, 3 heard via APRS-IS.</summary>
    [ObservableProperty]
    private int _channelFilter;

    public static IReadOnlyList<string> ChannelChoices { get; } =
        ["Toutes les voies", "TNC1 (Liaison A)", "TNC2 (Liaison B)", "Internet"];

    public StationsViewModel(StationRegistry registry, AppSettings settings)
    {
        _registry = registry;
        _ownStation = settings.Station;
        _registry.StationUpdated += _ => Dispatcher.UIThread.Post(Refresh);
        _registry.StationsChanged += () => Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>The station settings were saved: our position, so the distances, may have changed.</summary>
    public void OnStationSettingsSaved() => Refresh();

    partial void OnDirectOnlyChanged(bool value) => Refresh();

    partial void OnChannelFilterChanged(int value) => Refresh();

    /// <summary>With a TNC picked, "direct" means heard direct on that TNC.</summary>
    private bool Matches(HeardStation s) => ChannelFilter switch
    {
        1 or 2 => s.ReceptionOn(ChannelFilter) is { } rx && (!DirectOnly || rx.LastDirect is not null),
        3 => s.LastInternetPath is not null && (!DirectOnly || s.HeardDirect),
        _ => !DirectOnly || s.HeardDirect,
    };

    private void Refresh()
    {
        var own = _ownStation;
        (double Lat, double Lon)? from = own.Latitude is { } lat && own.Longitude is { } lon ? (lat, lon) : null;
        var sorted = _registry.Stations
            .Where(Matches)
            .OrderBy(s => s.Callsign, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StationRowViewModel(s, from, own.Callsign))
            .ToList();

        Stations.Clear();
        foreach (var row in sorted)
            Stations.Add(row);
    }
}

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Aprs;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "Couverture" tab: the stations (digipeaters) whose reach is
/// estimated over the last 24 h, from the frames they repeated first — see
/// <see cref="DigiCoverage"/>. The watched list is kept in the settings, the
/// samples in <c>coverage.json</c>.
/// </summary>
public sealed partial class CoverageViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly StationRegistry _registry;
    private readonly DigiCoverage _coverage = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _dirty;

    public ObservableCollection<CoverageRowViewModel> Rows { get; } = new();

    public bool HasRows => Rows.Count > 0;

    /// <summary>Stations seen as first hop in the last 24 h and not watched yet.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = [];

    [ObservableProperty]
    private string? _selectedSuggestion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string? _newCallsign;

    [ObservableProperty]
    private string? _addError;

    public CoverageViewModel(AppSettings settings, StationRegistry registry)
    {
        _settings = settings;
        _registry = registry;
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));

        foreach (string call in _settings.CoverageStations)
            if (_coverage.Watch(call))
                Rows.Add(NewRow(call));
        _coverage.Restore(CoverageStore.Load());
        _coverage.RelaysSeenChanged += UpdateSuggestions;

        // The window slides even when nothing comes in.
        _refreshTimer.Tick += (_, _) =>
        {
            if (_coverage.Prune())
                _dirty = true;
            Refresh();
        };
        _refreshTimer.Start();
        Refresh();
    }

    /// <summary>Every received packet, radio and APRS-IS, goes through here.</summary>
    public void Record(AprsPacket packet)
    {
        if (!_coverage.Record(packet))
            return;
        _dirty = true;
        Refresh();
    }

    public bool IsWatched(string callsign) => _coverage.IsWatched(callsign);

    /// <summary>Starts watching a station. False, with <see cref="AddError"/> set, if the callsign is invalid or already watched.</summary>
    public bool Watch(string callsign)
    {
        AddError = null;
        if (Normalize(callsign) is not { } call)
        {
            AddError = "Indicatif invalide (ex. F1ZXX-3).";
            return false;
        }
        if (!_coverage.Watch(call))
        {
            AddError = $"{call} est déjà suivie.";
            return false;
        }
        _settings.CoverageStations.Add(call);
        _settings.Save();
        var row = NewRow(call);
        row.Update(_coverage.Summarize(call, PositionOf(call)));
        Rows.Add(row);
        _dirty = true;
        UpdateSuggestions();
        return true;
    }

    /// <summary>Stops watching a station; its samples are forgotten.</summary>
    public void Unwatch(string callsign)
    {
        if (!_coverage.Unwatch(callsign))
            return;
        _settings.CoverageStations.RemoveAll(c => string.Equals(c, callsign, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
        if (Rows.FirstOrDefault(r => string.Equals(r.Callsign, callsign, StringComparison.OrdinalIgnoreCase)) is { } row)
            Rows.Remove(row);
        _dirty = true;
        UpdateSuggestions();
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (Watch(NewCallsign ?? ""))
        {
            NewCallsign = null;
            SelectedSuggestion = null;
        }
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewCallsign);

    partial void OnSelectedSuggestionChanged(string? value)
    {
        if (value is not null)
            NewCallsign = value;
    }

    /// <summary>For the map popup: the 24 h coverage line of a watched station, else <c>null</c>.</summary>
    public string? PopupLine(string callsign) =>
        _coverage.Summarize(callsign, PositionOf(callsign)) is { } s ? CoverageRowViewModel.PopupLine(s) : null;

    /// <summary>Can be watched: an AX.25 callsign (an object name such as <c>EVENT</c> can't digipeat).</summary>
    public static bool CanWatch(string callsign) => Normalize(callsign) is not null;

    /// <summary>Saves the samples if anything changed since the last save.</summary>
    public void Save()
    {
        if (!_dirty)
            return;
        CoverageStore.Save(_coverage.Logs);
        _dirty = false;
    }

    private CoverageRowViewModel NewRow(string callsign) =>
        new(callsign, Unwatch, call => _coverage.HeardDirect(call, PositionOf(call)));

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        foreach (var row in Rows)
            row.Update(_coverage.Summarize(row.Callsign, PositionOf(row.Callsign), now));
    }

    private void UpdateSuggestions()
    {
        var list = _coverage.RelaysSeen
            .Where(c => !_coverage.IsWatched(c))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!list.SequenceEqual(Suggestions, StringComparer.OrdinalIgnoreCase))
            Suggestions = list;
    }

    /// <summary>Where the relay is now: the station list, or our own settings when it is us.</summary>
    private (double Lat, double Lon)? PositionOf(string callsign)
    {
        if (_registry.Find(callsign) is { Latitude: { } lat, Longitude: { } lon })
            return (lat, lon);
        var own = _settings.Station;
        if (string.Equals(callsign, own.Callsign, StringComparison.OrdinalIgnoreCase)
            && own.Latitude is { } ownLat && own.Longitude is { } ownLon)
            return (ownLat, ownLon);
        return null;
    }

    /// <summary>Upper case, without a "-0" SSID (as in a frame's path); <c>null</c> if not an AX.25 callsign.</summary>
    private static string? Normalize(string callsign)
    {
        var m = CallsignPattern().Match(callsign.Trim().ToUpperInvariant());
        if (!m.Success || !m.Groups[1].Value.Any(char.IsAsciiDigit))
            return null;
        return m.Groups[2].Success && m.Groups[2].Value != "0"
            ? $"{m.Groups[1].Value}-{m.Groups[2].Value}"
            : m.Groups[1].Value;
    }

    [GeneratedRegex("^([A-Z0-9]{1,6})(?:-([0-9]|1[0-5]))?$")]
    private static partial Regex CallsignPattern();
}

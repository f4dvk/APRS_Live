using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the "Objets" tab: a free-form list of APRS objects the operator
/// configures and sends under their own name (events, relays, hazards...),
/// independent from the station's own beacon.
/// </summary>
public partial class ObjectsViewModel : ViewModelBase
{
    private readonly AppSettings _appSettings;
    private readonly LinkViewModel _linkA;
    private readonly LinkViewModel _linkB;
    private readonly StationRegistry _stationRegistry;

    public ObservableCollection<ObjectItemViewModel> Items { get; } = new();

    public ObjectsViewModel(AppSettings appSettings, LinkViewModel linkA, LinkViewModel linkB, StationRegistry stationRegistry)
    {
        _appSettings = appSettings;
        _linkA = linkA;
        _linkB = linkB;
        _stationRegistry = stationRegistry;

        foreach (var entry in _appSettings.Objects)
            Items.Add(CreateItem(entry));
    }

    [RelayCommand]
    private void Add()
    {
        var entry = new ObjectSettings();
        _appSettings.Objects.Add(entry);
        _appSettings.Save();
        Items.Add(CreateItem(entry));
    }

    private ObjectItemViewModel CreateItem(ObjectSettings entry)
    {
        var item = new ObjectItemViewModel(_appSettings, entry, _linkA, _linkB, _stationRegistry);
        item.DeleteRequested += OnDeleteRequested;
        return item;
    }

    private void OnDeleteRequested(ObjectItemViewModel item)
    {
        item.DeleteRequested -= OnDeleteRequested;
        _appSettings.Objects.Remove(item.Settings);
        _appSettings.Save();
        Items.Remove(item);
    }
}

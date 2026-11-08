using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mapsui.Extensions;
using Mapsui.Projections;
using Mapsui.UI.Avalonia;
using AprsLive.App.ViewModels;
using AprsLive.Core.Settings;
using AprsLive.Core.Stations;

namespace AprsLive.App.Views;

public partial class MainWindow : Window
{
    public static readonly AppTheme[] ThemeChoices = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    private MapControl? _mapControl;
    private Popup? _stationPopup;
    private StackPanel? _stationPopupContent;

    public MainWindow()
    {
        InitializeComponent();

        _mapControl = this.FindControl<MapControl>("Map");
        if (_mapControl is not null)
        {
            _mapControl.PointerPressed += OnMapPointerPressed;
            _mapControl.PointerMoved += OnMapPointerMoved;
            _mapControl.PointerExited += (_, _) => (DataContext as MainViewModel)?.MapController.ClearReceptionPath();
        }

        _stationPopup = this.FindControl<Popup>("StationPopup");
        _stationPopupContent = this.FindControl<StackPanel>("StationPopupContent");

        DataContextChanged += (_, _) => AttachMap();
        AttachMap();

        // WindowState=Maximized alone is unreliable on some Linux window
        // managers (the maximize request can be dropped, or race against the
        // screen list not being populated yet right at Opened). Sizing the
        // window to the screen's working area directly, deferred a tick past
        // Opened, is a more reliable way to get the same full-screen result.
        Opened += (_, _) => Dispatcher.UIThread.Post(MaximizeToScreen, DispatcherPriority.Background);
        Closing += OnClosing;
    }

    private bool _linksClosedForExit;

    // A serial TNC left in KISS mode would keep the radio's own APRS/packet
    // off: the close waits for its exit commands (a few seconds), then goes on.
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel vm || _linksClosedForExit)
            return;
        vm.SaveSessionState();
        if (!vm.HasExitCommandsToSend)
            return;

        e.Cancel = true;
        _linksClosedForExit = true;
        IsEnabled = false;
        try
        {
            await vm.CloseLinksForExitAsync();
        }
        finally
        {
            Close();
        }
    }

    private void MaximizeToScreen()
    {
        if (Screens.Primary is not { } screen)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        Position = screen.WorkingArea.Position;
        Width = screen.WorkingArea.Width;
        Height = screen.WorkingArea.Height;
    }

    private void AttachMap()
    {
        if (_mapControl is null || DataContext is not MainViewModel vm)
            return;

        // A just-added map feature (e.g. the station marker after sending a
        // beacon) otherwise only paints after the next unrelated redraw —
        // RefreshData() re-triggers the fetch/render cycle, ForceUpdate() repaints.
        vm.MapController.ForceRedraw = () =>
        {
            try { _mapControl.RefreshData(); } catch { /* viewport not ready */ }
            _mapControl.ForceUpdate();
        };
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mapControl is null || DataContext is not MainViewModel vm)
            return;

        var props = e.GetCurrentPoint(_mapControl).Properties;

        if (props.IsLeftButtonPressed && e.ClickCount == 2)
        {
            var at = e.GetPosition(_mapControl);
            if (_stationPopup is not null)
                _stationPopup.IsOpen = false;   // opened by the first click of the pair
            vm.MapController.ZoomInAt(at.X, at.Y);
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            var pos = e.GetPosition(_mapControl);
            var world = _mapControl.Map.Navigator.Viewport.ScreenToWorld(pos.X, pos.Y);
            var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);

            var placeHere = new MenuItem { Header = "Positionner ma station ici" };
            placeHere.Click += (_, _) => vm.Station.SetPosition(lat, lon);

            var menu = new ContextMenu { ItemsSource = new[] { placeHere } };
            menu.Open(_mapControl);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(_mapControl);
            var station = vm.MapController.HitTestStation(pos.X, pos.Y, toleranceScreenPx: 16);
            if (station is not null)
            {
                ShowStationPopup(station);
                e.Handled = true;
            }
        }
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mapControl is null || DataContext is not MainViewModel vm)
            return;
        var point = e.GetCurrentPoint(_mapControl);
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
            return;   // panning: leave the overlay alone

        var station = vm.MapController.HitTestStation(point.Position.X, point.Position.Y, toleranceScreenPx: 16);
        if (station is null)
            vm.MapController.ClearReceptionPath();
        else
            vm.MapController.ShowReceptionPath(station, vm.OwnPosition);
    }

    private void ShowStationPopup(HeardStation station)
    {
        if (_stationPopup is null || _stationPopupContent is null || DataContext is not MainViewModel vm)
            return;

        _stationPopupContent.Children.Clear();

        var header = new DockPanel();
        var closeButton = new Button
        {
            Content = "✕",
            Padding = new Avalonia.Thickness(4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
        };
        closeButton.Click += (_, _) => _stationPopup.IsOpen = false;
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(new TextBlock
        {
            Text = station.Callsign,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _stationPopupContent.Children.Add(header);
        _stationPopupContent.Children.Add(new TextBlock
        {
            Text = $"Reçu : {station.LastHeard.LocalDateTime:dd/MM/yyyy HH:mm:ss}",
            Foreground = Brushes.Black,
        });

        if (!string.IsNullOrWhiteSpace(station.Comment))
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = station.Comment,
                FontStyle = FontStyle.Italic,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
            });

        var motionParts = new List<string>();
        if (station.CourseDeg is { } course) motionParts.Add($"Cap {course:0}°");
        if (station.SpeedKnots is { } speed) motionParts.Add($"{speed:0} nds");
        if (station.AltitudeM is { } alt) motionParts.Add($"{alt:0} m");
        if (motionParts.Count > 0)
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", motionParts),
                Foreground = Brushes.Black,
            });

        string route = station.LastRoute switch
        {
            StationRoute.Direct => "Direct",
            StationRoute.Digipeater => "Via digi",
            StationRoute.Internet => "Internet",
            _ => "Local",
        };
        if (StationRowViewModel.LastCopyChannel(station) is { } port)
            route = $"{route} · {port}";
        var grey = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        _stationPopupContent.Children.Add(new TextBlock
        {
            Text = $"{route} · {station.PathText}",
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
            Foreground = grey,
            TextWrapping = TextWrapping.Wrap,
        });
        if (station.AlsoHeardVia.Count > 0)
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = $"Aussi reçue : {string.Join(" ; ", station.AlsoHeardVia)}",
                FontFamily = new FontFamily("monospace"),
                FontSize = 11,
                Foreground = grey,
                TextWrapping = TextWrapping.Wrap,
            });
        if (station.LastRoute != StationRoute.Direct && station.LastHeardDirect is { } direct)
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = $"Dernière réception directe : {direct.LocalDateTime:dd/MM HH:mm:ss}",
                FontSize = 11,
                Foreground = grey,
            });
        foreach (string line in StationRowViewModel.ChannelLines(station))
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 11,
                Foreground = grey,
                TextWrapping = TextWrapping.Wrap,
            });

        if (vm.Coverage.PopupLine(station.Callsign) is { } coverageLine)
            _stationPopupContent.Children.Add(new TextBlock
            {
                Text = coverageLine,
                FontSize = 11,
                Foreground = grey,
                TextWrapping = TextWrapping.Wrap,
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var messageButton = new Button { Content = "Message" };
        messageButton.Click += (_, _) =>
        {
            vm.Messages.OpenConversationWith(station.Callsign);
            _stationPopup.IsOpen = false;
        };
        buttons.Children.Add(messageButton);
        // Our own station is always in the Couverture tab: nothing to start or stop.
        if (CoverageViewModel.CanWatch(station.Callsign) && !vm.Coverage.IsOwnStation(station.Callsign))
        {
            bool watched = vm.Coverage.IsWatched(station.Callsign);
            var coverageButton = new Button { Content = watched ? "Arrêter le suivi" : "Suivre la couverture" };
            ToolTip.SetTip(coverageButton, watched
                ? "Ne plus estimer la couverture de cette station (onglet Couverture)"
                : "Estimer sur 24 h la couverture de cette station comme digi (onglet Couverture)");
            coverageButton.Click += (_, _) =>
            {
                if (watched)
                    vm.Coverage.Unwatch(station.Callsign);
                else
                    vm.Coverage.Watch(station.Callsign);
                _stationPopup.IsOpen = false;
            };
            buttons.Children.Add(coverageButton);
        }
        _stationPopupContent.Children.Add(buttons);

        _stationPopup.IsOpen = true;
    }

    private async void OnBrowseMbTilesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier MBTiles hors-ligne",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("MBTiles") { Patterns = ["*.mbtiles"] }],
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file?.TryGetLocalPath() is { } path)
            vm.LoadOfflineMap(path);
    }
}

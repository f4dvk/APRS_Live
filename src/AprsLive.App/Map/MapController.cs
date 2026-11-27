using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using AprsLive.Core.Stations;

namespace AprsLive.App.Map;

/// <summary>
/// Owns the Mapsui <see cref="Mapsui.Map"/> and its base tile layer: online
/// OSM by default, or an offline raster MBTiles file — plus the
/// heard-stations layer (which includes "my station": sending a beacon
/// feeds it through the same <c>StationRegistry</c> as a received packet,
/// so there is exactly one marker per callsign, always clickable — no
/// separate "my station" layer to duplicate it). Track/trail overlay layers
/// are added in a later phase.
/// </summary>
public sealed class MapController
{
    private readonly WritableLayer _heardStations = new() { Name = "Stations", Style = null };
    private readonly WritableLayer _receptionPath = new() { Name = "Chemin", Style = null };
    private readonly Dictionary<string, HeardStation> _stationIndex = new(StringComparer.OrdinalIgnoreCase);
    private string? _pathShownFor;

    private static readonly Color RfPathColor = new(231, 76, 60, 230);
    private static readonly Color InternetPathColor = new(52, 120, 220, 220);

    private ILayer _baseLayer;
    private MRect? _offlineExtent;

    public MapController()
    {
        Map = new Mapsui.Map { CRS = "EPSG:3857" };
        _baseLayer = OpenStreetMap.CreateTileLayer("APRS_Live");
        Map.Layers.Add(_baseLayer);
        Map.Layers.Add(_receptionPath);   // under the icons
        Map.Layers.Add(_heardStations);
        Map.Navigator.ViewportChanged += (_, _) => RaiseZoomLevelChanged();
    }

    public Mapsui.Map Map { get; }

    public bool IsOffline { get; private set; }

    /// <summary>
    /// Set by the view once the Avalonia MapControl exists — calls its
    /// RefreshData()/ForceUpdate(), which <see cref="Map.RefreshGraphics"/>
    /// alone doesn't reliably trigger for a just-added feature (same fix as
    /// PCT_Report needed: a newly placed sprite otherwise only paints after
    /// the next unrelated redraw, e.g. a pan/zoom).
    /// </summary>
    public Action? ForceRedraw { get; set; }

    /// <summary>Raised with the current OSM-style zoom level (0-19) whenever the viewport changes.</summary>
    public event Action<int>? ZoomLevelChanged;

    public void ZoomIn() => Map.Navigator.ZoomIn(200, null);

    /// <summary>One zoom step in, keeping the given screen point where it is (double-click).</summary>
    public void ZoomInAt(double screenX, double screenY) =>
        Map.Navigator.ZoomIn(new Mapsui.Manipulations.ScreenPosition(screenX, screenY), 200, null);

    public void ZoomOut() => Map.Navigator.ZoomOut(200, null);

    /// <summary>Centre the view on (lat, lon) at the given OSM-style zoom level (0-19).</summary>
    public void CenterOn(double lat, double lon, int zoom)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        double resolution = 156543.03392804097 / Math.Pow(2, Math.Clamp(zoom, 0, 19));
        Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution, 0, null);
    }

    /// <summary>Rebuilds the heard-stations layer from the current registry snapshot.</summary>
    public void UpdateHeardStations(IEnumerable<HeardStation> stations)
    {
        _heardStations.Clear();
        _stationIndex.Clear();

        foreach (var s in stations)
        {
            if (!s.HasPosition) continue;
            _stationIndex[s.Callsign] = s;

            var (x, y) = SphericalMercator.FromLonLat(s.Longitude!.Value, s.Latitude!.Value);
            var feature = new GeometryFeature { Geometry = new Point(x, y) };
            foreach (var layer in AprsSymbolSprite.Layers(s.SymbolTable, s.SymbolCode))
                feature.Styles.Add(new ImageStyle
                {
                    Image = layer,
                    SymbolScale = 1.0,   // 24 px sheet at native size, as on aprs.fi
                });
            // A Mic-E emergency: white on red, so it stands out from the other labels.
            feature.Styles.Add(new LabelStyle
            {
                Text = s.Callsign,
                ForeColor = s.IsEmergency ? Color.White : Color.Black,
                BackColor = new Brush(s.IsEmergency ? new Color(198, 40, 40, 230) : new Color(255, 255, 255, 205)),
                Offset = new Offset(0, 19),
                Font = new Font { Size = 11 },
            });
            _heardStations.Add(feature);
        }

        _heardStations.DataHasChanged();
        _pathShownFor = null;   // positions may have moved: redraw on the next hover
        Try(() => Map.RefreshGraphics());
        Try(() => ForceRedraw?.Invoke());
    }

    /// <summary>
    /// Draws how <paramref name="station"/>'s last packet reached us (see
    /// <see cref="ReceptionPath"/>): solid for RF, dashed when it only came
    /// from APRS-IS, with a dot on each relay.
    /// </summary>
    public void ShowReceptionPath(HeardStation station, (double Lat, double Lon)? ownPosition)
    {
        if (_pathShownFor == station.Callsign)
            return;
        _pathShownFor = station.Callsign;
        _receptionPath.Clear();

        var result = ReceptionPath.Compute(station,
            call => _stationIndex.TryGetValue(call, out var s) ? s : null, ownPosition);
        if (result is not null)
            AddPathFeatures(result.Points, result.ViaInternet);

        _receptionPath.DataHasChanged();
        Try(() => Map.RefreshGraphics());
        Try(() => ForceRedraw?.Invoke());
    }

    public void ClearReceptionPath()
    {
        if (_pathShownFor is null)
            return;
        _pathShownFor = null;
        _receptionPath.Clear();
        _receptionPath.DataHasChanged();
        Try(() => Map.RefreshGraphics());
        Try(() => ForceRedraw?.Invoke());
    }

    private void AddPathFeatures(IReadOnlyList<(double Lat, double Lon)> points, bool viaInternet)
    {
        var color = viaInternet ? InternetPathColor : RfPathColor;
        var coords = points.Select(p =>
        {
            var (x, y) = SphericalMercator.FromLonLat(p.Lon, p.Lat);
            return new Coordinate(x, y);
        }).ToArray();

        var line = new GeometryFeature { Geometry = new LineString(coords) };
        line.Styles.Add(new VectorStyle
        {
            Line = new Pen(color, 3) { PenStyle = viaInternet ? PenStyle.Dash : PenStyle.Solid },
        });
        _receptionPath.Add(line);

        // a dot on each relay, so a multi-hop path reads at a glance
        foreach (var c in coords.Skip(1).Take(coords.Length - 2))
        {
            var dot = new GeometryFeature { Geometry = new Point(c.X, c.Y) };
            dot.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.45,
                Fill = new Brush(color),
                Outline = new Pen(Color.White, 1.5),
            });
            _receptionPath.Add(dot);
        }
    }

    /// <summary>Nearest heard station within <paramref name="toleranceScreenPx"/> pixels of a screen point, or null.</summary>
    public HeardStation? HitTestStation(double screenX, double screenY, double toleranceScreenPx)
    {
        var world = Map.Navigator.Viewport.ScreenToWorld(screenX, screenY);
        double toleranceWorld = toleranceScreenPx * Map.Navigator.Viewport.Resolution;

        HeardStation? best = null;
        double bestDist = double.MaxValue;
        foreach (var s in _stationIndex.Values)
        {
            if (!s.HasPosition) continue;
            var (x, y) = SphericalMercator.FromLonLat(s.Longitude!.Value, s.Latitude!.Value);
            double dist = Math.Sqrt(Math.Pow(x - world.X, 2) + Math.Pow(y - world.Y, 2));
            if (dist <= toleranceWorld && dist < bestDist)
            {
                best = s;
                bestDist = dist;
            }
        }
        return best;
    }

    private static void Try(Action a) { try { a(); } catch { /* map torn down / viewport not ready */ } }

    private void RaiseZoomLevelChanged()
    {
        double resolution = Map.Navigator.Viewport.Resolution;
        if (resolution <= 0)
            return;

        // Standard OSM/Web-Mercator resolution-to-zoom relationship (resolution
        // at zoom 0 is ~156543.03 m/px at the equator, halving each level).
        int zoom = (int)Math.Round(Math.Log2(156543.03392804097 / resolution));
        ZoomLevelChanged?.Invoke(Math.Clamp(zoom, 0, 19));
    }

    public void UseOnlineOsm()
    {
        IsOffline = false;
        _offlineExtent = null;
        SwapBaseLayer(OpenStreetMap.CreateTileLayer("APRS_Live"));
    }

    public void UseOfflineMbTiles(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Fichier MBTiles introuvable.", path);

        SQLitePCL.Batteries_V2.Init();
        var source = new BruTile.MbTiles.MbTilesTileSource(new SQLite.SQLiteConnectionString(path));

        // Mapsui's TileLayer renders raster tiles (PNG/JPEG). A vector (pbf/mvt)
        // MBTiles feeds gzipped protobuf to the image decoder -> it throws on
        // the render thread and the whole window stops repainting.
        var format = (source.Schema.Format ?? "").Trim().ToLowerInvariant();
        if (format.Contains("pbf") || format.Contains("mvt") || format.Contains("vector"))
            throw new NotSupportedException(
                "Tuiles vectorielles (pbf) non prises en charge — il faut un .mbtiles de tuiles raster (PNG ou JPEG).");

        SwapBaseLayer(new TileLayer(source) { Name = "MBTiles" });

        var extent = source.Schema.Extent;
        _offlineExtent = new MRect(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
        IsOffline = true;
    }

    /// <summary>Fit the view to the offline tileset's coverage. No-op online.</summary>
    public void RecenterOnOfflineMap()
    {
        if (_offlineExtent is { } box)
            Map.Navigator.ZoomToBox(box, MBoxFit.Fit, 0, null);
    }

    private void SwapBaseLayer(ILayer layer)
    {
        Map.Layers.Remove(_baseLayer);
        _baseLayer = layer;
        Map.Layers.Insert(0, layer);
    }
}

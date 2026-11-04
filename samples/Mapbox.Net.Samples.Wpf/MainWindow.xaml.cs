using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Mapbox.Net.Annotations;
using Mapbox.Net.Controls;
using Mapbox.Net.Events;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Plugins;
using Mapbox.Net.Style;

namespace Mapbox.Net.Samples.Wpf;

public partial class MainWindow : Window
{
    private readonly bool _mapbox;
    private readonly List<IDisposable> _helpers = new();
    private DrawPlugin? _draw;
    private GeocoderPlugin? _geocoder;
    private TurfPlugin? _turf;

    public MainWindow()
    {
        InitializeComponent();

        var token = Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN");
        _mapbox = !string.IsNullOrWhiteSpace(token);
        MapView.Engine = _mapbox ? MapEngine.Mapbox : MapEngine.MapLibre;
        MapView.AccessToken = token;
        MapView.Options.InitialControls.Add(new NavigationControl { VisualizePitch = true });
        MapView.Options.InitialControls.Add(new ScaleControl());
        MapView.Options.InitialControls.Add(new FullscreenControl());
        MapView.HostOptions.EnableDevTools = true;

        EngineText.Text = _mapbox
            ? "Engine: Mapbox GL JS"
            : "Engine: MapLibre GL JS + OpenFreeMap (set MAPBOX_ACCESS_TOKEN to use Mapbox)";

        var styles = _mapbox
            ? new[] { MapStyles.MapboxStandard, MapStyles.MapboxStreets, MapStyles.MapboxOutdoors, MapStyles.MapboxDark, MapStyles.MapboxSatelliteStreets }
            : new[] { MapStyles.OpenFreeMapLiberty, MapStyles.OpenFreeMapBright, MapStyles.OpenFreeMapPositron };
        StyleCombo.ItemsSource = styles;
        StyleCombo.SelectedIndex = 0;

        var map = MapView.Map;
        map.Loaded += (_, e) => Log($"load  engine={map.Engine} v{map.EngineVersion} zoom={e.Camera.Zoom:0.0}");
        map.Click += (_, e) => Log($"click {e.LngLat}  px={e.Point}");
        map.MoveEnded += (_, e) => Log($"moveend user={e.IsUserInitiated} zoom={e.Camera.Zoom:0.00}");
        map.MouseMove += (_, e) => PointerText.Text = $"{e.LngLat.Longitude:0.00000}, {e.LngLat.Latitude:0.00000}";
        map.Error += (_, e) => Log($"ERROR {e.Message}");
        map.Log += (_, e) => Log($"[{e.Level}] {e.Message}");
        map.StyleImageMissing += (_, e) => Log($"missing image '{e.ImageId}'");
        MapView.InitializationFailed += (_, e) => MessageBox.Show(this, e.Message, "WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private MapboxMap Map => MapView.Map;

    private void Log(string text)
    {
        EventLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {text}");
        if (EventLog.Items.Count > 300) EventLog.Items.RemoveAt(EventLog.Items.Count - 1);
    }

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Log("EXCEPTION " + ex.Message); }
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StyleCombo.SelectedItem is string style) MapView.MapStyle = style;
    }

    // ---------------------------------------------------------------- camera

    private async void OnFlyParis(object sender, RoutedEventArgs e)
        => await Run(async () =>
        {
            await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.3522, 48.8566), Zoom = 12, Pitch = 0, Bearing = 0 });
            Log("flyTo completed");
        });

    private async void OnFlyNewYork(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(-74.0066, 40.7135), Zoom = 15.5, Pitch = 60, Bearing = -17 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) }));

    private async void OnResetNorth(object sender, RoutedEventArgs e) => await Run(() => Map.ResetNorthAsync(resetPitch: true));

    // ---------------------------------------------------------------- data

    private async void OnAddCities(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (await Map.HasSourceAsync("cities")) return;
        await Map.AddSourceAsync(new GeoJsonSource("cities", SampleData.Cities()) { GenerateId = true });
        await Map.AddLayerAsync(new CircleLayer("cities-circles", "cities")
        {
            CircleRadius = Expr.Interpolate(Interpolation.Linear, Expr.Get("population"), (100_000, 5), (2_000_000, 18)),
            CircleColor = Expr.Case("#2563eb", (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), "#f97316")),
            CircleOpacity = 0.85,
            CircleStrokeColor = "#ffffff",
            CircleStrokeWidth = 2,
            CircleRadiusTransition = StyleTransition.FromMilliseconds(300),
        });
        await Map.AddLayerAsync(new SymbolLayer("cities-labels", "cities")
        {
            TextField = Expr.Get("name"),
            TextSize = 13,
            TextOffset = (0, 1.4),
            TextAnchor = Anchor.Top,
            TextColor = "#111827",
            TextHaloColor = "#ffffff",
            TextHaloWidth = 1.5,
        });
        _helpers.Add(await Map.EnableHoverCursorAsync("cities-circles"));
        _helpers.Add(await Map.EnableHoverStateAsync("cities-circles", "cities"));
        _helpers.Add(await Map.ShowPopupOnClickAsync("cities-circles", "<b>{name}</b><br/>{population} inhabitants"));
        _helpers.Add(Map.OnLayer("cities-circles", LayerEvent.Click, (_, args) => Log($"city clicked: {args.Feature?.GetProperty<string>("name")}")));
        await Map.FitToAsync(SampleData.Cities(), new FitBoundsOptions { Padding = 60 });
    });

    private async void OnAddClusters(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (await Map.HasSourceAsync("random")) return;
        var layers = await Map.AddClusteredPointsAsync("random", SampleData.RandomPoints(3000, new LngLatBounds(-5, 42, 8, 51)));
        _helpers.Add(Map.OnLayer(layers.PointLayerId, LayerEvent.Click, (_, args) => Log($"point #{args.Feature?.GetProperty<int>("index")}")));
        await Map.FlyToAsync(new LngLat(2.5, 46.5), 5);
    });

    private async void OnAddHeatmap(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (await Map.HasSourceAsync("heat")) return;
        await Map.AddHeatmapAsync("heat", SampleData.RandomPoints(5000, new LngLatBounds(1.8, 48.6, 2.8, 49.1)), "weight", 10);
        await Map.FlyToAsync(new LngLat(2.35, 48.85), 9);
    });

    private async void OnAdd3DBuildings(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Map.Options.Style == MapStyles.MapboxStandard) { Log("Mapbox Standard already renders 3D buildings."); }
        else if (!await Map.HasLayerAsync("mbxnet-3d-buildings")) await Map.Add3DBuildingsAsync();
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.2945, 48.8584), Zoom = 16, Pitch = 60, Bearing = 30 });
    });

    private async void OnTerrain(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_mapbox) { Log("Terrain needs a DEM source: Mapbox engine, or EnableTerrainAsync(demTilesUrl: ...) with MapLibre."); return; }
        await Map.EnableTerrainAsync(1.5);
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(6.8652, 45.8326), Zoom = 12, Pitch = 70, Bearing = 150 });
    });

    private async void OnClear(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        foreach (var h in _helpers) h.Dispose();
        _helpers.Clear();
        foreach (var id in new[] { "cities", "random", "heat" }) await Map.RemoveSourceAndLayersAsync(id);
        await Map.TryRemoveLayerAsync("mbxnet-3d-buildings");
        await Map.TryRemoveLayerAsync("turf-buffer");
        await Map.TryRemoveSourceAsync("turf-buffer");
        await Map.ClearMarkersAsync();
        await Map.CloseAllPopupsAsync();
        Log("cleared");
    });

    // ---------------------------------------------------------------- annotations

    private async void OnAddMarker(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var camera = await Map.GetCameraAsync();
        var marker = await Map.AddMarkerAsync(new MarkerOptions(camera.Center)
        {
            Color = "#dc2626",
            Draggable = true,
            Tooltip = "Drag me",
            Popup = new PopupOptions { Html = "<b>Mapbox.Net</b><br/>Drag the marker!" },
        });
        marker.DragEnded += (_, args) => Log($"marker dropped at {args.Position}");
        marker.Click += (_, _) => Log("marker clicked");
    });

    private async void OnAddHtmlControl(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var control = new HtmlControl(
            "<button data-command='paris' title='Paris' style='font-weight:bold'>P</button>" +
            "<button data-command='globe' title='Globe'>🌍</button>", ControlPosition.TopLeft);
        control.CommandInvoked += async (_, args) =>
        {
            if (args.Command == "paris") await Run(() => Map.FlyToAsync(new LngLat(2.3522, 48.8566), 12));
            else await Run(async () => { await Map.SetProjectionAsync("globe"); await Map.FlyToAsync(new LngLat(10, 30), 1.5); });
        };
        await Map.AddControlAsync(control);
    });

    // ---------------------------------------------------------------- plugins

    private async void OnDraw(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_draw is not null) return;
        _draw = new DrawPlugin { DisplayControlsDefault = false, Controls = new DrawControls { Point = true, LineString = true, Polygon = true, Trash = true } };
        _draw.Created += (_, a) => Log($"draw.create {a.Features.Count} feature(s): {string.Join(", ", a.Features.Select(f => f.Geometry?.Type))}");
        _draw.Updated += (_, a) => Log($"draw.update ({a.Action})");
        _draw.Deleted += (_, a) => Log($"draw.delete {a.Features.Count}");
        await Map.AddPluginAsync(_draw);
        await _draw.ChangeModeAsync(DrawModes.DrawPolygon);
        Log("Draw a polygon (double-click to finish).");
    });

    private async void OnGeocoder(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_geocoder is not null) return;
        _geocoder = new GeocoderPlugin(ControlPosition.TopLeft) { Placeholder = "Search a place…", Language = "fr", Limit = 5 };
        _geocoder.ResultSelected += (_, a) => Log($"geocoder: {a.PlaceName} {a.Position}");
        await Map.AddPluginAsync(_geocoder);
    });

    private async void OnTurf(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        _turf ??= await Map.AddPluginAsync(new TurfPlugin());
        var center = (await Map.GetCameraAsync()).Center;
        var buffer = await _turf.BufferAsync(new GeoJson.Point(center.Longitude, center.Latitude), 2, TurfUnits.Kilometers);
        var area = await _turf.AreaAsync(buffer);
        await Map.TryRemoveLayerAsync("turf-buffer");
        await Map.TryRemoveSourceAsync("turf-buffer");
        await Map.AddSourceAsync(new GeoJsonSource("turf-buffer", buffer));
        await Map.AddLayerAsync(new FillLayer("turf-buffer", "turf-buffer") { FillColor = "#10b981", FillOpacity = 0.3, FillOutlineColor = "#047857" });
        Log($"turf buffer area: {area / 1_000_000:0.00} km²");
    });

    // ---------------------------------------------------------------- export

    private async void OnScreenshot(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"mapbox-net-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        await MapView.SaveScreenshotAsync(path);
        Log("screenshot saved: " + path);
    });

    private async void OnPdf(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"mapbox-net-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        Log(await MapView.PrintToPdfAsync(path) ? "PDF saved: " + path : "PDF export failed");
    });
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.GeoJson;
using Cartovia.Plugins;
using Cartovia.Style;

namespace Cartovia.Samples.Wpf;

public partial class MainWindow : Window
{
    private const string BuildingsLayer = "cartovia-3d-buildings";

    private readonly bool _mapbox;
    private readonly Dictionary<string, List<IDisposable>> _helpers = new();
    private DrawPlugin? _draw;
    private GeocoderPlugin? _geocoder;
    private TurfPlugin? _turf;

    public MainWindow()
    {
        InitializeComponent();

        // Mapbox when a token is available, otherwise MapLibre + OpenFreeMap (no account needed).
        var token = Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN");
        _mapbox = !string.IsNullOrWhiteSpace(token);
        MapView.Engine = _mapbox ? MapEngine.Mapbox : MapEngine.MapLibre;
        MapView.AccessToken = token;
        MapView.Options.InitialControls.Add(new NavigationControl { VisualizePitch = true });
        MapView.Options.InitialControls.Add(new ScaleControl());
        MapView.Options.AttributionControl = true;
        MapView.HostOptions.EnableDevTools = true;
        MapView.HostOptions.DefaultBackgroundColor = System.Drawing.Color.FromArgb(10, 15, 28);

        BuildStyleChips();

        var map = MapView.Map;
        map.Loaded += OnMapLoaded;
        map.Click += (_, e) => Log($"click        {e.LngLat}  px {e.Point}");
        map.MoveEnded += (_, e) => Log($"moveend      zoom {e.Camera.Zoom:0.00}  user={e.IsUserInitiated}");
        map.MouseMove += (_, e) => PointerText.Text = e.LngLat.ToString();
        map.StyleLoaded += async (_, _) =>
        {
            Log("style.load");
            // Built-in 3D buildings differ between styles: re-apply the toggle to the new style.
            if (map.IsLoaded) await Run(SetBuildingsAsync);
        };
        map.UserLayersRestored += (_, _) => Log("user layers restored after style change");
        map.Error += (_, e) => Log($"error        {e.Message}");
        map.Log += (_, e) => Log($"[{e.Level}] {e.Message}");
        MapView.InitializationFailed += (_, e) => MessageBox.Show(this, e.Message, "WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private CartoviaMap Map => MapView.Map;

    // ---------------------------------------------------------------- startup

    private async void OnMapLoaded(object? sender, MapCameraEventArgs e)
    {
        EngineDot.Fill = (Brush)FindResource("SuccessBrush");
        EngineText.Text = $"{(_mapbox ? "Mapbox GL JS" : "MapLibre GL JS")} {Map.EngineVersion}";
        Log($"load         {Map.Engine} {Map.EngineVersion}");
        if (!_mapbox) TerrainToggle.ToolTip = "Uses the public AWS Terrain Tiles (no key).";

        // Showcase defaults: 3D buildings and a marker with its popup open.
        Sidebar.ScrollToHome();
        BuildingsToggle.IsChecked = true;
        await Run(SetBuildingsAsync);
        await Run(async () =>
        {
            var marker = await Map.AddMarkerAsync(new MarkerOptions(new LngLat(2.34988, 48.85296))
            {
                Color = "#6366F1",
                Popup = new PopupOptions
                {
                    Html = "<div style='font:13px Segoe UI;line-height:1.45'><b>Notre-Dame de Paris</b><br/>" +
                           "<span style='color:#64748b'>Marker + popup from C#</span></div>",
                    Offset = 32,
                    CloseButton = false,
                },
            });
            await marker.TogglePopupAsync();
        });
    }

    private void BuildStyleChips()
    {
        var styles = _mapbox
            ? new (string Name, string Url)[] { ("Standard", MapStyles.MapboxStandard), ("Streets", MapStyles.MapboxStreets), ("Dark", MapStyles.MapboxDark), ("Satellite", MapStyles.MapboxSatelliteStreets) }
            : new (string Name, string Url)[] { ("Liberty", MapStyles.OpenFreeMapLiberty), ("Bright", MapStyles.OpenFreeMapBright), ("Positron", MapStyles.OpenFreeMapPositron) };
        MapView.MapStyle = styles[0].Url;
        foreach (var (name, url) in styles)
        {
            var chip = new RadioButton { Content = name, GroupName = "styles", Style = (System.Windows.Style)FindResource("Chip"), IsChecked = url == styles[0].Url };
            chip.Checked += (_, _) =>
            {
                MapView.MapStyle = url;
                Log($"style        {name}");
            };
            StylePanel.Children.Add(chip);
        }
    }

    // ---------------------------------------------------------------- helpers

    private void Log(string text)
    {
        EventLog.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {text}");
        if (EventLog.Items.Count > 500) EventLog.Items.RemoveAt(EventLog.Items.Count - 1);
    }

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Log("exception    " + ex.Message); }
    }

    private void Track(string key, IDisposable d)
    {
        if (!_helpers.TryGetValue(key, out var list)) _helpers[key] = list = new List<IDisposable>();
        list.Add(d);
    }

    private void Release(string key)
    {
        if (!_helpers.TryGetValue(key, out var list)) return;
        foreach (var d in list) d.Dispose();
        _helpers.Remove(key);
    }

    private static bool On(object sender) => ((ToggleButton)sender).IsChecked == true;

    // ---------------------------------------------------------------- camera

    private async void OnFlyParis(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.3470, 48.8540), Zoom = 15.6, Pitch = 60, Bearing = -24 }));

    private async void OnFlyNewYork(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(-74.0066, 40.7135), Zoom = 15.5, Pitch = 60, Bearing = -17 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) }));

    private async void OnFlyMontBlanc(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (TerrainToggle.IsChecked != true) { TerrainToggle.IsChecked = true; await SetTerrainAsync(); }
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(6.8652, 45.8326), Zoom = 12.3, Pitch = 72, Bearing = 160 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) });
    });

    private async void OnFlyTokyo(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(139.7671, 35.6812), Zoom = 14, Pitch = 45, Bearing = 25 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(7) }));

    private async void OnResetNorth(object sender, RoutedEventArgs e) => await Run(() => Map.ResetNorthAsync(resetPitch: true));

    // ---------------------------------------------------------------- layers

    private async void OnToggleCities(object sender, RoutedEventArgs e) => await Run(SetCitiesAsync);

    private async Task SetCitiesAsync()
    {
        if (CitiesToggle.IsChecked != true)
        {
            Release("cities");
            await Map.RemoveSourceAndLayersAsync("cities");
            return;
        }
        if (await Map.HasSourceAsync("cities")) return;
        await Map.AddSourceAsync(new GeoJsonSource("cities", SampleData.Cities()) { GenerateId = true });
        await Map.AddLayerAsync(new CircleLayer("cities-circles", "cities")
        {
            CircleRadius = Expr.Interpolate(Interpolation.Linear, Expr.Get("population"), (20_000, 6), (2_000_000, 20)),
            CircleColor = Expr.Case("#6366F1", (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), "#F97316")),
            CircleOpacity = 0.9,
            CircleStrokeColor = "#ffffff",
            CircleStrokeWidth = 2.5,
            CirclePitchAlignment = CirclePitchAlignment.Map,
        });
        await Map.AddLayerAsync(new SymbolLayer("cities-labels", "cities")
        {
            TextField = Expr.Get("name"),
            TextSize = 13,
            TextOffset = (0, 1.6),
            TextAnchor = Anchor.Top,
            TextColor = "#1E1B4B",
            TextHaloColor = "#ffffff",
            TextHaloWidth = 2,
        });
        Track("cities", await Map.EnableHoverCursorAsync("cities-circles"));
        Track("cities", await Map.EnableHoverStateAsync("cities-circles", "cities"));
        Track("cities", await Map.ShowPopupOnClickAsync("cities-circles",
            "<div style='font:13px Segoe UI'><b>{name}</b><br/><span style='color:#64748b'>{population} inhabitants</span></div>"));
        Track("cities", Map.OnLayer("cities-circles", LayerEvent.Click, (_, a) => Log($"layer click  {a.Feature?.GetProperty<string>("name")}")));
    }

    private async void OnToggleClusters(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!On(sender)) { Release("clusters"); await Map.RemoveSourceAndLayersAsync("random"); return; }
        var layers = await Map.AddClusteredPointsAsync("random", SampleData.RandomPoints(4000, new LngLatBounds(-5, 42.3, 8, 51)),
            new ClusterStyle
            {
                Colors = new List<(int, string)> { (0, "#818CF8"), (100, "#6366F1"), (750, "#4338CA") },
                PointColor = "#22D3EE",
            });
        Track("clusters", Map.OnLayer(layers.PointLayerId, LayerEvent.Click, (_, a) => Log($"point        #{a.Feature?.GetProperty<int>("index")}")));
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.5, 46.6), Zoom = 5.2, Pitch = 0, Bearing = 0 });
    });

    private async void OnToggleHeatmap(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!On(sender)) { await Map.RemoveSourceAndLayersAsync("heat"); return; }
        await Map.AddHeatmapAsync("heat", SampleData.RandomPoints(6000, new LngLatBounds(2.1, 48.75, 2.6, 49.0), seed: 7), "weight", 10, 25);
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.35, 48.86), Zoom = 10.5, Pitch = 0, Bearing = 0 });
    });

    private async void OnToggleBuildings(object sender, RoutedEventArgs e) => await Run(SetBuildingsAsync);

    private async Task SetBuildingsAsync()
    {
        bool on = BuildingsToggle.IsChecked == true;
        if (Map.Options.Style == MapStyles.MapboxStandard)
        {
            await Map.SetConfigPropertyAsync("basemap", "show3dObjects", on);
            return;
        }
        // Styles that already ship 3D buildings (e.g. OpenFreeMap Liberty): toggle their visibility.
        var builtIn = await Map.EvalAsync<string[]>(
            "return map.getStyle().layers.filter(function (l) { return l.type === 'fill-extrusion' && l.id !== args; }).map(function (l) { return l.id; });",
            BuildingsLayer) ?? Array.Empty<string>();
        foreach (var id in builtIn) await Map.SetLayerVisibilityAsync(id, on);
        if (builtIn.Length > 0) { await Map.TryRemoveLayerAsync(BuildingsLayer); return; }

        if (on && !await Map.HasLayerAsync(BuildingsLayer)) await Map.Add3DBuildingsAsync(color: "#cbd5e1", layerId: BuildingsLayer);
        else if (!on) await Map.TryRemoveLayerAsync(BuildingsLayer);
    }

    private async void OnToggleTerrain(object sender, RoutedEventArgs e) => await Run(SetTerrainAsync);

    private async Task SetTerrainAsync()
    {
        if (TerrainToggle.IsChecked == true) await Map.EnableTerrainAsync(1.4);
        else await Map.DisableTerrainAsync();
    }

    private async void OnToggleGlobe(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await Map.SetProjectionAsync(On(sender) ? "globe" : "mercator");
        if (On(sender)) await Map.FlyToAsync(new CameraOptions { Center = new LngLat(10, 25), Zoom = 1.6, Pitch = 0, Bearing = 0 });
    });

    // ---------------------------------------------------------------- cadastre (France, IGN Parcellaire Express)

    // Official IGN vector tiles of the French land registry (Géoplateforme, open data, no key).
    // Source layers: parcelle, feuille (cadastral sheets), commune, localisant (label points), batiment…
    private const string CadastreTiles = "https://data.geopf.fr/tms/1.0.0/PCI/{z}/{x}/{y}.pbf";
    private FeatureIdentifier? _selectedParcel;

    private async void OnFlySaintEmilion(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (CadastreToggle.IsChecked != true) { CadastreToggle.IsChecked = true; await SetCadastreAsync(); }
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(-0.1554, 44.8935), Zoom = 17.2, Pitch = 0, Bearing = 0 });
    });

    private async void OnToggleCadastre(object sender, RoutedEventArgs e) => await Run(SetCadastreAsync);

    private async Task SetCadastreAsync()
    {
        bool on = CadastreToggle.IsChecked == true;
        ParcelPanel.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (!on)
        {
            Release("cadastre");
            _selectedParcel = null;
            ShowParcel(null);
            await Map.RemoveSourceAndLayersAsync("cadastre");
            return;
        }
        if (await Map.HasSourceAsync("cadastre")) return;

        await Map.AddSourceAsync(new VectorSource("cadastre")
        {
            Tiles = new[] { CadastreTiles },
            MinZoom = 5,
            MaxZoom = 19,
            // Parcels get their unique id (IDU, e.g. "33394000AB0123") as feature id: required by feature-state.
            PromoteId = new Dictionary<string, string> { ["parcelle"] = "idu" },
            Attribution = "© <a href=\"https://geoservices.ign.fr/\">IGN</a> – Parcellaire Express (PCI)",
        });

        // Parcels go below the labels of the base style so street names stay readable.
        var beforeId = await Map.EvalAsync<string>(
            "var l = map.getStyle().layers.find(function (l) { return l.type === 'symbol'; }); return l ? l.id : null;");
        var hovered = Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true);
        var selected = Expr.Equal(Expr.Coalesce(Expr.FeatureState("selected"), false), true);

        await Map.AddLayerAsync(new FillLayer("cadastre-parcels-fill", "cadastre")
        {
            SourceLayer = "parcelle",
            MinZoom = 15,
            FillColor = Expr.Case("#6366F1", (selected, "#F97316")),
            FillOpacity = Expr.Case(0.05, (selected, 0.45), (hovered, 0.32)),
        }, beforeId);
        await Map.AddLayerAsync(new LineLayer("cadastre-parcels-line", "cadastre")
        {
            SourceLayer = "parcelle",
            MinZoom = 15,
            LineColor = "#4F46E5",
            LineOpacity = 0.75,
            LineWidth = Expr.Interpolate(Interpolation.Linear, Expr.Zoom(), (15, 0.4), (19, 1.4)),
        }, beforeId);
        await Map.AddLayerAsync(new LineLayer("cadastre-sheets", "cadastre")
        {
            SourceLayer = "feuille",
            MinZoom = 15,
            LineColor = "#312E81",
            LineWidth = 1.6,
            LineDasharray = new[] { 3.0, 2.0 },
        }, beforeId);
        await Map.AddLayerAsync(new LineLayer("cadastre-communes", "cadastre")
        {
            SourceLayer = "commune",
            LineColor = "#7C3AED",
            LineWidth = Expr.Interpolate(Interpolation.Linear, Expr.Zoom(), (11, 1.2), (17, 3.5)),
            LineOpacity = 0.8,
        }, beforeId);
        // Outline of the hovered / selected parcel, drawn above everything else.
        await Map.AddLayerAsync(new LineLayer("cadastre-parcels-highlight", "cadastre")
        {
            SourceLayer = "parcelle",
            MinZoom = 15,
            LineColor = Expr.Case("#4338CA", (selected, "#EA580C")),
            LineWidth = 3,
            LineOpacity = Expr.Case(0.0, (selected, 1.0), (hovered, 1.0)),
        });
        await Map.AddLayerAsync(new SymbolLayer("cadastre-numbers", "cadastre")
        {
            SourceLayer = "localisant",
            MinZoom = 17,
            TextField = Expr.Get("numero"),
            TextFont = await GetLabelFontAsync(),
            TextSize = 11,
            TextColor = "#312E81",
            TextHaloColor = "#ffffff",
            TextHaloWidth = 1.5,
        });

        // Hover effect (feature-state "hover", handled in the page: no round trip per mouse move).
        Track("cadastre", await Map.EnableHoverCursorAsync("cadastre-parcels-fill"));
        Track("cadastre", await Map.EnableHoverStateAsync("cadastre-parcels-fill", "cadastre", "parcelle"));
        // The hovered parcel is also reported to C#, here to show it in the details card.
        Track("cadastre", Map.OnLayer("cadastre-parcels-fill", LayerEvent.MouseMove, (_, a) =>
        {
            if (_selectedParcel is null && a.Features.Count > 0) ShowParcel(a.Features[0]);
        }));
        Track("cadastre", Map.OnLayer("cadastre-parcels-fill", LayerEvent.MouseLeave, (_, _) =>
        {
            if (_selectedParcel is null) ShowParcel(null);
        }));
        // Selection: handled in C# so the application keeps the selected parcel.
        Track("cadastre", Map.OnLayer("cadastre-parcels-fill", LayerEvent.Click, async (_, a) => await Run(async () =>
        {
            if (a.Features.Count == 0) return;
            var feature = a.Features[0];
            var id = FeatureIdentifier.From(feature);
            if (_selectedParcel is not null) await Map.RemoveFeatureStateAsync(_selectedParcel, "selected");
            if (_selectedParcel == id) { _selectedParcel = null; ShowParcel(feature); return; }
            _selectedParcel = id;
            await Map.SetFeatureStateAsync(id, new { selected = true });
            ShowParcel(feature);
            Log($"parcel       {id.Id}  {feature.GetProperty<string>("nom_com")} {feature.GetProperty<string>("section")} {feature.GetProperty<string>("numero")}");
        })));

        var zoom = (await Map.GetCameraAsync()).Zoom;
        if (zoom < 15) ParcelHint.Text = "Zoom in (level 15+) to see the parcels, or fly to Saint-Émilion.";
    }

    private async Task<string[]> GetLabelFontAsync()
    {
        // Fonts depend on the glyphs of the base style: reuse the font of one of its labels.
        var font = await Map.EvalAsync<string[]>(
            "var l = map.getStyle().layers.find(function (l) { return l.type === 'symbol' && l.layout && Array.isArray(l.layout['text-font']); });" +
            "if (!l) return null; var f = l.layout['text-font']; if (f[0] === 'literal') f = f[1];" +
            "return Array.isArray(f) && f.every(function (s) { return typeof s === 'string'; }) ? f : null;");
        return font is { Length: > 0 } ? font : new[] { "Open Sans Regular", "Arial Unicode MS Regular" };
    }

    private void ShowParcel(QueriedFeature? feature)
    {
        if (feature is null)
        {
            ParcelDetails.Visibility = System.Windows.Visibility.Collapsed;
            ParcelHint.Visibility = System.Windows.Visibility.Visible;
            ParcelHint.Text = "Hover a parcel, click to select it.";
            return;
        }
        ParcelHint.Visibility = _selectedParcel is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ParcelHint.Text = "Click to select this parcel.";
        ParcelDetails.Visibility = System.Windows.Visibility.Visible;
        ParcelCommune.Text = $"{feature.GetProperty<string>("nom_com")} ({feature.GetProperty<string>("code_insee")})";
        ParcelSection.Text = feature.GetProperty<string>("section") ?? "—";
        ParcelNumber.Text = feature.GetProperty<string>("numero") ?? "—";
        var area = feature.GetProperty<double?>("contenance");
        ParcelArea.Text = area is { } m2 ? $"{m2:N0} m²" : "—";
        ParcelId.Text = feature.GetProperty<string>("idu") ?? "—";
    }

    // ---------------------------------------------------------------- tools

    private async void OnToggleDraw(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!On(sender)) { if (_draw is not null) await _draw.RemoveAsync(); _draw = null; return; }
        _draw = new DrawPlugin(ControlPosition.TopLeft)
        {
            DisplayControlsDefault = false,
            Controls = new DrawControls { Point = true, LineString = true, Polygon = true, Trash = true },
        };
        _draw.Created += (_, a) => Log($"draw.create  {string.Join(", ", a.Features.Select(f => f.Geometry?.Type))}");
        _draw.Updated += (_, a) => Log($"draw.update  {a.Action}");
        _draw.Deleted += (_, a) => Log($"draw.delete  {a.Features.Count} feature(s)");
        await Map.AddPluginAsync(_draw);
        await _draw.ChangeModeAsync(DrawModes.DrawPolygon);
        Log("draw         click to add vertices, double-click to finish");
    });

    private async void OnToggleSearch(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!On(sender)) { if (_geocoder is not null) await _geocoder.RemoveAsync(); _geocoder = null; return; }
        _geocoder = new GeocoderPlugin(ControlPosition.TopLeft) { Placeholder = "Search a place…", Limit = 6 };
        _geocoder.ResultSelected += (_, a) => Log($"search       {a.PlaceName}");
        await Map.AddPluginAsync(_geocoder);
    });

    private async void OnAddMarker(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var camera = await Map.GetCameraAsync();
        var marker = await Map.AddMarkerAsync(new MarkerOptions(camera.Center)
        {
            Color = "#F97316",
            Draggable = true,
            Tooltip = "Drag me",
            Popup = new PopupOptions { Html = "<div style='font:13px Segoe UI'><b>Cartovia</b><br/>Drag the marker around.</div>", Offset = 28 },
        });
        marker.DragEnded += (_, a) => Log($"marker       dropped at {a.Position}");
        Log($"marker       added at {camera.Center}");
    });

    private async void OnTurf(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        _turf ??= await Map.AddPluginAsync(new TurfPlugin());
        var center = (await Map.GetCameraAsync()).Center;
        var buffer = await _turf.BufferAsync(new GeoJson.Point(center.Longitude, center.Latitude), 2, TurfUnits.Kilometers);
        var area = await _turf.AreaAsync(buffer);
        await Map.RemoveSourceAndLayersAsync("buffer");
        await Map.AddSourceAsync(new GeoJsonSource("buffer", buffer));
        await Map.AddLayerAsync(new FillLayer("buffer-fill", "buffer") { FillColor = "#22D3EE", FillOpacity = 0.18 });
        await Map.AddLayerAsync(new LineLayer("buffer-line", "buffer") { LineColor = "#0891B2", LineWidth = 2, LineDasharray = new[] { 2.0, 1.5 } });
        Log($"turf         2 km buffer, area {area / 1_000_000:0.00} km²");
    });

    // ---------------------------------------------------------------- export & log

    private async void OnScreenshot(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"cartovia-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        await MapView.SaveScreenshotAsync(path);
        Log("screenshot   " + path);
    });

    private async void OnPdf(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"cartovia-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        Log(await MapView.PrintToPdfAsync(path) ? "pdf          " + path : "pdf          export failed");
    });

    private void OnShowLog(object sender, RoutedEventArgs e)
    {
        LogToggle.IsChecked = LogToggle.IsChecked != true;
        OnToggleLog(sender, e);
    }

    private void OnToggleLog(object sender, RoutedEventArgs e)
        => LogPanel.Visibility = LogToggle.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    // ---------------------------------------------------------------- dark title bar (Windows 10 20H1+ / 11)

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        int caption = 0x001C0F0A; // COLORREF (BGR) of #0A0F1C
        _ = DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

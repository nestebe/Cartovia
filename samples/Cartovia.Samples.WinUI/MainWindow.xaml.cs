using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.GeoJson;
using Cartovia.Style;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Cartovia.Samples.WinUI;

public sealed partial class MainWindow : Window
{
    private const string BuildingsLayer = "cartovia-3d-buildings";
    private readonly bool _mapbox;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        // Size in effective pixels (AppWindow works in physical pixels) and dark title bar.
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1480 * scale), (int)(940 * scale)));
        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = ColorHelper.FromArgb(255, 0x0A, 0x0F, 0x1C);
        bar.ForegroundColor = bar.ButtonForegroundColor = Colors.White;
        bar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 0x1D, 0x28, 0x40);

        var token = Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN");
        _mapbox = !string.IsNullOrWhiteSpace(token);
        MapView.Engine = _mapbox ? MapEngine.Mapbox : MapEngine.MapLibre;
        MapView.AccessToken = token;
        MapView.Options.InitialControls.Add(new NavigationControl { VisualizePitch = true });
        MapView.Options.InitialControls.Add(new ScaleControl());
        MapView.HostOptions.EnableDevTools = true;

        var styles = _mapbox
            ? new (string Name, string Url)[] { ("Standard", MapStyles.MapboxStandard), ("Streets", MapStyles.MapboxStreets), ("Dark", MapStyles.MapboxDark) }
            : new (string Name, string Url)[] { ("Liberty", MapStyles.OpenFreeMapLiberty), ("Bright", MapStyles.OpenFreeMapBright), ("Positron", MapStyles.OpenFreeMapPositron) };
        MapView.MapStyle = styles[0].Url;
        foreach (var (name, url) in styles)
        {
            var chip = new Button { Content = name, Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["Pill"] };
            chip.Click += (_, _) => MapView.MapStyle = url;
            StylePanel.Children.Add(chip);
        }

        var map = MapView.Map;
        map.Loaded += OnMapLoaded;
        map.MoveEnded += (_, e) => UpdateStatus(e.Camera);
        map.StyleLoaded += async (_, _) => { if (map.IsLoaded) await Run(SetBuildingsAsync); };
    }

    private CartoviaMap Map => MapView.Map;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private async void OnMapLoaded(object? sender, MapCameraEventArgs e)
    {
        EngineDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 0x22, 0xC5, 0x5E));
        EngineText.Text = $"{(_mapbox ? "Mapbox GL JS" : "MapLibre GL JS")} {Map.EngineVersion}";
        UpdateStatus(e.Camera);
        _loaded = true;
        BuildingsToggle.IsOn = true;
        await Run(async () =>
        {
            var marker = await Map.AddMarkerAsync(new MarkerOptions(new LngLat(2.34988, 48.85296))
            {
                Color = "#6366F1",
                Popup = new PopupOptions
                {
                    Html = "<div style='font:13px Segoe UI;line-height:1.45'><b>Notre-Dame de Paris</b><br/>" +
                           "<span style='color:#64748b'>Marker + popup from C# (WinUI 3)</span></div>",
                    Offset = 32,
                    CloseButton = false,
                },
            });
            await marker.TogglePopupAsync();
        });
    }

    private void UpdateStatus(CameraState c)
        => StatusText.Text = string.Create(CultureInfo.InvariantCulture,
            $"{c.Center}    Zoom {c.Zoom:0.00}    Bearing {c.Bearing:0}°    Pitch {c.Pitch:0}°");

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { EngineText.Text = ex.Message; }
    }

    private async void OnFlyParis(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.3470, 48.8540), Zoom = 15.6, Pitch = 60, Bearing = -24 }));

    private async void OnFlyNewYork(object sender, RoutedEventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(-74.0066, 40.7135), Zoom = 15.5, Pitch = 60, Bearing = -17 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) }));

    private async void OnFlyMontBlanc(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        TerrainToggle.IsOn = true;
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(6.8652, 45.8326), Zoom = 12.3, Pitch = 72, Bearing = 160 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) });
    });

    private async void OnToggleBuildings(object sender, RoutedEventArgs e) { if (_loaded) await Run(SetBuildingsAsync); }

    private async Task SetBuildingsAsync()
    {
        bool on = BuildingsToggle.IsOn;
        if (Map.Options.Style == MapStyles.MapboxStandard) { await Map.SetConfigPropertyAsync("basemap", "show3dObjects", on); return; }
        var builtIn = await Map.EvalAsync<string[]>(
            "return map.getStyle().layers.filter(function (l) { return l.type === 'fill-extrusion' && l.id !== args; }).map(function (l) { return l.id; });",
            BuildingsLayer) ?? Array.Empty<string>();
        foreach (var id in builtIn) await Map.SetLayerVisibilityAsync(id, on);
        if (builtIn.Length > 0) { await Map.TryRemoveLayerAsync(BuildingsLayer); return; }
        if (on && !await Map.HasLayerAsync(BuildingsLayer)) await Map.Add3DBuildingsAsync(color: "#cbd5e1", layerId: BuildingsLayer);
        else if (!on) await Map.TryRemoveLayerAsync(BuildingsLayer);
    }

    private async void OnToggleCities(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (!CitiesToggle.IsOn) { await Map.RemoveSourceAndLayersAsync("cities"); return; }
        if (await Map.HasSourceAsync("cities")) return;
        var layers = await Map.AddGeoJsonAsync("cities", SampleData.Cities(), "#6366F1");
        await Map.ShowPopupOnClickAsync(layers[0], "<b>{name}</b><br/>{population} inhabitants");
        await Map.FitToAsync(SampleData.Cities(), new FitBoundsOptions { Padding = 60 });
    });

    private async void OnToggleClusters(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (!ClustersToggle.IsOn) { await Map.RemoveSourceAndLayersAsync("random"); return; }
        await Map.AddClusteredPointsAsync("random", SampleData.RandomPoints(4000, new LngLatBounds(-5, 42.3, 8, 51)),
            new ClusterStyle { Colors = new List<(int, string)> { (0, "#818CF8"), (100, "#6366F1"), (750, "#4338CA") }, PointColor = "#22D3EE" });
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.5, 46.6), Zoom = 5.2, Pitch = 0, Bearing = 0 });
    });

    private async void OnToggleTerrain(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (TerrainToggle.IsOn) await Map.EnableTerrainAsync(1.4); else await Map.DisableTerrainAsync();
    });

    private async void OnAddMarker(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var camera = await Map.GetCameraAsync();
        await Map.AddMarkerAsync(new MarkerOptions(camera.Center) { Color = "#F97316", Draggable = true, Tooltip = "Drag me" });
    });

    private async void OnScreenshot(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"cartovia-winui-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        await MapView.SaveScreenshotAsync(path);
        EngineText.Text = "Saved " + Path.GetFileName(path);
    });
}

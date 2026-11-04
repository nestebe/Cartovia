using System.Globalization;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.Style;

namespace Cartovia.Samples.Maui;

public partial class MainPage : ContentPage
{
    private const string BuildingsLayer = "cartovia-3d-buildings";
    private readonly bool _mapbox;
    private bool _loaded;

    public MainPage()
    {
        InitializeComponent();

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
            var chip = new Button { Text = name, Style = (Microsoft.Maui.Controls.Style)Application.Current!.Resources["Pill"] };
            chip.Clicked += (_, _) => MapView.MapStyle = url;
            StylePanel.Children.Add(chip);
        }

        MapView.MapLoaded += OnMapLoaded;
        MapView.CameraChanged += (_, e) => UpdateStatus(e.Camera);
        MapView.Map.StyleLoaded += async (_, _) => { if (_loaded) await Run(SetBuildingsAsync); };
    }

    private CartoviaMap Map => MapView.Map;

    private async void OnMapLoaded(object? sender, MapCameraEventArgs e)
    {
        EngineDot.Fill = new SolidColorBrush(Color.FromArgb("#22C55E"));
        EngineText.Text = $"{(_mapbox ? "Mapbox GL JS" : "MapLibre GL JS")} {Map.EngineVersion}";
        UpdateStatus(e.Camera);
        _loaded = true;
        BuildingsSwitch.IsToggled = true;
        await Run(async () =>
        {
            var marker = await Map.AddMarkerAsync(new MarkerOptions(new LngLat(2.34988, 48.85296))
            {
                Color = "#6366F1",
                Popup = new PopupOptions
                {
                    Html = "<div style='font:13px Segoe UI;line-height:1.45'><b>Notre-Dame de Paris</b><br/>" +
                           "<span style='color:#64748b'>Marker + popup from C# (.NET MAUI)</span></div>",
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

    private async void OnFlyParis(object? sender, EventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.3470, 48.8540), Zoom = 15.6, Pitch = 60, Bearing = -24 }));

    private async void OnFlyNewYork(object? sender, EventArgs e)
        => await Run(() => Map.FlyToAsync(new CameraOptions { Center = new LngLat(-74.0066, 40.7135), Zoom = 15.5, Pitch = 60, Bearing = -17 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) }));

    private async void OnFlyMontBlanc(object? sender, EventArgs e) => await Run(async () =>
    {
        TerrainSwitch.IsToggled = true;
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(6.8652, 45.8326), Zoom = 12.3, Pitch = 72, Bearing = 160 },
            new AnimationOptions { Duration = TimeSpan.FromSeconds(6) });
    });

    private async void OnToggleBuildings(object? sender, ToggledEventArgs e) { if (_loaded) await Run(SetBuildingsAsync); }

    private async Task SetBuildingsAsync()
    {
        bool on = BuildingsSwitch.IsToggled;
        if (Map.Options.Style == MapStyles.MapboxStandard) { await Map.SetConfigPropertyAsync("basemap", "show3dObjects", on); return; }
        var builtIn = await Map.EvalAsync<string[]>(
            "return map.getStyle().layers.filter(function (l) { return l.type === 'fill-extrusion' && l.id !== args; }).map(function (l) { return l.id; });",
            BuildingsLayer) ?? Array.Empty<string>();
        foreach (var id in builtIn) await Map.SetLayerVisibilityAsync(id, on);
        if (builtIn.Length > 0) { await Map.TryRemoveLayerAsync(BuildingsLayer); return; }
        if (on && !await Map.HasLayerAsync(BuildingsLayer)) await Map.Add3DBuildingsAsync(color: "#cbd5e1", layerId: BuildingsLayer);
        else if (!on) await Map.TryRemoveLayerAsync(BuildingsLayer);
    }

    private async void OnToggleCities(object? sender, ToggledEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (!e.Value) { await Map.RemoveSourceAndLayersAsync("cities"); return; }
        if (await Map.HasSourceAsync("cities")) return;
        var layers = await Map.AddGeoJsonAsync("cities", SampleData.Cities(), "#6366F1");
        await Map.ShowPopupOnClickAsync(layers[0], "<b>{name}</b><br/>{population} inhabitants");
        await Map.FitToAsync(SampleData.Cities(), new FitBoundsOptions { Padding = 60 });
    });

    private async void OnToggleClusters(object? sender, ToggledEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (!e.Value) { await Map.RemoveSourceAndLayersAsync("random"); return; }
        await Map.AddClusteredPointsAsync("random", SampleData.RandomPoints(4000, new LngLatBounds(-5, 42.3, 8, 51)),
            new ClusterStyle { Colors = new List<(int, string)> { (0, "#818CF8"), (100, "#6366F1"), (750, "#4338CA") }, PointColor = "#22D3EE" });
        await Map.FlyToAsync(new CameraOptions { Center = new LngLat(2.5, 46.6), Zoom = 5.2, Pitch = 0, Bearing = 0 });
    });

    private async void OnToggleTerrain(object? sender, ToggledEventArgs e) => await Run(async () =>
    {
        if (!_loaded) return;
        if (e.Value) await Map.EnableTerrainAsync(1.4); else await Map.DisableTerrainAsync();
    });
}

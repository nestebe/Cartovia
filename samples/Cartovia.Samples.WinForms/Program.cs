using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.GeoJson;
using Cartovia.Plugins;
using Cartovia.Style;
using Cartovia.WinForms;

namespace Cartovia.Samples.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly MapView _map = new() { Dock = DockStyle.Fill };
    private readonly ListBox _log = new() { Dock = DockStyle.Bottom, Height = 140, Font = new Font("Consolas", 9) };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
    private readonly ToolStripStatusLabel _status = new();

    public MainForm()
    {
        Text = "Cartovia — WinForms sample";
        Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var token = Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN");
        bool mapbox = !string.IsNullOrWhiteSpace(token);
        _map.Engine = mapbox ? MapEngine.Mapbox : MapEngine.MapLibre;
        _map.AccessToken = token;
        _map.MapStyle = mapbox ? MapStyles.MapboxStreets : MapStyles.OpenFreeMapLiberty;
        _map.Center = new LngLat(2.5753, 44.3506);
        _map.Zoom = 6;
        _map.Options.InitialControls.Add(new NavigationControl());
        _map.Options.InitialControls.Add(new ScaleControl());

        AddButton("Fly to Rodez", async () => await _map.Map.FlyToAsync(new LngLat(2.5753, 44.3506), 13));
        AddButton("Add marker", async () =>
        {
            var marker = await _map.Map.AddMarkerAsync(new MarkerOptions(_map.Center) { Color = "#7c3aed", Draggable = true, Popup = new PopupOptions { Text = "Hello from WinForms" } });
            marker.DragEnded += (_, e) => Log($"marker → {e.Position}");
        });
        AddButton("Add GeoJSON", async () =>
        {
            if (await _map.Map.HasSourceAsync("zone")) return;
            var zone = new FeatureCollection()
                .Add(new Feature(Polygon.Circle(new LngLat(2.5753, 44.3506), 15_000), new { name = "15 km around Rodez" }))
                .Add(new Feature(new LineString(new LngLat(2.5753, 44.3506), new LngLat(1.4442, 43.6047)), new { name = "To Toulouse" }));
            var layers = await _map.Map.AddGeoJsonAsync("zone", zone, "#e11d48");
            await _map.Map.ShowPopupOnClickAsync(layers.First(), "<b>{name}</b>");
            await _map.Map.FitToAsync(zone, new FitBoundsOptions { Padding = 40 });
        });
        AddButton("Draw", async () => await _map.Map.AddPluginAsync(new DrawPlugin()));
        AddButton("Search", async () => await _map.Map.AddPluginAsync(new GeocoderPlugin { Language = "fr" }));
        AddButton("Dark style", async () => await _map.Map.SetStyleAsync(mapbox ? MapStyles.MapboxDark : MapStyles.OpenFreeMapPositron));
        AddButton("Screenshot", async () =>
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "cartovia-winforms.png");
            await _map.SaveScreenshotAsync(path);
            Log("saved " + path);
        });

        _map.MapLoaded += (_, e) => Log($"loaded ({_map.Map.Engine} {_map.Map.EngineVersion})");
        _map.MapClick += (_, e) => Log($"click {e.LngLat}");
        _map.CameraChanged += (_, e) => _status.Text = $"Center {e.Camera.Center}   Zoom {e.Camera.Zoom:0.00}";
        _map.Map.Error += (_, e) => Log("ERROR " + e.Message);
        _map.Map.Log += (_, e) => Log($"[{e.Level}] {e.Message}");

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        Controls.Add(_map);
        Controls.Add(_toolbar);
        Controls.Add(_log);
        Controls.Add(statusStrip);
    }

    private void AddButton(string text, Func<System.Threading.Tasks.Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { Log("EXCEPTION " + ex.Message); }
        };
        _toolbar.Controls.Add(button);
    }

    private void Log(string text)
    {
        _log.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        if (_log.Items.Count > 200) _log.Items.RemoveAt(_log.Items.Count - 1);
    }
}

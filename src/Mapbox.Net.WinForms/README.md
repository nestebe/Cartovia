# Mapbox.Net.WinForms

Windows Forms map control for **Mapbox GL JS** and **MapLibre GL JS**, hosted in WebView2. Targets .NET 6, 8 and 10 (Windows).

```csharp
var mapView = new MapView
{
    Dock = DockStyle.Fill,
    Engine = MapEngine.Mapbox,
    AccessToken = "pk.your-token",
    MapStyle = MapStyles.MapboxStreets,
    Center = new LngLat(2.35, 48.85),
    Zoom = 11,
};
mapView.MapLoaded += async (s, e) =>
{
    var layers = await mapView.Map.AddGeoJsonAsync("zones", myFeatureCollection, "#e11d48");
    await mapView.Map.ShowPopupOnClickAsync(layers[0], "<b>{name}</b>");
};
mapView.MapClick += (s, e) => statusLabel.Text = e.LngLat.ToString();
Controls.Add(mapView);
```

The full API (layers, expressions, markers, popups, controls, plugins, events) is on `mapView.Map`
(see Mapbox.Net.Core). `SaveScreenshotAsync`, `PrintToPdfAsync` and `HostOptions` are available on the control.

The package also removes the `Microsoft.Web.WebView2.Wpf` reference that the WebView2 package adds to Windows Forms
apps (warning MSB3277). Set `MapboxNetKeepWebView2Wpf=true` to keep it.

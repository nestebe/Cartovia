# Windows Forms control

`Cartovia.WinForms.MapView` is a `UserControl` available in the designer toolbox. The full API is on `mapView.Map`.

```csharp
var mapView = new MapView
{
    Dock = DockStyle.Fill,
    Engine = MapEngine.MapLibre,                 // default
    MapStyle = MapStyles.OpenFreeMapLiberty,
    Center = new LngLat(2.35, 48.85),
    Zoom = 11,
};
mapView.Options.InitialControls.Add(new NavigationControl());
mapView.HostOptions.AllowGeolocation = true;
Controls.Add(mapView);
```

## Designer properties

| Category | Property | Notes |
|---|---|---|
| Map | `Engine`, `AccessToken` | Changing them after load recreates the map. |
| Map | `MapStyle` | Changing it after load calls `SetStyleAsync`. |
| Camera | `Center`, `Zoom`, `Bearing`, `Pitch` | Reading returns the current camera; setting moves the map. |

`Options` (`MapOptions`) and `HostOptions` (`WebView2HostOptions`) must be configured before the handle is created
(constructor or `Load` event of the form).

## Events

```csharp
mapView.MapLoaded += async (s, e) =>
{
    var layers = await mapView.Map.AddGeoJsonAsync("zones", zones, "#e11d48");
    await mapView.Map.ShowPopupOnClickAsync(layers[0], "<b>{name}</b>");
};
mapView.MapClick += (s, e) => statusLabel.Text = e.LngLat.ToString();
mapView.CameraChanged += (s, e) => zoomLabel.Text = $"Zoom {e.Camera.Zoom:0.0}";
mapView.InitializationFailed += (s, e) => MessageBox.Show(e.Message);
```

Events are raised on the UI thread: you can update controls directly.

## Export

```csharp
await mapView.SaveScreenshotAsync(@"C:\temp\map.png");
await mapView.PrintToPdfAsync(@"C:\temp\map.pdf");
```

## Note about MSB3277

The WebView2 NuGet package references its WPF assembly in every project. The Cartovia.WinForms package removes that
reference from Windows Forms apps to avoid the `WindowsBase` conflict warning (MSB3277). Set the MSBuild property
`CartoviaKeepWebView2Wpf` to `true` to keep it.

# Mapbox.Net.Wpf

WPF map control for **Mapbox GL JS** and **MapLibre GL JS**, hosted in WebView2. Targets .NET 6, 8 and 10 (Windows).

```xml
<Window xmlns:mbx="clr-namespace:Mapbox.Net.Wpf;assembly=Mapbox.Net.Wpf">
    <mbx:MapView x:Name="MapView"
                 Engine="Mapbox" AccessToken="{Binding MapboxToken}"
                 MapStyle="mapbox://styles/mapbox/standard"
                 Center="{Binding Center}" Zoom="{Binding Zoom}"
                 ClickCommand="{Binding MapClickCommand}" />
</Window>
```

```csharp
var map = MapView.Map;                       // full API (Mapbox.Net.Core)
map.Loaded += async (s, e) =>
{
    await map.AddMarkerAsync(new LngLat(2.35, 48.85), color: "#e11d48", popupHtml: "<b>Paris</b>");
    await map.AddPluginAsync(new DrawPlugin());
};
map.Click += (s, e) => Debug.WriteLine(e.LngLat);
```

- Bindable, two-way camera (`Center`, `Zoom`, `Bearing`, `Pitch`), `Engine`, `AccessToken`, `MapStyle`, `IsMapLoaded`,
  `ClickCommand`, `LoadedCommand`.
- `UseCompositionControl = true` uses `WebView2CompositionControl`, which lets you draw WPF content over the map.
- `SaveScreenshotAsync`, `PrintToPdfAsync`, `OpenDevTools`, `HostOptions` (geolocation, dev tools, user data folder…).
- Use **MapLibre** with free styles (`MapStyles.OpenFreeMapLiberty`) to run without a Mapbox account.

Requires the Microsoft Edge WebView2 Runtime (built into Windows 11; the control shows a download link otherwise).

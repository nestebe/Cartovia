# Cartovia.Wpf

WPF map control of **Cartovia**: vector maps with **MapLibre GL JS** (open source, no account needed) or
**Mapbox GL JS**, hosted in WebView2. Targets .NET 6, 8 and 10 (Windows).

```xml
<Window xmlns:carto="clr-namespace:Cartovia.Wpf;assembly=Cartovia.Wpf">
    <!-- MapLibre + OpenFreeMap: free, no key -->
    <carto:MapView x:Name="MapView"
                   MapStyle="https://tiles.openfreemap.org/styles/liberty"
                   Center="{Binding Center}" Zoom="{Binding Zoom}"
                   ClickCommand="{Binding MapClickCommand}" />

    <!-- Or Mapbox: Engine="Mapbox" AccessToken="{Binding MapboxToken}" MapStyle="mapbox://styles/mapbox/standard" -->
</Window>
```

```csharp
var map = MapView.Map;                       // full API (Cartovia.Core)
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

Requires the Microsoft Edge WebView2 Runtime (built into Windows 11; the control shows a download link otherwise).

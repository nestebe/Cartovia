# Cartovia.WinUI

WinUI 3 (Windows App SDK) map control of **Cartovia**: vector maps with **MapLibre GL JS** (open source, no account
needed) or **Mapbox GL JS**, hosted in the WinUI `WebView2`. .NET 8 and 10, Windows 10 1809+.

```xml
<Page xmlns:carto="using:Cartovia.WinUI">
    <carto:MapView x:Name="MapView"
                   MapStyle="https://tiles.openfreemap.org/styles/liberty"
                   Longitude="2.35" Latitude="48.85" Zoom="12" Pitch="45" />
</Page>
```

```csharp
var map = MapView.Map;                       // full API (Cartovia.Core)
map.Loaded += async (s, e) =>
{
    await map.AddMarkerAsync(new LngLat(2.35, 48.85), color: "#6366F1", popupHtml: "<b>Paris</b>");
    await map.AddPluginAsync(new DrawPlugin());
};
```

- Dependency properties: `Engine`, `AccessToken`, `MapStyle`, `Center` (code) / `Longitude` + `Latitude` (XAML),
  `Zoom`, `Bearing`, `Pitch` (updated when the map moves; bind with `Mode=TwoWay`), `IsMapLoaded`, `ClickCommand`,
  `LoadedCommand`.
- `SaveScreenshotAsync`, `PrintToPdfAsync`, `OpenDevTools`, `Options`, `HostOptions`.
- Depends on Windows App SDK 1.8 or later (the version used by .NET MAUI 10).

Documentation: https://github.com/nestebe/Cartovia/blob/main/docs/winui.md

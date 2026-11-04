# Cartovia.Avalonia

Avalonia map control of **Cartovia**: vector maps with **MapLibre GL JS** (open source, no account needed) or
**Mapbox GL JS**, hosted in WebView2. Avalonia 12, .NET 8 and 10, Windows.

```xml
<Window xmlns:carto="clr-namespace:Cartovia.Avalonia;assembly=Cartovia.Avalonia">
    <carto:MapView x:Name="MapView"
                   MapStyle="https://tiles.openfreemap.org/styles/liberty"
                   Center="{Binding Center}" Zoom="{Binding Zoom}"
                   ClickCommand="{Binding MapClickCommand}" />
</Window>
```

```csharp
var map = MapView.Map;                       // full API (Cartovia.Core)
map.Loaded += async (s, e) =>
{
    await map.AddMarkerAsync(new LngLat(2.35, 48.85), color: "#6366F1", popupHtml: "<b>Paris</b>");
    await map.AddPluginAsync(new DrawPlugin());
};
```

- Styled properties: `Engine`, `AccessToken`, `MapStyle`, two-way `Center` / `Zoom` / `Bearing` / `Pitch`,
  `IsMapLoaded`, `ClickCommand`, `LoadedCommand`.
- `SaveScreenshotAsync`, `PrintToPdfAsync`, `OpenDevTools`, `Options`, `HostOptions`.
- The map is a native child window (`NativeControlHost`): Avalonia content cannot overlap it; use markers, popups or
  HTML controls for map overlays.

Documentation: https://github.com/nestebe/Cartovia/blob/main/docs/avalonia.md

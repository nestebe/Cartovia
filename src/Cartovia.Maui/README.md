# Cartovia.Maui

.NET MAUI map control of **Cartovia**: vector maps with **MapLibre GL JS** (open source, no account needed) or
**Mapbox GL JS**. Runs on **Windows** (WinUI WebView2); other platforms show a placeholder so multi-platform apps
still build.

```csharp
// MauiProgram.cs
builder.UseMauiApp<App>().UseCartovia();
```

```xml
<ContentPage xmlns:carto="clr-namespace:Cartovia.Maui;assembly=Cartovia.Maui">
    <carto:MapView x:Name="MapView"
                   MapStyle="https://tiles.openfreemap.org/styles/liberty"
                   Center="2.35, 48.85" Zoom="12" Pitch="45" />
</ContentPage>
```

```csharp
MapView.Map.Loaded += async (s, e) =>
    await MapView.Map.AddMarkerAsync(new LngLat(2.35, 48.85), color: "#6366F1", popupHtml: "<b>Paris</b>");
```

- Bindable properties: `Engine`, `AccessToken`, `MapStyle`, two-way `Center` / `Zoom` / `Bearing` / `Pitch`,
  `IsMapLoaded`, `ClickCommand`, `LoadedCommand`.
- Full API on `MapView.Map` (Cartovia.Core); `SaveScreenshotAsync`, `PrintToPdfAsync`, `HostOptions` on Windows.

Documentation: https://github.com/nestebe/Cartovia/blob/main/docs/maui.md

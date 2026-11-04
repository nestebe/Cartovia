# Getting started

## Install

| UI framework | Package |
|---|---|
| WPF | `dotnet add package Cartovia.Wpf` |
| Windows Forms | `dotnet add package Cartovia.WinForms` |
| Avalonia 12 | `dotnet add package Cartovia.Avalonia` (`net8.0-windows` / `net10.0-windows`) |
| WinUI 3 (Windows App SDK) | `dotnet add package Cartovia.WinUI` |
| .NET MAUI (Windows) | `dotnet add package Cartovia.Maui` |
| Mapbox REST APIs (optional) | `dotnet add package Cartovia.Mapbox.Services` |

Target frameworks: `net6.0-windows`, `net8.0-windows`, `net10.0-windows` (Avalonia: `net8.0-windows`, `net10.0-windows`).
The control needs the **Microsoft Edge WebView2 Runtime**, built into Windows 11 and installed on nearly every
Windows 10 machine. When it is missing, the control shows a message with a download link
(see [Advanced topics](advanced.md#webview2-runtime)).

## Your first map (WPF)

```xml
<Window x:Class="Demo.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:carto="clr-namespace:Cartovia.Wpf;assembly=Cartovia.Wpf"
        Title="Demo" Width="1000" Height="700">
    <carto:MapView x:Name="MapView" Center="2.35, 48.85" Zoom="11" />
</Window>
```

```csharp
public MainWindow()
{
    InitializeComponent();
    MapView.Map.Loaded += async (_, _) =>
        await MapView.Map.AddMarkerAsync(new LngLat(2.3522, 48.8566), color: "#e11d48", popupHtml: "<b>Paris</b>");
}
```

That's it: MapLibre GL JS and the free OpenFreeMap "Liberty" style are used by default, no key needed.

## Your first map (Windows Forms)

```csharp
var mapView = new Cartovia.WinForms.MapView
{
    Dock = DockStyle.Fill,
    Center = new LngLat(2.35, 48.85),
    Zoom = 11,
};
mapView.MapLoaded += async (_, _) => await mapView.Map.AddMarkerAsync(new LngLat(2.3522, 48.8566));
Controls.Add(mapView);
```

## Choosing an engine

| | MapLibre GL JS (default) | Mapbox GL JS |
|---|---|---|
| License | Open source (BSD-3-Clause) | Proprietary, Mapbox Terms of Service |
| Account / token | Not needed | Required (`AccessToken`) |
| Cost | Free (your tile provider may charge) | Billed per map load by Mapbox |
| Styles | Any MapLibre style (OpenFreeMap, MapTiler, Stadia, self-hosted…) | `mapbox://` styles, Mapbox Standard |
| Exclusive features | Color relief layer, terrain / globe controls | Standard style config, featureset interactions, 3D lights, snow / rain, fog, Directions plugin |

```xml
<!-- MapLibre (default) -->
<carto:MapView MapStyle="https://tiles.openfreemap.org/styles/liberty" />

<!-- Mapbox -->
<carto:MapView Engine="Mapbox" AccessToken="{Binding MapboxToken}" MapStyle="mapbox://styles/mapbox/standard" />
```

The C# API is the same for both engines. Members that only exist in one engine say so in their XML documentation
("Mapbox GL JS only" / "MapLibre GL JS only").

## Styles

`MapStyles` lists well-known style URLs:

| Constant | Engine |
|---|---|
| `MapStyles.OpenFreeMapLiberty`, `OpenFreeMapBright`, `OpenFreeMapPositron` | MapLibre, free, no key |
| `MapStyles.MapLibreDemo` | MapLibre (world countries only) |
| `MapStyles.MapboxStandard`, `MapboxStandardSatellite`, `MapboxStreets`, `MapboxOutdoors`, `MapboxLight`, `MapboxDark`, `MapboxSatellite`, `MapboxSatelliteStreets`, `MapboxNavigationDay`, `MapboxNavigationNight` | Mapbox |

Any style URL or inline style JSON works (`MapOptions.Style`, `MapOptions.StyleJson`, `SetStyleAsync`).

## Keeping your Mapbox token safe

- Use a **public token** (`pk.…`) with the minimal scopes; never ship secret tokens (`sk.…`).
- If you restrict the token by URL, allow `https://cartovia.local` (the origin of the embedded page).
- Load the token from configuration rather than hard-coding it.

## Next steps

- [WPF](wpf.md), [Windows Forms](winforms.md), [Avalonia](avalonia.md), [WinUI 3](winui.md) or [.NET MAUI](maui.md) control
- [Production guide](production.md) before shipping
- [Sources and layers](sources-and-layers.md) to display your data
- [Events](events.md) to react to clicks and camera moves

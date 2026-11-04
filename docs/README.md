# Cartovia documentation

Guides with runnable examples for every part of the API. All snippets assume:

```csharp
using Cartovia;            // CartoviaMap, MapOptions, LngLat, MapStyles, camera types…
using Cartovia.Style;      // layers, sources, expressions (Expr), style options
using Cartovia.GeoJson;    // Point, LineString, Polygon, Feature, FeatureCollection…
using Cartovia.Events;     // event argument types
using Cartovia.Annotations;// markers and popups
using Cartovia.Controls;   // map controls
using Cartovia.Plugins;    // Draw, Geocoder, Directions, Turf, custom plugins
```

and that `map` is the `CartoviaMap` of a control (`mapView.Map`).

## Guides

| Guide | What you will find |
|---|---|
| [Getting started](getting-started.md) | Install, first map, engines, styles, tokens, WebView2 runtime |
| [WPF control](wpf.md) | XAML, bindable camera, MVVM commands, composition mode, export |
| [Windows Forms control](winforms.md) | Designer properties, events, export |
| [Avalonia control](avalonia.md) | AXAML, styled properties, two-way camera, hosting notes |
| [WinUI 3 control](winui.md) | Windows App SDK control, XAML, properties, export |
| [.NET MAUI control](maui.md) | `UseCartovia()`, bindable properties, Windows support |
| [Map and camera](map-and-camera.md) | Lifecycle, `MapOptions`, fly / ease / fit, projection, interaction handlers |
| [Sources and layers](sources-and-layers.md) | GeoJSON / vector / raster sources, typed layers, updating properties, images, 3D, terrain, fog, lights |
| [Expressions](expressions.md) | Data-driven and zoom-driven styling with `Expr` |
| [Events](events.md) | Map, camera, mouse and layer events, generic subscriptions, Mapbox interactions |
| [Markers, popups and controls](annotations-and-controls.md) | Markers, popups, built-in controls, custom HTML controls |
| [Queries and feature state](queries-and-feature-state.md) | `QueryRenderedFeaturesAsync`, feature state, clusters, elevation |
| [Plugins](plugins.md) | Draw, Geocoder, Directions, Turf, and writing your own plugin |
| [Extensions](extensions.md) | One-call helpers: clusters, heatmap, hover / selection, click popups, 3D buildings, terrain |
| [GeoJSON](geojson.md) | Building, parsing and measuring GeoJSON in C# |
| [Mapbox services](mapbox-services.md) | Optional REST clients: geocoding, directions, isochrones, static images… |
| [Production guide](production.md) | Authenticated tile servers (request rules), large datasets, PMTiles / offline, custom WebGL layers, error handling, testing |
| [Advanced topics](advanced.md) | Raw engine calls, JavaScript, offline assets, WebView2 options, threading, troubleshooting |

## Architecture in one picture

```
 your app (WPF / WinForms / Avalonia / WinUI 3 / MAUI)
        │  MapView control
        ▼
 CartoviaMap  ── typed C# API, events, plugins          (Cartovia.Core)
        │  JSON messages
        ▼
 WebView2 host ── serves the embedded page, data, permissions  (Cartovia.WebView2, or the WinUI host)
        │
        ▼
 bridge.js ── MapLibre GL JS or Mapbox GL JS (WebGL)
```

Every C# call becomes a JSON message executed by `bridge.js`; results and events come back the same way.
Calls made before the map is loaded are queued, so you can configure the map right after creating the control.

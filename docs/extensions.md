# Extensions

One-call helpers for common map features. Helpers returning `IDisposable` can be removed with `Dispose()`.

## Quick GeoJSON layers

Adds a source and default layers depending on the geometry types (circles, lines, fill + outline):

```csharp
IReadOnlyList<string> layerIds = await map.AddGeoJsonAsync("zones", featureCollection, color: "#e11d48");
// "zones-fill", "zones-outline", "zones-lines", "zones-points" depending on the data
await map.RemoveSourceAndLayersAsync("zones");
```

## Clusters

```csharp
ClusterLayers layers = await map.AddClusteredPointsAsync("shops", points, new ClusterStyle
{
    ClusterRadius = 60,
    Colors = new List<(int, string)> { (0, "#818CF8"), (100, "#6366F1"), (750, "#4338CA") },
    Radii = new List<(int, double)> { (0, 16), (100, 24), (750, 34) },
    PointColor = "#22D3EE",
    ZoomOnClick = true,                      // zoom into a cluster when clicked
});
map.OnLayer(layers.PointLayerId, LayerEvent.Click, (s, e) => Open(e.Feature!));
```

Creates the clustered source and three layers: `ClusterLayerId`, `CountLayerId`, `PointLayerId`.

## Heatmap

```csharp
string layerId = await map.AddHeatmapAsync("incidents", points, weightProperty: "severity", maxWeight: 10, radius: 25);
```

## Hover and selection

Uses feature state, so the layer style decides how hovered / selected features look:

```csharp
await map.AddSourceAsync(new GeoJsonSource("zones", zones) { GenerateId = true });
await map.AddLayerAsync(new FillLayer("zones-fill", "zones")
{
    FillColor = Expr.Case("#93c5fd",
        (Expr.Equal(Expr.Coalesce(Expr.FeatureState("selected"), false), true), "#1d4ed8"),
        (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), "#60a5fa")),
});

IDisposable hover = await map.EnableHoverStateAsync("zones-fill", "zones");              // state "hover"
IDisposable select = await map.EnableSelectionStateAsync("zones-fill", "zones");         // state "selected", toggled on click
IDisposable cursor = await map.EnableHoverCursorAsync("zones-fill");                      // pointer cursor
```

For vector sources pass `sourceLayer`.

## Popups on click

The template uses `{property}` (HTML-escaped) and `{{{property}}}` (raw HTML) placeholders:

```csharp
IDisposable popups = await map.ShowPopupOnClickAsync("cities",
    "<b>{name}</b><br/>{population} inhabitants<br/>{{{description_html}}}",
    new PopupOptions { MaxWidth = "300px" });
```

## 3D buildings

```csharp
// Mapbox classic styles (Streets, Light, Dark…): composite / building / height
await map.Add3DBuildingsAsync();

// OpenMapTiles styles (OpenFreeMap Bright / Positron, MapTiler…)
await map.Add3DBuildingsAsync(sourceId: "openmaptiles", sourceLayer: "building",
    heightProperty: "render_height", baseProperty: "render_min_height", color: "#cbd5e1");
```

Mapbox Standard and OpenFreeMap Liberty already include 3D buildings
(`SetConfigPropertyAsync("basemap", "show3dObjects", false)` hides them in Standard).

## 3D terrain

```csharp
await map.EnableTerrainAsync(exaggeration: 1.5);            // Mapbox DEM, or AWS Terrain Tiles with MapLibre
await map.EnableTerrainAsync(1.2, demTilesUrl: "https://api.maptiler.com/tiles/terrain-rgb-v2/tiles.json?key=…", encoding: DemEncoding.Mapbox);
await map.FlyToAsync(new CameraOptions { Center = new LngLat(6.8652, 45.8326), Zoom = 12, Pitch = 70 });
await map.DisableTerrainAsync();
```

## Raster overlays (WMS / XYZ)

```csharp
string id = await map.AddRasterOverlayAsync("cadastre",
    new[] { "https://wxs.example.com/wms?SERVICE=WMS&REQUEST=GetMap&LAYERS=parcels&BBOX={bbox-epsg-3857}&WIDTH=256&HEIGHT=256&SRS=EPSG:3857&FORMAT=image/png&TRANSPARENT=true" },
    opacity: 0.7, attribution: "© Cadastre");
```

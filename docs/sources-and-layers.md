# Sources and layers

A **source** provides data; a **layer** draws a source with a style. Sources and layers added through the API are
automatically re-added after a style change (`MapOptions.PreserveUserLayersOnStyleChange`).

## Sources

### GeoJSON

```csharp
var cities = new FeatureCollection()
    .Add(new Feature(new Point(2.3522, 48.8566), new { name = "Paris", population = 2_133_000 }))
    .Add(new Feature(new Point(4.8357, 45.7640), new { name = "Lyon", population = 522_000 }));

await map.AddSourceAsync(new GeoJsonSource("cities", cities) { GenerateId = true });

// From a URL
await map.AddSourceAsync(new GeoJsonSource("earthquakes", "https://example.com/earthquakes.geojson"));

// Replace the data later (live tracking, filtering in C#…)
await map.SetGeoJsonDataAsync("cities", updatedCollection);
```

Useful `GeoJsonSource` options: `Cluster`, `ClusterRadius`, `ClusterMaxZoom`, `ClusterProperties`, `GenerateId`,
`PromoteId`, `LineMetrics` (required by `line-gradient`), `Tolerance`, `Buffer`, `MaxZoom`, `Filter`, `Dynamic`.

### Vector and raster tiles

```csharp
// Vector tiles from a TileJSON URL or tile templates
await map.AddSourceAsync(new VectorSource("parcels", "https://tiles.example.com/parcels.json") { PromoteId = "id" });
await map.AddSourceAsync(new VectorSource("roads") { Tiles = new[] { "https://tiles.example.com/roads/{z}/{x}/{y}.pbf" }, MaxZoom = 14 });

// Raster tiles (XYZ / WMS)
await map.AddSourceAsync(new RasterSource("ortho",
    "https://data.geopf.fr/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=ORTHOIMAGERY.ORTHOPHOTOS&STYLE=normal&TILEMATRIXSET=PM&FORMAT=image/jpeg&TILEMATRIX={z}&TILEROW={y}&TILECOL={x}")
    { TileSize = 256, Attribution = "© IGN" });

// Elevation (terrain, hillshade)
await map.AddSourceAsync(RasterDemSource.MapboxTerrain());   // Mapbox
await map.AddSourceAsync(new RasterDemSource("dem") { Tiles = new[] { CartoviaMap.AwsTerrariumTiles }, Encoding = DemEncoding.Terrarium });
```

### Image, video and raw sources

```csharp
await map.AddSourceAsync(new ImageSource("plan", "https://example.com/plan.png",
    topLeft: new LngLat(2.29, 48.86), topRight: new LngLat(2.30, 48.86),
    bottomRight: new LngLat(2.30, 48.85), bottomLeft: new LngLat(2.29, 48.85)));

await map.AddSourceAsync(new RawSource("custom", JsonNode.Parse("""{ "type": "vector", "url": "pmtiles://…" }""")!.AsObject()));
```

### Managing sources

```csharp
bool exists = await map.HasSourceAsync("cities");
JsonObject? definition = await map.GetSourceAsync("cities");
bool loaded = await map.IsSourceLoadedAsync("parcels");
await map.SetSourceTilesAsync("roads", new[] { "https://tiles.example.com/v2/{z}/{x}/{y}.pbf" });
await map.RemoveSourceAndLayersAsync("cities");     // removes the layers using it, then the source
await map.TryRemoveSourceAsync("cities");
```

## Layers

Every layer type of the MapLibre and Mapbox style specifications is a C# class with one property per paint / layout
property. The XML documentation shows the engine support, default value, range and units.

| Class | Draws |
|---|---|
| `FillLayer` | Polygons |
| `LineLayer` | Lines and polygon outlines |
| `CircleLayer` | Points as circles |
| `SymbolLayer` | Icons and labels |
| `HeatmapLayer` | Point density |
| `FillExtrusionLayer` | 3D extruded polygons (buildings…) |
| `RasterLayer` | Raster tiles / images |
| `HillshadeLayer` | Relief shading from a DEM |
| `BackgroundLayer` | Background color / pattern |
| `ColorReliefLayer` | Elevation coloring (MapLibre) |
| `ModelLayer`, `SkyLayer`, `RasterParticleLayer`, `SlotLayer`, `ClipLayer` | Mapbox-specific layers |
| `RawLayer` | Any layer type, configured by property name |

### Adding layers

```csharp
await map.AddLayerAsync(new FillLayer("parcels-fill", "parcels", sourceLayer: "parcels")
{
    FillColor = "#3b82f6",
    FillOpacity = 0.35,
    MinZoom = 13,
    Filter = Expr.Equal(Expr.Get("zone"), "U"),
});

await map.AddLayerAsync(new LineLayer("routes", "routes")
{
    LineColor = System.Drawing.Color.OrangeRed,       // System.Drawing.Color or any CSS color string
    LineWidth = Expr.InterpolateZoom((10, 2), (16, 8)),
    LineCap = LineCap.Round,
    LineJoin = LineJoin.Round,
    LineDasharray = new[] { 2.0, 1.0 },
});

await map.AddLayerAsync(new SymbolLayer("labels", "cities")
{
    TextField = Expr.Get("name"),
    TextFont = new[] { "Noto Sans Regular" },
    TextSize = 14,
    TextOffset = (0, 1.2),
    TextAnchor = Anchor.Top,
    TextHaloColor = "white",
    TextHaloWidth = 1.5,
    IconImage = "pin",
    IconAllowOverlap = true,
}, beforeLayerId: "road-label");                     // insert below an existing layer
```

Values accept constants or expressions: `NumberValue`, `ColorValue`, `StringValue`, `BoolValue`, `EnumValue<T>`,
`NumberArrayValue`… convert implicitly from `double`, `string`, `System.Drawing.Color`, arrays, tuples and `Expression`.

Paint transitions are typed too:

```csharp
var circles = new CircleLayer("pts", "cities")
{
    CircleRadius = 6,
    CircleRadiusTransition = StyleTransition.FromMilliseconds(400),
};
```

Font names must exist in the style's glyphs (`map.GetStyleAsync()` shows the `glyphs` URL).

### Updating layers

```csharp
await map.SetPaintPropertyAsync("parcels-fill", "fill-color", "#ef4444");
await map.SetLayoutPropertyAsync("labels", "text-size", 16);
await map.SetFilterAsync("parcels-fill", Expr.GreaterThan(Expr.Get("area"), 1000));
await map.SetLayerVisibilityAsync("labels", false);
await map.SetLayerZoomRangeAsync("parcels-fill", 12, 22);
await map.MoveLayerAsync("labels");                       // to the top

// Or reuse a typed object: every property set on it is applied to the existing layer
await map.UpdateLayerAsync(new CircleLayer("pts", "cities") { CircleColor = "#16a34a", CircleRadius = 9 });
```

### Inspecting and removing

```csharp
IReadOnlyList<string> ids = await map.GetLayerIdsAsync();     // bottom to top
bool has = await map.HasLayerAsync("labels");
JsonObject? json = await map.GetLayerAsync("labels");
JsonElement color = await map.GetPaintPropertyAsync("parcels-fill", "fill-color");
await map.TryRemoveLayerAsync("labels");
```

### Any property by name

Typed properties cover the specs; you can still set anything by name:

```csharp
var layer = new RawLayer("experimental", "line", "routes");
layer.SetPaint("line-emissive-strength", 1).SetLayout("line-z-offset", 10);
await map.AddLayerAsync(layer);
```

## Images (icons and patterns)

```csharp
await map.AddImageAsync("pin", "https://example.com/pin.png");
await map.AddImageAsync("logo", File.ReadAllBytes("logo.png"), "image/png", new ImageOptions { PixelRatio = 2 });
await map.AddSvgImageAsync("dot", "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24'><circle cx='12' cy='12' r='10' fill='#e11d48'/></svg>", 24, 24);
await map.AddImageAsync("arrow", arrowPng, options: new ImageOptions { Sdf = true });   // recolor with icon-color

// Provide images on demand
map.StyleImageMissing += async (s, e) => await map.AddImageAsync(e.ImageId, $"https://icons.example.com/{e.ImageId}.png");
```

## Style

```csharp
await map.SetStyleAsync(MapStyles.OpenFreeMapPositron);        // user sources / layers are restored
map.UserLayersRestored += (s, e) => Console.WriteLine("Layers restored");
JsonObject? style = await map.GetStyleAsync();
await map.SetStyleAsync(myStyleJson);                          // inline style JSON
```

## 3D, atmosphere and lights

```csharp
// Terrain (Mapbox DEM by default, AWS Terrain Tiles with MapLibre)
await map.EnableTerrainAsync(exaggeration: 1.5);
await map.DisableTerrainAsync();
await map.SetTerrainAsync(new TerrainOptions { Source = "dem", Exaggeration = 1.2 });

// Sky (MapLibre)
await map.SetSkyAsync(new SkyOptions { SkyColor = "#87CEEB", HorizonColor = "#ffffff", FogColor = "#dddddd" });

// Fog, snow, rain and 3D lights (Mapbox)
await map.SetFogAsync(new FogOptions { Color = "white", HorizonBlend = 0.1, StarIntensity = 0.2 });
await map.SetSnowAsync(new SnowOptions { Density = 0.8, Intensity = 1 });
await map.SetRainAsync(null);
await map.SetLightsAsync(new[]
{
    Light3D.Ambient("ambient", new AmbientLightOptions { Intensity = 0.4 }),
    Light3D.Directional("sun", new DirectionalLightOptions { Direction = new[] { 210.0, 30.0 }, CastShadows = true }),
});

// Classic light (both engines)
await map.SetLightAsync(new LightOptions { Intensity = 0.6 });
```

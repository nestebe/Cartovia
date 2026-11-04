# Queries and feature state

## Querying rendered features

```csharp
// At a pixel (e.g. from a click)
map.Click += async (s, e) =>
{
    var hits = await map.QueryRenderedFeaturesAsync(e.Point, new QueryOptions { Layers = new[] { "parcels-fill" } });
    foreach (var f in hits)
        Console.WriteLine($"{f.LayerId} {f.SourceLayer} {f.GetProperty<string>("id")}");
};

// In a box around a point (tolerant clicks)
var near = await map.QueryRenderedFeaturesAsync(ScreenBox.Around(new ScreenPoint(300, 200), 8));

// At a geographic position
var here = await map.QueryRenderedFeaturesAsync(new LngLat(2.35, 48.85));

// Everything visible in the viewport, with a filter
var visibleShops = await map.QueryRenderedFeaturesAsync(new QueryOptions
{
    Layers = new[] { "poi" },
    Filter = Expr.Equal(Expr.Get("class"), "shop"),
});
```

Each result is a `QueriedFeature` (a GeoJSON `Feature`) with `LayerId`, `LayerType`, `Source`, `SourceLayer`, `State`
and `Namespace`.

## Querying source features

Returns the features of the loaded tiles, rendered or not (may contain duplicates across tile boundaries):

```csharp
var roads = await map.QuerySourceFeaturesAsync("roads", sourceLayer: "transportation",
    filter: Expr.Equal(Expr.Get("class"), "motorway"));
```

## Feature state

Feature state stores per-feature values used by `["feature-state", …]` expressions, without re-uploading data.
Features need an id: use `GenerateId = true`, `PromoteId`, or ids in your GeoJSON.

```csharp
await map.AddSourceAsync(new GeoJsonSource("zones", zones) { PromoteId = "code" });
await map.AddLayerAsync(new FillLayer("zones-fill", "zones")
{
    FillColor = Expr.Case("#93c5fd", (Expr.Equal(Expr.Coalesce(Expr.FeatureState("selected"), false), true), "#1d4ed8")),
});

await map.SetFeatureStateAsync(new FeatureIdentifier("zones", "75056"), new { selected = true });
JsonObject? state = await map.GetFeatureStateAsync(new FeatureIdentifier("zones", "75056"));
await map.RemoveFeatureStateAsync(new FeatureIdentifier("zones", "75056"), key: "selected");

// From a click
map.OnLayer("zones-fill", LayerEvent.Click, async (s, e) =>
    await map.SetFeatureStateAsync(FeatureIdentifier.From(e.Feature!), new { selected = true }));
```

For vector tiles, pass the source layer: `new FeatureIdentifier("parcels", 42, SourceLayer: "parcels")`.

Ready-made hover / selection behaviours: see [Extensions](extensions.md#hover-and-selection).

## Clusters

```csharp
map.OnLayer("pts-clusters", LayerEvent.Click, async (s, e) =>
{
    long clusterId = e.Feature!.GetProperty<long>("cluster_id");
    double zoom = await map.GetClusterExpansionZoomAsync("pts", clusterId);
    var leaves = await map.GetClusterLeavesAsync("pts", clusterId, limit: 50);
    var children = await map.GetClusterChildrenAsync("pts", clusterId);
    await map.EaseToAsync(new CameraOptions { Center = e.LngLat, Zoom = zoom });
});
```

## Terrain elevation

```csharp
await map.EnableTerrainAsync();
double? meters = await map.QueryTerrainElevationAsync(new LngLat(6.8652, 45.8326));
```

Returns `null` when terrain is disabled or the tile is not loaded yet.

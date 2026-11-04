# Mapbox services (optional)

`Cartovia.Mapbox.Services` is a typed client for the Mapbox REST APIs. It works with any engine (you can display a
Mapbox route on a MapLibre map) but requires a Mapbox access token and is billed by Mapbox.

```bash
dotnet add package Cartovia.Mapbox.Services
```

```csharp
using Cartovia.Mapbox.Services;

using var mapbox = new MapboxClient("pk.your-token");     // reuse one instance (thread-safe)
```

| Property | API |
|---|---|
| `Geocoding` | Geocoding v6: forward, structured, reverse, batch |
| `Search` | Search Box v1: suggest, retrieve, forward, category |
| `Directions` | Directions v5 |
| `Matrix` | Matrix v1 |
| `Isochrone` | Isochrone v1 |
| `Optimization` | Optimization v1 |
| `MapMatching` | Map Matching v5 |
| `Tilequery` | Tilequery v4 (and an elevation helper) |
| `StaticImages` | Static Images v1 |

Results use the Cartovia GeoJSON types, so they go straight onto the map.

## Route on the map

```csharp
var response = await mapbox.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new[]
{
    new RouteWaypoint(2.3522, 48.8566),
    new RouteWaypoint(4.8357, 45.7640),
}) { Steps = true, Language = "en" });

var route = response.BestRoute!;
await map.AddSourceAsync(new GeoJsonSource("route", new Feature(route.Geometry!)));
await map.AddLayerAsync(new LineLayer("route", "route") { LineColor = "#2563eb", LineWidth = 5, LineCap = LineCap.Round });
await map.FitBoundsAsync(route.GetBounds()!.Value, new FitBoundsOptions { Padding = 60 });
Console.WriteLine($"{route.Distance / 1000:0} km, {route.DurationTime}");
```

## Search and reverse geocoding

```csharp
var places = await mapbox.Geocoding.ForwardAsync(new ForwardGeocodingRequest("10 rue de Rivoli, Paris") { Limit = 3 });
foreach (var p in places.Features) Console.WriteLine($"{p.FullAddress} {p.Location}");

map.Click += async (s, e) =>
{
    var here = await mapbox.Geocoding.ReverseAsync(e.LngLat);
    await map.ShowPopupAsync(e.LngLat, here.First?.FullAddress ?? "Unknown place");
};
```

## Isochrones

```csharp
var iso = await mapbox.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, new LngLat(2.35, 48.85))
{
    ContoursMinutes = { 5, 10, 15 },
});
await map.AddSourceAsync(new GeoJsonSource("iso", iso.FeatureCollection));
await map.AddLayerAsync(new FillLayer("iso", "iso") { FillColor = Expr.Get("fillColor"), FillOpacity = 0.25 });
```

## Errors

```csharp
try { await mapbox.Directions.GetRouteAsync(request); }
catch (MapboxApiException ex) when (ex.ErrorCode == "NoRoute") { /* no route */ }
catch (MapboxApiException ex) when (ex.IsRateLimited) { await Task.Delay(ex.RateLimit?.RetryAfter ?? TimeSpan.FromMinutes(1)); }
```

The full reference with every API (matrix, optimization, map matching, tilequery, static images…) is in the
[package README](../src/Cartovia.Mapbox.Services/README.md).

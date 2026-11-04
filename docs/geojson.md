# GeoJSON

`Cartovia.GeoJson` is a small RFC 7946 model with System.Text.Json converters, used by sources, queries, plugins and
the Mapbox services.

## Building data

```csharp
var point = new Point(2.3522, 48.8566);
var line = new LineString(new LngLat(2.35, 48.85), new LngLat(4.83, 45.76), new LngLat(5.37, 43.29));
var polygon = new Polygon(new LngLat(0, 0), new LngLat(1, 0), new LngLat(1, 1), new LngLat(0, 1));   // closed automatically
var circle = Polygon.Circle(new LngLat(2.35, 48.85), radiusMeters: 1500);
var rect = Polygon.FromBounds(new LngLatBounds(2.2, 48.8, 2.5, 48.9));
var multi = new MultiPolygon(new[] { polygon, circle });

var feature = new Feature(point, new { name = "Paris", population = 2_133_000 }, id: 75056);
var collection = new FeatureCollection().Add(feature).Add(new Feature(line, new { name = "Route" }));
```

`Position` holds longitude, latitude and an optional altitude, and converts to / from `LngLat`.

## Properties

```csharp
string? name = feature.GetProperty<string>("name");
int population = feature.GetProperty<int>("population");
feature.SetProperty("visited", true);
JsonObject raw = feature.Properties;
```

## Parsing and serializing

```csharp
var fc = GeoJsonObject.Parse<FeatureCollection>(File.ReadAllText("communes.geojson"));
GeoJsonObject anything = GeoJsonObject.Parse(json);
string text = fc.ToJson();

// Also works with System.Text.Json directly
var f = JsonSerializer.Deserialize<Feature>(json, CartoviaJson.Options);
```

## Measuring

```csharp
LngLatBounds? bounds = fc.GetBounds();                // or geometry.GetBounds()
double meters = line.GetLength();                     // haversine
double d = new LngLat(2.35, 48.85).DistanceTo(new LngLat(4.83, 45.76));
IEnumerable<Position> all = polygon.GetPositions();
```

For advanced analysis (buffers, unions, intersections…) see `TurfPlugin` in [Plugins](plugins.md#turf), or use
NetTopologySuite and serialize to GeoJSON text.

## On the map

```csharp
await map.AddSourceAsync(new GeoJsonSource("data", fc));
await map.SetGeoJsonDataAsync("data", fc);
await map.FitToAsync(fc, new FitBoundsOptions { Padding = 40 });
```

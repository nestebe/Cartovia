# Cartovia.Mapbox.Services

Optional **Mapbox** extension of Cartovia: a strongly-typed .NET client for the
[Mapbox web services](https://docs.mapbox.com/api/overview/) (a Mapbox access token is required).
It is built on `HttpClient` and `System.Text.Json` only, and targets .NET 6, 8 and 10.

Results come back as the `Cartovia.GeoJson` types from **Cartovia.Core** (`Feature`, `FeatureCollection`,
`LineString`, `Polygon`…), so you can add them straight to a map source in the Cartovia WPF / WinForms controls.

| Service | Property | Mapbox API |
|---|---|---|
| Geocoding (forward, structured, reverse, batch) | `Geocoding` | Geocoding API v6 |
| Search Box (suggest, retrieve, forward, category) | `Search` | Search Box API v1 |
| Directions | `Directions` | Directions API v5 |
| Matrix | `Matrix` | Matrix API v1 |
| Isochrone | `Isochrone` | Isochrone API v1 |
| Optimization | `Optimization` | Optimization API v1 |
| Map Matching | `MapMatching` | Map Matching API v5 |
| Static Images | `StaticImages` | Static Images API v1 |
| Tilequery (and an elevation helper) | `Tilequery` | Tilequery API v4 |

## Getting started

```csharp
using Cartovia;
using Cartovia.Mapbox.Services;

using var mapbox = new MapboxClient("pk.your-access-token");
```

`MapboxClient` is thread-safe: create one instance and reuse it. You can pass your own `HttpClient`, for example
one from `IHttpClientFactory`, and the client won't dispose it. To call a proxy, set the base URL:

```csharp
var mapbox = new MapboxClient(new MapboxClientOptions(token) { BaseUrl = new Uri("https://my-proxy/mapbox/") }, httpClient);
```

Each service sits behind an interface (`IMapboxClient`, `IGeocodingService`, `IDirectionsService`…), so you can
mock it in your own tests. Every call also accepts a `CancellationToken`.

## Geocoding (v6)

```csharp
var result = await mapbox.Geocoding.ForwardAsync(new ForwardGeocodingRequest("10 Downing Street, London")
{
    Countries = { "gb" },
    Types = GeocodingTypes.Address | GeocodingTypes.Place,
    Proximity = new LngLat(-0.1276, 51.5072),
    Language = "en",
    Limit = 5,
});

foreach (var place in result.Features)
    Console.WriteLine($"{place.DisplayName} @ {place.Location} ({place.FeatureType}, {place.MatchCode?.Confidence})");

// result.FeatureCollection is a Cartovia.GeoJson.FeatureCollection: use it as the data of a GeoJSON map source.
```

Structured input, reverse geocoding and batch geocoding:

```csharp
var structured = await mapbox.Geocoding.ForwardAsync(new ForwardGeocodingRequest(new StructuredAddress
{
    AddressNumber = "1600", Street = "Pennsylvania Ave NW", Place = "Washington", Region = "DC", Country = "us",
}) { Autocomplete = false });

var here = await mapbox.Geocoding.ReverseAsync(new LngLat(2.2945, 48.8584));
Console.WriteLine(here.First?.Context?.Place?.Name);     // Paris

var batch = await mapbox.Geocoding.BatchAsync(new GeocodingRequest[]
{
    new ForwardGeocodingRequest("Lyon") { Limit = 1 },
    new ReverseGeocodingRequest(new LngLat(4.8357, 45.7640)),
});
```

Each `GeocodingFeature` exposes the raw GeoJSON `Feature`, and typed accessors for `Name`, `FullAddress`,
`PlaceFormatted`, `Location`, `Coordinates` (accuracy and routable points), `BBox`, `Context` (country, region,
postcode, place, street…) and `MatchCode`.

## Search Box (autocomplete)

```csharp
var session = SearchBoxSession.NewToken();               // one session per search
var suggestions = await mapbox.Search.SuggestAsync(new SearchBoxSuggestRequest("eiffel", session)
{
    Proximity = new LngLat(2.35, 48.85), Limit = 5,
});
var chosen = suggestions.Suggestions[0];
var feature = (await mapbox.Search.RetrieveAsync(new SearchBoxRetrieveRequest(chosen, session))).First;

var cafes = await mapbox.Search.CategoryAsync(new SearchBoxCategoryRequest("cafe") { Proximity = feature!.Location });
```

## Directions

```csharp
var request = new DirectionsRequest(RoutingProfile.DrivingTraffic, new[]
{
    new RouteWaypoint(2.3522, 48.8566) { Name = "Paris" },
    new RouteWaypoint(3.0573, 50.6292) { IsSilent = true },             // via point, no new leg
    new RouteWaypoint(4.3517, 50.8503) { Approach = Approach.Curb, Radius = double.PositiveInfinity },
})
{
    Alternatives = true,
    Steps = true,
    Overview = RouteOverview.Full,
    Annotations = RouteAnnotations.Duration | RouteAnnotations.Congestion,
    Exclude = RouteExclusions.Toll | RouteExclusions.Ferry,
    Language = "fr",
    DepartAt = DateTimeOffset.Now.AddHours(1),
};

var response = await mapbox.Directions.GetRouteAsync(request);
var best = response.BestRoute!;
Console.WriteLine($"{best.Distance / 1000:0.0} km, {best.DurationTime}");
foreach (var step in best.Legs.SelectMany(l => l.Steps))
    Console.WriteLine(step.Maneuver.Instruction);

LineString line = best.Geometry!;                           // draw it as a line layer
LngLatBounds bounds = best.GetBounds()!.Value;              // fit the camera to the route
var allRoutes = response.ToFeatureCollection();             // one feature per route
```

The client always requests `geometries=geojson`. If the URL would exceed about 8,000 characters (for example with
many waypoints and names), it switches to the form-encoded `POST` variant of the API. The other options are
available as properties, and any parameter that isn't modelled can go in `AdditionalParameters`.

## Matrix, Isochrone, Optimization, Map Matching

```csharp
var matrix = await mapbox.Matrix.GetMatrixAsync(new MatrixRequest(RoutingProfile.Driving, a, b, c) { Sources = new[] { 0 } });
double? seconds = matrix.GetDuration(0, 2);                 // null when no route exists

var iso = await mapbox.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, center)
{
    ContoursMinutes = { 5, 10, 15 },
    ContourColors = { "6706ce", "04e813", "4286f4" },
    Denoise = 1,
});
FeatureCollection polygons = iso.FeatureCollection;          // polygons with color / contour properties

var trip = await mapbox.Optimization.OptimizeAsync(new OptimizationRequest(RoutingProfile.Driving, depot, s1, s2, s3)
{
    Source = TripSource.First, Roundtrip = true,
});
var order = trip.GetVisitOrder();

var match = await mapbox.MapMatching.MatchAsync(new MapMatchingRequest(RoutingProfile.Driving,
    gpsFixes.Select(f => new TracePoint(f.Position, f.Time) { Radius = f.AccuracyMeters })) { Tidy = true });
LineString snapped = match.Matchings[0].Geometry!;
```

## Tilequery and elevation

```csharp
var hits = await mapbox.Tilequery.QueryAsync(new TilequeryRequest("mapbox.mapbox-streets-v8", point)
{
    Radius = 50, Limit = 10, Layers = { "poi_label" },
});
foreach (var f in hits.Features) Console.WriteLine($"{f.Layer} {f.GetProperty<string>("name")} {f.Distance} m");

double? meters = await mapbox.Tilequery.GetElevationAsync(new LngLat(6.8652, 45.8326)); // from terrain contours
```

## Static Images

```csharp
var image = new StaticImageRequest
{
    Style = "mapbox/outdoors-v12",
    Width = 800, Height = 500, HighDensity = true,
    Overlays =
    {
        new StaticPath(best.Geometry!) { StrokeWidth = 4, StrokeColor = "#3b82f6", StrokeOpacity = 0.9 },
        new StaticMarker(start, label: "a", color: "#22c55e", size: StaticMarkerSize.Large),
        new StaticMarker(end, label: "b", color: "#ef4444", size: StaticMarkerSize.Large),
    },
    Padding = new StaticImagePadding(40),                    // auto viewport fitted to the overlays
};

byte[] png = await mapbox.StaticImages.GetImageAsync(image);
Uri url = mapbox.StaticImages.BuildUrl(image);              // contains your token: don't log it
```

Set `Center`, `Zoom`, `Bearing` and `Pitch`, or `Bounds`, to choose the viewport yourself. You can also add
`StaticGeoJsonOverlay` (simplestyle properties are honoured) and `StaticCustomMarker`. The helper
`PolylineCodec` encodes and decodes polylines.

## Errors and rate limits

When a request fails, the client throws `MapboxApiException`:

```csharp
try { await mapbox.Directions.GetRouteAsync(request); }
catch (MapboxApiException ex) when (ex.ErrorCode == "NoRoute") { /* no route between the points */ }
catch (MapboxApiException ex) when (ex.IsRateLimited)
{
    await Task.Delay(ex.RateLimit?.RetryAfter ?? TimeSpan.FromSeconds(60));
}
```

- `StatusCode`, `ApiMessage` (the Mapbox `message`), `ErrorCode` (the Mapbox `code`, such as `NoRoute`,
  `NoSegment`, `InvalidInput` or `NoMatch`) and `ResponseBody`.
- `RateLimit` is read from the `X-Rate-Limit-Interval`, `X-Rate-Limit-Limit`, `X-Rate-Limit-Reset` and
  `Retry-After` headers.
- A navigation response whose `code` isn't `Ok` also throws, even when the HTTP status is 200.
- Your access token never appears in exception messages, paths or bodies.

Numbers are always formatted with the invariant culture, and every value is URL-encoded.

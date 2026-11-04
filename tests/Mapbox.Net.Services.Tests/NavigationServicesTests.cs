using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Xunit;

namespace Mapbox.Net.Services.Tests;

public class MatrixTests
{
    private const string Sample = """
    {
      "code": "Ok",
      "durations": [
        [0, 573, 1169.5],
        [573, 0, null],
        [1169.5, 597, 0]
      ],
      "distances": [
        [0, 2307.4, 6458.3],
        [2307.4, 0, 3864.5],
        [6458.3, 3864.5, 0]
      ],
      "destinations": [
        { "distance": 4.2, "name": "Mission Street", "location": [-122.418408, 37.751668] },
        { "distance": 15.1, "name": "22nd Street", "location": [-122.422959, 37.755184] },
        { "distance": 9.6, "name": "", "location": [-122.426911, 37.759695] }
      ],
      "sources": [
        { "distance": 4.2, "name": "Mission Street", "location": [-122.418408, 37.751668] },
        { "distance": 15.1, "name": "22nd Street", "location": [-122.422959, 37.755184] },
        { "distance": 9.6, "name": "", "location": [-122.426911, 37.759695] }
      ]
    }
    """;

    [Fact]
    public async Task Builds_url_and_parses_matrix_with_nulls()
    {
        var (client, handler) = TestClient.Create(Sample);
        var request = new MatrixRequest(RoutingProfile.Walking,
            new LngLat(-122.42, 37.78), new LngLat(-122.45, 37.91), new LngLat(-122.48, 37.73))
        {
            Sources = new[] { 0, 1 },
            Destinations = new[] { 2 },
            FallbackSpeed = 4.5,
        };
        request.Points[2].Approach = Approach.Curb;
        var response = await client.Matrix.GetMatrixAsync(request);

        Assert.Equal("/directions-matrix/v1/mapbox/walking/-122.42,37.78;-122.45,37.91;-122.48,37.73", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("duration,distance", p["annotations"]);
        Assert.Equal("0;1", p["sources"]);
        Assert.Equal("2", p["destinations"]);
        Assert.Equal(";;curb", p["approaches"]);
        Assert.Equal("4.5", p["fallback_speed"]);

        Assert.Equal(573, response.GetDuration(0, 1));
        Assert.Null(response.GetDuration(1, 2));
        Assert.Equal(3864.5, response.GetDistance(2, 1));
        Assert.Equal("22nd Street", response.Sources[1].Name);
        Assert.Equal(3, response.Destinations.Count);
    }

    [Fact]
    public async Task Rejects_out_of_range_indices()
    {
        var (client, _) = TestClient.Create(Sample);
        var request = new MatrixRequest(RoutingProfile.Driving, new LngLat(0, 0), new LngLat(1, 1)) { Sources = new[] { 2 } };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.Matrix.GetMatrixAsync(request));
    }
}

public class IsochroneTests
{
    private const string Sample = """
    {
      "features": [
        {
          "properties": { "fill-opacity": 0.33, "fillColor": "#bf04e8", "opacity": 0.33, "fill": "#bf04e8", "fillOpacity": 0.33,
                          "color": "#bf04e8", "contour": 10, "metric": "time" },
          "geometry": { "coordinates": [[[-118.22258, 33.99038], [-118.22261, 33.99045], [-118.2225, 33.99050], [-118.22258, 33.99038]]], "type": "Polygon" },
          "type": "Feature"
        },
        {
          "properties": { "fill-opacity": 0.33, "fillColor": "#6706ce", "opacity": 0.33, "fill": "#6706ce", "fillOpacity": 0.33,
                          "color": "#6706ce", "contour": 5, "metric": "time" },
          "geometry": { "coordinates": [[[-118.22258, 33.99038], [-118.2226, 33.9904], [-118.2225, 33.9905], [-118.22258, 33.99038]]], "type": "Polygon" },
          "type": "Feature"
        }
      ],
      "type": "FeatureCollection"
    }
    """;

    [Fact]
    public async Task Builds_url_and_parses_contours()
    {
        var (client, handler) = TestClient.Create(Sample);
        var response = await client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Driving, new LngLat(-118.22258, 33.99038))
        {
            ContoursMinutes = { 5, 10 },
            ContourColors = { "#6706ce", "BF04E8" },
            Denoise = 0.5,
            Generalize = 500,
            Exclude = RouteExclusions.Motorway | RouteExclusions.Toll,
        });

        Assert.Equal("/isochrone/v1/mapbox/driving/-118.22258,33.99038", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("5,10", p["contours_minutes"]);
        Assert.Equal("6706ce,bf04e8", p["contours_colors"]);
        Assert.Equal("true", p["polygons"]);
        Assert.Equal("0.5", p["denoise"]);
        Assert.Equal("500", p["generalize"]);
        Assert.Equal("motorway,toll", p["exclude"]);
        Assert.False(p.ContainsKey("contours_meters"));

        Assert.Equal(2, response.FeatureCollection.Features.Count);
        var c = response.Contours[0];
        Assert.Equal(10, c.Contour);
        Assert.Equal("time", c.Metric);
        Assert.Equal("#bf04e8", c.Color);
        Assert.Equal(0.33, c.Opacity);
        Assert.IsType<Polygon>(c.Geometry);
    }

    [Fact]
    public async Task Requires_exactly_one_contour_kind_and_matching_colors()
    {
        var (client, handler) = TestClient.Create(Sample);
        var center = new LngLat(0, 0);
        await Assert.ThrowsAsync<ArgumentException>(() => client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, center)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, center)
        { ContoursMinutes = { 5 }, ContoursMeters = { 1000 } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, center)
        { ContoursMinutes = { 5, 10 }, ContourColors = { "ff0000" } }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Walking, center)
        { ContoursMinutes = { 1, 2, 3, 4, 5 } }));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Meters_contours()
    {
        var (client, handler) = TestClient.Create(Sample);
        await client.Isochrone.GetIsochronesAsync(new IsochroneRequest(RoutingProfile.Cycling, new LngLat(1, 2)) { ContoursMeters = { 1000, 5000 }, Polygons = false });
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("1000,5000", p["contours_meters"]);
        Assert.Equal("false", p["polygons"]);
    }
}

public class OptimizationTests
{
    private const string Sample = """
    {
      "code": "Ok",
      "waypoints": [
        { "distance": 9.3, "name": "North Lincoln Avenue", "location": [-122.42, 37.78], "waypoint_index": 0, "trips_index": 0 },
        { "distance": 4.1, "name": "Clay Street", "location": [-122.45, 37.91], "waypoint_index": 2, "trips_index": 0 },
        { "distance": 2.7, "name": "Market Street", "location": [-122.48, 37.73], "waypoint_index": 1, "trips_index": 0 }
      ],
      "trips": [
        {
          "geometry": { "coordinates": [[-122.42, 37.78], [-122.48, 37.73], [-122.45, 37.91], [-122.42, 37.78]], "type": "LineString" },
          "legs": [
            { "summary": "", "weight": 1360.3, "duration": 1213.1, "steps": [], "distance": 7894.5 },
            { "summary": "", "weight": 2100.5, "duration": 1924.5, "steps": [], "distance": 18994.2 },
            { "summary": "", "weight": 1600.9, "duration": 1421.8, "steps": [], "distance": 15020.8 }
          ],
          "weight_name": "routability",
          "weight": 5061.7,
          "duration": 4559.4,
          "distance": 41909.5
        }
      ]
    }
    """;

    [Fact]
    public async Task Builds_url_and_parses_trip()
    {
        var (client, handler) = TestClient.Create(Sample);
        var response = await client.Optimization.OptimizeAsync(new OptimizationRequest(RoutingProfile.Driving,
            new LngLat(-122.42, 37.78), new LngLat(-122.45, 37.91), new LngLat(-122.48, 37.73))
        {
            Roundtrip = false,
            Source = TripSource.First,
            Destination = TripDestination.Last,
            Distributions = { new TripDistribution(0, 2) },
            Overview = RouteOverview.None,
        });

        Assert.Equal("/optimized-trips/v1/mapbox/driving/-122.42,37.78;-122.45,37.91;-122.48,37.73", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("false", p["roundtrip"]);
        Assert.Equal("first", p["source"]);
        Assert.Equal("last", p["destination"]);
        Assert.Equal("0,2", p["distributions"]);
        Assert.Equal("false", p["overview"]);
        Assert.Equal("geojson", p["geometries"]);

        var trip = Assert.Single(response.Trips);
        Assert.Equal(41909.5, trip.Distance);
        Assert.Equal(3, trip.Legs.Count);
        Assert.Equal(4, trip.Geometry!.Coordinates.Count);
        var order = response.GetVisitOrder();
        Assert.Equal(new[] { "North Lincoln Avenue", "Market Street", "Clay Street" }, order.Select(w => w.Name));
        Assert.Single(response.ToFeatureCollection().Features);
    }

    [Fact]
    public async Task NoTrips_throws()
    {
        var (client, _) = TestClient.Create("""{"code":"NoTrips","message":"No trips found"}""");
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() =>
            client.Optimization.OptimizeAsync(new OptimizationRequest(RoutingProfile.Driving, new LngLat(0, 0), new LngLat(1, 1))));
        Assert.Equal("NoTrips", ex.ErrorCode);
    }
}

public class MapMatchingTests
{
    private const string Sample = """
    {
      "code": "Ok",
      "matchings": [
        {
          "confidence": 0.86,
          "weight_name": "auto",
          "weight": 72.5,
          "duration": 61.4,
          "distance": 417.8,
          "legs": [ { "summary": "", "weight": 72.5, "duration": 61.4, "steps": [], "distance": 417.8 } ],
          "geometry": { "coordinates": [[-117.17282, 32.71204], [-117.17288, 32.71225], [-117.17293, 32.71244]], "type": "LineString" }
        }
      ],
      "tracepoints": [
        { "alternatives_count": 0, "waypoint_index": 0, "matchings_index": 0, "distance": 1.1, "name": "North Harbor Drive", "location": [-117.17282, 32.71204] },
        null,
        { "alternatives_count": 1, "waypoint_index": 1, "matchings_index": 0, "distance": 3.2, "name": "North Harbor Drive", "location": [-117.17293, 32.71244] }
      ]
    }
    """;

    [Fact]
    public async Task Builds_url_and_parses_matchings()
    {
        var (client, handler) = TestClient.Create(Sample);
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var points = new[]
        {
            new TracePoint(new LngLat(-117.17282, 32.712041), t0) { Radius = 10 },
            new TracePoint(new LngLat(-117.17288, 32.712245), t0.AddSeconds(5)) { Radius = 12, IsWaypoint = false },
            new TracePoint(new LngLat(-117.17293, 32.712444), t0.AddSeconds(10)) { Radius = 8 },
        };
        var response = await client.MapMatching.MatchAsync(new MapMatchingRequest(RoutingProfile.Driving, points)
        {
            Tidy = true,
            Steps = true,
            Ignore = MapMatchingIgnore.Oneways | MapMatchingIgnore.Restrictions,
        });

        Assert.Equal("/matching/v5/mapbox/driving/-117.17282,32.712041;-117.17288,32.712245;-117.17293,32.712444", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("geojson", p["geometries"]);
        Assert.Equal("10;12;8", p["radiuses"]);
        var unix = t0.ToUnixTimeSeconds();
        Assert.Equal($"{unix};{unix + 5};{unix + 10}", p["timestamps"]);
        Assert.Equal("0;2", p["waypoints"]);
        Assert.Equal("true", p["tidy"]);
        Assert.Equal("oneways,restrictions", p["ignore"]);

        var matching = Assert.Single(response.Matchings);
        Assert.Equal(0.86, matching.Confidence);
        Assert.Equal(417.8, matching.Distance);
        Assert.Equal(3, response.Tracepoints.Count);
        Assert.Null(response.Tracepoints[1]);
        Assert.Equal(1, response.Tracepoints[2]!.AlternativesCount);
        Assert.Equal("North Harbor Drive", response.Tracepoints[0]!.Name);
        var feature = Assert.Single(response.ToFeatureCollection().Features);
        Assert.Equal(0.86, feature.Properties["confidence"]!.GetValue<double>());
    }

    [Fact]
    public async Task Partial_timestamps_are_rejected()
    {
        var (client, _) = TestClient.Create(Sample);
        var request = new MapMatchingRequest(RoutingProfile.Walking, new[]
        {
            new TracePoint(new LngLat(0, 0), DateTimeOffset.UtcNow), new TracePoint(new LngLat(1, 1)),
        });
        await Assert.ThrowsAsync<ArgumentException>(() => client.MapMatching.MatchAsync(request));
    }

    [Fact]
    public async Task Long_traces_are_posted()
    {
        var (client, handler) = TestClient.Create(Sample);
        var points = Enumerable.Range(0, 100).Select(i => new TracePoint(new LngLat(-117.123456789 + i * 0.0001234567, 32.123456789 + i * 0.0001234567)) { Radius = 25, Name = "point number " + i }).ToList();
        await client.MapMatching.MatchAsync(new MapMatchingRequest(RoutingProfile.Driving, points) { Steps = true, AdditionalParameters = { ["note"] = new string('n', 3000) } });
        Assert.Equal(HttpMethod.Post, handler.Last.Method);
        Assert.Equal("/matching/v5/mapbox/driving", handler.Last.Uri.AbsolutePath);
        Assert.Equal(100, TestClient.Params(handler.Last.Body)["coordinates"].Split(';').Length);
    }
}

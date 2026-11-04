using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.GeoJson;
using Xunit;

namespace Cartovia.Mapbox.Services.Tests;

public class DirectionsTests
{
    internal const string Sample = """
    {
      "routes": [
        {
          "weight_name": "auto",
          "weight": 1332.6,
          "duration": 1181.2,
          "duration_typical": 1250.4,
          "distance": 9836.7,
          "legs": [
            {
              "via_waypoints": [],
              "admins": [ { "iso_3166_1_alpha3": "USA", "iso_3166_1": "US" } ],
              "weight": 1332.6,
              "duration": 1181.2,
              "steps": [
                {
                  "intersections": [
                    { "entry": [true], "bearings": [125], "duration": 12.4, "mapbox_streets_v8": { "class": "street" },
                      "is_urban": true, "admin_index": 0, "out": 0, "geometry_index": 0, "location": [-84.518399, 39.134126] }
                  ],
                  "maneuver": {
                    "type": "depart", "instruction": "Drive southeast on Dudley Street.",
                    "bearing_after": 125, "bearing_before": 0, "location": [-84.518399, 39.134126]
                  },
                  "name": "Dudley Street",
                  "duration": 23.4,
                  "distance": 154.2,
                  "driving_side": "right",
                  "weight": 28.6,
                  "mode": "driving",
                  "geometry": { "coordinates": [[-84.518399, 39.134126], [-84.51682, 39.133405]], "type": "LineString" },
                  "voiceInstructions": [ { "distanceAlongGeometry": 154.2, "announcement": "Drive southeast on Dudley Street.", "ssmlAnnouncement": "<speak>Drive southeast</speak>" } ],
                  "bannerInstructions": [ { "distanceAlongGeometry": 154.2, "primary": { "text": "Highland Avenue", "type": "turn", "modifier": "left", "components": [ { "text": "Highland Avenue", "type": "text" } ] } } ]
                },
                {
                  "intersections": [ { "entry": [true, false], "bearings": [18, 304], "in": 1, "out": 0, "location": [-84.51682, 39.133405],
                    "lanes": [ { "valid": true, "active": true, "indications": ["left"], "valid_indication": "left" } ] } ],
                  "maneuver": { "type": "arrive", "modifier": "left", "instruction": "Your destination is on the left.",
                    "bearing_after": 0, "bearing_before": 18, "location": [-84.51682, 39.133405] },
                  "name": "Highland Avenue",
                  "ref": "OH 4",
                  "duration": 0, "distance": 0, "driving_side": "right", "weight": 0, "mode": "driving",
                  "geometry": { "coordinates": [[-84.51682, 39.133405], [-84.51682, 39.133405]], "type": "LineString" }
                }
              ],
              "distance": 9836.7,
              "summary": "Dudley Street, Highland Avenue",
              "annotation": {
                "distance": [154.2, 9682.5],
                "duration": [23.4, 1157.8],
                "speed": [6.6, 8.4],
                "congestion": ["low", "unknown"],
                "congestion_numeric": [12, null],
                "maxspeed": [ { "speed": 40, "unit": "km/h" }, { "unknown": true } ]
              }
            }
          ],
          "geometry": { "coordinates": [[-84.518399, 39.134126], [-84.51682, 39.133405], [-84.512023, 39.102849]], "type": "LineString" }
        }
      ],
      "waypoints": [
        { "distance": 26.041, "name": "Dudley Street", "location": [-84.518399, 39.134126] },
        { "distance": 9.233, "name": "Highland Avenue", "location": [-84.512023, 39.102849] }
      ],
      "code": "Ok",
      "uuid": "AOAnJ1D4Q0mU1i-9-Q_6tQxNI1ZN4WAhRf2Bx9tZp7jcKrSl0uQfqA=="
    }
    """;

    [Fact]
    public async Task Builds_get_url_with_every_option()
    {
        var (client, handler) = TestClient.Create(Sample);
        using var culture = new CultureScope("de-DE");

        var request = new DirectionsRequest(RoutingProfile.DrivingTraffic, new[]
        {
            new RouteWaypoint(-84.518641, 39.134270) { Bearing = new Bearing(45, 90), Approach = Approach.Curb, Name = "Home" },
            new RouteWaypoint(-84.51, 39.12) { IsSilent = true, Radius = 50.5 },
            new RouteWaypoint(-84.512023, 39.102779) { Radius = double.PositiveInfinity, Name = "Work" },
        })
        {
            Alternatives = true,
            Steps = true,
            Overview = RouteOverview.Full,
            Annotations = RouteAnnotations.Duration | RouteAnnotations.Distance | RouteAnnotations.MaxSpeed | RouteAnnotations.CongestionNumeric,
            Exclude = RouteExclusions.Toll | RouteExclusions.Ferry | RouteExclusions.CashOnlyTolls,
            ExcludePoints = { new LngLat(-84.515, 39.125) },
            Language = "fr",
            BannerInstructions = true,
            VoiceInstructions = true,
            VoiceUnits = VoiceUnits.Metric,
            DepartAt = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.FromHours(1)),
            MaxHeight = 3.5,
        };
        await client.Directions.GetRouteAsync(request);

        var r = handler.Last;
        Assert.Equal(HttpMethod.Get, r.Method);
        Assert.Equal("/directions/v5/mapbox/driving-traffic/-84.518641,39.13427;-84.51,39.12;-84.512023,39.102779", Uri.UnescapeDataString(r.Uri.AbsolutePath));
        var p = TestClient.Params(r.Uri);
        Assert.Equal("geojson", p["geometries"]);
        Assert.Equal("true", p["alternatives"]);
        Assert.Equal("true", p["steps"]);
        Assert.Equal("full", p["overview"]);
        Assert.Equal("distance,duration,congestion_numeric,maxspeed", p["annotations"]);
        Assert.Equal("toll,ferry,cash_only_tolls,point(-84.515 39.125)", p["exclude"]);
        Assert.Equal("45,90;;", p["bearings"]);
        Assert.Equal(";50.5;unlimited", p["radiuses"]);
        Assert.Equal("curb;;", p["approaches"]);
        Assert.Equal("0;2", p["waypoints"]);
        Assert.Equal("Home;Work", p["waypoint_names"]);
        Assert.Equal("fr", p["language"]);
        Assert.Equal("metric", p["voice_units"]);
        Assert.Equal("2026-03-01T08:30:00+01:00", p["depart_at"]);
        Assert.Equal("3.5", p["max_height"]);
        Assert.Equal(TestClient.Token, p["access_token"]);
    }

    [Fact]
    public async Task Parses_routes_legs_steps_and_waypoints()
    {
        var (client, _) = TestClient.Create(Sample);
        var response = await client.Directions.GetRouteAsync(RoutingProfile.Driving, new[] { new LngLat(-84.518641, 39.134270), new LngLat(-84.512023, 39.102779) });

        Assert.Equal("Ok", response.Code);
        var route = Assert.Single(response.Routes);
        Assert.Same(route, response.BestRoute);
        Assert.Equal(9836.7, route.Distance);
        Assert.Equal(1181.2, route.Duration);
        Assert.Equal(1250.4, route.DurationTypical);
        Assert.Equal("auto", route.WeightName);
        Assert.Equal(TimeSpan.FromSeconds(1181.2), route.DurationTime);
        Assert.Equal(3, route.Geometry!.Coordinates.Count);
        Assert.Equal(-84.512023, route.GetBounds()!.Value.East);

        var leg = Assert.Single(route.Legs);
        Assert.Equal("Dudley Street, Highland Avenue", leg.Summary);
        Assert.Equal(2, leg.Steps.Count);
        Assert.True(leg.ExtensionData!.ContainsKey("admins"));
        var step = leg.Steps[0];
        Assert.Equal("Dudley Street", step.Name);
        Assert.Equal("depart", step.Maneuver.Type);
        Assert.Equal("Drive southeast on Dudley Street.", step.Maneuver.Instruction);
        Assert.Equal(new LngLat(-84.518399, 39.134126), step.Maneuver.Location);
        Assert.Equal(125, step.Maneuver.BearingAfter);
        Assert.Equal("right", step.DrivingSide);
        Assert.Equal(2, step.Geometry!.Coordinates.Count);
        Assert.Equal("<speak>Drive southeast</speak>", step.VoiceInstructions![0].SsmlAnnouncement);
        Assert.Equal("Highland Avenue", step.BannerInstructions![0].Primary!.Text);
        Assert.Equal("left", leg.Steps[1].Maneuver.Modifier);
        Assert.Equal("OH 4", leg.Steps[1].Ref);
        var intersection = leg.Steps[1].Intersections[0];
        Assert.Equal(1, intersection.In);
        Assert.Equal("left", intersection.Lanes![0].ValidIndication);

        Assert.Equal(new[] { 154.2, 9682.5 }, leg.Annotation!.Distance);
        Assert.Equal(new int?[] { 12, null }, leg.Annotation.CongestionNumeric);
        Assert.Equal(40, leg.Annotation.MaxSpeed![0].Speed);
        Assert.Equal("km/h", leg.Annotation.MaxSpeed[0].Unit);
        Assert.True(leg.Annotation.MaxSpeed[1].Unknown);

        Assert.Equal(2, response.Waypoints.Count);
        Assert.Equal("Highland Avenue", response.Waypoints[1].Name);
        Assert.Equal(new LngLat(-84.512023, 39.102849), response.Waypoints[1].Location);
        Assert.Equal(26.041, response.Waypoints[0].Distance);

        var fc = response.ToFeatureCollection();
        var feature = Assert.Single(fc.Features);
        Assert.IsType<LineString>(feature.Geometry);
        Assert.Equal(9836.7, feature.Properties["distance"]!.GetValue<double>());
        Assert.Equal(0, feature.Properties["route_index"]!.GetValue<int>());
        Assert.All(response.WaypointsToFeatureCollection().Features, f => Assert.IsType<Point>(f.Geometry));
    }

    [Fact]
    public async Task Uses_form_post_when_the_url_is_too_long()
    {
        var (client, handler) = TestClient.Create(Sample);
        var waypoints = Enumerable.Range(0, 25)
            .Select(i => new RouteWaypoint(2.0 + i * 0.001234567, 48.0 + i * 0.001234567) { Name = new string('x', 400) + i, Bearing = new Bearing(i, 45) })
            .ToList();
        await client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Walking, waypoints) { Steps = true });

        var r = handler.Last;
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("/directions/v5/mapbox/walking", r.Uri.AbsolutePath);
        Assert.Equal("application/x-www-form-urlencoded", r.ContentType);
        Assert.Equal(TestClient.Token, TestClient.Params(r.Uri)["access_token"]);
        var form = TestClient.Params(r.Body);
        Assert.Equal(25, form["coordinates"].Split(';').Length);
        Assert.StartsWith("2,48;2.001234567,48.001234567;", form["coordinates"]);
        Assert.Equal("geojson", form["geometries"]);
        Assert.Equal("true", form["steps"]);
        Assert.Equal(25, form["waypoint_names"].Split(';').Length);
        Assert.False(form.ContainsKey("access_token"));
    }

    [Fact]
    public async Task NoRoute_code_throws_MapboxApiException()
    {
        var (client, _) = TestClient.Create("""{"code":"NoRoute","message":"No route found","routes":[]}""");
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() =>
            client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new LngLat(0, 0), new LngLat(1, 1))));
        Assert.Equal("NoRoute", ex.ErrorCode);
        Assert.Equal("No route found", ex.ApiMessage);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
    }

    [Fact]
    public async Task Validates_waypoint_count_and_silent_endpoints()
    {
        var (client, handler) = TestClient.Create(Sample);
        await Assert.ThrowsAsync<ArgumentException>(() => client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new LngLat(0, 0))));
        var tooMany = Enumerable.Range(0, 26).Select(i => new LngLat(i * 0.01, 0)).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, tooMany)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new[]
        {
            new RouteWaypoint(0, 0) { IsSilent = true }, new RouteWaypoint(1, 1),
        })));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        var (client, _) = TestClient.Create(Sample);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new LngLat(0, 0), new LngLat(1, 1)), cts.Token));
    }
}

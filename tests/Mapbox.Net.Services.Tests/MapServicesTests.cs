using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Xunit;

namespace Mapbox.Net.Services.Tests;

public class TilequeryTests
{
    private const string Sample = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "id": 1234,
          "geometry": { "type": "Point", "coordinates": [-122.42901, 37.80633] },
          "properties": { "class": "park", "type": "park", "name": "Fort Mason",
                          "tilequery": { "distance": 0, "geometry": "polygon", "layer": "landuse" } }
        },
        {
          "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [-122.4291, 37.8064] },
          "properties": { "class": "street", "name": "Bay Street",
                          "tilequery": { "distance": 12.5, "geometry": "linestring", "layer": "road" } }
        }
      ]
    }
    """;

    private const string TerrainSample = """
    {
      "type": "FeatureCollection",
      "features": [
        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [7.6585, 45.9763] },
          "properties": { "ele": 4470, "index": 10, "tilequery": { "distance": 0, "geometry": "polygon", "layer": "contour" } } },
        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [7.6585, 45.9763] },
          "properties": { "ele": 4480, "index": -1, "tilequery": { "distance": 0, "geometry": "polygon", "layer": "contour" } } },
        { "type": "Feature", "geometry": { "type": "Point", "coordinates": [7.6585, 45.9763] },
          "properties": { "ele": 4400, "index": 5, "tilequery": { "distance": 0, "geometry": "polygon", "layer": "contour" } } }
      ]
    }
    """;

    [Fact]
    public async Task Builds_url_and_parses_features()
    {
        var (client, handler) = TestClient.Create(Sample);
        var response = await client.Tilequery.QueryAsync(new TilequeryRequest(new[] { "mapbox.mapbox-streets-v8", "mapbox.mapbox-terrain-v2" }, new LngLat(-122.42901, 37.80633))
        {
            Radius = 25,
            Limit = 10,
            Dedupe = false,
            Geometry = TilequeryGeometry.Polygon,
            Layers = { "landuse", "road" },
        });

        Assert.Equal("/v4/mapbox.mapbox-streets-v8,mapbox.mapbox-terrain-v2/tilequery/-122.42901,37.80633.json", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("25", p["radius"]);
        Assert.Equal("10", p["limit"]);
        Assert.Equal("false", p["dedupe"]);
        Assert.Equal("polygon", p["geometry"]);
        Assert.Equal("landuse,road", p["layers"]);

        Assert.Equal(2, response.Features.Count);
        var park = response.Features[0];
        Assert.Equal("landuse", park.Layer);
        Assert.Equal("polygon", park.GeometryType);
        Assert.Equal(0, park.Distance);
        Assert.Equal("Fort Mason", park.GetProperty<string>("name"));
        Assert.Equal(1234, park.Feature.Id!.GetValue<int>());
        Assert.Equal(12.5, response.Features[1].Distance);
        Assert.Equal(2, response.FeatureCollection.Features.Count);
    }

    [Fact]
    public async Task Elevation_uses_terrain_contours()
    {
        var (client, handler) = TestClient.Create(TerrainSample);
        var elevation = await client.Tilequery.GetElevationAsync(new LngLat(7.6585, 45.9763));
        Assert.Equal(4480, elevation);
        Assert.StartsWith("/v4/mapbox.mapbox-terrain-v2/tilequery/7.6585,45.9763.json", handler.Last.Uri.AbsolutePath);
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("contour", p["layers"]);
        Assert.Equal("50", p["limit"]);
    }
}

public class StaticImagesTests
{
    [Fact]
    public void Builds_url_with_center_bearing_pitch_and_markers()
    {
        using var client = new MapboxClient(TestClient.Token);
        using var culture = new CultureScope("fr-FR");
        var request = new StaticImageRequest
        {
            Style = "mapbox://styles/mapbox/outdoors-v12",
            Center = new LngLat(-122.4241, 37.78),
            Zoom = 15.256,
            Bearing = 20,
            Pitch = 60,
            Width = 800,
            Height = 600,
            HighDensity = true,
            Overlays =
            {
                new StaticMarker(new LngLat(-122.4241, 37.78), "a", "#F74E4E", StaticMarkerSize.Large),
                new StaticMarker(new LngLat(-122.43, 37.79)),
                new StaticMarker(new LngLat(-122.42, 37.77), label: "embassy"),
            },
            Attribution = false,
            Logo = false,
            BeforeLayer = "road-label",
        };

        var url = client.StaticImages.BuildUrl(request);
        Assert.Equal(
            "https://api.mapbox.com/styles/v1/mapbox/outdoors-v12/static/pin-l-a+f74e4e(-122.4241,37.78),pin-s(-122.43,37.79),pin-s-embassy(-122.42,37.77)/-122.4241,37.78,15.26,20,60/800x600@2x",
            Uri.UnescapeDataString(url.GetLeftPart(UriPartial.Path)));
        var p = TestClient.Params(url);
        Assert.Equal("false", p["attribution"]);
        Assert.Equal("false", p["logo"]);
        Assert.Equal("road-label", p["before_layer"]);
        Assert.Equal(TestClient.Token, p["access_token"]);
    }

    [Fact]
    public void Builds_url_with_auto_viewport_path_geojson_and_padding()
    {
        using var client = new MapboxClient(TestClient.Token);
        var path = new StaticPath(new[] { new LngLat(-120.2, 38.5), new LngLat(-120.95, 40.7), new LngLat(-126.453, 43.252) })
        {
            StrokeWidth = 5, StrokeColor = "f44", StrokeOpacity = 0.5, FillColor = "00f", FillOpacity = 0.25,
        };
        var geojson = new StaticGeoJsonOverlay(new Feature { Geometry = new Point(-73.99, 40.7), Properties = new System.Text.Json.Nodes.JsonObject { ["title"] = "NYC", ["marker-color"] = "#f00" } });
        var request = new StaticImageRequest { Overlays = { path, geojson }, Padding = new StaticImagePadding(5, 8, 10, 7) };

        var url = client.StaticImages.BuildUrl(request);
        var raw = url.AbsoluteUri;
        Assert.Contains("/static/path-5+f44-0.5+00f-0.25(_p~iF~ps%7CU_ulLnnqC_mqNvxq%60%40)", raw);
        Assert.Contains(",geojson(%7B%22type%22%3A%22Feature%22", raw);
        Assert.Contains("%23f00", raw); // '#' must be encoded
        Assert.EndsWith("/auto/600x400", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("5,8,10,7", TestClient.Params(url)["padding"]);
    }

    [Fact]
    public void Builds_url_with_bounds_and_custom_marker()
    {
        using var client = new MapboxClient(TestClient.Token);
        var request = new StaticImageRequest
        {
            Style = "myuser/ckabc123",
            Bounds = new LngLatBounds(-77.043686, 38.892035, -77.028923, 38.904192),
            Overlays = { new StaticCustomMarker(new Uri("https://docs.mapbox.com/api/img/custom-marker.png"), new LngLat(-76.9, 38.9)) },
            Padding = new StaticImagePadding(10),
        };
        var url = client.StaticImages.BuildUrl(request);
        Assert.Contains("/styles/v1/myuser/ckabc123/static/url-https%3A%2F%2Fdocs.mapbox.com%2Fapi%2Fimg%2Fcustom-marker.png(-76.9,38.9)/", url.AbsoluteUri);
        Assert.Contains("/[-77.043686,38.892035,-77.028923,38.904192]/600x400", Uri.UnescapeDataString(url.AbsoluteUri));
        Assert.Equal("10", TestClient.Params(url)["padding"]);
    }

    [Fact]
    public void Validates_request()
    {
        using var client = new MapboxClient(TestClient.Token);
        Assert.Throws<InvalidOperationException>(() => client.StaticImages.BuildUrl(new StaticImageRequest()));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.StaticImages.BuildUrl(new StaticImageRequest { Center = new LngLat(0, 0), Width = 2000 }));
        Assert.Throws<ArgumentException>(() => client.StaticImages.BuildUrl(new StaticImageRequest { Center = new LngLat(0, 0), Style = "streets" }));
        Assert.Throws<ArgumentException>(() => client.StaticImages.BuildUrl(new StaticImageRequest { Overlays = { new StaticMarker(new LngLat(0, 0), color: "red") } }));
        Assert.Throws<InvalidOperationException>(() => client.StaticImages.BuildUrl(new StaticImageRequest { Center = new LngLat(0, 0), Padding = new StaticImagePadding(5) }));
    }

    [Fact]
    public async Task Downloads_image_bytes()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var (client, handler) = TestClient.CreateBytes(png, "image/png");
        var bytes = await client.StaticImages.GetImageAsync(new StaticImageRequest { Center = new LngLat(2.35, 48.85), Zoom = 10 });
        Assert.Equal(png, bytes);
        Assert.Equal(HttpMethod.Get, handler.Last.Method);
        Assert.Equal("/styles/v1/mapbox/streets-v12/static/2.35,48.85,10/600x400", Uri.UnescapeDataString(handler.Last.Uri.AbsolutePath));
    }
}

public class SearchBoxTests
{
    private const string SuggestSample = """
    {
      "suggestions": [
        {
          "name": "Michigan Stadium",
          "mapbox_id": "dXJuOm1ieHBvaTo0ZTg2ZWFkNS1jOWMwLTQ3OWEtOTA5Mi1kMDVlNDQ3NDdlODk",
          "feature_type": "poi",
          "address": "1201 S Main St",
          "full_address": "1201 S Main St, Ann Arbor, Michigan 48104, United States of America",
          "place_formatted": "Ann Arbor, Michigan 48104, United States of America",
          "context": {
            "country": { "name": "United States of America", "country_code": "US", "country_code_alpha_3": "USA" },
            "region": { "name": "Michigan", "region_code": "MI", "region_code_full": "US-MI" },
            "postcode": { "name": "48104" },
            "place": { "name": "Ann Arbor" },
            "street": { "name": "s main st" }
          },
          "language": "en",
          "maki": "marker",
          "poi_category": ["sports", "stadium"],
          "poi_category_ids": ["sports", "stadium"],
          "external_ids": { "foursquare": "4ad8f0c9f964a520951521e3" },
          "metadata": {}
        }
      ],
      "attribution": "© 2024 Mapbox and its suppliers. All rights reserved."
    }
    """;

    private const string RetrieveSample = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "geometry": { "coordinates": [-83.748708, 42.265837], "type": "Point" },
          "properties": {
            "name": "Michigan Stadium",
            "mapbox_id": "dXJuOm1ieHBvaTo0ZTg2ZWFkNS1jOWMwLTQ3OWEtOTA5Mi1kMDVlNDQ3NDdlODk",
            "feature_type": "poi",
            "address": "1201 S Main St",
            "full_address": "1201 S Main St, Ann Arbor, Michigan 48104, United States of America",
            "place_formatted": "Ann Arbor, Michigan 48104, United States of America",
            "context": { "country": { "name": "United States of America", "country_code": "US" }, "place": { "name": "Ann Arbor" } },
            "coordinates": { "latitude": 42.265837, "longitude": -83.748708,
              "routable_points": [ { "name": "POI", "latitude": 42.2662, "longitude": -83.7491 } ] },
            "language": "en",
            "maki": "marker",
            "poi_category": ["sports", "stadium"],
            "poi_category_ids": ["sports", "stadium"],
            "external_ids": { "foursquare": "4ad8f0c9f964a520951521e3" },
            "metadata": { "phone": "+1 734-647-2583", "website": "https://mgoblue.com" }
          }
        }
      ],
      "attribution": "© 2024 Mapbox and its suppliers. All rights reserved."
    }
    """;

    [Fact]
    public async Task Suggest_then_retrieve_share_the_session_token()
    {
        var (client, handler) = TestClient.Create(SuggestSample);
        var suggest = await client.Search.SuggestAsync(new SearchBoxSuggestRequest("michigan stadium")
        {
            Proximity = new LngLat(-83.748, 42.265),
            Types = SearchBoxTypes.Poi | SearchBoxTypes.Address,
            Limit = 5,
            Language = "en",
            Countries = { "US" },
        });

        Assert.Equal("/search/searchbox/v1/suggest", handler.Last.Uri.AbsolutePath);
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("michigan stadium", p["q"]);
        Assert.Equal("address,poi", p["types"]);
        Assert.Equal("-83.748,42.265", p["proximity"]);
        Assert.Equal("us", p["country"]);
        Assert.True(Guid.TryParse(p["session_token"], out _));
        Assert.Equal(p["session_token"], suggest.SessionToken);

        var s = Assert.Single(suggest.Suggestions);
        Assert.Equal("Michigan Stadium", s.Name);
        Assert.Equal("poi", s.FeatureType);
        Assert.Equal("Michigan", s.Context!.Region!.Name);
        Assert.Equal(new[] { "sports", "stadium" }, s.PoiCategoryIds);

        var (client2, handler2) = TestClient.Create(RetrieveSample);
        var retrieved = await client2.Search.RetrieveAsync(new SearchBoxRetrieveRequest(s, suggest.SessionToken));
        Assert.Equal("/search/searchbox/v1/retrieve/" + s.MapboxId, handler2.Last.Uri.AbsolutePath);
        Assert.Equal(suggest.SessionToken, TestClient.Params(handler2.Last.Uri)["session_token"]);

        var f = retrieved.First!;
        Assert.Equal(new LngLat(-83.748708, 42.265837), f.Location);
        Assert.Equal("1201 S Main St", f.Address);
        Assert.Equal("4ad8f0c9f964a520951521e3", f.ExternalIds!["foursquare"]);
        Assert.Equal("+1 734-647-2583", f.Metadata!.Value.GetProperty("phone").GetString());
        Assert.Equal(-83.7491, f.Coordinates!.RoutablePoints![0].Longitude);
        Assert.IsType<Point>(f.Feature.Geometry);
    }

    [Fact]
    public async Task Forward_and_category_urls()
    {
        var (client, handler) = TestClient.Create(RetrieveSample);
        await client.Search.ForwardAsync(new SearchBoxForwardRequest("cafe paris") { Limit = 3, BBox = new LngLatBounds(2.2, 48.8, 2.4, 48.9) });
        Assert.Equal("/search/searchbox/v1/forward", handler.Last.Uri.AbsolutePath);
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("cafe paris", p["q"]);
        Assert.Equal("2.2,48.8,2.4,48.9", p["bbox"]);

        var result = await client.Search.CategoryAsync(new SearchBoxCategoryRequest("coffee") { Proximity = new LngLat(2.35, 48.85), Limit = 10 });
        Assert.Equal("/search/searchbox/v1/category/coffee", handler.Last.Uri.AbsolutePath);
        Assert.Equal("10", TestClient.Params(handler.Last.Uri)["limit"]);
        Assert.Single(result.Features);
    }
}

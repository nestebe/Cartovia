using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Xunit;

namespace Mapbox.Net.Services.Tests;

public class GeocodingTests
{
    internal const string ForwardSample = """
    {
      "type": "FeatureCollection",
      "features": [
        {
          "type": "Feature",
          "id": "dXJuOm1ieGFkcjo0ZTE1ZTlkMS1iNzFmLTQ0ZWItODJkMS1lNzk1YTJiN2U1MTc",
          "geometry": { "type": "Point", "coordinates": [-111.86313, 40.725163] },
          "properties": {
            "mapbox_id": "dXJuOm1ieGFkcjo0ZTE1ZTlkMS1iNzFmLTQ0ZWItODJkMS1lNzk1YTJiN2U1MTc",
            "feature_type": "address",
            "full_address": "974 2100 South, Salt Lake City, Utah 84106, United States",
            "name": "974 2100 South",
            "name_preferred": "974 2100 South",
            "coordinates": {
              "longitude": -111.86313,
              "latitude": 40.725163,
              "accuracy": "rooftop",
              "routable_points": [ { "name": "default", "latitude": 40.725503, "longitude": -111.86313 } ]
            },
            "place_formatted": "Salt Lake City, Utah 84106, United States",
            "match_code": {
              "address_number": "matched", "street": "matched", "postcode": "unmatched", "place": "unmatched",
              "region": "unmatched", "locality": "not_applicable", "country": "inferred", "confidence": "low"
            },
            "context": {
              "address": { "mapbox_id": "dXJuOm1ieGFkcjo0ZTE1", "address_number": "974", "street_name": "2100 South", "name": "974 2100 South" },
              "street": { "mapbox_id": "dXJuOm1ieGFkcjo0ZTE1ZTlk", "name": "2100 South" },
              "neighborhood": { "mapbox_id": "dXJuOm1ieHBsYzpGNlRF", "name": "Fairmont", "alternate": { "mapbox_id": "x", "name": "Winfield" } },
              "postcode": { "mapbox_id": "dXJuOm1ieHBsYzpFZ1hF", "name": "84106" },
              "place": { "mapbox_id": "dXJuOm1ieHBsYzpFV3RF", "name": "Salt Lake City", "wikidata_id": "Q23337" },
              "district": { "mapbox_id": "dXJuOm1ieHBsYzpBUVRF", "name": "Salt Lake County", "wikidata_id": "Q484556" },
              "region": { "mapbox_id": "dXJuOm1ieHBsYzpCVVRF", "name": "Utah", "wikidata_id": "Q829", "region_code": "UT", "region_code_full": "US-UT" },
              "country": { "mapbox_id": "dXJuOm1ieHBsYzpJdVE", "name": "United States", "wikidata_id": "Q30", "country_code": "US", "country_code_alpha_3": "USA" }
            }
          }
        },
        {
          "type": "Feature",
          "id": "dXJuOm1ieHBsYzpFV3RF",
          "geometry": { "type": "Point", "coordinates": [-111.891, 40.7608] },
          "properties": {
            "mapbox_id": "dXJuOm1ieHBsYzpFV3RF",
            "feature_type": "place",
            "name": "Salt Lake City",
            "place_formatted": "Utah, United States",
            "bbox": [-112.101, 40.699, -111.739, 40.853],
            "context": { "country": { "name": "United States", "country_code": "US", "country_code_alpha_3": "USA" } }
          }
        }
      ],
      "attribution": "NOTICE: © 2024 Mapbox and its suppliers. All rights reserved."
    }
    """;

    [Fact]
    public async Task Forward_builds_query_with_invariant_numbers()
    {
        var (client, handler) = TestClient.Create(ForwardSample);
        using var culture = new CultureScope("fr-FR");

        await client.Geocoding.ForwardAsync(new ForwardGeocodingRequest("974 2100 South; Salt Lake City")
        {
            Proximity = new LngLat(-111.8631, 40.7251),
            BBox = new LngLatBounds(-112.5, 40.5, -111.5, 41.0),
            Countries = { "US", "ca" },
            Language = "en",
            Limit = 3,
            Types = GeocodingTypes.Address | GeocodingTypes.Place | GeocodingTypes.SecondaryAddress,
            Autocomplete = false,
            Worldview = "us",
            Permanent = true,
        });

        var r = handler.Last;
        Assert.Equal(HttpMethod.Get, r.Method);
        Assert.Equal("https://api.mapbox.com/search/geocode/v6/forward", r.Uri.GetLeftPart(UriPartial.Path));
        var p = TestClient.Params(r.Uri);
        Assert.Equal("974 2100 South, Salt Lake City", p["q"]);
        Assert.Equal("-111.8631,40.7251", p["proximity"]);
        Assert.Equal("-112.5,40.5,-111.5,41", p["bbox"]);
        Assert.Equal("us,ca", p["country"]);
        Assert.Equal("en", p["language"]);
        Assert.Equal("3", p["limit"]);
        Assert.Equal("place,address,secondary_address", p["types"]);
        Assert.Equal("false", p["autocomplete"]);
        Assert.Equal("us", p["worldview"]);
        Assert.Equal("true", p["permanent"]);
        Assert.Equal(TestClient.Token, p["access_token"]);
        Assert.DoesNotContain(" ", r.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Forward_structured_input_and_ip_proximity()
    {
        var (client, handler) = TestClient.Create(ForwardSample);
        await client.Geocoding.ForwardAsync(new ForwardGeocodingRequest(new StructuredAddress
        {
            AddressNumber = "974", Street = "2100 South", Place = "Salt Lake City", Region = "UT", Country = "us",
        }) { ProximityFromIp = true });

        var p = TestClient.Params(handler.Last.Uri);
        Assert.False(p.ContainsKey("q"));
        Assert.Equal("974", p["address_number"]);
        Assert.Equal("2100 South", p["street"]);
        Assert.Equal("Salt Lake City", p["place"]);
        Assert.Equal("UT", p["region"]);
        Assert.Equal("us", p["country"]);
        Assert.Equal("ip", p["proximity"]);
    }

    [Fact]
    public async Task Forward_parses_typed_results()
    {
        var (client, _) = TestClient.Create(ForwardSample);
        var response = await client.Geocoding.ForwardAsync("974 2100 South");

        Assert.Equal(2, response.Features.Count);
        Assert.StartsWith("NOTICE", response.Attribution);
        var a = response.First!;
        Assert.Equal("address", a.FeatureType);
        Assert.Equal("974 2100 South", a.Name);
        Assert.Equal("974 2100 South, Salt Lake City, Utah 84106, United States", a.FullAddress);
        Assert.Equal("974 2100 South, Salt Lake City, Utah 84106, United States", a.DisplayName);
        Assert.Equal(new LngLat(-111.86313, 40.725163), a.Location);
        Assert.Equal("rooftop", a.Coordinates!.Accuracy);
        Assert.Equal(40.725503, a.Coordinates.RoutablePoints![0].Latitude);
        Assert.Equal("low", a.MatchCode!.Confidence);
        Assert.Equal("matched", a.MatchCode.AddressNumber);
        Assert.Equal("Utah", a.Context!.Region!.Name);
        Assert.Equal("US-UT", a.Context.Region.RegionCodeFull);
        Assert.Equal("USA", a.Context.Country!.CountryCodeAlpha3);
        Assert.Equal("974", a.Context.Address!.AddressNumber);
        Assert.Equal("Salt Lake County", a.Context.District!.Name);
        Assert.True(a.Context.Neighborhood!.ExtensionData!.ContainsKey("alternate"));

        // GeoJSON side: ready for a map source.
        Assert.IsType<Point>(a.Feature.Geometry);
        Assert.Equal("address", a.Feature.Properties["feature_type"]!.GetValue<string>());
        Assert.Equal(2, response.FeatureCollection.Features.Count);

        var place = response.Features[1];
        Assert.Equal(new LngLatBounds(-112.101, 40.699, -111.739, 40.853), place.BBox);
        Assert.Null(place.Coordinates);
        Assert.Equal(new LngLat(-111.891, 40.7608), place.Location); // falls back to the geometry
        Assert.Equal("Salt Lake City, Utah, United States", place.DisplayName);
    }

    [Fact]
    public async Task Reverse_builds_query()
    {
        var (client, handler) = TestClient.Create(ForwardSample);
        await client.Geocoding.ReverseAsync(new ReverseGeocodingRequest(new LngLat(2.294481, 48.858370))
        {
            Types = GeocodingTypes.Address,
            Limit = 2,
            Language = "fr",
        });
        Assert.Equal("/search/geocode/v6/reverse", handler.Last.Uri.AbsolutePath);
        var p = TestClient.Params(handler.Last.Uri);
        Assert.Equal("2.294481", p["longitude"]);
        Assert.Equal("48.85837", p["latitude"]);
        Assert.Equal("address", p["types"]);
        Assert.Equal("2", p["limit"]);
        Assert.Equal("fr", p["language"]);
    }

    [Fact]
    public async Task Batch_posts_json_array_and_parses_each_result()
    {
        var json = "{\"batch\":[" + ForwardSample + ",{\"type\":\"FeatureCollection\",\"features\":[],\"attribution\":\"x\"}]}";
        var (client, handler) = TestClient.Create(json);
        var results = await client.Geocoding.BatchAsync(new GeocodingRequest[]
        {
            new ForwardGeocodingRequest("974 2100 South") { Limit = 1, Types = GeocodingTypes.Address, Proximity = new LngLat(-111.9, 40.7) },
            new ReverseGeocodingRequest(new LngLat(-73.986136, 40.748086)) { Types = GeocodingTypes.Address },
        }, permanent: true);

        var r = handler.Last;
        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("/search/geocode/v6/batch", r.Uri.AbsolutePath);
        Assert.Equal("application/json", r.ContentType);
        Assert.Equal("true", TestClient.Params(r.Uri)["permanent"]);
        using var body = JsonDocument.Parse(r.Body!);
        var items = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("974 2100 South", items[0].GetProperty("q").GetString());
        Assert.Equal(1, items[0].GetProperty("limit").GetInt32());
        Assert.Equal("address", items[0].GetProperty("types")[0].GetString());
        Assert.Equal(-111.9, items[0].GetProperty("proximity")[0].GetDouble());
        Assert.Equal(-73.986136, items[1].GetProperty("longitude").GetDouble());
        Assert.Equal(40.748086, items[1].GetProperty("latitude").GetDouble());

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].Features.Count);
        Assert.Empty(results[1].Features);
        Assert.Null(results[1].First);
    }

    [Fact]
    public async Task Forward_requires_query_or_structured()
    {
        var (client, handler) = TestClient.Create(ForwardSample);
        await Assert.ThrowsAsync<ArgumentException>(() => client.Geocoding.ForwardAsync(new ForwardGeocodingRequest("  ")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Geocoding.ForwardAsync(new ForwardGeocodingRequest(new StructuredAddress())));
        Assert.Empty(handler.Requests);
    }
}

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Cartovia.Mapbox.Services.Tests;

public class ClientTests
{
    [Fact]
    public async Task Http_error_maps_to_MapboxApiException_without_leaking_the_token()
    {
        var (client, _) = TestClient.Create($$"""{"message":"Not Authorized - Invalid Token {{TestClient.Token}}"}""", HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() => client.Geocoding.ForwardAsync("Paris"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.True(ex.IsUnauthorized);
        Assert.Equal("/search/geocode/v6/forward", ex.RequestPath);
        Assert.StartsWith("Not Authorized - Invalid Token", ex.ApiMessage);
        Assert.DoesNotContain(TestClient.Token, ex.Message);
        Assert.DoesNotContain(TestClient.Token, ex.ApiMessage);
        Assert.DoesNotContain(TestClient.Token, ex.ResponseBody);
        Assert.DoesNotContain(TestClient.Token, ex.ToString());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Rate_limit_headers_are_exposed()
    {
        var reset = DateTimeOffset.UtcNow.AddSeconds(42);
        var (client, _) = TestClient.Create("""{"message":"Too Many Requests"}""", (HttpStatusCode)429, r =>
        {
            r.Headers.Add("X-Rate-Limit-Interval", "60");
            r.Headers.Add("X-Rate-Limit-Limit", "600");
            r.Headers.Add("X-Rate-Limit-Reset", reset.ToUnixTimeSeconds().ToString());
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
        });
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() =>
            client.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, new LngLat(0, 0), new LngLat(1, 1))));

        Assert.True(ex.IsRateLimited);
        Assert.NotNull(ex.RateLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), ex.RateLimit!.Interval);
        Assert.Equal(600, ex.RateLimit.Limit);
        Assert.Equal(reset.ToUnixTimeSeconds(), ex.RateLimit.Reset!.Value.ToUnixTimeSeconds());
        Assert.Equal(TimeSpan.FromSeconds(42), ex.RateLimit.RetryAfter);
        Assert.Equal("Too Many Requests", ex.ApiMessage);
    }

    [Fact]
    public async Task Navigation_error_code_is_read_from_error_body()
    {
        var (client, _) = TestClient.Create("""{"message":"Coordinate is invalid: 200,0","code":"InvalidInput"}""", (HttpStatusCode)422);
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() =>
            client.Matrix.GetMatrixAsync(new MatrixRequest(RoutingProfile.Driving, new LngLat(200, 0), new LngLat(1, 1))));
        Assert.Equal("InvalidInput", ex.ErrorCode);
        Assert.Equal(422, (int)ex.StatusCode);
    }

    [Fact]
    public async Task Non_json_error_body_is_handled()
    {
        var (client, _) = TestClient.Create("<html>Bad gateway</html>", HttpStatusCode.BadGateway);
        var ex = await Assert.ThrowsAsync<MapboxApiException>(() => client.Geocoding.ReverseAsync(new LngLat(1, 2)));
        Assert.Null(ex.ApiMessage);
        Assert.Equal("<html>Bad gateway</html>", ex.ResponseBody);
    }

    [Fact]
    public async Task Base_url_override_is_honoured()
    {
        var (client, handler) = TestClient.Create(GeocodingTests.ForwardSample, baseUrl: new Uri("https://proxy.example.com/mapbox"));
        await client.Geocoding.ForwardAsync("Paris");
        Assert.Equal("https://proxy.example.com/mapbox/search/geocode/v6/forward", handler.Last.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Uri("https://proxy.example.com/mapbox"), client.BaseUrl);
    }

    [Fact]
    public void Constructor_validates_arguments()
    {
        Assert.Throws<ArgumentException>(() => new MapboxClient(" "));
        Assert.Throws<ArgumentException>(() => new MapboxClient(new MapboxClientOptions("pk.x") { BaseUrl = new Uri("/relative", UriKind.Relative) }));
    }

    [Fact]
    public async Task Requests_send_user_agent_and_token_last()
    {
        HttpRequestMessage? seen = null;
        var handler = new FakeHandler(r =>
        {
            seen = r;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GeocodingTests.ForwardSample) };
        });
        using var client = new MapboxClient(TestClient.Token, new HttpClient(handler));
        await client.Geocoding.ForwardAsync("Paris");
        Assert.Contains(seen!.Headers.UserAgent, p => p.Product?.Name == "Cartovia.Mapbox.Services");
        Assert.EndsWith("access_token=" + Uri.EscapeDataString(TestClient.Token), seen.RequestUri!.Query);
    }

    [Fact]
    public void Interfaces_are_exposed_for_mocking()
    {
        IMapboxClient client = new MapboxClient(TestClient.Token);
        Assert.IsAssignableFrom<IGeocodingService>(client.Geocoding);
        Assert.IsAssignableFrom<ISearchBoxService>(client.Search);
        Assert.IsAssignableFrom<IDirectionsService>(client.Directions);
        Assert.IsAssignableFrom<IMatrixService>(client.Matrix);
        Assert.IsAssignableFrom<IIsochroneService>(client.Isochrone);
        Assert.IsAssignableFrom<IOptimizationService>(client.Optimization);
        Assert.IsAssignableFrom<IMapMatchingService>(client.MapMatching);
        Assert.IsAssignableFrom<IStaticImagesService>(client.StaticImages);
        Assert.IsAssignableFrom<ITilequeryService>(client.Tilequery);
    }
}

public class PolylineCodecTests
{
    private static readonly LngLat[] GoogleExample = { new(-120.2, 38.5), new(-120.95, 40.7), new(-126.453, 43.252) };

    [Fact]
    public void Encodes_reference_example() => Assert.Equal("_p~iF~ps|U_ulLnnqC_mqNvxq`@", PolylineCodec.Encode(GoogleExample));

    [Fact]
    public void Decodes_reference_example()
    {
        var decoded = PolylineCodec.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
        Assert.Equal(3, decoded.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(GoogleExample[i].Longitude, decoded[i].Longitude, 5);
            Assert.Equal(GoogleExample[i].Latitude, decoded[i].Latitude, 5);
        }
    }

    [Fact]
    public void Round_trips_with_precision_6()
    {
        var points = new[] { new LngLat(2.294481, 48.858370), new LngLat(-0.000001, -0.5), new LngLat(179.999999, -89.999999) };
        var decoded = PolylineCodec.Decode(PolylineCodec.Encode(points, 6), 6);
        Assert.Equal(points.Length, decoded.Count);
        Assert.True(points.Zip(decoded).All(p => Math.Abs(p.First.Longitude - p.Second.Longitude) < 1e-6 && Math.Abs(p.First.Latitude - p.Second.Latitude) < 1e-6));
    }

    [Fact]
    public void Invalid_polyline_throws() => Assert.Throws<FormatException>(() => PolylineCodec.Decode("_p~iF~ps|U_"));
}

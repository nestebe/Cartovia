using System;
using System.Net.Http;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Entry point of the Mapbox web services. Mock this interface (or the individual services) in tests.</summary>
public interface IMapboxClient
{
    /// <summary>Geocoding API v6.</summary>
    IGeocodingService Geocoding { get; }

    /// <summary>Search Box API v1.</summary>
    ISearchBoxService Search { get; }

    /// <summary>Directions API v5.</summary>
    IDirectionsService Directions { get; }

    /// <summary>Matrix API v1.</summary>
    IMatrixService Matrix { get; }

    /// <summary>Isochrone API v1.</summary>
    IIsochroneService Isochrone { get; }

    /// <summary>Optimization API v1.</summary>
    IOptimizationService Optimization { get; }

    /// <summary>Map Matching API v5.</summary>
    IMapMatchingService MapMatching { get; }

    /// <summary>Static Images API v1.</summary>
    IStaticImagesService StaticImages { get; }

    /// <summary>Tilequery API v4.</summary>
    ITilequeryService Tilequery { get; }
}

/// <summary>Options of <see cref="MapboxClient"/>.</summary>
public sealed class MapboxClientOptions
{
    /// <summary>Default API base URL.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://api.mapbox.com/");

    /// <summary>Creates options.</summary>
    public MapboxClientOptions(string accessToken) => AccessToken = accessToken;

    /// <summary>Mapbox access token (public <c>pk.</c> or secret <c>sk.</c> token with the required scopes).</summary>
    public string AccessToken { get; set; }

    /// <summary>API base URL (override for a proxy or tests). Default <c>https://api.mapbox.com/</c>.</summary>
    public Uri BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Request timeout applied when the client creates its own <see cref="HttpClient"/>. Default 100 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}

/// <summary>
/// Client for the Mapbox web services. Thread-safe; create one instance and reuse it.
/// </summary>
/// <example>
/// <code>
/// using var mapbox = new MapboxClient("pk.…");
/// var result = await mapbox.Geocoding.ForwardAsync(new ForwardGeocodingRequest("Eiffel Tower") { Limit = 1 });
/// var route = await mapbox.Directions.GetRouteAsync(new DirectionsRequest(RoutingProfile.Driving, a, b));
/// </code>
/// </example>
public sealed class MapboxClient : IMapboxClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>Creates a client.</summary>
    /// <param name="accessToken">Mapbox access token.</param>
    /// <param name="httpClient">Optional HttpClient (e.g. from IHttpClientFactory). It is not disposed by this client.</param>
    public MapboxClient(string accessToken, HttpClient? httpClient = null)
        : this(new MapboxClientOptions(accessToken), httpClient) { }

    /// <summary>Creates a client.</summary>
    /// <param name="options">Options.</param>
    /// <param name="httpClient">Optional HttpClient. It is not disposed by this client.</param>
    public MapboxClient(MapboxClientOptions options, HttpClient? httpClient = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.AccessToken)) throw new ArgumentException("An access token is required.", nameof(options));
        if (options.BaseUrl is null || !options.BaseUrl.IsAbsoluteUri) throw new ArgumentException("BaseUrl must be an absolute URL.", nameof(options));

        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = options.Timeout };
        Http = new MapboxHttp(_httpClient, options.AccessToken.Trim(), options.BaseUrl);

        Geocoding = new GeocodingService(Http);
        Search = new SearchBoxService(Http);
        Directions = new DirectionsService(Http);
        Matrix = new MatrixService(Http);
        Isochrone = new IsochroneService(Http);
        Optimization = new OptimizationService(Http);
        MapMatching = new MapMatchingService(Http);
        StaticImages = new StaticImagesService(Http);
        Tilequery = new TilequeryService(Http);
    }

    internal MapboxHttp Http { get; }

    /// <summary>API base URL.</summary>
    public Uri BaseUrl => Http.BaseUri;

    /// <inheritdoc />
    public IGeocodingService Geocoding { get; }

    /// <inheritdoc />
    public ISearchBoxService Search { get; }

    /// <inheritdoc />
    public IDirectionsService Directions { get; }

    /// <inheritdoc />
    public IMatrixService Matrix { get; }

    /// <inheritdoc />
    public IIsochroneService Isochrone { get; }

    /// <inheritdoc />
    public IOptimizationService Optimization { get; }

    /// <inheritdoc />
    public IMapMatchingService MapMatching { get; }

    /// <inheritdoc />
    public IStaticImagesService StaticImages { get; }

    /// <inheritdoc />
    public ITilequeryService Tilequery { get; }

    /// <summary>Disposes the HttpClient if it was created by this instance.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

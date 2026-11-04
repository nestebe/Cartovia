using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.GeoJson;
using Cartovia.Mapbox.Services.Internal;

namespace Cartovia.Mapbox.Services;

/// <summary>
/// Search Box API v1 (<c>/search/searchbox/v1</c>): interactive search of addresses, places and POIs.
/// Typical flow: <see cref="SuggestAsync"/> while the user types, then <see cref="RetrieveAsync"/> with the
/// selected suggestion and the same session token.
/// </summary>
public interface ISearchBoxService
{
    /// <summary>Returns suggestions (no coordinates) for partial input.</summary>
    Task<SearchBoxSuggestResponse> SuggestAsync(SearchBoxSuggestRequest request, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the full feature (with coordinates) of a suggestion.</summary>
    Task<SearchBoxResponse> RetrieveAsync(SearchBoxRetrieveRequest request, CancellationToken cancellationToken = default);

    /// <summary>One-shot text search returning features with coordinates.</summary>
    Task<SearchBoxResponse> ForwardAsync(SearchBoxForwardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns POIs of a category (e.g. <c>restaurant</c>, <c>cafe</c>, <c>gas_station</c>).</summary>
    Task<SearchBoxResponse> CategoryAsync(SearchBoxCategoryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Feature types of the Search Box API (<c>types</c>).</summary>
[Flags]
public enum SearchBoxTypes
{
    /// <summary>No filter.</summary>
    None = 0,
    /// <summary>Countries.</summary>
    Country = 1,
    /// <summary>Regions.</summary>
    Region = 2,
    /// <summary>Postal codes.</summary>
    Postcode = 4,
    /// <summary>Districts.</summary>
    District = 8,
    /// <summary>Places.</summary>
    Place = 16,
    /// <summary>Cities.</summary>
    City = 32,
    /// <summary>Localities.</summary>
    Locality = 64,
    /// <summary>Neighborhoods.</summary>
    Neighborhood = 128,
    /// <summary>Streets.</summary>
    Street = 256,
    /// <summary>Addresses.</summary>
    Address = 512,
    /// <summary>Points of interest.</summary>
    Poi = 1024,
    /// <summary>Categories (suggest only).</summary>
    Category = 2048,
}

/// <summary>Options shared by Search Box requests.</summary>
public abstract class SearchBoxRequest
{
    private protected SearchBoxRequest() { }

    /// <summary>Response language, ISO 639-1 (<c>language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Maximum number of results (<c>limit</c>).</summary>
    public int? Limit { get; set; }

    /// <summary>Bias results toward this location (<c>proximity</c>).</summary>
    public LngLat? Proximity { get; set; }

    /// <summary>Limit results to a bounding box (<c>bbox</c>).</summary>
    public LngLatBounds? BBox { get; set; }

    /// <summary>ISO 3166 alpha-2 country codes (<c>country</c>).</summary>
    public IList<string> Countries { get; set; } = new List<string>();

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    private protected void AddCommon(QueryBuilder q)
    {
        q.Add("language", Language)
            .Add("limit", Limit)
            .Add("proximity", Proximity is { } p ? Fmt.LngLat(p) : null)
            .Add("bbox", BBox is { } b ? Fmt.BBox(b) : null)
            .Add("country", Countries.Select(c => c.Trim().ToLowerInvariant()));
    }
}

/// <summary>A suggest request.</summary>
public sealed class SearchBoxSuggestRequest : SearchBoxRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="query">The user input.</param>
    /// <param name="sessionToken">Session token shared with the following retrieve call; a new one is generated when null.</param>
    public SearchBoxSuggestRequest(string query, string? sessionToken = null)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        SessionToken = sessionToken ?? SearchBoxSession.NewToken();
    }

    /// <summary>User input (<c>q</c>).</summary>
    public string Query { get; set; }

    /// <summary>Session token (<c>session_token</c>), used for billing.</summary>
    public string SessionToken { get; set; }

    /// <summary>Feature types (<c>types</c>).</summary>
    public SearchBoxTypes Types { get; set; }

    /// <summary>POI category ids to include (<c>poi_category</c>).</summary>
    public IList<string> PoiCategories { get; set; } = new List<string>();

    /// <summary>POI category ids to exclude (<c>poi_category_exclusions</c>).</summary>
    public IList<string> PoiCategoryExclusions { get; set; } = new List<string>();

    /// <summary>Origin used to compute distances / ETAs (<c>origin</c>).</summary>
    public LngLat? Origin { get; set; }

    internal QueryBuilder BuildQuery()
    {
        if (string.IsNullOrWhiteSpace(Query)) throw new ArgumentException("The query cannot be empty.", nameof(Query));
        var q = new QueryBuilder().Add("q", Query).Add("session_token", SessionToken);
        AddCommon(q);
        if (Types != SearchBoxTypes.None) q.Add("types", EnumText.Flags(Types));
        q.Add("poi_category", PoiCategories)
            .Add("poi_category_exclusions", PoiCategoryExclusions)
            .Add("origin", Origin is { } o ? Fmt.LngLat(o) : null)
            .AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>A retrieve request.</summary>
public sealed class SearchBoxRetrieveRequest
{
    /// <summary>Creates a request from a suggestion id.</summary>
    public SearchBoxRetrieveRequest(string mapboxId, string sessionToken)
    {
        MapboxId = mapboxId ?? throw new ArgumentNullException(nameof(mapboxId));
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
    }

    /// <summary>Creates a request from a suggestion, reusing the token of its suggest response.</summary>
    public SearchBoxRetrieveRequest(SearchBoxSuggestion suggestion, string sessionToken)
        : this((suggestion ?? throw new ArgumentNullException(nameof(suggestion))).MapboxId
               ?? throw new ArgumentException("The suggestion has no mapbox_id.", nameof(suggestion)), sessionToken) { }

    /// <summary>The <c>mapbox_id</c> of the suggestion.</summary>
    public string MapboxId { get; set; }

    /// <summary>Same session token as the suggest request.</summary>
    public string SessionToken { get; set; }

    /// <summary>Response language (<c>language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();
}

/// <summary>A forward search request.</summary>
public sealed class SearchBoxForwardRequest : SearchBoxRequest
{
    /// <summary>Creates a request.</summary>
    public SearchBoxForwardRequest(string query) => Query = query ?? throw new ArgumentNullException(nameof(query));

    /// <summary>Search text (<c>q</c>).</summary>
    public string Query { get; set; }

    /// <summary>Feature types (<c>types</c>).</summary>
    public SearchBoxTypes Types { get; set; }

    /// <summary>Return partial matches (<c>auto_complete</c>).</summary>
    public bool? AutoComplete { get; set; }

    /// <summary>POI category ids to include (<c>poi_category</c>).</summary>
    public IList<string> PoiCategories { get; set; } = new List<string>();

    internal QueryBuilder BuildQuery()
    {
        if (string.IsNullOrWhiteSpace(Query)) throw new ArgumentException("The query cannot be empty.", nameof(Query));
        var q = new QueryBuilder().Add("q", Query);
        AddCommon(q);
        if (Types != SearchBoxTypes.None) q.Add("types", EnumText.Flags(Types));
        q.Add("auto_complete", AutoComplete).Add("poi_category", PoiCategories).AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>A category search request.</summary>
public sealed class SearchBoxCategoryRequest : SearchBoxRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="categoryId">Canonical category id, e.g. <c>restaurant</c>, <c>cafe</c>, <c>hotel</c>.</param>
    public SearchBoxCategoryRequest(string categoryId) => CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));

    /// <summary>Canonical category id.</summary>
    public string CategoryId { get; set; }

    /// <summary>POI category ids to exclude (<c>poi_category_exclusions</c>).</summary>
    public IList<string> PoiCategoryExclusions { get; set; } = new List<string>();

    internal QueryBuilder BuildQuery()
    {
        if (string.IsNullOrWhiteSpace(CategoryId)) throw new ArgumentException("The category id cannot be empty.", nameof(CategoryId));
        var q = new QueryBuilder();
        AddCommon(q);
        q.Add("poi_category_exclusions", PoiCategoryExclusions).AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>Session token helper for the Search Box API.</summary>
public static class SearchBoxSession
{
    /// <summary>Generates a new session token (UUIDv4).</summary>
    public static string NewToken() => Guid.NewGuid().ToString();
}

/// <summary>Suggest response.</summary>
public sealed class SearchBoxSuggestResponse
{
    /// <summary>Suggestions.</summary>
    public IReadOnlyList<SearchBoxSuggestion> Suggestions { get; init; } = Array.Empty<SearchBoxSuggestion>();

    /// <summary>Attribution notice.</summary>
    public string? Attribution { get; init; }

    /// <summary>The session token used for the request; pass it to <see cref="ISearchBoxService.RetrieveAsync"/>.</summary>
    [JsonIgnore] public string SessionToken { get; internal set; } = string.Empty;
}

/// <summary>A suggestion (without coordinates; call retrieve to get them).</summary>
public sealed class SearchBoxSuggestion
{
    /// <summary>Name.</summary>
    public string? Name { get; init; }
    /// <summary>Preferred name.</summary>
    public string? NamePreferred { get; init; }
    /// <summary>Identifier to pass to retrieve.</summary>
    public string? MapboxId { get; init; }
    /// <summary>Feature type (<c>poi</c>, <c>address</c>, <c>category</c>…).</summary>
    public string? FeatureType { get; init; }
    /// <summary>Short address.</summary>
    public string? Address { get; init; }
    /// <summary>Full address.</summary>
    public string? FullAddress { get; init; }
    /// <summary>Formatted context.</summary>
    public string? PlaceFormatted { get; init; }
    /// <summary>Context.</summary>
    public PlaceContext? Context { get; init; }
    /// <summary>Language.</summary>
    public string? Language { get; init; }
    /// <summary>Maki icon name.</summary>
    public string? Maki { get; init; }
    /// <summary>POI categories (display names).</summary>
    public IReadOnlyList<string>? PoiCategory { get; init; }
    /// <summary>POI category ids.</summary>
    public IReadOnlyList<string>? PoiCategoryIds { get; init; }
    /// <summary>Brand names.</summary>
    public IReadOnlyList<string>? Brand { get; init; }
    /// <summary>Distance in meters from the origin, when provided.</summary>
    public double? Distance { get; init; }
    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Best display label.</summary>
    [JsonIgnore] public string DisplayName => string.Join(", ", new[] { Name, PlaceFormatted ?? FullAddress }.Where(s => !string.IsNullOrEmpty(s)));

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>Retrieve / forward / category response.</summary>
public sealed class SearchBoxResponse
{
    internal SearchBoxResponse(FeatureCollection features, string? attribution)
    {
        FeatureCollection = features;
        Attribution = attribution;
        Features = features.Features.Select(PlaceFeature.Create<SearchBoxFeature>).ToList();
    }

    /// <summary>Results.</summary>
    public IReadOnlyList<SearchBoxFeature> Features { get; }

    /// <summary>The raw GeoJSON returned by the API.</summary>
    public FeatureCollection FeatureCollection { get; }

    /// <summary>Attribution notice.</summary>
    public string? Attribution { get; }

    /// <summary>The first result, or null.</summary>
    public SearchBoxFeature? First => Features.Count > 0 ? Features[0] : null;

    internal static SearchBoxResponse Parse(JsonElement root)
        => new(ServicesJson.Deserialize<FeatureCollection>(root), ServicesJson.GetString(root, "attribution"));
}

/// <summary>A Search Box feature.</summary>
public sealed class SearchBoxFeature : PlaceFeature
{
    /// <summary>Short address.</summary>
    public string? Address { get; init; }
    /// <summary>Language.</summary>
    public string? Language { get; init; }
    /// <summary>Maki icon name.</summary>
    public string? Maki { get; init; }
    /// <summary>POI categories (display names).</summary>
    public IReadOnlyList<string>? PoiCategory { get; init; }
    /// <summary>POI category ids.</summary>
    public IReadOnlyList<string>? PoiCategoryIds { get; init; }
    /// <summary>Brand names.</summary>
    public IReadOnlyList<string>? Brand { get; init; }
    /// <summary>Brand ids.</summary>
    public IReadOnlyList<string>? BrandId { get; init; }
    /// <summary>External ids (e.g. foursquare).</summary>
    public IReadOnlyDictionary<string, string>? ExternalIds { get; init; }
    /// <summary>POI metadata (phone, website, opening hours…), raw JSON.</summary>
    public JsonElement? Metadata { get; init; }
}

internal sealed class SearchBoxService : ISearchBoxService
{
    private const string BasePath = "search/searchbox/v1/";
    private readonly MapboxHttp _http;

    public SearchBoxService(MapboxHttp http) => _http = http;

    public Task<SearchBoxSuggestResponse> SuggestAsync(SearchBoxSuggestRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var token = request.SessionToken;
        return _http.GetJsonAsync(BasePath + "suggest", request.BuildQuery(), root =>
        {
            var response = ServicesJson.Deserialize<SearchBoxSuggestResponse>(root);
            response.SessionToken = token;
            return response;
        }, cancellationToken);
    }

    public Task<SearchBoxResponse> RetrieveAsync(SearchBoxRetrieveRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.MapboxId)) throw new ArgumentException("MapboxId is required.", nameof(request));
        var q = new QueryBuilder().Add("session_token", request.SessionToken).Add("language", request.Language).AddRange(request.AdditionalParameters);
        return _http.GetJsonAsync(BasePath + "retrieve/" + Uri.EscapeDataString(request.MapboxId), q, SearchBoxResponse.Parse, cancellationToken);
    }

    public Task<SearchBoxResponse> ForwardAsync(SearchBoxForwardRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return _http.GetJsonAsync(BasePath + "forward", request.BuildQuery(), SearchBoxResponse.Parse, cancellationToken);
    }

    public Task<SearchBoxResponse> CategoryAsync(SearchBoxCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var q = request.BuildQuery();
        return _http.GetJsonAsync(BasePath + "category/" + Uri.EscapeDataString(request.CategoryId.Trim()), q, SearchBoxResponse.Parse, cancellationToken);
    }
}

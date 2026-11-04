using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Geocoding API v6 (<c>/search/geocode/v6</c>).</summary>
public interface IGeocodingService
{
    /// <summary>Forward geocoding: text or structured address to coordinates.</summary>
    Task<GeocodingResponse> ForwardAsync(ForwardGeocodingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reverse geocoding: coordinates to places.</summary>
    Task<GeocodingResponse> ReverseAsync(ReverseGeocodingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Batch geocoding: many forward and/or reverse queries in one request. Results are in request order.</summary>
    /// <param name="requests">The queries (forward and reverse can be mixed).</param>
    /// <param name="permanent">Whether results will be stored permanently (<c>permanent</c>, requires a suitable account).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<GeocodingResponse>> BatchAsync(IEnumerable<GeocodingRequest> requests, bool? permanent = null, CancellationToken cancellationToken = default);
}

/// <summary>Feature types of the Geocoding API (<c>types</c>).</summary>
[Flags]
public enum GeocodingTypes
{
    /// <summary>No filter.</summary>
    None = 0,
    /// <summary>Countries.</summary>
    Country = 1,
    /// <summary>First-level administrative divisions.</summary>
    Region = 2,
    /// <summary>Postal codes.</summary>
    Postcode = 4,
    /// <summary>Second-level administrative divisions.</summary>
    District = 8,
    /// <summary>Cities, towns, villages.</summary>
    Place = 16,
    /// <summary>Official sub-city features.</summary>
    Locality = 32,
    /// <summary>Colloquial sub-city features.</summary>
    Neighborhood = 64,
    /// <summary>Streets.</summary>
    Street = 128,
    /// <summary>Blocks (Japan).</summary>
    Block = 256,
    /// <summary>Addresses.</summary>
    Address = 512,
    /// <summary>Sub-unit addresses (US).</summary>
    [JsonValue("secondary_address")] SecondaryAddress = 1024,
}

/// <summary>Base of geocoding requests (options shared by forward and reverse).</summary>
public abstract class GeocodingRequest
{
    private protected GeocodingRequest() { }

    /// <summary>Feature types to return (<c>types</c>).</summary>
    public GeocodingTypes Types { get; set; }

    /// <summary>Response language(s), IETF tags, comma-separated (<c>language</c>, e.g. <c>fr</c> or <c>fr,en</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Maximum number of results (<c>limit</c>; forward 1-10, reverse 1-5).</summary>
    public int? Limit { get; set; }

    /// <summary>ISO 3166 alpha-2 country codes to restrict results to (<c>country</c>).</summary>
    public IList<string> Countries { get; set; } = new List<string>();

    /// <summary>Worldview for disputed borders, e.g. <c>us</c>, <c>cn</c>, <c>jp</c>, <c>in</c> (<c>worldview</c>).</summary>
    public string? Worldview { get; set; }

    /// <summary>Whether results will be stored permanently (<c>permanent</c>). Ignored in batches (use the batch argument).</summary>
    public bool? Permanent { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal abstract QueryBuilder BuildQuery();

    internal abstract JsonObject ToBatchJson();

    private protected void AddCommon(QueryBuilder q)
    {
        if (Types != GeocodingTypes.None) q.Add("types", EnumText.Flags(Types));
        q.Add("country", Countries.Select(c => c.Trim().ToLowerInvariant()))
            .Add("language", Language)
            .Add("limit", Limit)
            .Add("worldview", Worldview);
    }

    private protected void AddCommon(JsonObject o)
    {
        if (Types != GeocodingTypes.None) o["types"] = new JsonArray(EnumText.Flags(Types).Select(t => (JsonNode?)t).ToArray());
        if (Countries.Count > 0) o["country"] = new JsonArray(Countries.Select(c => (JsonNode?)c.Trim().ToLowerInvariant()).ToArray());
        if (Language is not null) o["language"] = Language;
        if (Limit is { } l) o["limit"] = l;
        if (Worldview is not null) o["worldview"] = Worldview;
    }
}

/// <summary>Structured address input for forward geocoding (replaces the free-text query).</summary>
public sealed class StructuredAddress
{
    /// <summary>Number and street, e.g. <c>1600 Pennsylvania Ave NW</c> (<c>address_line1</c>).</summary>
    public string? AddressLine1 { get; set; }
    /// <summary>House number (<c>address_number</c>).</summary>
    public string? AddressNumber { get; set; }
    /// <summary>Street name (<c>street</c>).</summary>
    public string? Street { get; set; }
    /// <summary>Block (Japan) (<c>block</c>).</summary>
    public string? Block { get; set; }
    /// <summary>City, town or village (<c>place</c>).</summary>
    public string? Place { get; set; }
    /// <summary>State / province (<c>region</c>).</summary>
    public string? Region { get; set; }
    /// <summary>Postal code (<c>postcode</c>).</summary>
    public string? Postcode { get; set; }
    /// <summary>Locality (<c>locality</c>).</summary>
    public string? Locality { get; set; }
    /// <summary>Neighborhood (<c>neighborhood</c>).</summary>
    public string? Neighborhood { get; set; }
    /// <summary>Country name or ISO code (<c>country</c>). An ISO code acts as a hard filter.</summary>
    public string? Country { get; set; }

    internal IEnumerable<KeyValuePair<string, string>> Pairs()
    {
        var all = new (string, string?)[]
        {
            ("address_line1", AddressLine1), ("address_number", AddressNumber), ("street", Street), ("block", Block),
            ("place", Place), ("region", Region), ("postcode", Postcode), ("locality", Locality),
            ("neighborhood", Neighborhood), ("country", Country),
        };
        return all.Where(p => !string.IsNullOrWhiteSpace(p.Item2)).Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2!));
    }
}

/// <summary>A forward geocoding request.</summary>
public sealed class ForwardGeocodingRequest : GeocodingRequest
{
    /// <summary>Creates a free-text request.</summary>
    public ForwardGeocodingRequest(string query) => Query = query ?? throw new ArgumentNullException(nameof(query));

    /// <summary>Creates a structured-address request.</summary>
    public ForwardGeocodingRequest(StructuredAddress address)
        => Structured = address ?? throw new ArgumentNullException(nameof(address));

    /// <summary>Free-text search (<c>q</c>, max 256 characters / 20 words, no semicolons).</summary>
    public string? Query { get; set; }

    /// <summary>Structured address (used when <see cref="Query"/> is null).</summary>
    public StructuredAddress? Structured { get; set; }

    /// <summary>Bias results toward this location (<c>proximity</c>).</summary>
    public LngLat? Proximity { get; set; }

    /// <summary>Bias results toward the caller's IP location (<c>proximity=ip</c>). Ignored when <see cref="Proximity"/> is set.</summary>
    public bool ProximityFromIp { get; set; }

    /// <summary>Limit results to this bounding box (<c>bbox</c>).</summary>
    public LngLatBounds? BBox { get; set; }

    /// <summary>Return partial matches (<c>autocomplete</c>, API default true).</summary>
    public bool? Autocomplete { get; set; }

    internal override QueryBuilder BuildQuery()
    {
        var q = new QueryBuilder();
        if (Query is not null)
        {
            if (string.IsNullOrWhiteSpace(Query)) throw new ArgumentException("The query cannot be empty.", nameof(Query));
            q.Add("q", Query.Replace(';', ','));
        }
        else if (Structured is not null)
        {
            var pairs = Structured.Pairs().ToList();
            if (pairs.Count == 0) throw new ArgumentException("The structured address is empty.", nameof(Structured));
            q.AddRange(pairs);
        }
        else throw new ArgumentException("Either Query or Structured must be set.");

        AddCommon(q);
        q.Add("proximity", Proximity is { } p ? Fmt.LngLat(p) : ProximityFromIp ? "ip" : null)
            .Add("bbox", BBox is { } b ? Fmt.BBox(b) : null)
            .Add("autocomplete", Autocomplete)
            .Add("permanent", Permanent)
            .AddRange(AdditionalParameters);
        return q;
    }

    internal override JsonObject ToBatchJson()
    {
        var o = new JsonObject();
        if (Query is not null) o["q"] = Query.Replace(';', ',');
        else if (Structured is not null) foreach (var p in Structured.Pairs()) o[p.Key] = p.Value;
        else throw new ArgumentException("Either Query or Structured must be set.");
        AddCommon(o);
        if (Proximity is { } prox) o["proximity"] = new JsonArray(prox.Longitude, prox.Latitude);
        else if (ProximityFromIp) o["proximity"] = "ip";
        if (BBox is { } b) o["bbox"] = new JsonArray(b.West, b.South, b.East, b.North);
        if (Autocomplete is { } a) o["autocomplete"] = a;
        foreach (var kv in AdditionalParameters) o[kv.Key] = kv.Value;
        return o;
    }
}

/// <summary>A reverse geocoding request.</summary>
public sealed class ReverseGeocodingRequest : GeocodingRequest
{
    /// <summary>Creates a request.</summary>
    public ReverseGeocodingRequest(LngLat location) => Location = location;

    /// <summary>The location to look up.</summary>
    public LngLat Location { get; set; }

    internal override QueryBuilder BuildQuery()
    {
        var q = new QueryBuilder()
            .Add("longitude", Location.Longitude)
            .Add("latitude", Location.Latitude);
        AddCommon(q);
        q.Add("permanent", Permanent).AddRange(AdditionalParameters);
        return q;
    }

    internal override JsonObject ToBatchJson()
    {
        var o = new JsonObject { ["longitude"] = Location.Longitude, ["latitude"] = Location.Latitude };
        AddCommon(o);
        foreach (var kv in AdditionalParameters) o[kv.Key] = kv.Value;
        return o;
    }
}

/// <summary>A geocoding response.</summary>
public sealed class GeocodingResponse
{
    internal GeocodingResponse(FeatureCollection features, string? attribution)
    {
        FeatureCollection = features;
        Attribution = attribution;
        Features = features.Features.Select(PlaceFeature.Create<GeocodingFeature>).ToList();
    }

    /// <summary>Results, best match first.</summary>
    public IReadOnlyList<GeocodingFeature> Features { get; }

    /// <summary>The raw GeoJSON returned by the API, ready to be added as a map source.</summary>
    public FeatureCollection FeatureCollection { get; }

    /// <summary>Attribution notice.</summary>
    public string? Attribution { get; }

    /// <summary>The best result, or null.</summary>
    public GeocodingFeature? First => Features.Count > 0 ? Features[0] : null;

    internal static GeocodingResponse Parse(JsonElement root)
        => new(ServicesJson.Deserialize<FeatureCollection>(root), ServicesJson.GetString(root, "attribution"));
}

/// <summary>A Geocoding v6 result.</summary>
public sealed class GeocodingFeature : PlaceFeature
{
    /// <summary>How each part of the query matched (forward geocoding, address results).</summary>
    public MatchCode? MatchCode { get; init; }
}

/// <summary>Match quality of a forward geocoding result.</summary>
public sealed class MatchCode
{
    /// <summary>House number match (<c>matched</c>, <c>unmatched</c>, <c>not_applicable</c>, <c>inferred</c>, <c>plausible</c>).</summary>
    public string? AddressNumber { get; init; }
    /// <summary>Street match.</summary>
    public string? Street { get; init; }
    /// <summary>Postcode match.</summary>
    public string? Postcode { get; init; }
    /// <summary>Place match.</summary>
    public string? Place { get; init; }
    /// <summary>Region match.</summary>
    public string? Region { get; init; }
    /// <summary>Locality match.</summary>
    public string? Locality { get; init; }
    /// <summary>Country match.</summary>
    public string? Country { get; init; }
    /// <summary>Overall confidence: <c>exact</c>, <c>high</c>, <c>medium</c>, <c>low</c>.</summary>
    public string? Confidence { get; init; }
}

internal sealed class GeocodingService : IGeocodingService
{
    private const string BasePath = "search/geocode/v6/";
    private readonly MapboxHttp _http;

    public GeocodingService(MapboxHttp http) => _http = http;

    public Task<GeocodingResponse> ForwardAsync(ForwardGeocodingRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return _http.GetJsonAsync(BasePath + "forward", request.BuildQuery(), GeocodingResponse.Parse, cancellationToken);
    }

    public Task<GeocodingResponse> ReverseAsync(ReverseGeocodingRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return _http.GetJsonAsync(BasePath + "reverse", request.BuildQuery(), GeocodingResponse.Parse, cancellationToken);
    }

    public Task<IReadOnlyList<GeocodingResponse>> BatchAsync(IEnumerable<GeocodingRequest> requests, bool? permanent = null, CancellationToken cancellationToken = default)
    {
        if (requests is null) throw new ArgumentNullException(nameof(requests));
        var array = new JsonArray(requests.Select(r => (JsonNode?)(r ?? throw new ArgumentException("Null request.", nameof(requests))).ToBatchJson()).ToArray());
        if (array.Count == 0) throw new ArgumentException("At least one request is required.", nameof(requests));
        var query = new QueryBuilder().Add("permanent", permanent);
        return _http.PostJsonAsync(BasePath + "batch", query, array.ToJsonString(), root =>
        {
            IReadOnlyList<GeocodingResponse> list = root.TryGetProperty("batch", out var batch) && batch.ValueKind == JsonValueKind.Array
                ? batch.EnumerateArray().Select(GeocodingResponse.Parse).ToList()
                : Array.Empty<GeocodingResponse>();
            return list;
        }, cancellationToken);
    }
}

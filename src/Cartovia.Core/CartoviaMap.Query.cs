using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Cartovia.Events;
using Cartovia.Style;

namespace Cartovia;

/// <summary>Options of <see cref="CartoviaMap.QueryRenderedFeaturesAsync(ScreenPoint, QueryOptions?, CancellationToken)"/>.</summary>
public sealed class QueryOptions
{
    /// <summary>Restricts the query to these layers.</summary>
    public IList<string>? Layers { get; set; }
    /// <summary>Filter expression applied to the results.</summary>
    public Expression? Filter { get; set; }
    /// <summary>Validates the filter. Default: true.</summary>
    public bool? Validate { get; set; }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Layers is not null) o["layers"] = new JsonArray(Layers.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
        if (Filter is not null) o["filter"] = Filter.ToJsonNode();
        if (Validate is { } v) o["validate"] = v;
        return o;
    }
}

/// <summary>Identifies a feature for feature-state operations.</summary>
/// <param name="Source">Source id.</param>
/// <param name="Id">Feature id (number or string, see <c>promoteId</c> / <c>generateId</c>).</param>
/// <param name="SourceLayer">Source layer (vector sources).</param>
public sealed record FeatureIdentifier(string Source, object Id, string? SourceLayer = null)
{
    internal JsonObject ToJson()
    {
        var o = new JsonObject { ["source"] = Source, ["id"] = BridgeJson.ToNode(Id) };
        if (SourceLayer is not null) o["sourceLayer"] = SourceLayer;
        return o;
    }

    /// <summary>Builds the identifier of a queried feature.</summary>
    /// <exception cref="ArgumentException">The feature has no id or source.</exception>
    public static FeatureIdentifier From(QueriedFeature feature)
    {
        if (feature.Id is null || feature.Source is null) throw new ArgumentException("The feature has no id or source (use generateId / promoteId).", nameof(feature));
        object id = feature.Id is JsonValue v && v.TryGetValue<double>(out var d) ? d : feature.Id.ToString();
        return new FeatureIdentifier(feature.Source, id, feature.SourceLayer);
    }
}

public sealed partial class CartoviaMap
{
    /// <summary>Features rendered at a point.</summary>
    public Task<IReadOnlyList<QueriedFeature>> QueryRenderedFeaturesAsync(ScreenPoint point, QueryOptions? options = null, CancellationToken cancellationToken = default)
        => QueryRenderedAsync(BridgeJson.ToNode(point), options, cancellationToken);

    /// <summary>Features rendered in a box.</summary>
    public Task<IReadOnlyList<QueriedFeature>> QueryRenderedFeaturesAsync(ScreenBox box, QueryOptions? options = null, CancellationToken cancellationToken = default)
        => QueryRenderedAsync(BridgeJson.ToNode(box.ToArray()), options, cancellationToken);

    /// <summary>Every feature rendered in the viewport.</summary>
    public Task<IReadOnlyList<QueriedFeature>> QueryRenderedFeaturesAsync(QueryOptions? options = null, CancellationToken cancellationToken = default)
        => QueryRenderedAsync(null, options, cancellationToken);

    /// <summary>Features rendered at a geographic position.</summary>
    public async Task<IReadOnlyList<QueriedFeature>> QueryRenderedFeaturesAsync(LngLat position, QueryOptions? options = null, CancellationToken cancellationToken = default)
        => await QueryRenderedFeaturesAsync(await ProjectAsync(position, cancellationToken).ConfigureAwait(false), options, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<QueriedFeature>> QueryRenderedAsync(JsonNode? geometry, QueryOptions? options, CancellationToken cancellationToken)
    {
        var json = await CallAsync("query.rendered", new JsonObject
        {
            ["geometry"] = geometry,
            ["options"] = options?.ToJson() ?? new JsonObject(),
        }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return ParseFeatures(json);
    }

    /// <summary>
    /// Features of a source loaded in the current tiles (rendered or not; may contain duplicates across tiles).
    /// </summary>
    public async Task<IReadOnlyList<QueriedFeature>> QuerySourceFeaturesAsync(string sourceId, string? sourceLayer = null, Expression? filter = null, CancellationToken cancellationToken = default)
    {
        var opts = new JsonObject();
        if (sourceLayer is not null) opts["sourceLayer"] = sourceLayer;
        if (filter is not null) opts["filter"] = filter.ToJsonNode();
        var json = await CallAsync("query.source", new JsonObject { ["source"] = sourceId, ["options"] = opts }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return ParseFeatures(json);
    }

    /// <summary>Terrain elevation in meters at a position (null when terrain is disabled or not loaded).</summary>
    public async Task<double?> QueryTerrainElevationAsync(LngLat position, CancellationToken cancellationToken = default)
    {
        var json = await CallAsync("query.elevation", new JsonObject { ["lngLat"] = BridgeJson.ToNode(position) }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return json.ValueKind == JsonValueKind.Number ? json.GetDouble() : null;
    }

    /// <summary>Sets feature state (used by <c>["feature-state", …]</c> expressions).</summary>
    /// <param name="feature">Feature identifier.</param>
    /// <param name="state">State object, e.g. <c>new { hover = true }</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SetFeatureStateAsync(FeatureIdentifier feature, object state, CancellationToken cancellationToken = default)
        => InvokeAsync("setFeatureState", cancellationToken, feature.ToJson(), state);

    /// <summary>Removes feature state (a single key, the whole state of a feature, or of every feature of the source when <c>Id</c> is null).</summary>
    public Task RemoveFeatureStateAsync(FeatureIdentifier feature, string? key = null, CancellationToken cancellationToken = default)
        => InvokeAsync("removeFeatureState", cancellationToken, feature.ToJson(), key);

    /// <summary>Reads feature state.</summary>
    public async Task<JsonObject?> GetFeatureStateAsync(FeatureIdentifier feature, CancellationToken cancellationToken = default)
    {
        var json = await InvokeAsync("getFeatureState", cancellationToken, feature.ToJson()).ConfigureAwait(false);
        return json.ValueKind == JsonValueKind.Object ? JsonNode.Parse(json.GetRawText()) as JsonObject : null;
    }

    /// <summary>Zoom level at which a cluster expands.</summary>
    public async Task<double> GetClusterExpansionZoomAsync(string sourceId, long clusterId, CancellationToken cancellationToken = default)
    {
        var json = await CallAsync("source.cluster", new JsonObject { ["id"] = sourceId, ["op"] = "expansionZoom", ["clusterId"] = clusterId }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return json.GetDouble();
    }

    /// <summary>Direct children of a cluster (points or sub-clusters).</summary>
    public async Task<IReadOnlyList<QueriedFeature>> GetClusterChildrenAsync(string sourceId, long clusterId, CancellationToken cancellationToken = default)
        => ParseFeatures(await CallAsync("source.cluster", new JsonObject { ["id"] = sourceId, ["op"] = "children", ["clusterId"] = clusterId }, cancellationToken, Readiness.Loaded).ConfigureAwait(false));

    /// <summary>Points contained in a cluster (paged).</summary>
    public async Task<IReadOnlyList<QueriedFeature>> GetClusterLeavesAsync(string sourceId, long clusterId, int limit = 10, int offset = 0, CancellationToken cancellationToken = default)
        => ParseFeatures(await CallAsync("source.cluster", new JsonObject
        {
            ["id"] = sourceId, ["op"] = "leaves", ["clusterId"] = clusterId, ["limit"] = limit, ["offset"] = offset,
        }, cancellationToken, Readiness.Loaded).ConfigureAwait(false));

    private static IReadOnlyList<QueriedFeature> ParseFeatures(JsonElement json)
        => json.ValueKind == JsonValueKind.Array
            ? json.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(QueriedFeature.FromJson).ToList()
            : Array.Empty<QueriedFeature>();
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Tilequery API v4 (<c>/v4/{tileset}/tilequery</c>): features of vector tilesets around a point.</summary>
public interface ITilequeryService
{
    /// <summary>Returns the features of one or more tilesets at or around a location.</summary>
    Task<TilequeryResponse> QueryAsync(TilequeryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Geometry type filter of a Tilequery request.</summary>
public enum TilequeryGeometry
{
    /// <summary>Polygons only.</summary>
    Polygon,
    /// <summary>Lines only.</summary>
    Linestring,
    /// <summary>Points only.</summary>
    Point,
}

/// <summary>A Tilequery request.</summary>
public sealed class TilequeryRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="tilesetIds">One or more tileset ids (e.g. <c>mapbox.mapbox-streets-v8</c>); several ids are composited.</param>
    /// <param name="location">The query location.</param>
    public TilequeryRequest(IEnumerable<string> tilesetIds, LngLat location)
    {
        TilesetIds = (tilesetIds ?? throw new ArgumentNullException(nameof(tilesetIds))).ToList();
        Location = location;
    }

    /// <summary>Creates a request for a single tileset.</summary>
    public TilequeryRequest(string tilesetId, LngLat location) : this(new[] { tilesetId }, location) { }

    /// <summary>Tileset ids.</summary>
    public IList<string> TilesetIds { get; }

    /// <summary>Query location.</summary>
    public LngLat Location { get; set; }

    /// <summary>Search radius in meters, 0 = only features containing the point (<c>radius</c>).</summary>
    public int? Radius { get; set; }

    /// <summary>Maximum number of features, 1-50 (<c>limit</c>, API default 5).</summary>
    public int? Limit { get; set; }

    /// <summary>Removes duplicate features (<c>dedupe</c>, API default true).</summary>
    public bool? Dedupe { get; set; }

    /// <summary>Geometry type filter (<c>geometry</c>).</summary>
    public TilequeryGeometry? Geometry { get; set; }

    /// <summary>Layers to query (<c>layers</c>).</summary>
    public IList<string> Layers { get; set; } = new List<string>();

    /// <summary>Bands to query, for rasterarray tilesets (<c>bands</c>).</summary>
    public IList<string> Bands { get; set; } = new List<string>();

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        if (TilesetIds.Count == 0 || TilesetIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty tileset id is required.", nameof(TilesetIds));
        if (Radius < 0) throw new ArgumentOutOfRangeException(nameof(Radius));
        return new QueryBuilder()
            .Add("radius", Radius)
            .Add("limit", Limit)
            .Add("dedupe", Dedupe)
            .Add("geometry", Geometry is { } g ? EnumText.Of(g) : null)
            .Add("layers", Layers)
            .Add("bands", Bands)
            .AddRange(AdditionalParameters);
    }
}

/// <summary>Tilequery response.</summary>
public sealed class TilequeryResponse
{
    internal TilequeryResponse(FeatureCollection features)
    {
        FeatureCollection = features;
        Features = features.Features.Select(f => new TilequeryFeature(f)).ToList();
    }

    /// <summary>The raw GeoJSON returned by the API.</summary>
    public FeatureCollection FeatureCollection { get; }

    /// <summary>Typed view of each feature, nearest first.</summary>
    public IReadOnlyList<TilequeryFeature> Features { get; }
}

/// <summary>A feature returned by Tilequery.</summary>
public sealed class TilequeryFeature
{
    internal TilequeryFeature(Feature feature)
    {
        Feature = feature;
        var info = ServicesJson.GetProperty<TilequeryInfo>(feature, "tilequery");
        Layer = info?.Layer;
        GeometryType = info?.Geometry;
        Distance = info?.Distance ?? 0;
    }

    /// <summary>The GeoJSON feature (geometry is the closest point of the original feature).</summary>
    public Feature Feature { get; }

    /// <summary>Source layer of the feature.</summary>
    public string? Layer { get; }

    /// <summary>Original geometry type (<c>point</c>, <c>linestring</c>, <c>polygon</c>).</summary>
    public string? GeometryType { get; }

    /// <summary>Distance in meters from the query point (0 when inside a polygon).</summary>
    public double Distance { get; }

    /// <summary>Reads a feature property.</summary>
    public T? GetProperty<T>(string name) => ServicesJson.GetProperty<T>(Feature, name);
}

internal sealed class TilequeryInfo
{
    public string? Layer { get; init; }
    public string? Geometry { get; init; }
    public double Distance { get; init; }
}

/// <summary>Convenience helpers on top of <see cref="ITilequeryService"/>.</summary>
public static class TilequeryServiceExtensions
{
    /// <summary>Tileset used by <see cref="GetElevationAsync"/>.</summary>
    public const string TerrainTileset = "mapbox.mapbox-terrain-v2";

    /// <summary>
    /// Approximate ground elevation in meters, read from the contour lines of <c>mapbox.mapbox-terrain-v2</c>
    /// (highest contour containing the point; 10 m resolution at best). Returns null where there is no data.
    /// Mapbox has no dedicated elevation REST API; this is the approach recommended in the Mapbox documentation.
    /// </summary>
    public static async Task<double?> GetElevationAsync(this ITilequeryService service, LngLat location, CancellationToken cancellationToken = default)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        var request = new TilequeryRequest(TerrainTileset, location) { Layers = { "contour" }, Limit = 50 };
        var response = await service.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        double? max = null;
        foreach (var f in response.Features)
        {
            if (f.GetProperty<double?>("ele") is { } ele && (max is null || ele > max)) max = ele;
        }
        return max;
    }
}

internal sealed class TilequeryService : ITilequeryService
{
    private readonly MapboxHttp _http;

    public TilequeryService(MapboxHttp http) => _http = http;

    public Task<TilequeryResponse> QueryAsync(TilequeryRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var tilesets = string.Join(",", request.TilesetIds.Select(t => Uri.EscapeDataString(t.Trim())));
        var path = "v4/" + tilesets + "/tilequery/" + Fmt.LngLat(request.Location) + ".json";
        return _http.GetJsonAsync(path, query,
            root => new TilequeryResponse(ServicesJson.Deserialize<FeatureCollection>(root)), cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Mapbox.Net.Bridge;
using Mapbox.Net.GeoJson;

namespace Mapbox.Net.Style;

/// <summary>Tiling scheme of a tile source.</summary>
public enum TileScheme
{
    /// <summary>Slippy map tilenames (OSM / Google).</summary>
    Xyz,
    /// <summary>OSGeo TMS.</summary>
    Tms,
}

/// <summary>Encoding of a raster DEM source.</summary>
public enum DemEncoding
{
    /// <summary>Mapbox Terrain RGB.</summary>
    Mapbox,
    /// <summary>Mapzen Terrarium.</summary>
    Terrarium,
    /// <summary>Custom encoding (MapLibre: provide factors in <see cref="Source.Extra"/>).</summary>
    Custom,
}

/// <summary>
/// Base class of style sources. Every source can carry extra members through <see cref="Extra"/>.
/// </summary>
public abstract class Source
{
    /// <summary>Creates a source.</summary>
    protected Source(string id, string type)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A source id is required.", nameof(id));
        Id = id;
        Type = type;
    }

    /// <summary>Unique source id.</summary>
    public string Id { get; }

    /// <summary>Style-specification source type.</summary>
    public string Type { get; }

    /// <summary>Attribution displayed by the attribution control.</summary>
    public string? Attribution { get; set; }

    /// <summary>Additional members merged into the source JSON.</summary>
    public JsonObject Extra { get; } = new();

    /// <summary>Writes type-specific members.</summary>
    protected abstract void Write(JsonObject json);

    /// <summary>Serializes the source definition (without its id).</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["type"] = Type };
        if (Attribution is not null) o["attribution"] = Attribution;
        Write(o);
        foreach (var kv in Extra) o[kv.Key] = kv.Value?.DeepClone();
        return o;
    }

    /// <summary>Helper writing an optional value.</summary>
    protected static void Put(JsonObject o, string name, object? value)
    {
        if (value is not null) o[name] = BridgeJson.ToNode(value);
    }
}

/// <summary>Base class of tiled sources (vector, raster, raster-dem).</summary>
public abstract class TileSource : Source
{
    /// <summary>Creates a tiled source.</summary>
    protected TileSource(string id, string type) : base(id, type) { }

    /// <summary>TileJSON URL (e.g. <c>mapbox://mapbox.mapbox-streets-v8</c>).</summary>
    public string? Url { get; set; }

    /// <summary>Tile URL templates (<c>https://…/{z}/{x}/{y}.pbf</c>).</summary>
    public IList<string>? Tiles { get; set; }

    /// <summary>Bounds of the available tiles (<c>[w, s, e, n]</c>).</summary>
    public LngLatBounds? Bounds { get; set; }

    /// <summary>Minimum zoom of the tiles.</summary>
    public double? MinZoom { get; set; }

    /// <summary>Maximum zoom of the tiles (overzoomed beyond).</summary>
    public double? MaxZoom { get; set; }

    /// <summary>Tiling scheme.</summary>
    public TileScheme? Scheme { get; set; }

    /// <summary>Volatile tiles are not cached locally. Mapbox only.</summary>
    public bool? Volatile { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        Put(o, "url", Url);
        if (Tiles is not null) o["tiles"] = new JsonArray(Tiles.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        if (Bounds is { } b) o["bounds"] = new JsonArray(b.West, b.South, b.East, b.North);
        Put(o, "minzoom", MinZoom);
        Put(o, "maxzoom", MaxZoom);
        Put(o, "scheme", Scheme);
        Put(o, "volatile", Volatile);
    }
}

/// <summary>Vector tile source.</summary>
public sealed class VectorSource : TileSource
{
    /// <summary>Creates a vector source from a TileJSON URL.</summary>
    public VectorSource(string id, string? url = null) : base(id, "vector") => Url = url;

    /// <summary>Feature property used as feature id (enables feature-state). String or per-layer object.</summary>
    public object? PromoteId { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        base.Write(o);
        Put(o, "promoteId", PromoteId);
    }
}

/// <summary>Raster tile source.</summary>
public sealed class RasterSource : TileSource
{
    /// <summary>Creates a raster source from tile templates.</summary>
    public RasterSource(string id, params string[] tiles) : base(id, "raster")
    {
        if (tiles.Length > 0) Tiles = tiles.ToList();
    }

    /// <summary>Tile size in pixels. Default: 512.</summary>
    public int? TileSize { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        base.Write(o);
        Put(o, "tileSize", TileSize);
    }
}

/// <summary>Raster elevation (DEM) source, used for terrain and hillshading.</summary>
public sealed class RasterDemSource : TileSource
{
    /// <summary>Creates a raster DEM source.</summary>
    public RasterDemSource(string id, string? url = null) : base(id, "raster-dem") => Url = url;

    /// <summary>Mapbox Terrain DEM v1 (Mapbox engine).</summary>
    public static RasterDemSource MapboxTerrain(string id = "mapbox-dem")
        => new(id, "mapbox://mapbox.mapbox-terrain-dem-v1") { TileSize = 512, MaxZoom = 14 };

    /// <summary>Tile size in pixels. Default: 512.</summary>
    public int? TileSize { get; set; }

    /// <summary>Elevation encoding.</summary>
    public DemEncoding? Encoding { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        base.Write(o);
        Put(o, "tileSize", TileSize);
        Put(o, "encoding", Encoding);
    }
}

/// <summary>Raster array source (multi-band data, e.g. wind for raster-particle layers). Mapbox only.</summary>
public sealed class RasterArraySource : TileSource
{
    /// <summary>Creates the source.</summary>
    public RasterArraySource(string id, string? url = null) : base(id, "raster-array") => Url = url;

    /// <summary>Tile size in pixels.</summary>
    public int? TileSize { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        base.Write(o);
        Put(o, "tileSize", TileSize);
    }
}

/// <summary>GeoJSON source: inline data or a URL, with optional clustering.</summary>
public sealed class GeoJsonSource : Source
{
    /// <summary>Creates a GeoJSON source with inline data.</summary>
    public GeoJsonSource(string id, GeoJsonObject? data = null) : base(id, "geojson")
        => Data = (object?)data ?? new FeatureCollection();

    /// <summary>Creates a GeoJSON source loaded from a URL.</summary>
    public GeoJsonSource(string id, string url) : base(id, "geojson") => Data = url;

    /// <summary>Data: a <see cref="GeoJsonObject"/>, a URL string, or any object serializable to GeoJSON.</summary>
    public object Data { get; set; }

    /// <summary>Maximum zoom at which tiles are created. Default: 18.</summary>
    public double? MaxZoom { get; set; }
    /// <summary>Tile buffer size. Default: 128.</summary>
    public int? Buffer { get; set; }
    /// <summary>Douglas-Peucker simplification tolerance. Default: 0.375.</summary>
    public double? Tolerance { get; set; }
    /// <summary>Clusters points. Default: false.</summary>
    public bool? Cluster { get; set; }
    /// <summary>Cluster radius in pixels. Default: 50.</summary>
    public double? ClusterRadius { get; set; }
    /// <summary>Maximum zoom at which points are clustered.</summary>
    public double? ClusterMaxZoom { get; set; }
    /// <summary>Minimum number of points to form a cluster. Default: 2.</summary>
    public int? ClusterMinPoints { get; set; }
    /// <summary>Aggregated cluster properties, e.g. <c>{"sum": ["+", ["get", "value"]]}</c>.</summary>
    public JsonObject? ClusterProperties { get; set; }
    /// <summary>Computes line distance metrics (needed by <c>line-gradient</c>).</summary>
    public bool? LineMetrics { get; set; }
    /// <summary>Generates numeric feature ids (enables feature-state).</summary>
    public bool? GenerateId { get; set; }
    /// <summary>Feature property used as feature id.</summary>
    public string? PromoteId { get; set; }
    /// <summary>Filter applied to the data before tiling.</summary>
    public Expression? Filter { get; set; }
    /// <summary>Optimizes frequent partial updates (<c>updateData</c>). Mapbox 3.4+.</summary>
    public bool? Dynamic { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        o["data"] = Data is string url ? JsonValue.Create(url) : BridgeJson.ToNode(Data);
        Put(o, "maxzoom", MaxZoom);
        Put(o, "buffer", Buffer);
        Put(o, "tolerance", Tolerance);
        Put(o, "cluster", Cluster);
        Put(o, "clusterRadius", ClusterRadius);
        Put(o, "clusterMaxZoom", ClusterMaxZoom);
        Put(o, "clusterMinPoints", ClusterMinPoints);
        Put(o, "clusterProperties", ClusterProperties);
        Put(o, "lineMetrics", LineMetrics);
        Put(o, "generateId", GenerateId);
        Put(o, "promoteId", PromoteId);
        Put(o, "filter", Filter);
        Put(o, "dynamic", Dynamic);
    }
}

/// <summary>Georeferenced image source.</summary>
public sealed class ImageSource : Source
{
    /// <summary>Creates the source.</summary>
    /// <param name="id">Source id.</param>
    /// <param name="url">Image URL (or data URL).</param>
    /// <param name="topLeft">Top left corner.</param>
    /// <param name="topRight">Top right corner.</param>
    /// <param name="bottomRight">Bottom right corner.</param>
    /// <param name="bottomLeft">Bottom left corner.</param>
    public ImageSource(string id, string url, LngLat topLeft, LngLat topRight, LngLat bottomRight, LngLat bottomLeft)
        : base(id, "image")
    {
        Url = url;
        Coordinates = new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    /// <summary>Image URL.</summary>
    public string Url { get; set; }

    /// <summary>Corners: top left, top right, bottom right, bottom left.</summary>
    public LngLat[] Coordinates { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        o["url"] = Url;
        o["coordinates"] = BridgeJson.ToNode(Coordinates);
    }
}

/// <summary>Georeferenced video source.</summary>
public sealed class VideoSource : Source
{
    /// <summary>Creates the source.</summary>
    public VideoSource(string id, IEnumerable<string> urls, LngLat topLeft, LngLat topRight, LngLat bottomRight, LngLat bottomLeft)
        : base(id, "video")
    {
        Urls = urls.ToList();
        Coordinates = new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    /// <summary>Video URLs (several formats).</summary>
    public IList<string> Urls { get; set; }

    /// <summary>Corners: top left, top right, bottom right, bottom left.</summary>
    public LngLat[] Coordinates { get; set; }

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        o["urls"] = new JsonArray(Urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray());
        o["coordinates"] = BridgeJson.ToNode(Coordinates);
    }
}

/// <summary>A source of any type defined by raw JSON.</summary>
public sealed class RawSource : Source
{
    private readonly JsonObject _definition;

    /// <summary>Creates a source from its JSON definition (must contain <c>type</c>).</summary>
    public RawSource(string id, JsonObject definition)
        : base(id, definition["type"]?.GetValue<string>() ?? throw new ArgumentException("The definition must contain a 'type'.", nameof(definition)))
        => _definition = (JsonObject)definition.DeepClone();

    /// <inheritdoc />
    protected override void Write(JsonObject o)
    {
        foreach (var kv in _definition) if (kv.Key != "type") o[kv.Key] = kv.Value?.DeepClone();
    }
}

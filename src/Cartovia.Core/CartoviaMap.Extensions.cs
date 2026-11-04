using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Bridge;
using Cartovia.GeoJson;
using Cartovia.Style;

namespace Cartovia;

/// <summary>Appearance of <see cref="CartoviaMap.AddClusteredPointsAsync"/>.</summary>
public sealed class ClusterStyle
{
    /// <summary>Cluster radius in pixels. Default: 50.</summary>
    public double ClusterRadius { get; set; } = 50;
    /// <summary>Maximum zoom at which points are clustered. Default: 14.</summary>
    public double ClusterMaxZoom { get; set; } = 14;
    /// <summary>Cluster colors by point count: (minimum count, color). The first entry is the base color.</summary>
    public IList<(int MinCount, string Color)> Colors { get; set; } = new List<(int, string)> { (0, "#51bbd6"), (100, "#f1f075"), (750, "#f28cb1") };
    /// <summary>Cluster radii by point count: (minimum count, radius). The first entry is the base radius.</summary>
    public IList<(int MinCount, double Radius)> Radii { get; set; } = new List<(int, double)> { (0, 18), (100, 26), (750, 36) };
    /// <summary>Color of unclustered points.</summary>
    public string PointColor { get; set; } = "#11b4da";
    /// <summary>Radius of unclustered points.</summary>
    public double PointRadius { get; set; } = 6;
    /// <summary>Font stack of the count labels. Null: style default (must exist in the style glyphs).</summary>
    public string[]? Font { get; set; }
    /// <summary>Zoom into a cluster when it is clicked. Default: true.</summary>
    public bool ZoomOnClick { get; set; } = true;
    /// <summary>Aggregated cluster properties.</summary>
    public JsonObject? ClusterProperties { get; set; }
}

/// <summary>Ids of the source and layers created by <see cref="CartoviaMap.AddClusteredPointsAsync"/>.</summary>
/// <param name="SourceId">GeoJSON source id.</param>
/// <param name="ClusterLayerId">Cluster circles.</param>
/// <param name="CountLayerId">Cluster count labels.</param>
/// <param name="PointLayerId">Unclustered points.</param>
public sealed record ClusterLayers(string SourceId, string ClusterLayerId, string CountLayerId, string PointLayerId)
{
    /// <summary>Every layer id, bottom to top.</summary>
    public IEnumerable<string> LayerIds => new[] { ClusterLayerId, CountLayerId, PointLayerId };
}

public sealed partial class CartoviaMap
{
    private async Task<IDisposable> AddHelperAsync(string command, JsonObject args, CancellationToken cancellationToken)
    {
        long sid = NextId();
        args["sid"] = sid;
        await CallAsync(command, args, cancellationToken).ConfigureAwait(false);
        return new ActionDisposable(() =>
        {
            if (!_disposed) FireAndForget(CallAsync("helper.remove", new JsonObject { ["sid"] = sid }, CancellationToken.None), "Helper removal");
        });
    }

    /// <summary>Shows a cursor (default: pointer) when the mouse is over the features of a layer.</summary>
    /// <returns>Dispose to remove the behaviour.</returns>
    public Task<IDisposable> EnableHoverCursorAsync(string layerId, string cursor = "pointer", CancellationToken cancellationToken = default)
        => AddHelperAsync("helper.hoverCursor", new JsonObject { ["layer"] = layerId, ["cursor"] = cursor }, cancellationToken);

    /// <summary>
    /// Sets the feature state <paramref name="stateKey"/> (default <c>hover</c>) to true on the feature under the mouse.
    /// Style it with <c>Expr.Case(normal, (Expr.Equal(Expr.FeatureState("hover"), true), highlighted))</c>.
    /// Features need ids (<c>GenerateId</c> / <c>PromoteId</c>).
    /// </summary>
    public Task<IDisposable> EnableHoverStateAsync(string layerId, string sourceId, string? sourceLayer = null, string stateKey = "hover", CancellationToken cancellationToken = default)
        => AddHelperAsync("helper.hoverState", new JsonObject { ["layer"] = layerId, ["source"] = sourceId, ["sourceLayer"] = sourceLayer, ["stateKey"] = stateKey }, cancellationToken);

    /// <summary>Sets the feature state <paramref name="stateKey"/> (default <c>selected</c>) on the clicked feature (single selection).</summary>
    public Task<IDisposable> EnableSelectionStateAsync(string layerId, string sourceId, string? sourceLayer = null, string stateKey = "selected", bool toggle = true, CancellationToken cancellationToken = default)
        => AddHelperAsync("helper.selectState", new JsonObject { ["layer"] = layerId, ["source"] = sourceId, ["sourceLayer"] = sourceLayer, ["stateKey"] = stateKey, ["toggle"] = toggle }, cancellationToken);

    /// <summary>
    /// Opens a popup when a feature of the layer is clicked. The template uses <c>{property}</c> (HTML-escaped)
    /// and <c>{{{property}}}</c> (raw) placeholders.
    /// </summary>
    /// <example><code>await map.ShowPopupOnClickAsync("cities", "&lt;b&gt;{name}&lt;/b&gt;&lt;br/&gt;{population} inhabitants");</code></example>
    public Task<IDisposable> ShowPopupOnClickAsync(string layerId, string htmlTemplate, PopupOptions? options = null, CancellationToken cancellationToken = default)
        => AddHelperAsync("helper.clickPopup", new JsonObject
        {
            ["layer"] = layerId,
            ["template"] = htmlTemplate,
            ["options"] = options?.ToJson()["options"]?.DeepClone(),
        }, cancellationToken);

    /// <summary>
    /// Adds a GeoJSON source and default layers depending on the geometry types
    /// (circle for points, line for lines, fill + outline for polygons).
    /// </summary>
    /// <param name="id">Source id, also used as prefix of the layer ids (<c>{id}-points</c>, <c>{id}-lines</c>, <c>{id}-fill</c>, <c>{id}-outline</c>).</param>
    /// <param name="data">Data.</param>
    /// <param name="color">Main color.</param>
    /// <param name="beforeLayerId">Insert the layers below this layer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the created layers.</returns>
    public async Task<IReadOnlyList<string>> AddGeoJsonAsync(string id, GeoJsonObject data, string color = "#3b82f6", string? beforeLayerId = null, CancellationToken cancellationToken = default)
    {
        await AddSourceAsync(new GeoJsonSource(id, data) { GenerateId = true }, cancellationToken).ConfigureAwait(false);
        var types = CollectGeometryTypes(data);
        var layers = new List<Layer>();
        if (types.Contains(GeoJsonType.Polygon) || types.Contains(GeoJsonType.MultiPolygon))
        {
            layers.Add(new FillLayer(id + "-fill", id)
            {
                FillColor = color,
                FillOpacity = Expr.Case(0.35, (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), 0.6)),
                Filter = Expr.In(Expr.GeometryType(), "Polygon", "MultiPolygon"),
            });
            layers.Add(new LineLayer(id + "-outline", id)
            {
                LineColor = color, LineWidth = 2,
                Filter = Expr.In(Expr.GeometryType(), "Polygon", "MultiPolygon"),
            });
        }
        if (types.Contains(GeoJsonType.LineString) || types.Contains(GeoJsonType.MultiLineString))
        {
            layers.Add(new LineLayer(id + "-lines", id)
            {
                LineColor = color, LineWidth = 3, LineCap = LineCap.Round, LineJoin = LineJoin.Round,
                Filter = Expr.In(Expr.GeometryType(), "LineString", "MultiLineString"),
            });
        }
        if (types.Contains(GeoJsonType.Point) || types.Contains(GeoJsonType.MultiPoint))
        {
            layers.Add(new CircleLayer(id + "-points", id)
            {
                CircleColor = color, CircleRadius = 6, CircleStrokeColor = "#ffffff", CircleStrokeWidth = 2,
                Filter = Expr.In(Expr.GeometryType(), "Point", "MultiPoint"),
            });
        }
        foreach (var l in layers) await AddLayerAsync(l, beforeLayerId, cancellationToken).ConfigureAwait(false);
        return layers.Select(l => l.Id).ToList();
    }

    /// <summary>Removes a source and every layer using it.</summary>
    public async Task RemoveSourceAndLayersAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        await EvalAsync(@"
var style = map.getStyle();
(style.layers || []).filter(function (l) { return l.source === args; }).forEach(function (l) { map.removeLayer(l.id); });
return null;", sourceId, cancellationToken).ConfigureAwait(false);
        await TryRemoveSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds clustered points: a clustered GeoJSON source, cluster circles colored and sized by count,
    /// count labels, unclustered points and (optionally) zoom-on-click.
    /// </summary>
    public async Task<ClusterLayers> AddClusteredPointsAsync(string id, FeatureCollection points, ClusterStyle? style = null, CancellationToken cancellationToken = default)
    {
        style ??= new ClusterStyle();
        var result = new ClusterLayers(id, id + "-clusters", id + "-cluster-count", id + "-points");
        await AddSourceAsync(new GeoJsonSource(id, points)
        {
            Cluster = true,
            ClusterRadius = style.ClusterRadius,
            ClusterMaxZoom = style.ClusterMaxZoom,
            ClusterProperties = style.ClusterProperties,
        }, cancellationToken).ConfigureAwait(false);

        var count = Expr.Get("point_count");
        var colors = style.Colors.OrderBy(c => c.MinCount).ToList();
        var radii = style.Radii.OrderBy(r => r.MinCount).ToList();
        await AddLayerAsync(new CircleLayer(result.ClusterLayerId, id)
        {
            Filter = Expr.Has("point_count"),
            CircleColor = Expr.Step(count, colors[0].Color, colors.Skip(1).Select(c => new Stop(c.MinCount, c.Color)).ToArray()),
            CircleRadius = Expr.Step(count, radii[0].Radius, radii.Skip(1).Select(r => new Stop(r.MinCount, r.Radius)).ToArray()),
            CircleStrokeColor = "#ffffff",
            CircleStrokeWidth = 1,
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var countLayer = new SymbolLayer(result.CountLayerId, id)
        {
            Filter = Expr.Has("point_count"),
            TextField = Expr.Get("point_count_abbreviated"),
            TextSize = 12,
            TextAllowOverlap = true,
        };
        if (style.Font is not null) countLayer.TextFont = style.Font;
        await AddLayerAsync(countLayer, cancellationToken: cancellationToken).ConfigureAwait(false);

        await AddLayerAsync(new CircleLayer(result.PointLayerId, id)
        {
            Filter = Expr.Not(Expr.Has("point_count")),
            CircleColor = style.PointColor,
            CircleRadius = style.PointRadius,
            CircleStrokeWidth = 1,
            CircleStrokeColor = "#ffffff",
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (style.ZoomOnClick)
        {
            await AddHelperAsync("helper.clusterZoom", new JsonObject { ["layer"] = result.ClusterLayerId, ["source"] = id }, cancellationToken).ConfigureAwait(false);
            await EnableHoverCursorAsync(result.ClusterLayerId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>Adds a heatmap of points weighted by an optional numeric property.</summary>
    public async Task<string> AddHeatmapAsync(string id, FeatureCollection points, string? weightProperty = null, double maxWeight = 1, double radius = 20, CancellationToken cancellationToken = default)
    {
        await AddSourceAsync(new GeoJsonSource(id, points), cancellationToken).ConfigureAwait(false);
        var layer = new HeatmapLayer(id + "-heat", id)
        {
            HeatmapRadius = Expr.InterpolateZoom((0, radius / 4), (12, radius)),
            HeatmapIntensity = Expr.InterpolateZoom((0, 1), (12, 3)),
            HeatmapColor = Expr.Interpolate(Interpolation.Linear, Expr.Op("heatmap-density"),
                (0, "rgba(33,102,172,0)"), (0.2, "rgb(103,169,207)"), (0.4, "rgb(209,229,240)"),
                (0.6, "rgb(253,219,199)"), (0.8, "rgb(239,138,98)"), (1, "rgb(178,24,43)")),
        };
        if (weightProperty is not null)
            layer.HeatmapWeight = Expr.Interpolate(Interpolation.Linear, Expr.Get(weightProperty), (0, 0), (maxWeight, 1));
        await AddLayerAsync(layer, cancellationToken: cancellationToken).ConfigureAwait(false);
        return layer.Id;
    }

    /// <summary>
    /// Enables 3D terrain. Mapbox: uses Mapbox Terrain DEM by default. MapLibre: uses the public AWS Terrain Tiles
    /// (Terrarium encoding, no key) by default, or <paramref name="demTilesUrl"/>.
    /// </summary>
    /// <param name="exaggeration">Vertical exaggeration.</param>
    /// <param name="demTilesUrl">A TileJSON URL or a tile template (<c>https://…/{z}/{x}/{y}.png</c>) of a raster-dem source.</param>
    /// <param name="encoding">DEM encoding when <paramref name="demTilesUrl"/> is set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnableTerrainAsync(double exaggeration = 1.5, string? demTilesUrl = null, DemEncoding? encoding = null, CancellationToken cancellationToken = default)
    {
        const string sourceId = "cartovia-dem";
        if (!await HasSourceAsync(sourceId, cancellationToken).ConfigureAwait(false))
        {
            RasterDemSource source;
            if (demTilesUrl is null && Engine == MapEngine.Mapbox)
            {
                source = RasterDemSource.MapboxTerrain(sourceId);
            }
            else
            {
                if (demTilesUrl is null) { demTilesUrl = AwsTerrariumTiles; encoding ??= DemEncoding.Terrarium; }
                source = demTilesUrl.Contains("{z}")
                    ? new RasterDemSource(sourceId) { Tiles = new[] { demTilesUrl }, TileSize = 256, MaxZoom = 15, Encoding = encoding }
                    : new RasterDemSource(sourceId, demTilesUrl) { TileSize = 256, Encoding = encoding };
                if (demTilesUrl == AwsTerrariumTiles) source.Attribution = "Terrain: <a href=\"https://registry.opendata.aws/terrain-tiles/\">AWS Terrain Tiles</a>";
            }
            await AddSourceAsync(source, cancellationToken).ConfigureAwait(false);
        }
        await SetTerrainAsync(new TerrainOptions { Source = sourceId, Exaggeration = exaggeration }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Public elevation tiles (AWS Open Data, Terrarium encoding) used by default with MapLibre.</summary>
    public const string AwsTerrariumTiles = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";

    /// <summary>Disables 3D terrain.</summary>
    public Task DisableTerrainAsync(CancellationToken cancellationToken = default) => SetTerrainAsync(null, cancellationToken);

    /// <summary>
    /// Adds extruded 3D buildings for classic vector styles. Defaults target Mapbox Streets
    /// (<c>composite</c> / <c>building</c> / <c>height</c>); for OpenMapTiles styles (OpenFreeMap, MapTiler) use
    /// <c>("openmaptiles", "building", "render_height", "render_min_height")</c>. Mapbox Standard already has 3D buildings.
    /// </summary>
    public async Task<string> Add3DBuildingsAsync(string? sourceId = null, string sourceLayer = "building", string? heightProperty = null,
        string? baseProperty = null, string color = "#aaaaaa", double minZoom = 14, string layerId = "cartovia-3d-buildings", CancellationToken cancellationToken = default)
    {
        bool mapbox = Engine == MapEngine.Mapbox;
        sourceId ??= mapbox ? "composite" : "openmaptiles";
        heightProperty ??= mapbox ? "height" : "render_height";
        baseProperty ??= mapbox ? "min_height" : "render_min_height";
        var layer = new FillExtrusionLayer(layerId, sourceId, sourceLayer)
        {
            MinZoom = minZoom,
            FillExtrusionColor = color,
            FillExtrusionHeight = Expr.InterpolateZoom((minZoom, 0), (minZoom + 0.05, Expr.Coalesce(Expr.Get(heightProperty), 0))),
            FillExtrusionBase = Expr.InterpolateZoom((minZoom, 0), (minZoom + 0.05, Expr.Coalesce(Expr.Get(baseProperty), 0))),
            FillExtrusionOpacity = 0.7,
        };
        if (mapbox) layer.Filter = Expr.Equal(Expr.Get("extrude"), "true");
        await AddLayerAsync(layer, cancellationToken: cancellationToken).ConfigureAwait(false);
        return layerId;
    }

    /// <summary>Adds a raster tile overlay (WMS / XYZ) with an optional opacity.</summary>
    public async Task<string> AddRasterOverlayAsync(string id, IEnumerable<string> tileUrls, double opacity = 1, int tileSize = 256, string? attribution = null, string? beforeLayerId = null, CancellationToken cancellationToken = default)
    {
        await AddSourceAsync(new RasterSource(id, tileUrls.ToArray()) { TileSize = tileSize, Attribution = attribution }, cancellationToken).ConfigureAwait(false);
        var layer = new RasterLayer(id + "-raster", id) { RasterOpacity = opacity };
        await AddLayerAsync(layer, beforeLayerId, cancellationToken).ConfigureAwait(false);
        return layer.Id;
    }

    private static HashSet<GeoJsonType> CollectGeometryTypes(GeoJsonObject data)
    {
        var set = new HashSet<GeoJsonType>();
        void Visit(Geometry? g)
        {
            if (g is null) return;
            if (g is GeometryCollection gc) { foreach (var x in gc.Geometries) Visit(x); }
            else set.Add(g.Type);
        }
        switch (data)
        {
            case FeatureCollection fc: foreach (var f in fc.Features) Visit(f.Geometry); break;
            case Feature f: Visit(f.Geometry); break;
            case Geometry g: Visit(g); break;
        }
        return set;
    }
}

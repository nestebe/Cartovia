using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;

namespace Mapbox.Net.Plugins;

/// <summary>Distance units of Turf.js.</summary>
public enum TurfUnits
{
    /// <summary>Meters.</summary>
    Meters,
    /// <summary>Kilometers.</summary>
    Kilometers,
    /// <summary>Miles.</summary>
    Miles,
    /// <summary>Nautical miles.</summary>
    [JsonValue("nauticalmiles")] NauticalMiles,
    /// <summary>Feet.</summary>
    Feet,
    /// <summary>Degrees.</summary>
    Degrees,
}

/// <summary>
/// Geospatial analysis with Turf.js running in the page (buffer, area, length, along, simplify, union…).
/// Typed helpers cover the most common functions; <see cref="CallAsync"/> reaches any Turf function.
/// </summary>
public sealed class TurfPlugin : MapPlugin
{
    /// <inheritdoc />
    public override string Name => "turf";

    /// <summary>Script URL.</summary>
    public string ScriptUrl { get; set; } = PluginAssets.TurfScript;

    /// <summary>Calls any Turf function: <c>turf[function](...args)</c>.</summary>
    public Task<JsonElement> CallAsync(string function, params object?[] args)
    {
        var all = new object?[args.Length + 1];
        all[0] = function;
        args.CopyTo(all, 1);
        return InvokeAsync("call", all);
    }

    private async Task<T> CallGeoJsonAsync<T>(string function, params object?[] args) where T : GeoJsonObject
        => GeoJsonObject.Parse<T>((await CallAsync(function, args).ConfigureAwait(false)).GetRawText());

    private static JsonObject Units(TurfUnits units) => new() { ["units"] = MapboxJson.EnumToString(units) };

    /// <summary>Buffer around a geometry / feature.</summary>
    public Task<Feature> BufferAsync(GeoJsonObject geoJson, double radius, TurfUnits units = TurfUnits.Meters)
        => CallGeoJsonAsync<Feature>("buffer", geoJson, radius, Units(units));

    /// <summary>Area in square meters.</summary>
    public async Task<double> AreaAsync(GeoJsonObject geoJson) => (await CallAsync("area", geoJson).ConfigureAwait(false)).GetDouble();

    /// <summary>Length of lines.</summary>
    public async Task<double> LengthAsync(GeoJsonObject geoJson, TurfUnits units = TurfUnits.Meters)
        => (await CallAsync("length", geoJson, Units(units)).ConfigureAwait(false)).GetDouble();

    /// <summary>Distance between two points.</summary>
    public async Task<double> DistanceAsync(LngLat from, LngLat to, TurfUnits units = TurfUnits.Meters)
        => (await CallAsync("distance", from, to, Units(units)).ConfigureAwait(false)).GetDouble();

    /// <summary>Point at a distance along a line.</summary>
    public Task<Feature> AlongAsync(LineString line, double distance, TurfUnits units = TurfUnits.Meters)
        => CallGeoJsonAsync<Feature>("along", line, distance, Units(units));

    /// <summary>Centroid.</summary>
    public Task<Feature> CentroidAsync(GeoJsonObject geoJson) => CallGeoJsonAsync<Feature>("centroid", geoJson);

    /// <summary>Center of mass.</summary>
    public Task<Feature> CenterOfMassAsync(GeoJsonObject geoJson) => CallGeoJsonAsync<Feature>("centerOfMass", geoJson);

    /// <summary>Bounding box.</summary>
    public async Task<LngLatBounds> BBoxAsync(GeoJsonObject geoJson)
    {
        var b = await CallAsync("bbox", geoJson).ConfigureAwait(false);
        return new LngLatBounds(b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble());
    }

    /// <summary>Simplifies a geometry (Douglas-Peucker).</summary>
    public Task<T> SimplifyAsync<T>(T geoJson, double tolerance, bool highQuality = false) where T : GeoJsonObject
        => CallGeoJsonAsync<T>("simplify", geoJson, new JsonObject { ["tolerance"] = tolerance, ["highQuality"] = highQuality });

    /// <summary>Convex hull of features.</summary>
    public Task<Feature> ConvexAsync(GeoJsonObject geoJson) => CallGeoJsonAsync<Feature>("convex", geoJson);

    /// <summary>Union of polygons.</summary>
    public Task<Feature> UnionAsync(FeatureCollection polygons) => CallGeoJsonAsync<Feature>("union", polygons);

    /// <summary>Intersection of polygons (null when empty).</summary>
    public async Task<Feature?> IntersectAsync(FeatureCollection polygons)
    {
        var r = await CallAsync("intersect", polygons).ConfigureAwait(false);
        return r.ValueKind == JsonValueKind.Object ? GeoJsonObject.Parse<Feature>(r.GetRawText()) : null;
    }

    /// <summary>True if the point is inside the polygon.</summary>
    public async Task<bool> BooleanPointInPolygonAsync(LngLat point, GeoJsonObject polygon)
        => (await CallAsync("booleanPointInPolygon", point, polygon).ConfigureAwait(false)).ValueKind == JsonValueKind.True;

    /// <summary>Points inside polygons.</summary>
    public Task<FeatureCollection> PointsWithinPolygonAsync(FeatureCollection points, GeoJsonObject polygons)
        => CallGeoJsonAsync<FeatureCollection>("pointsWithinPolygon", points, polygons);

    /// <summary>Nearest point of a collection.</summary>
    public Task<Feature> NearestPointAsync(LngLat target, FeatureCollection points)
        => CallGeoJsonAsync<Feature>("nearestPoint", target, points);

    /// <summary>Circle polygon.</summary>
    public Task<Feature> CircleAsync(LngLat center, double radius, TurfUnits units = TurfUnits.Meters, int steps = 64)
        => CallGeoJsonAsync<Feature>("circle", center, radius, new JsonObject { ["units"] = MapboxJson.EnumToString(units), ["steps"] = steps });

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine) => new[] { ScriptUrl };
}

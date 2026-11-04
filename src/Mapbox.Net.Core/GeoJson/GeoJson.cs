using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mapbox.Net.Json;

namespace Mapbox.Net.GeoJson;

/// <summary>A GeoJSON position: longitude, latitude and optional altitude. Serialized as an array.</summary>
[JsonConverter(typeof(PositionJsonConverter))]
public readonly record struct Position(double Longitude, double Latitude, double? Altitude = null)
{
    /// <summary>Converts to a <see cref="LngLat"/> (altitude is dropped).</summary>
    public static implicit operator LngLat(Position p) => new(p.Longitude, p.Latitude);

    /// <summary>Converts from a <see cref="LngLat"/>.</summary>
    public static implicit operator Position(LngLat p) => new(p.Longitude, p.Latitude);

    /// <summary>Converts from a <c>(lng, lat)</c> tuple.</summary>
    public static implicit operator Position((double Lng, double Lat) t) => new(t.Lng, t.Lat);
}

/// <summary>JSON converter for <see cref="Position"/>.</summary>
public sealed class PositionJsonConverter : JsonConverter<Position>
{
    /// <inheritdoc />
    public override Position Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A GeoJSON position must be an array.");
        reader.Read(); double lng = reader.GetDouble();
        reader.Read(); double lat = reader.GetDouble();
        double? alt = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (alt is null && reader.TokenType == JsonTokenType.Number) alt = reader.GetDouble();
        }
        return new Position(lng, lat, alt);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Position value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Longitude);
        writer.WriteNumberValue(value.Latitude);
        if (value.Altitude is { } a) writer.WriteNumberValue(a);
        writer.WriteEndArray();
    }
}

/// <summary>GeoJSON object types.</summary>
public enum GeoJsonType
{
    /// <summary>Point geometry.</summary>
    [JsonValue("Point")] Point,
    /// <summary>MultiPoint geometry.</summary>
    [JsonValue("MultiPoint")] MultiPoint,
    /// <summary>LineString geometry.</summary>
    [JsonValue("LineString")] LineString,
    /// <summary>MultiLineString geometry.</summary>
    [JsonValue("MultiLineString")] MultiLineString,
    /// <summary>Polygon geometry.</summary>
    [JsonValue("Polygon")] Polygon,
    /// <summary>MultiPolygon geometry.</summary>
    [JsonValue("MultiPolygon")] MultiPolygon,
    /// <summary>GeometryCollection.</summary>
    [JsonValue("GeometryCollection")] GeometryCollection,
    /// <summary>Feature.</summary>
    [JsonValue("Feature")] Feature,
    /// <summary>FeatureCollection.</summary>
    [JsonValue("FeatureCollection")] FeatureCollection,
}

/// <summary>Base class of every GeoJSON object (RFC 7946).</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public abstract class GeoJsonObject
{
    /// <summary>The GeoJSON type.</summary>
    public abstract GeoJsonType Type { get; }

    /// <summary>Optional bounding box.</summary>
    public double[]? BBox { get; set; }

    /// <summary>Serializes this object to a GeoJSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, typeof(GeoJsonObject), MapboxJson.Options);

    /// <inheritdoc />
    public override string ToString() => ToJson();

    /// <summary>Parses any GeoJSON string.</summary>
    public static GeoJsonObject Parse(string json)
        => JsonSerializer.Deserialize<GeoJsonObject>(json, MapboxJson.Options) ?? throw new JsonException("Empty GeoJSON.");

    /// <summary>Parses a GeoJSON string and casts it to the requested type.</summary>
    public static T Parse<T>(string json) where T : GeoJsonObject
        => Parse(json) as T ?? throw new JsonException($"The GeoJSON is not a {typeof(T).Name}.");
}

/// <summary>Base class of GeoJSON geometries.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public abstract class Geometry : GeoJsonObject
{
    /// <summary>Enumerates every position of the geometry.</summary>
    public abstract IEnumerable<Position> GetPositions();

    /// <summary>Computes the bounding box of the geometry, or null if it is empty.</summary>
    public LngLatBounds? GetBounds()
    {
        var points = GetPositions().Select(p => (LngLat)p).ToList();
        return points.Count == 0 ? null : LngLatBounds.FromPoints(points);
    }
}

/// <summary>Point geometry.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class Point : Geometry
{
    /// <summary>Creates a point.</summary>
    public Point(Position coordinates) => Coordinates = coordinates;
    /// <summary>Creates a point.</summary>
    public Point(double longitude, double latitude) => Coordinates = new Position(longitude, latitude);
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.Point;
    /// <summary>The coordinates.</summary>
    public Position Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() { yield return Coordinates; }
}

/// <summary>MultiPoint geometry.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class MultiPoint : Geometry
{
    /// <summary>Creates a multipoint.</summary>
    public MultiPoint(IEnumerable<Position> coordinates) => Coordinates = coordinates.ToList();
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.MultiPoint;
    /// <summary>The coordinates.</summary>
    public IList<Position> Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Coordinates;
}

/// <summary>LineString geometry.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class LineString : Geometry
{
    /// <summary>Creates a line string.</summary>
    public LineString(IEnumerable<Position> coordinates) => Coordinates = coordinates.ToList();
    /// <summary>Creates a line string from coordinates.</summary>
    public LineString(params LngLat[] coordinates) => Coordinates = coordinates.Select(c => (Position)c).ToList();
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.LineString;
    /// <summary>The coordinates.</summary>
    public IList<Position> Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Coordinates;

    /// <summary>Length of the line in meters (haversine).</summary>
    public double GetLength()
    {
        double total = 0;
        for (int i = 1; i < Coordinates.Count; i++) total += ((LngLat)Coordinates[i - 1]).DistanceTo(Coordinates[i]);
        return total;
    }
}

/// <summary>MultiLineString geometry.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class MultiLineString : Geometry
{
    /// <summary>Creates a multi line string.</summary>
    public MultiLineString(IEnumerable<IEnumerable<Position>> coordinates)
        => Coordinates = coordinates.Select(l => (IList<Position>)l.ToList()).ToList();
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.MultiLineString;
    /// <summary>The coordinates.</summary>
    public IList<IList<Position>> Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Coordinates.SelectMany(l => l);
}

/// <summary>Polygon geometry. The first ring is the exterior ring, the others are holes.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class Polygon : Geometry
{
    /// <summary>Creates a polygon from rings.</summary>
    public Polygon(IEnumerable<IEnumerable<Position>> rings)
        => Coordinates = rings.Select(r => (IList<Position>)r.ToList()).ToList();

    /// <summary>Creates a polygon from its exterior ring (closed automatically if needed).</summary>
    public Polygon(params LngLat[] exteriorRing)
    {
        var ring = exteriorRing.Select(c => (Position)c).ToList();
        if (ring.Count > 0 && ring[0] != ring[^1]) ring.Add(ring[0]);
        Coordinates = new List<IList<Position>> { ring };
    }

    /// <summary>Creates a rectangular polygon from bounds.</summary>
    public static Polygon FromBounds(LngLatBounds b)
        => new(b.SouthWest, b.SouthEast, b.NorthEast, b.NorthWest, b.SouthWest);

    /// <summary>Creates an approximate circle polygon.</summary>
    /// <param name="center">Center of the circle.</param>
    /// <param name="radiusMeters">Radius in meters.</param>
    /// <param name="steps">Number of vertices.</param>
    public static Polygon Circle(LngLat center, double radiusMeters, int steps = 64)
    {
        const double R = 6371008.8;
        var pts = new LngLat[steps + 1];
        double lat1 = center.Latitude * Math.PI / 180, lng1 = center.Longitude * Math.PI / 180, d = radiusMeters / R;
        for (int i = 0; i < steps; i++)
        {
            double brng = 2 * Math.PI * i / steps;
            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(brng));
            double lng2 = lng1 + Math.Atan2(Math.Sin(brng) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
            pts[i] = new LngLat(lng2 * 180 / Math.PI, lat2 * 180 / Math.PI);
        }
        pts[steps] = pts[0];
        return new Polygon(pts);
    }

    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.Polygon;
    /// <summary>The rings.</summary>
    public IList<IList<Position>> Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Coordinates.SelectMany(r => r);
}

/// <summary>MultiPolygon geometry.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class MultiPolygon : Geometry
{
    /// <summary>Creates a multipolygon.</summary>
    public MultiPolygon(IEnumerable<IEnumerable<IEnumerable<Position>>> coordinates)
        => Coordinates = coordinates.Select(p => (IList<IList<Position>>)p.Select(r => (IList<Position>)r.ToList()).ToList()).ToList();
    /// <summary>Creates a multipolygon from polygons.</summary>
    public MultiPolygon(IEnumerable<Polygon> polygons)
        => Coordinates = polygons.Select(p => p.Coordinates).ToList();
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.MultiPolygon;
    /// <summary>The polygons.</summary>
    public IList<IList<IList<Position>>> Coordinates { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Coordinates.SelectMany(p => p.SelectMany(r => r));
}

/// <summary>GeometryCollection.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class GeometryCollection : Geometry
{
    /// <summary>Creates a geometry collection.</summary>
    public GeometryCollection(IEnumerable<Geometry> geometries) => Geometries = geometries.ToList();
    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.GeometryCollection;
    /// <summary>The geometries.</summary>
    public IList<Geometry> Geometries { get; set; }
    /// <inheritdoc />
    public override IEnumerable<Position> GetPositions() => Geometries.SelectMany(g => g.GetPositions());
}

/// <summary>A GeoJSON feature: a geometry with properties and an optional identifier.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public class Feature : GeoJsonObject
{
    /// <summary>Creates an empty feature.</summary>
    public Feature() { }

    /// <summary>Creates a feature.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <param name="properties">Properties: a <see cref="JsonObject"/>, a dictionary or any object (anonymous types welcome).</param>
    /// <param name="id">Optional identifier (string or number). Numeric ids enable feature-state.</param>
    public Feature(Geometry? geometry, object? properties = null, object? id = null)
    {
        Geometry = geometry;
        Properties = properties is null ? new JsonObject() : MapboxJson.ToNode(properties) as JsonObject
            ?? throw new ArgumentException("Properties must serialize to a JSON object.", nameof(properties));
        Id = id is null ? null : MapboxJson.ToNode(id);
    }

    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.Feature;

    /// <summary>Identifier (string or number).</summary>
    public JsonNode? Id { get; set; }

    /// <summary>The geometry (may be null).</summary>
    public Geometry? Geometry { get; set; }

    /// <summary>Feature properties.</summary>
    public JsonObject Properties { get; set; } = new();

    /// <summary>Reads a property converted to <typeparamref name="T"/>, or <c>default</c> if absent.</summary>
    public T? GetProperty<T>(string name)
        => Properties.TryGetPropertyValue(name, out var node) && node is not null
            ? node.Deserialize<T>(MapboxJson.Options)
            : default;

    /// <summary>Sets a property value.</summary>
    public Feature SetProperty(string name, object? value)
    {
        Properties[name] = MapboxJson.ToNode(value);
        return this;
    }
}

/// <summary>A collection of features.</summary>
[JsonConverter(typeof(GeoJsonConverterFactory))]
public sealed class FeatureCollection : GeoJsonObject
{
    /// <summary>Creates an empty collection.</summary>
    public FeatureCollection() { }
    /// <summary>Creates a collection.</summary>
    public FeatureCollection(IEnumerable<Feature> features) => Features = features.ToList();

    /// <inheritdoc />
    public override GeoJsonType Type => GeoJsonType.FeatureCollection;

    /// <summary>The features.</summary>
    public IList<Feature> Features { get; set; } = new List<Feature>();

    /// <summary>Adds a feature and returns the collection (fluent).</summary>
    public FeatureCollection Add(Feature feature) { Features.Add(feature); return this; }

    /// <summary>Computes the bounds of all features, or null if empty.</summary>
    public LngLatBounds? GetBounds()
    {
        var points = Features.Where(f => f.Geometry is not null).SelectMany(f => f.Geometry!.GetPositions()).Select(p => (LngLat)p).ToList();
        return points.Count == 0 ? null : LngLatBounds.FromPoints(points);
    }
}

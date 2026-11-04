using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cartovia.GeoJson;

/// <summary>Creates converters for every <see cref="GeoJsonObject"/> type.</summary>
public sealed class GeoJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeof(GeoJsonObject).IsAssignableFrom(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(GeoJsonConverter<>).MakeGenericType(typeToConvert))!;
}

/// <summary>Reads and writes GeoJSON objects (polymorphic on the <c>type</c> member).</summary>
public sealed class GeoJsonConverter<T> : JsonConverter<T> where T : GeoJsonObject
{
    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using var doc = JsonDocument.ParseValue(ref reader);
        var obj = GeoJsonReader.Read(doc.RootElement, options);
        return obj as T ?? throw new JsonException($"Expected {typeof(T).Name} but got {obj.Type}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => GeoJsonWriter.Write(writer, value, options);
}

internal static class GeoJsonReader
{
    public static GeoJsonObject Read(JsonElement e, JsonSerializerOptions options)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("type", out var typeProp))
            throw new JsonException("A GeoJSON object must have a 'type' member.");

        GeoJsonObject result = typeProp.GetString() switch
        {
            "Point" => new Point(Pos(e.GetProperty("coordinates"))),
            "MultiPoint" => new MultiPoint(Line(e.GetProperty("coordinates"))),
            "LineString" => new LineString(Line(e.GetProperty("coordinates"))),
            "MultiLineString" => new MultiLineString(Rings(e.GetProperty("coordinates"))),
            "Polygon" => new Polygon(Rings(e.GetProperty("coordinates"))),
            "MultiPolygon" => new MultiPolygon(e.GetProperty("coordinates").EnumerateArray().Select(Rings).ToList()),
            "GeometryCollection" => new GeometryCollection(e.GetProperty("geometries").EnumerateArray().Select(g => (Geometry)Read(g, options)).ToList()),
            "Feature" => ReadFeature(e, options),
            "FeatureCollection" => new FeatureCollection(e.GetProperty("features").EnumerateArray().Select(f => (Feature)Read(f, options)).ToList()),
            var t => throw new JsonException($"Unknown GeoJSON type '{t}'."),
        };

        if (e.TryGetProperty("bbox", out var bbox) && bbox.ValueKind == JsonValueKind.Array)
            result.BBox = bbox.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        return result;
    }

    private static Feature ReadFeature(JsonElement e, JsonSerializerOptions options)
    {
        var f = new Feature();
        if (e.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object)
            f.Geometry = (Geometry)Read(g, options);
        if (e.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
            f.Properties = JsonNode.Parse(p.GetRawText())!.AsObject();
        if (e.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            f.Id = JsonNode.Parse(id.GetRawText());
        return f;
    }

    private static Position Pos(JsonElement e)
    {
        double lng = e[0].GetDouble(), lat = e[1].GetDouble();
        double? alt = e.GetArrayLength() > 2 && e[2].ValueKind == JsonValueKind.Number ? e[2].GetDouble() : null;
        return new Position(lng, lat, alt);
    }

    private static List<Position> Line(JsonElement e) => e.EnumerateArray().Select(Pos).ToList();

    private static List<List<Position>> Rings(JsonElement e) => e.EnumerateArray().Select(Line).ToList();
}

internal static class GeoJsonWriter
{
    public static void Write(Utf8JsonWriter w, GeoJsonObject value, JsonSerializerOptions options)
    {
        w.WriteStartObject();
        w.WriteString("type", value.Type.ToString());
        switch (value)
        {
            case Point p:
                w.WritePropertyName("coordinates"); WritePos(w, p.Coordinates); break;
            case MultiPoint mp:
                w.WritePropertyName("coordinates"); WriteLine(w, mp.Coordinates); break;
            case LineString ls:
                w.WritePropertyName("coordinates"); WriteLine(w, ls.Coordinates); break;
            case MultiLineString mls:
                w.WritePropertyName("coordinates"); WriteRings(w, mls.Coordinates); break;
            case Polygon pg:
                w.WritePropertyName("coordinates"); WriteRings(w, pg.Coordinates); break;
            case MultiPolygon mpg:
                w.WritePropertyName("coordinates");
                w.WriteStartArray();
                foreach (var poly in mpg.Coordinates) WriteRings(w, poly);
                w.WriteEndArray();
                break;
            case GeometryCollection gc:
                w.WritePropertyName("geometries");
                w.WriteStartArray();
                foreach (var g in gc.Geometries) Write(w, g, options);
                w.WriteEndArray();
                break;
            case Feature f:
                if (f.Id is not null) { w.WritePropertyName("id"); f.Id.WriteTo(w); }
                w.WritePropertyName("geometry");
                if (f.Geometry is null) w.WriteNullValue(); else Write(w, f.Geometry, options);
                w.WritePropertyName("properties");
                f.Properties.WriteTo(w);
                break;
            case FeatureCollection fc:
                w.WritePropertyName("features");
                w.WriteStartArray();
                foreach (var f in fc.Features) Write(w, f, options);
                w.WriteEndArray();
                break;
        }
        if (value.BBox is { Length: > 0 } bbox)
        {
            w.WritePropertyName("bbox");
            w.WriteStartArray();
            foreach (var d in bbox) w.WriteNumberValue(d);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void WritePos(Utf8JsonWriter w, Position p)
    {
        w.WriteStartArray();
        w.WriteNumberValue(p.Longitude);
        w.WriteNumberValue(p.Latitude);
        if (p.Altitude is { } a) w.WriteNumberValue(a);
        w.WriteEndArray();
    }

    private static void WriteLine(Utf8JsonWriter w, IEnumerable<Position> line)
    {
        w.WriteStartArray();
        foreach (var p in line) WritePos(w, p);
        w.WriteEndArray();
    }

    private static void WriteRings(Utf8JsonWriter w, IEnumerable<IEnumerable<Position>> rings)
    {
        w.WriteStartArray();
        foreach (var r in rings) WriteLine(w, r);
        w.WriteEndArray();
    }
}

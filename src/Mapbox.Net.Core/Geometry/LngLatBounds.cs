using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mapbox.Net;

/// <summary>
/// A geographic bounding box defined by its south-west and north-east corners.
/// Serialized as <c>[[west, south], [east, north]]</c>.
/// </summary>
[JsonConverter(typeof(LngLatBoundsJsonConverter))]
public readonly record struct LngLatBounds(LngLat SouthWest, LngLat NorthEast)
{
    /// <summary>Creates bounds from west, south, east, north values.</summary>
    public LngLatBounds(double west, double south, double east, double north)
        : this(new LngLat(west, south), new LngLat(east, north)) { }

    /// <summary>Western longitude.</summary>
    public double West => SouthWest.Longitude;
    /// <summary>Southern latitude.</summary>
    public double South => SouthWest.Latitude;
    /// <summary>Eastern longitude.</summary>
    public double East => NorthEast.Longitude;
    /// <summary>Northern latitude.</summary>
    public double North => NorthEast.Latitude;
    /// <summary>North-west corner.</summary>
    public LngLat NorthWest => new(West, North);
    /// <summary>South-east corner.</summary>
    public LngLat SouthEast => new(East, South);

    /// <summary>Center of the bounds.</summary>
    public LngLat Center => new((West + East) / 2, (South + North) / 2);

    /// <summary>Returns true if the coordinate is inside the bounds.</summary>
    public bool Contains(LngLat p) =>
        p.Latitude >= South && p.Latitude <= North &&
        (West <= East ? p.Longitude >= West && p.Longitude <= East : p.Longitude >= West || p.Longitude <= East);

    /// <summary>Returns new bounds extended to include the coordinate.</summary>
    public LngLatBounds Extend(LngLat p) => new(
        Math.Min(West, p.Longitude), Math.Min(South, p.Latitude),
        Math.Max(East, p.Longitude), Math.Max(North, p.Latitude));

    /// <summary>Returns new bounds extended to include other bounds.</summary>
    public LngLatBounds Extend(LngLatBounds other) => Extend(other.SouthWest).Extend(other.NorthEast);

    /// <summary>Computes the bounds of a set of coordinates.</summary>
    /// <exception cref="ArgumentException">The sequence is empty.</exception>
    public static LngLatBounds FromPoints(IEnumerable<LngLat> points)
    {
        if (points is null) throw new ArgumentNullException(nameof(points));
        LngLatBounds? result = null;
        foreach (var p in points)
            result = result is null ? new LngLatBounds(p, p) : result.Value.Extend(p);
        return result ?? throw new ArgumentException("At least one point is required.", nameof(points));
    }

    /// <summary>Returns <c>[west, south, east, north]</c>.</summary>
    public double[] ToBBox() => new[] { West, South, East, North };
}

/// <summary>JSON converter for <see cref="LngLatBounds"/>.</summary>
public sealed class LngLatBoundsJsonConverter : JsonConverter<LngLatBounds>
{
    /// <inheritdoc />
    public override LngLatBounds Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 4 && root[0].ValueKind == JsonValueKind.Number)
                return new LngLatBounds(root[0].GetDouble(), root[1].GetDouble(), root[2].GetDouble(), root[3].GetDouble());
            if (root.GetArrayLength() == 2)
                return new LngLatBounds(ReadPoint(root[0], options), ReadPoint(root[1], options));
        }
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("_sw", out var sw) && root.TryGetProperty("_ne", out var ne))
                return new LngLatBounds(ReadPoint(sw, options), ReadPoint(ne, options));
            if (root.TryGetProperty("southWest", out sw) && root.TryGetProperty("northEast", out ne))
                return new LngLatBounds(ReadPoint(sw, options), ReadPoint(ne, options));
        }
        throw new JsonException("Invalid LngLatBounds value.");
    }

    private static LngLat ReadPoint(JsonElement e, JsonSerializerOptions options) => e.Deserialize<LngLat>(options);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LngLatBounds value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStartArray(); writer.WriteNumberValue(value.West); writer.WriteNumberValue(value.South); writer.WriteEndArray();
        writer.WriteStartArray(); writer.WriteNumberValue(value.East); writer.WriteNumberValue(value.North); writer.WriteEndArray();
        writer.WriteEndArray();
    }
}

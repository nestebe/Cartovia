using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cartovia;

/// <summary>A pixel position relative to the top-left corner of the map. Serialized as <c>[x, y]</c>.</summary>
[JsonConverter(typeof(ScreenPointJsonConverter))]
public readonly record struct ScreenPoint(double X, double Y)
{
    /// <summary>Implicit conversion from a <c>(x, y)</c> tuple.</summary>
    public static implicit operator ScreenPoint((double X, double Y) t) => new(t.X, t.Y);

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X}, {Y}");
}

/// <summary>A screen-space rectangle given by two corners.</summary>
public readonly record struct ScreenBox(ScreenPoint Min, ScreenPoint Max)
{
    /// <summary>Creates a box from coordinates.</summary>
    public ScreenBox(double x1, double y1, double x2, double y2) : this(new ScreenPoint(x1, y1), new ScreenPoint(x2, y2)) { }

    /// <summary>Creates a square box centered on a point.</summary>
    public static ScreenBox Around(ScreenPoint center, double radius)
        => new(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);

    internal ScreenPoint[] ToArray() => new[] { Min, Max };
}

/// <summary>JSON converter for <see cref="ScreenPoint"/>.</summary>
public sealed class ScreenPointJsonConverter : JsonConverter<ScreenPoint>
{
    /// <inheritdoc />
    public override ScreenPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var e = doc.RootElement;
        if (e.ValueKind == JsonValueKind.Array) return new ScreenPoint(e[0].GetDouble(), e[1].GetDouble());
        if (e.ValueKind == JsonValueKind.Object) return new ScreenPoint(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble());
        throw new JsonException("Invalid point value.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ScreenPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}

/// <summary>Padding in pixels, used by camera operations. Implicitly convertible from a number (uniform padding).</summary>
public sealed record MapPadding(double Top = 0, double Right = 0, double Bottom = 0, double Left = 0)
{
    /// <summary>Uniform padding.</summary>
    public static implicit operator MapPadding(double all) => new(all, all, all, all);

    /// <summary>Uniform padding.</summary>
    public static implicit operator MapPadding(int all) => new(all, all, all, all);
}

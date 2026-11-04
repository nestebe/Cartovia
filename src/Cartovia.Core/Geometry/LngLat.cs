using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cartovia;

/// <summary>
/// A geographic coordinate (longitude, latitude) in WGS84 degrees.
/// Serialized as <c>[lng, lat]</c>; deserialized from <c>[lng, lat]</c>, <c>{lng, lat}</c> or <c>{lon, lat}</c>.
/// </summary>
[JsonConverter(typeof(LngLatJsonConverter))]
[System.ComponentModel.TypeConverter(typeof(LngLatTypeConverter))]
public readonly record struct LngLat(double Longitude, double Latitude)
{
    /// <summary>Longitude alias.</summary>
    [JsonIgnore] public double Lng => Longitude;

    /// <summary>Latitude alias.</summary>
    [JsonIgnore] public double Lat => Latitude;

    /// <summary>Returns a coordinate with the longitude wrapped to the [-180, 180] range.</summary>
    public LngLat Wrap()
    {
        double lng = ((Longitude + 180) % 360 + 360) % 360 - 180;
        return new LngLat(lng == -180 && Longitude > 0 ? 180 : lng, Latitude);
    }

    /// <summary>Great-circle distance in meters (haversine, mean Earth radius).</summary>
    public double DistanceTo(LngLat other)
    {
        const double R = 6371008.8;
        double rad = Math.PI / 180;
        double dLat = (other.Latitude - Latitude) * rad;
        double dLng = (other.Longitude - Longitude) * rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(Latitude * rad) * Math.Cos(other.Latitude * rad) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Returns the coordinate as a <c>[lng, lat]</c> array.</summary>
    public double[] ToArray() => new[] { Longitude, Latitude };

    /// <summary>Deconstructs into longitude and latitude.</summary>
    public void Deconstruct(out double lng, out double lat) { lng = Longitude; lat = Latitude; }

    /// <summary>Implicit conversion from a <c>(lng, lat)</c> tuple.</summary>
    public static implicit operator LngLat((double Lng, double Lat) t) => new(t.Lng, t.Lat);

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(Longitude, 6)}, {Math.Round(Latitude, 6)}");
}

/// <summary>JSON converter for <see cref="LngLat"/>.</summary>
public sealed class LngLatJsonConverter : JsonConverter<LngLat>
{
    /// <inheritdoc />
    public override LngLat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read(); double lng = reader.GetDouble();
            reader.Read(); double lat = reader.GetDouble();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) { reader.Skip(); }
            return new LngLat(lng, lat);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            double lng = 0, lat = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name?.ToLowerInvariant())
                {
                    case "lng": case "lon": case "longitude": lng = reader.GetDouble(); break;
                    case "lat": case "latitude": lat = reader.GetDouble(); break;
                    default: reader.Skip(); break;
                }
            }
            return new LngLat(lng, lat);
        }
        throw new JsonException("Invalid LngLat value.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LngLat value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Longitude);
        writer.WriteNumberValue(value.Latitude);
        writer.WriteEndArray();
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Mapbox.Net.Services;

/// <summary>
/// Encodes and decodes the Google "encoded polyline" format used by Mapbox
/// (precision 5 for Static Images paths, 6 for <c>polyline6</c> geometries).
/// </summary>
public static class PolylineCodec
{
    /// <summary>Encodes coordinates as a polyline string.</summary>
    /// <param name="coordinates">The coordinates.</param>
    /// <param name="precision">Number of decimals (5 or 6).</param>
    public static string Encode(IEnumerable<LngLat> coordinates, int precision = 5)
    {
        if (coordinates is null) throw new ArgumentNullException(nameof(coordinates));
        if (precision is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(precision));
        double factor = Math.Pow(10, precision);
        var sb = new StringBuilder();
        long prevLat = 0, prevLng = 0;
        foreach (var c in coordinates)
        {
            long lat = (long)Math.Round(c.Latitude * factor, MidpointRounding.AwayFromZero);
            long lng = (long)Math.Round(c.Longitude * factor, MidpointRounding.AwayFromZero);
            EncodeValue(lat - prevLat, sb);
            EncodeValue(lng - prevLng, sb);
            prevLat = lat;
            prevLng = lng;
        }
        return sb.ToString();
    }

    /// <summary>Decodes a polyline string.</summary>
    /// <param name="polyline">The encoded polyline.</param>
    /// <param name="precision">Number of decimals (5 or 6).</param>
    /// <exception cref="FormatException">The string is not a valid encoded polyline.</exception>
    public static IReadOnlyList<LngLat> Decode(string polyline, int precision = 5)
    {
        if (polyline is null) throw new ArgumentNullException(nameof(polyline));
        if (precision is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(precision));
        double factor = Math.Pow(10, precision);
        var result = new List<LngLat>();
        int index = 0;
        long lat = 0, lng = 0;
        while (index < polyline.Length)
        {
            lat += DecodeValue(polyline, ref index);
            lng += DecodeValue(polyline, ref index);
            result.Add(new LngLat(lng / factor, lat / factor));
        }
        return result;
    }

    private static void EncodeValue(long value, StringBuilder sb)
    {
        long v = value < 0 ? ~(value << 1) : value << 1;
        while (v >= 0x20)
        {
            sb.Append((char)((0x20 | (v & 0x1f)) + 63));
            v >>= 5;
        }
        sb.Append((char)(v + 63));
    }

    private static long DecodeValue(string s, ref int index)
    {
        long result = 0;
        int shift = 0;
        int b;
        do
        {
            if (index >= s.Length) throw new FormatException("Truncated encoded polyline.");
            b = s[index++] - 63;
            if (b < 0 || b > 63) throw new FormatException("Invalid character in encoded polyline.");
            result |= (long)(b & 0x1f) << shift;
            shift += 5;
        } while (b >= 0x20);
        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }
}

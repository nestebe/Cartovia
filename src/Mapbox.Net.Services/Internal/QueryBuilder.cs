using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mapbox.Net.Services.Internal;

/// <summary>Formatting helpers that always use the invariant culture.</summary>
internal static class Fmt
{
    /// <summary>Formats a number without exponent notation, invariant culture, at most 9 decimals.</summary>
    public static string Num(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Numbers sent to the Mapbox API must be finite.");
        if (value == 0) value = 0; // normalizes -0
        return value.ToString("0.#########", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats an integer, invariant culture.</summary>
    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a boolean as <c>true</c>/<c>false</c>.</summary>
    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary><c>lng,lat</c>.</summary>
    public static string LngLat(LngLat p) => Num(p.Longitude) + "," + Num(p.Latitude);

    /// <summary><c>lng,lat;lng,lat;…</c>.</summary>
    public static string Coordinates(IEnumerable<LngLat> points) => string.Join(";", points.Select(LngLat));

    /// <summary><c>minLon,minLat,maxLon,maxLat</c>.</summary>
    public static string BBox(LngLatBounds b) => string.Join(",", Num(b.West), Num(b.South), Num(b.East), Num(b.North));

    /// <summary>Radius in meters; <see cref="double.PositiveInfinity"/> becomes <c>unlimited</c>.</summary>
    public static string Radius(double radius)
    {
        if (double.IsPositiveInfinity(radius)) return "unlimited";
        if (radius <= 0 || double.IsNaN(radius)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius must be greater than 0.");
        return Num(radius);
    }

    /// <summary>
    /// Formats a date for <c>depart_at</c> / <c>arrive_by</c>: <c>yyyy-MM-ddTHH:mm:ssZ</c> in UTC,
    /// or <c>yyyy-MM-ddTHH:mm:ss±hh:mm</c> with the offset.
    /// </summary>
    public static string Date(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Removes a leading <c>#</c> from a hex color and validates it.</summary>
    public static string Color(string color, string paramName)
    {
        if (color is null) throw new ArgumentNullException(paramName);
        var c = color.Trim().TrimStart('#');
        if (!(c.Length is 3 or 6) || !c.All(Uri.IsHexDigit))
            throw new ArgumentException($"'{color}' is not a 3- or 6-digit hex color.", paramName);
        return c.ToLowerInvariant();
    }
}

/// <summary>Builds an URL-encoded query string (or form body). Values are percent-encoded with <see cref="Uri.EscapeDataString(string)"/>.</summary>
internal sealed class QueryBuilder
{
    private readonly List<KeyValuePair<string, string>> _items = new();

    public IReadOnlyList<KeyValuePair<string, string>> Items => _items;

    public QueryBuilder Add(string name, string? value)
    {
        if (value is not null) _items.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    public QueryBuilder Add(string name, double? value) => value is { } v ? Add(name, Fmt.Num(v)) : this;

    public QueryBuilder Add(string name, int? value) => value is { } v ? Add(name, Fmt.Int(v)) : this;

    public QueryBuilder Add(string name, bool? value) => value is { } v ? Add(name, Fmt.Bool(v)) : this;

    public QueryBuilder Add(string name, DateTimeOffset? value) => value is { } v ? Add(name, Fmt.Date(v)) : this;

    public QueryBuilder Add(string name, IEnumerable<string>? values, string separator = ",")
    {
        if (values is null) return this;
        var list = values.ToList();
        return list.Count == 0 ? this : Add(name, string.Join(separator, list));
    }

    public QueryBuilder AddRange(IEnumerable<KeyValuePair<string, string>>? pairs)
    {
        if (pairs is null) return this;
        foreach (var p in pairs) Add(p.Key, p.Value);
        return this;
    }

    public bool Contains(string name) => _items.Any(i => i.Key == name);

    /// <summary>Returns <c>a=b&amp;c=d</c> (no leading <c>?</c>).</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var item in _items)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(item.Key)).Append('=').Append(Uri.EscapeDataString(item.Value));
        }
        return sb.ToString();
    }
}

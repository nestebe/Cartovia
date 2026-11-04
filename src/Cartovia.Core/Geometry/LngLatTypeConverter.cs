using System;
using System.ComponentModel;
using System.Globalization;

namespace Cartovia;

/// <summary>
/// Converts <see cref="LngLat"/> from / to <c>"lng, lat"</c> strings (XAML attributes, property grids, settings).
/// </summary>
public sealed class LngLatTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s) return Parse(s);
        return base.ConvertFrom(context, culture, value);
    }

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is LngLat p)
            return p.ToString();
        return base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>Parses <c>"lng, lat"</c> (invariant culture; separators: comma, semicolon or space).</summary>
    /// <exception cref="FormatException">Invalid text.</exception>
    public static LngLat Parse(string text)
    {
        var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            throw new FormatException($"'{text}' is not a valid \"longitude, latitude\" value.");
        return new LngLat(lng, lat);
    }
}

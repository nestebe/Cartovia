using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;

namespace Mapbox.Net.Services.Internal;

/// <summary>JSON options used to read Mapbox web service responses (snake_case members).</summary>
internal static class ServicesJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new MapboxEnumConverterFactory());
        o.MakeReadOnly();
        return o;
    }

    public static T Deserialize<T>(JsonElement element) where T : class
        => element.Deserialize<T>(Options) ?? throw new JsonException($"The Mapbox response could not be read as {typeof(T).Name}.");

    public static string? GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Reads a feature property, or <c>default</c> when absent or not convertible.</summary>
    public static T? GetProperty<T>(Feature feature, string name)
    {
        if (!feature.Properties.TryGetPropertyValue(name, out var node) || node is null) return default;
        try { return node.Deserialize<T>(Options); }
        catch (JsonException) { return default; }
        catch (InvalidOperationException) { return default; }
    }

    /// <summary>Reads a sub-object property as a <see cref="JsonObject"/>.</summary>
    public static JsonObject? GetObject(Feature feature, string name)
        => feature.Properties.TryGetPropertyValue(name, out var node) ? node as JsonObject : null;

    /// <summary>Serializes a GeoJSON object.</summary>
    public static string ToJson(GeoJsonObject value) => JsonSerializer.Serialize(value, typeof(GeoJsonObject), Options);
}

/// <summary>Enum helpers (honour <see cref="JsonValueAttribute"/>, kebab-case otherwise).</summary>
internal static class EnumText
{
    private static readonly ConcurrentDictionary<(Type, ulong), string> Cache = new();

    /// <summary>API string of a single enum value.</summary>
    public static string Of<T>(T value) where T : struct, Enum
    {
        var raw = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        return Cache.GetOrAdd((typeof(T), raw), _ =>
        {
            var name = Enum.GetName(typeof(T), value) ?? value.ToString();
            var field = typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field?.GetCustomAttribute<JsonValueAttribute>()?.Value ?? KebabCaseNamingPolicy.Instance.ConvertName(name);
        });
    }

    /// <summary>API strings of every single-bit flag set in <paramref name="value"/>, in declaration order.</summary>
    public static List<string> Flags<T>(T value) where T : struct, Enum
    {
        var result = new List<string>();
        ulong v = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        foreach (T f in Enum.GetValues(typeof(T)))
        {
            ulong fv = Convert.ToUInt64(f, CultureInfo.InvariantCulture);
            if (fv != 0 && (fv & (fv - 1)) == 0 && (v & fv) == fv) result.Add(Of(f));
        }
        return result;
    }
}

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Mapbox.Net.Json;

/// <summary>
/// Central JSON configuration used for every message exchanged with the map engine.
/// camelCase property names, nulls omitted, enums written in kebab-case (as in the style specification).
/// </summary>
public static class MapboxJson
{
    /// <summary>Shared serializer options. Do not mutate.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new MapboxEnumConverterFactory());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Serializes any value with <see cref="Options"/>.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Serializes any value to a <see cref="JsonNode"/> with <see cref="Options"/>.</summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => element.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText()),
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), Options),
    };

    /// <summary>Deserializes a <see cref="JsonElement"/> with <see cref="Options"/>.</summary>
    public static T? Deserialize<T>(JsonElement element)
        => element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? default : element.Deserialize<T>(Options);

    /// <summary>Deserializes a JSON string with <see cref="Options"/>.</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    internal static string ToKebabCase(string name) => KebabCaseNamingPolicy.Instance.ConvertName(name);

    /// <summary>Returns the JSON string used for an enum value (honours <see cref="JsonValueAttribute"/>).</summary>
    public static string EnumToString<TEnum>(TEnum value) where TEnum : struct, Enum => EnumMap<TEnum>.ToJson(value);
}

/// <summary>Converts PascalCase / camelCase names to kebab-case (<c>TopLeft</c> → <c>top-left</c>).</summary>
public sealed class KebabCaseNamingPolicy : JsonNamingPolicy
{
    /// <summary>Singleton instance.</summary>
    public static KebabCaseNamingPolicy Instance { get; } = new();

    /// <inheritdoc />
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool prevIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool prevIsUpper = i > 0 && char.IsUpper(name[i - 1]);
                if (i > 0 && (prevIsLowerOrDigit || (prevIsUpper && nextIsLower))) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == '_')
            {
                sb.Append('-');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Explicit JSON value for an enum member when the automatic kebab-case conversion is not suitable.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class JsonValueAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public JsonValueAttribute(string value) => Value = value;

    /// <summary>The literal JSON string.</summary>
    public string Value { get; }
}

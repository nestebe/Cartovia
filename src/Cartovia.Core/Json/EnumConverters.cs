using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cartovia.Json;

internal static class EnumMap<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToJsonMap = new();
    private static readonly Dictionary<string, TEnum> FromJsonMap = new(StringComparer.OrdinalIgnoreCase);

    static EnumMap()
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (TEnum)field.GetValue(null)!;
            var name = field.GetCustomAttribute<JsonValueAttribute>()?.Value
                       ?? KebabCaseNamingPolicy.Instance.ConvertName(field.Name);
            ToJsonMap[value] = name;
            FromJsonMap[name] = value;
            FromJsonMap[field.Name] = value;
        }
    }

    public static string ToJson(TEnum value) => ToJsonMap.TryGetValue(value, out var s) ? s : value.ToString();

    public static bool TryParse(string text, out TEnum value) => FromJsonMap.TryGetValue(text, out value);
}

/// <summary>Serializes enums as kebab-case strings (or the value of <see cref="JsonValueAttribute"/>).</summary>
public sealed class KebabEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(KebabEnumConverter<>).MakeGenericType(typeToConvert))!;
}

/// <summary>Kebab-case enum converter for a single enum type.</summary>
public sealed class KebabEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString() ?? string.Empty;
            if (EnumMap<TEnum>.TryParse(text, out var value)) return value;
            throw new JsonException($"Unknown value '{text}' for enum {typeof(TEnum).Name}.");
        }
        if (reader.TokenType == JsonTokenType.Number)
            return (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt64());
        throw new JsonException($"Unexpected token {reader.TokenType} for enum {typeof(TEnum).Name}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(EnumMap<TEnum>.ToJson(value));
}

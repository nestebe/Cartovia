using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Cartovia.Json;

namespace Cartovia.Style;

/// <summary>
/// Base class of every strongly-typed style property value. A value is either a constant
/// (number, color, string…) or an <see cref="Expression"/> (data-driven / zoom-driven styling).
/// </summary>
public abstract class StyleValue
{
    private readonly JsonNode? _node;

    /// <summary>Creates a value from its JSON representation.</summary>
    protected StyleValue(JsonNode? node) => _node = node;

    /// <summary>True when the value is an expression rather than a constant.</summary>
    public bool IsExpression => _node is JsonArray;

    /// <summary>Returns a copy of the JSON representation sent to the map engine.</summary>
    public JsonNode? ToJsonNode() => _node?.DeepClone();

    /// <inheritdoc />
    public override string ToString() => _node?.ToJsonString() ?? "null";

    internal static JsonNode Num(double v) => JsonValue.Create(v);
}

/// <summary>A number property value (constant or expression).</summary>
public sealed class NumberValue : StyleValue
{
    private NumberValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator NumberValue(double v) => new(Num(v));
    /// <summary>Constant.</summary>
    public static implicit operator NumberValue(int v) => new(Num(v));
    /// <summary>Expression.</summary>
    public static implicit operator NumberValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>A boolean property value (constant or expression).</summary>
public sealed class BoolValue : StyleValue
{
    private BoolValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator BoolValue(bool v) => new(JsonValue.Create(v));
    /// <summary>Expression.</summary>
    public static implicit operator BoolValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>A string property value (constant or expression).</summary>
public sealed class StringValue : StyleValue
{
    private StringValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator StringValue(string v) => new(JsonValue.Create(v));
    /// <summary>Expression.</summary>
    public static implicit operator StringValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>
/// A formatted text value: a plain string, a token string such as <c>"{name}"</c> or a <c>format</c> expression.
/// </summary>
public sealed class FormattedValue : StyleValue
{
    private FormattedValue(JsonNode? n) : base(n) { }
    /// <summary>Constant (may contain <c>{field}</c> tokens).</summary>
    public static implicit operator FormattedValue(string v) => new(JsonValue.Create(v));
    /// <summary>Expression.</summary>
    public static implicit operator FormattedValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>An image name (sprite / added image id) or an <c>image</c> expression.</summary>
public sealed class ImageValue : StyleValue
{
    private ImageValue(JsonNode? n) : base(n) { }
    /// <summary>Image id (may contain <c>{field}</c> tokens).</summary>
    public static implicit operator ImageValue(string v) => new(JsonValue.Create(v));
    /// <summary>Expression.</summary>
    public static implicit operator ImageValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>
/// A color value. Accepts any CSS color string (<c>"#3b82f6"</c>, <c>"rgba(0,0,0,0.5)"</c>, <c>"red"</c>…),
/// a <see cref="System.Drawing.Color"/>, or an expression.
/// </summary>
public sealed class ColorValue : StyleValue
{
    private ColorValue(JsonNode? n) : base(n) { }
    /// <summary>CSS color string.</summary>
    public static implicit operator ColorValue(string css) => new(JsonValue.Create(css));
    /// <summary>System.Drawing color.</summary>
    public static implicit operator ColorValue(System.Drawing.Color c) => new(JsonValue.Create(ToCss(c)));
    /// <summary>Expression.</summary>
    public static implicit operator ColorValue(Expression e) => new(e.ToJsonNode());

    /// <summary>Converts a <see cref="System.Drawing.Color"/> to a CSS <c>rgba()</c> string.</summary>
    public static string ToCss(System.Drawing.Color c)
        => string.Create(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{Math.Round(c.A / 255.0, 3)})");
}

/// <summary>An enum property value (constant or expression).</summary>
public sealed class EnumValue<TEnum> : StyleValue where TEnum : struct, Enum
{
    private EnumValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator EnumValue<TEnum>(TEnum v) => new(JsonValue.Create(CartoviaJson.EnumToString(v)));
    /// <summary>Expression.</summary>
    public static implicit operator EnumValue<TEnum>(Expression e) => new(e.ToJsonNode());
}

/// <summary>An array of numbers (offsets, translations, dash arrays, paddings…).</summary>
public sealed class NumberArrayValue : StyleValue
{
    private NumberArrayValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator NumberArrayValue(double[] v) => new(new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()));
    /// <summary>Constant (2 values, e.g. an offset).</summary>
    public static implicit operator NumberArrayValue((double, double) v) => new(new JsonArray(Num(v.Item1), Num(v.Item2)));
    /// <summary>Constant (3 values).</summary>
    public static implicit operator NumberArrayValue((double, double, double) v) => new(new JsonArray(Num(v.Item1), Num(v.Item2), Num(v.Item3)));
    /// <summary>Constant (4 values).</summary>
    public static implicit operator NumberArrayValue((double, double, double, double) v) => new(new JsonArray(Num(v.Item1), Num(v.Item2), Num(v.Item3), Num(v.Item4)));
    /// <summary>Expression.</summary>
    public static implicit operator NumberArrayValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>An array of strings (e.g. font stacks).</summary>
public sealed class StringArrayValue : StyleValue
{
    private StringArrayValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator StringArrayValue(string[] v) => new(new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()));
    /// <summary>Expression.</summary>
    public static implicit operator StringArrayValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>An array of enum values (e.g. <c>text-variable-anchor</c>).</summary>
public sealed class EnumArrayValue<TEnum> : StyleValue where TEnum : struct, Enum
{
    private EnumArrayValue(JsonNode? n) : base(n) { }
    /// <summary>Constant.</summary>
    public static implicit operator EnumArrayValue<TEnum>(TEnum[] v)
        => new(new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(CartoviaJson.EnumToString(x))).ToArray()));
    /// <summary>Expression.</summary>
    public static implicit operator EnumArrayValue<TEnum>(Expression e) => new(e.ToJsonNode());
}

/// <summary>Any JSON value, for properties whose type is not modelled.</summary>
public sealed class AnyValue : StyleValue
{
    private AnyValue(JsonNode? n) : base(n) { }
    /// <summary>Creates a value from any serializable object.</summary>
    public static AnyValue From(object? value) => new(value is StyleValue sv ? sv.ToJsonNode() : CartoviaJson.ToNode(value));
    /// <summary>JSON node.</summary>
    public static implicit operator AnyValue(JsonNode? n) => new(n?.DeepClone());
    /// <summary>Number.</summary>
    public static implicit operator AnyValue(double v) => new(Num(v));
    /// <summary>String.</summary>
    public static implicit operator AnyValue(string v) => new(JsonValue.Create(v));
    /// <summary>Boolean.</summary>
    public static implicit operator AnyValue(bool v) => new(JsonValue.Create(v));
    /// <summary>Expression.</summary>
    public static implicit operator AnyValue(Expression e) => new(e.ToJsonNode());
}

/// <summary>Transition (animation) applied when a paint property changes.</summary>
public sealed record StyleTransition(TimeSpan Duration, TimeSpan Delay = default)
{
    /// <summary>Creates a transition from milliseconds.</summary>
    public static StyleTransition FromMilliseconds(double duration, double delay = 0)
        => new(TimeSpan.FromMilliseconds(duration), TimeSpan.FromMilliseconds(delay));

    /// <summary>JSON representation (<c>{"duration": ms, "delay": ms}</c>).</summary>
    public JsonObject ToJsonNode() => new()
    {
        ["duration"] = Duration.TotalMilliseconds,
        ["delay"] = Delay.TotalMilliseconds,
    };
}

internal static class StyleValueHelper
{
    /// <summary>Converts anything acceptable as a style value to JSON.</summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        StyleValue sv => sv.ToJsonNode(),
        Expression e => e.ToJsonNode(),
        StyleTransition t => t.ToJsonNode(),
        System.Drawing.Color c => JsonValue.Create(ColorValue.ToCss(c)),
        Enum en => CartoviaJson.ToNode(en),
        _ => CartoviaJson.ToNode(value),
    };

    public static void SetOrRemove(IDictionary<string, object?> bag, string name, object? value)
    {
        if (value is null) bag.Remove(name); else bag[name] = value;
    }
}

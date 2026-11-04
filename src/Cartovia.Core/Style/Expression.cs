using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Cartovia.Json;

namespace Cartovia.Style;

/// <summary>
/// A style expression (<c>["operator", arg1, arg2, …]</c>). Build expressions with the <see cref="Expr"/> helpers,
/// or parse raw JSON with <see cref="Parse"/>.
/// </summary>
public sealed class Expression
{
    private readonly JsonArray _array;

    private Expression(JsonArray array) => _array = array;

    /// <summary>Creates an expression from an operator name and its arguments.</summary>
    /// <param name="op">Operator name (e.g. <c>"get"</c>, <c>"interpolate"</c>).</param>
    /// <param name="args">Arguments: numbers, strings, booleans, expressions, arrays, colors, style values, JSON nodes…</param>
    public static Expression Create(string op, params object?[] args)
    {
        var arr = new JsonArray { JsonValue.Create(op) };
        foreach (var a in args) arr.Add(ToArg(a));
        return new Expression(arr);
    }

    /// <summary>Parses a raw JSON expression such as <c>["get", "name"]</c>.</summary>
    public static Expression Parse(string json)
        => JsonNode.Parse(json) is JsonArray a ? new Expression(a) : throw new FormatException("An expression must be a JSON array.");

    /// <summary>Wraps an existing JSON array.</summary>
    public static Expression FromJson(JsonArray array) => new((JsonArray)array.DeepClone());

    /// <summary>The operator name.</summary>
    public string Operator => _array[0]?.GetValue<string>() ?? string.Empty;

    /// <summary>Returns a copy of the JSON array.</summary>
    public JsonArray ToJsonNode() => (JsonArray)_array.DeepClone();

    /// <summary>JSON text of the expression.</summary>
    public string ToJson() => _array.ToJsonString();

    /// <inheritdoc />
    public override string ToString() => ToJson();

    internal static JsonNode? ToArg(object? a) => a switch
    {
        null => null,
        Expression e => e.ToJsonNode(),
        StyleValue sv => sv.ToJsonNode(),
        JsonNode n => n.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        decimal m => JsonValue.Create(m),
        System.Drawing.Color c => JsonValue.Create(ColorValue.ToCss(c)),
        Enum en => CartoviaJson.ToNode(en),
        // Plain arrays inside an expression must be wrapped in a "literal".
        System.Collections.IEnumerable seq when a is not string => new JsonArray(
            JsonValue.Create("literal"),
            new JsonArray(seq.Cast<object?>().Select(ToArg).ToArray())),
        _ => CartoviaJson.ToNode(a),
    };

    // ---- Operators for natural C# composition ----

    /// <summary><c>["+", a, b]</c></summary>
    public static Expression operator +(Expression a, object b) => Create("+", a, b);
    /// <summary><c>["-", a, b]</c></summary>
    public static Expression operator -(Expression a, object b) => Create("-", a, b);
    /// <summary><c>["*", a, b]</c></summary>
    public static Expression operator *(Expression a, object b) => Create("*", a, b);
    /// <summary><c>["/", a, b]</c></summary>
    public static Expression operator /(Expression a, object b) => Create("/", a, b);
    /// <summary><c>["%", a, b]</c></summary>
    public static Expression operator %(Expression a, object b) => Create("%", a, b);
    /// <summary><c>["!", a]</c></summary>
    public static Expression operator !(Expression a) => Create("!", a);
}

/// <summary>A (input, output) pair used by <c>interpolate</c>, <c>step</c>, <c>match</c> and <c>case</c>.</summary>
public readonly record struct Stop(object Input, object? Output)
{
    /// <summary>Tuple conversion.</summary>
    public static implicit operator Stop((double Input, object? Output) t) => new(t.Input, t.Output);
    /// <summary>Tuple conversion.</summary>
    public static implicit operator Stop((int Input, object? Output) t) => new(t.Input, t.Output);
    /// <summary>Tuple conversion.</summary>
    public static implicit operator Stop((string Input, object? Output) t) => new(t.Input, t.Output);
    /// <summary>Tuple conversion.</summary>
    public static implicit operator Stop((Expression Input, object? Output) t) => new(t.Input, t.Output);
    /// <summary>Tuple conversion (several match labels).</summary>
    public static implicit operator Stop((string[] Input, object? Output) t) => new(t.Input, t.Output);
    /// <summary>Tuple conversion (several match labels).</summary>
    public static implicit operator Stop((double[] Input, object? Output) t) => new(t.Input, t.Output);
}

/// <summary>Interpolation type for <see cref="Expr.Interpolate(Interpolation, Expression, Stop[])"/>.</summary>
public sealed class Interpolation
{
    private readonly object?[] _json;
    private Interpolation(params object?[] json) => _json = json;

    /// <summary>Linear interpolation.</summary>
    public static Interpolation Linear { get; } = new("linear");
    /// <summary>Exponential interpolation with the given base.</summary>
    public static Interpolation Exponential(double @base) => new("exponential", @base);
    /// <summary>Cubic bézier interpolation.</summary>
    public static Interpolation CubicBezier(double x1, double y1, double x2, double y2) => new("cubic-bezier", x1, y1, x2, y2);

    internal JsonArray ToJson() => new(_json.Select(Expression.ToArg).ToArray());
}

/// <summary>
/// Expression builders. Hand-written helpers with typed signatures live here; every other operator
/// of the style specification is generated (see <c>Expr.g.cs</c>) and takes raw arguments.
/// </summary>
public static partial class Expr
{
    /// <summary>Generic operator: <c>[op, args…]</c>.</summary>
    public static Expression Op(string op, params object?[] args) => Expression.Create(op, args);

    /// <summary>Feature property: <c>["get", name]</c>.</summary>
    public static Expression Get(string property) => Expression.Create("get", property);

    /// <summary>Property of an object: <c>["get", name, obj]</c>.</summary>
    public static Expression Get(string property, Expression @object) => Expression.Create("get", property, @object);

    /// <summary>Feature has property: <c>["has", name]</c>.</summary>
    public static Expression Has(string property) => Expression.Create("has", property);

    /// <summary>Feature state value: <c>["feature-state", name]</c>.</summary>
    public static Expression FeatureState(string key) => Expression.Create("feature-state", key);

    /// <summary>Wraps an array or object literal: <c>["literal", value]</c>.</summary>
    public static Expression Literal(object value)
    {
        var node = value switch
        {
            System.Collections.IEnumerable seq when value is not string => new JsonArray(seq.Cast<object?>().Select(Expression.ToArg).ToArray()),
            _ => Expression.ToArg(value),
        };
        return Expression.Create("literal", node);
    }

    /// <summary>Current zoom level: <c>["zoom"]</c>.</summary>
    public static Expression Zoom() => Expression.Create("zoom");

    /// <summary>Equality: <c>["==", a, b]</c>.</summary>
    public static Expression Equal(object? a, object? b) => Expression.Create("==", a, b);
    /// <summary>Inequality: <c>["!=", a, b]</c>.</summary>
    public static Expression NotEqual(object? a, object? b) => Expression.Create("!=", a, b);
    /// <summary><c>["&gt;", a, b]</c></summary>
    public static Expression GreaterThan(object a, object b) => Expression.Create(">", a, b);
    /// <summary><c>["&gt;=", a, b]</c></summary>
    public static Expression GreaterOrEqual(object a, object b) => Expression.Create(">=", a, b);
    /// <summary><c>["&lt;", a, b]</c></summary>
    public static Expression LessThan(object a, object b) => Expression.Create("<", a, b);
    /// <summary><c>["&lt;=", a, b]</c></summary>
    public static Expression LessOrEqual(object a, object b) => Expression.Create("<=", a, b);
    /// <summary>Logical AND: <c>["all", …]</c>.</summary>
    public static Expression All(params Expression[] conditions) => Expression.Create("all", conditions.Cast<object?>().ToArray());
    /// <summary>Logical OR: <c>["any", …]</c>.</summary>
    public static Expression Any(params Expression[] conditions) => Expression.Create("any", conditions.Cast<object?>().ToArray());
    /// <summary>Logical NOT: <c>["!", condition]</c>.</summary>
    public static Expression Not(Expression condition) => Expression.Create("!", condition);

    /// <summary>
    /// Membership test: <c>["in", needle, ["literal", [values…]]]</c>.
    /// </summary>
    public static Expression In(object needle, params object[] values)
        => Expression.Create("in", needle, Literal(values));

    /// <summary>Geometry type of the feature: <c>["geometry-type"]</c>.</summary>
    public static Expression GeometryType() => Expression.Create("geometry-type");

    /// <summary>Feature id: <c>["id"]</c>.</summary>
    public static Expression Id() => Expression.Create("id");

    /// <summary>
    /// Continuous interpolation: <c>["interpolate", type, input, stop1, out1, stop2, out2…]</c>.
    /// </summary>
    /// <example><code>Expr.Interpolate(Interpolation.Linear, Expr.Zoom(), (5, 2), (15, 12))</code></example>
    public static Expression Interpolate(Interpolation type, Expression input, params Stop[] stops)
        => Expression.Create("interpolate", Flatten(new object?[] { type.ToJson(), input }, stops));

    /// <summary>Linear interpolation over the zoom level.</summary>
    public static Expression InterpolateZoom(params Stop[] stops) => Interpolate(Interpolation.Linear, Zoom(), stops);

    /// <summary>Interpolation in HCL color space.</summary>
    public static Expression InterpolateHcl(Interpolation type, Expression input, params Stop[] stops)
        => Expression.Create("interpolate-hcl", Flatten(new object?[] { type.ToJson(), input }, stops));

    /// <summary>Interpolation in CIELAB color space.</summary>
    public static Expression InterpolateLab(Interpolation type, Expression input, params Stop[] stops)
        => Expression.Create("interpolate-lab", Flatten(new object?[] { type.ToJson(), input }, stops));

    /// <summary>Stepped (discrete) values: <c>["step", input, base, stop1, out1, …]</c>.</summary>
    public static Expression Step(Expression input, object baseOutput, params Stop[] stops)
        => Expression.Create("step", Flatten(new[] { input, baseOutput }, stops));

    /// <summary>
    /// Pattern matching: <c>["match", input, label1, out1, label2, out2, …, fallback]</c>.
    /// Use <c>string[]</c> / <c>double[]</c> inputs to match several labels.
    /// </summary>
    public static Expression Match(Expression input, object fallback, params Stop[] cases)
    {
        var args = new List<object?> { input };
        foreach (var c in cases)
        {
            // Labels must be literal values (not wrapped in "literal").
            args.Add(c.Input switch
            {
                string[] s => new JsonArray(s.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                double[] d => new JsonArray(d.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                _ => c.Input,
            });
            args.Add(c.Output);
        }
        args.Add(fallback);
        return Expression.Create("match", args.ToArray());
    }

    /// <summary>Conditional: <c>["case", cond1, out1, cond2, out2, …, fallback]</c>.</summary>
    public static Expression Case(object fallback, params (Expression Condition, object Output)[] cases)
    {
        var args = new List<object?>();
        foreach (var (cond, output) in cases) { args.Add(cond); args.Add(output); }
        args.Add(fallback);
        return Expression.Create("case", args.ToArray());
    }

    /// <summary>First non-null value: <c>["coalesce", …]</c>.</summary>
    public static Expression Coalesce(params object[] values) => Expression.Create("coalesce", values);

    /// <summary>String concatenation: <c>["concat", …]</c>.</summary>
    public static Expression Concat(params object[] values) => Expression.Create("concat", values);

    /// <summary>Color from components: <c>["rgba", r, g, b, a]</c>.</summary>
    public static Expression Rgba(object r, object g, object b, object a) => Expression.Create("rgba", r, g, b, a);

    /// <summary>Color from components: <c>["rgb", r, g, b]</c>.</summary>
    public static Expression Rgb(object r, object g, object b) => Expression.Create("rgb", r, g, b);

    /// <summary>Converts to number: <c>["to-number", value, fallbacks…]</c>.</summary>
    public static Expression ToNumber(object value, params object[] fallbacks)
        => Expression.Create("to-number", new[] { value }.Concat(fallbacks).ToArray());

    /// <summary>Converts to string: <c>["to-string", value]</c>.</summary>
    public static Expression ToStringOf(object value) => Expression.Create("to-string", value);

    /// <summary>Binds variables: <c>["let", name1, value1, …, body]</c>.</summary>
    public static Expression Let(Expression body, params (string Name, object Value)[] bindings)
    {
        var args = new List<object?>();
        foreach (var (n, v) in bindings) { args.Add(n); args.Add(v); }
        args.Add(body);
        return Expression.Create("let", args.ToArray());
    }

    /// <summary>References a variable bound by <see cref="Let"/>: <c>["var", name]</c>.</summary>
    public static Expression Var(string name) => Expression.Create("var", name);

    /// <summary>Image reference: <c>["image", name]</c>.</summary>
    public static Expression Image(object name) => Expression.Create("image", name);

    /// <summary>Formatted text section.</summary>
    public sealed record FormatSection(object Content, double? FontScale = null, string[]? TextFont = null, object? TextColor = null);

    /// <summary>Formatted text: <c>["format", content1, {options}, content2, {options}…]</c>.</summary>
    public static Expression Format(params FormatSection[] sections)
    {
        var args = new List<object?>();
        foreach (var s in sections)
        {
            args.Add(s.Content);
            var opts = new JsonObject();
            if (s.FontScale is { } fs) opts["font-scale"] = fs;
            if (s.TextFont is { } tf) opts["text-font"] = Expression.ToArg(tf);
            if (s.TextColor is { } tc) opts["text-color"] = Expression.ToArg(tc);
            args.Add(opts);
        }
        return Expression.Create("format", args.ToArray());
    }

    private static object?[] Flatten(object?[] head, Stop[] stops)
    {
        var list = new List<object?>(head);
        foreach (var s in stops) { list.Add(s.Input); list.Add(s.Output); }
        return list.ToArray();
    }
}

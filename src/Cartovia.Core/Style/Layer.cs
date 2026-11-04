using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Cartovia.Style;

/// <summary>
/// Base class of every style layer. Strongly-typed subclasses (<see cref="CircleLayer"/>, <see cref="LineLayer"/>,
/// <see cref="SymbolLayer"/>, <see cref="FillExtrusionLayer"/>…) are generated from the official style
/// specifications and expose every paint and layout property. Any property can also be set by name with
/// <see cref="SetPaint"/> / <see cref="SetLayout"/>.
/// </summary>
public abstract partial class Layer
{
    /// <summary>Creates a layer.</summary>
    protected Layer(string id, string type)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A layer id is required.", nameof(id));
        Id = id;
        Type = type;
    }

    /// <summary>Unique layer id.</summary>
    public string Id { get; }

    /// <summary>Style-specification layer type (<c>fill</c>, <c>line</c>, <c>symbol</c>…).</summary>
    public string Type { get; }

    /// <summary>Id of the source used by the layer.</summary>
    public string? Source { get; set; }

    /// <summary>Layer of a vector tile source to use.</summary>
    public string? SourceLayer { get; set; }

    /// <summary>Minimum zoom level at which the layer is visible.</summary>
    public double? MinZoom { get; set; }

    /// <summary>Maximum zoom level (exclusive) at which the layer is visible.</summary>
    public double? MaxZoom { get; set; }

    /// <summary>Filter expression selecting the features to render.</summary>
    public Expression? Filter { get; set; }

    /// <summary>
    /// Slot of a Mapbox Standard style in which the layer is inserted (<c>bottom</c>, <c>middle</c>, <c>top</c>).
    /// Mapbox GL JS v3+ only.
    /// </summary>
    public string? Slot { get; set; }

    /// <summary>Arbitrary metadata stored with the layer.</summary>
    public JsonObject? Metadata { get; set; }

    /// <summary>Whether the layer is displayed.</summary>
    public EnumValue<Visibility>? Visibility
    {
        get => LayoutProperties.TryGetValue("visibility", out var v) ? v as EnumValue<Visibility> : null;
        set => StyleValueHelper.SetOrRemove(LayoutProperties, "visibility", value);
    }

    /// <summary>Additional top-level members merged into the layer JSON (for engine-specific extensions).</summary>
    public JsonObject Extra { get; } = new();

    /// <summary>Paint properties, by style-specification name.</summary>
    protected internal Dictionary<string, object?> PaintProperties { get; } = new(StringComparer.Ordinal);

    /// <summary>Layout properties, by style-specification name.</summary>
    protected internal Dictionary<string, object?> LayoutProperties { get; } = new(StringComparer.Ordinal);

    /// <summary>Sets a paint property by name (value: constant, <see cref="Expression"/>, <see cref="StyleValue"/>…).</summary>
    public Layer SetPaint(string name, object? value)
    {
        StyleValueHelper.SetOrRemove(PaintProperties, name, value);
        return this;
    }

    /// <summary>Sets a layout property by name (value: constant, <see cref="Expression"/>, <see cref="StyleValue"/>…).</summary>
    public Layer SetLayout(string name, object? value)
    {
        StyleValueHelper.SetOrRemove(LayoutProperties, name, value);
        return this;
    }

    /// <summary>Paint properties currently set, serialized to JSON.</summary>
    public JsonObject GetPaintJson() => ToObject(PaintProperties);

    /// <summary>Layout properties currently set, serialized to JSON.</summary>
    public JsonObject GetLayoutJson() => ToObject(LayoutProperties);

    /// <summary>Serializes the layer to its style-specification JSON.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["id"] = Id,
            ["type"] = Type,
        };
        if (Source is not null) o["source"] = Source;
        if (SourceLayer is not null) o["source-layer"] = SourceLayer;
        if (MinZoom is { } min) o["minzoom"] = min;
        if (MaxZoom is { } max) o["maxzoom"] = max;
        if (Filter is not null) o["filter"] = Filter.ToJsonNode();
        if (Slot is not null) o["slot"] = Slot;
        if (Metadata is not null) o["metadata"] = Metadata.DeepClone();
        if (LayoutProperties.Count > 0) o["layout"] = GetLayoutJson();
        if (PaintProperties.Count > 0) o["paint"] = GetPaintJson();
        foreach (var kv in Extra) o[kv.Key] = kv.Value?.DeepClone();
        return o;
    }

    /// <inheritdoc />
    public override string ToString() => ToJson().ToJsonString();

    private static JsonObject ToObject(Dictionary<string, object?> bag)
    {
        var o = new JsonObject();
        foreach (var kv in bag) o[kv.Key] = StyleValueHelper.ToNode(kv.Value);
        return o;
    }
}

/// <summary>A layer of any type, configured only through <see cref="Layer.SetPaint"/> and <see cref="Layer.SetLayout"/>.</summary>
public sealed class RawLayer : Layer
{
    /// <summary>Creates a raw layer.</summary>
    public RawLayer(string id, string type, string? source = null) : base(id, type) => Source = source;
}

/// <summary>Base class for style objects made of named properties (fog, terrain, lights…).</summary>
public abstract class StyleObject
{
    /// <summary>Properties by style-specification name.</summary>
    protected internal Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Sets any property by name.</summary>
    public StyleObject Set(string name, object? value)
    {
        StyleValueHelper.SetOrRemove(Properties, name, value);
        return this;
    }

    /// <summary>Serializes the object.</summary>
    public virtual JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var kv in Properties) o[kv.Key] = StyleValueHelper.ToNode(kv.Value);
        return o;
    }
}

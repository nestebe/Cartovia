using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;
using Mapbox.Net.Style;

namespace Mapbox.Net.Bridge;

/// <summary>Converts .NET values to the JSON sent to the map engine.</summary>
public static class BridgeJson
{
    /// <summary>
    /// Converts a value: layers, sources, style objects, expressions, style values, GeoJSON,
    /// geometry structs, enums, JSON nodes, dictionaries, anonymous objects…
    /// </summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        JsonElement e => e.ValueKind is JsonValueKind.Undefined ? null : JsonNode.Parse(e.GetRawText()),
        Layer l => l.ToJson(),
        Source s => s.ToJson(),
        StyleObject so => so.ToJson(),
        Expression ex => ex.ToJsonNode(),
        StyleValue sv => sv.ToJsonNode(),
        StyleTransition t => t.ToJsonNode(),
        GeoJsonObject g => JsonNode.Parse(g.ToJson()),
        MapPadding p => new JsonObject { ["top"] = p.Top, ["right"] = p.Right, ["bottom"] = p.Bottom, ["left"] = p.Left },
        System.Drawing.Color c => JsonValue.Create(ColorValue.ToCss(c)),
        TimeSpan ts => JsonValue.Create(ts.TotalMilliseconds),
        object?[] arr => new JsonArray(arr.Select(ToNode).ToArray()),
        _ => MapboxJson.ToNode(value),
    };

    /// <summary>Converts call arguments to a JSON array.</summary>
    public static JsonArray ToArray(IEnumerable<object?> values) => new(values.Select(ToNode).ToArray());
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;

namespace Mapbox.Net.Events;

/// <summary>Base arguments of every map event. <see cref="Data"/> holds the raw payload.</summary>
public class MapEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapEventArgs(string name, JsonElement data)
    {
        Name = name;
        Data = data;
    }

    /// <summary>Engine event name (<c>click</c>, <c>moveend</c>…).</summary>
    public string Name { get; }

    /// <summary>Raw event payload.</summary>
    public JsonElement Data { get; }

    internal static double? Num(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    internal static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    internal static T? Get<T>(JsonElement e, string name) where T : struct
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is not JsonValueKind.Null
            ? v.Deserialize<T>(MapboxJson.Options)
            : null;
}

/// <summary>Mouse buttons.</summary>
public enum MouseButton
{
    /// <summary>No button / unknown.</summary>
    None = -1,
    /// <summary>Left button.</summary>
    Left = 0,
    /// <summary>Middle button.</summary>
    Middle = 1,
    /// <summary>Right button.</summary>
    Right = 2,
}

/// <summary>Arguments of mouse events (click, dblclick, contextmenu, mousemove…), on the map or on a layer.</summary>
public sealed class MapMouseEventArgs : MapEventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapMouseEventArgs(string name, JsonElement data, string? layerId = null) : base(name, data)
    {
        LayerId = layerId;
        LngLat = Get<LngLat>(data, "lngLat") ?? default;
        Point = Get<ScreenPoint>(data, "point") ?? default;
        var b = Num(data, "button");
        Button = b is null ? MouseButton.None : (MouseButton)(int)b.Value;
        AltKey = Bool(data, "altKey");
        CtrlKey = Bool(data, "ctrlKey");
        ShiftKey = Bool(data, "shiftKey");
        WheelDelta = Num(data, "deltaY");
        Features = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(QueriedFeature.FromJson).ToList()
            : Array.Empty<QueriedFeature>();
    }

    /// <summary>Geographic position of the pointer.</summary>
    public LngLat LngLat { get; }

    /// <summary>Pixel position of the pointer relative to the map.</summary>
    public ScreenPoint Point { get; }

    /// <summary>Button involved in the event.</summary>
    public MouseButton Button { get; }

    /// <summary>Alt key pressed.</summary>
    public bool AltKey { get; }

    /// <summary>Ctrl key pressed.</summary>
    public bool CtrlKey { get; }

    /// <summary>Shift key pressed.</summary>
    public bool ShiftKey { get; }

    /// <summary>Wheel delta (wheel events only).</summary>
    public double? WheelDelta { get; }

    /// <summary>Layer the event is bound to (layer events only).</summary>
    public string? LayerId { get; }

    /// <summary>Features under the pointer (layer events only).</summary>
    public IReadOnlyList<QueriedFeature> Features { get; }

    /// <summary>First feature under the pointer, if any.</summary>
    public QueriedFeature? Feature => Features.Count > 0 ? Features[0] : null;
}

/// <summary>Arguments of camera events (move, zoom, rotate, pitch, drag…).</summary>
public sealed class MapCameraEventArgs : MapEventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapCameraEventArgs(string name, JsonElement data) : base(name, data)
    {
        Camera = CameraState.FromJson(data);
        IsUserInitiated = Bool(data, "userInitiated");
    }

    /// <summary>Camera at the time of the event.</summary>
    public CameraState Camera { get; }

    /// <summary>True when the movement comes from user interaction rather than from code.</summary>
    public bool IsUserInitiated { get; }
}

/// <summary>Arguments of the <c>error</c> event.</summary>
public sealed class MapErrorEventArgs : MapEventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapErrorEventArgs(string name, JsonElement data, bool isFatal = false) : base(name, data)
    {
        Message = Str(data, "message") ?? "Unknown error";
        Status = Num(data, "status") is { } s ? (int)s : null;
        Url = Str(data, "url");
        SourceId = Str(data, "sourceId");
        IsFatal = isFatal;
    }

    /// <summary>Error message.</summary>
    public string Message { get; }

    /// <summary>HTTP status, for network errors.</summary>
    public int? Status { get; }

    /// <summary>Failing URL, for network errors (may contain the access token).</summary>
    public string? Url { get; }

    /// <summary>Source involved, if any.</summary>
    public string? SourceId { get; }

    /// <summary>True when the map could not be created at all.</summary>
    public bool IsFatal { get; }
}

/// <summary>Arguments of data events (<c>data</c>, <c>sourcedata</c>, <c>styledata</c>…).</summary>
public sealed class MapDataEventArgs : MapEventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapDataEventArgs(string name, JsonElement data) : base(name, data)
    {
        DataType = Str(data, "dataType");
        SourceId = Str(data, "sourceId");
        IsSourceLoaded = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("isSourceLoaded", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;
        SourceDataType = Str(data, "sourceDataType");
    }

    /// <summary><c>source</c> or <c>style</c>.</summary>
    public string? DataType { get; }
    /// <summary>Source id, for source events.</summary>
    public string? SourceId { get; }
    /// <summary>Whether all tiles of the source are loaded.</summary>
    public bool? IsSourceLoaded { get; }
    /// <summary><c>metadata</c>, <c>content</c>, <c>visibility</c>…</summary>
    public string? SourceDataType { get; }
}

/// <summary>Arguments of <c>styleimagemissing</c>: add the image with <c>AddImageAsync</c> to fix it.</summary>
public sealed class StyleImageMissingEventArgs : MapEventArgs
{
    /// <summary>Creates the arguments.</summary>
    public StyleImageMissingEventArgs(string name, JsonElement data) : base(name, data) => ImageId = Str(data, "id") ?? string.Empty;

    /// <summary>Id of the missing image.</summary>
    public string ImageId { get; }
}

/// <summary>Severity of a page log message.</summary>
public enum MapLogLevel
{
    /// <summary>Information.</summary>
    Info,
    /// <summary>Warning.</summary>
    Warning,
    /// <summary>Error.</summary>
    Error,
}

/// <summary>A log message coming from the page (script errors, bridge warnings).</summary>
public sealed class MapLogEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapLogEventArgs(MapLogLevel level, string message) { Level = level; Message = message; }
    /// <summary>Severity.</summary>
    public MapLogLevel Level { get; }
    /// <summary>Message.</summary>
    public string Message { get; }
}

/// <summary>Camera position.</summary>
public sealed record CameraState(LngLat Center, double Zoom, double Bearing, double Pitch, LngLatBounds? Bounds)
{
    internal static CameraState FromJson(JsonElement e) => new(
        MapEventArgs.Get<LngLat>(e, "center") ?? default,
        MapEventArgs.Num(e, "zoom") ?? 0,
        MapEventArgs.Num(e, "bearing") ?? 0,
        MapEventArgs.Num(e, "pitch") ?? 0,
        MapEventArgs.Get<LngLatBounds>(e, "bounds"));
}

/// <summary>A feature returned by a query or a layer event, with its rendering context.</summary>
public sealed class QueriedFeature : Feature
{
    /// <summary>Id of the layer that rendered the feature.</summary>
    public string? LayerId { get; private set; }
    /// <summary>Type of the layer that rendered the feature.</summary>
    public string? LayerType { get; private set; }
    /// <summary>Source id.</summary>
    public string? Source { get; private set; }
    /// <summary>Source layer (vector sources).</summary>
    public string? SourceLayer { get; private set; }
    /// <summary>Feature state.</summary>
    public JsonObject? State { get; private set; }
    /// <summary>Featureset namespace (Mapbox Standard interactions).</summary>
    public string? Namespace { get; private set; }

    /// <summary>Parses a feature sent by the bridge.</summary>
    public static QueriedFeature FromJson(JsonElement e)
    {
        var f = new QueriedFeature();
        if (e.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object && g.TryGetProperty("type", out _))
        {
            try { f.Geometry = GeoJsonObject.Parse<Geometry>(g.GetRawText()); } catch (JsonException) { /* unsupported geometry */ }
        }
        if (e.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
            f.Properties = JsonNode.Parse(p.GetRawText())!.AsObject();
        if (e.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            f.Id = JsonNode.Parse(id.GetRawText());
        if (e.TryGetProperty("layer", out var l) && l.ValueKind == JsonValueKind.Object)
        {
            f.LayerId = MapEventArgs.Str(l, "id");
            f.LayerType = MapEventArgs.Str(l, "type");
        }
        f.Source = MapEventArgs.Str(e, "source");
        f.SourceLayer = MapEventArgs.Str(e, "sourceLayer");
        f.Namespace = MapEventArgs.Str(e, "namespace");
        if (e.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object)
            f.State = JsonNode.Parse(s.GetRawText())!.AsObject();
        return f;
    }
}

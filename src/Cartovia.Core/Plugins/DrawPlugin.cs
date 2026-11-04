using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Controls;
using Cartovia.GeoJson;
using Cartovia.Json;

namespace Cartovia.Plugins;

/// <summary>Drawing modes of <see cref="DrawPlugin"/>.</summary>
public static class DrawModes
{
    /// <summary>Select / move features.</summary>
    public const string SimpleSelect = "simple_select";
    /// <summary>Edit the vertices of a feature.</summary>
    public const string DirectSelect = "direct_select";
    /// <summary>Draw a point.</summary>
    public const string DrawPoint = "draw_point";
    /// <summary>Draw a line.</summary>
    public const string DrawLineString = "draw_line_string";
    /// <summary>Draw a polygon.</summary>
    public const string DrawPolygon = "draw_polygon";
    /// <summary>Static (no interaction; requires a custom mode registration).</summary>
    public const string Static = "static";
}

/// <summary>Buttons displayed by <see cref="DrawPlugin"/>.</summary>
public sealed class DrawControls
{
    /// <summary>Point button.</summary>
    public bool? Point { get; set; }
    /// <summary>Line button.</summary>
    public bool? LineString { get; set; }
    /// <summary>Polygon button.</summary>
    public bool? Polygon { get; set; }
    /// <summary>Trash button.</summary>
    public bool? Trash { get; set; }
    /// <summary>Combine button.</summary>
    public bool? CombineFeatures { get; set; }
    /// <summary>Uncombine button.</summary>
    public bool? UncombineFeatures { get; set; }
}

/// <summary>Arguments of draw events.</summary>
public sealed class DrawEventArgs : EventArgs
{
    internal DrawEventArgs(string eventName, JsonElement data)
    {
        EventName = eventName;
        Data = data;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array)
            Features = f.EnumerateArray().Select(x => GeoJsonObject.Parse<Feature>(x.GetRawText())).ToList();
        else
            Features = Array.Empty<Feature>();
        Mode = Events.MapEventArgs.Str(data, "mode");
        Action = Events.MapEventArgs.Str(data, "action");
    }

    /// <summary>Event name (<c>create</c>, <c>update</c>, <c>delete</c>…).</summary>
    public string EventName { get; }
    /// <summary>Features concerned.</summary>
    public IReadOnlyList<Feature> Features { get; }
    /// <summary>New mode (<c>modechange</c>).</summary>
    public string? Mode { get; }
    /// <summary>Update action: <c>move</c> or <c>change_coordinates</c> (<c>update</c>).</summary>
    public string? Action { get; }
    /// <summary>Raw payload.</summary>
    public JsonElement Data { get; }
}

/// <summary>
/// Drawing and editing of points, lines and polygons (mapbox-gl-draw). Works with both engines.
/// </summary>
public sealed class DrawPlugin : MapPlugin
{
    /// <summary>Creates the plugin.</summary>
    public DrawPlugin(ControlPosition? position = ControlPosition.TopLeft) => Position = position;

    /// <inheritdoc />
    public override string Name => "draw";

    /// <summary>Show all buttons by default. Default: true.</summary>
    public bool? DisplayControlsDefault { get; set; }
    /// <summary>Individual buttons.</summary>
    public DrawControls? Controls { get; set; }
    /// <summary>Initial mode.</summary>
    public string? DefaultMode { get; set; }
    /// <summary>Keyboard shortcuts. Default: true.</summary>
    public bool? KeyBindings { get; set; }
    /// <summary>Shift + drag box selection. Default: true.</summary>
    public bool? BoxSelect { get; set; }
    /// <summary>Click tolerance in pixels.</summary>
    public double? ClickBuffer { get; set; }
    /// <summary>Expose user properties to the draw styles (<c>user_*</c>).</summary>
    public bool? UserProperties { get; set; }
    /// <summary>Custom draw style layers (array of layer JSON).</summary>
    public JsonArray? Styles { get; set; }
    /// <summary>Raw options merged into the MapboxDraw options.</summary>
    public JsonObject AdditionalOptions { get; } = new();

    /// <summary>Script URL.</summary>
    public string ScriptUrl { get; set; } = PluginAssets.DrawScript;
    /// <summary>Stylesheet URL.</summary>
    public string StylesheetUrl { get; set; } = PluginAssets.DrawStylesheet;

    /// <summary>Features were created.</summary>
    public event EventHandler<DrawEventArgs>? Created;
    /// <summary>Features were deleted.</summary>
    public event EventHandler<DrawEventArgs>? Deleted;
    /// <summary>Features were moved or reshaped.</summary>
    public event EventHandler<DrawEventArgs>? Updated;
    /// <summary>The selection changed.</summary>
    public event EventHandler<DrawEventArgs>? SelectionChanged;
    /// <summary>The mode changed.</summary>
    public event EventHandler<DrawEventArgs>? ModeChanged;
    /// <summary>Features were combined.</summary>
    public event EventHandler<DrawEventArgs>? Combined;
    /// <summary>Features were uncombined.</summary>
    public event EventHandler<DrawEventArgs>? Uncombined;

    /// <summary>Every drawn feature.</summary>
    public async Task<FeatureCollection> GetAllAsync(CancellationToken cancellationToken = default)
        => GeoJsonObject.Parse<FeatureCollection>((await InvokeAsync("getAll", cancellationToken).ConfigureAwait(false)).GetRawText());

    /// <summary>A feature by id, or null.</summary>
    public async Task<Feature?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var json = await InvokeAsync("get", cancellationToken, id).ConfigureAwait(false);
        return json.ValueKind == JsonValueKind.Object ? GeoJsonObject.Parse<Feature>(json.GetRawText()) : null;
    }

    /// <summary>Adds features (geometry, feature or collection) and returns their ids.</summary>
    public async Task<IReadOnlyList<string>> AddAsync(GeoJsonObject geoJson, CancellationToken cancellationToken = default)
        => await InvokeAsync<List<string>>("add", cancellationToken, geoJson).ConfigureAwait(false) ?? new List<string>();

    /// <summary>Replaces every feature and returns the ids.</summary>
    public async Task<IReadOnlyList<string>> SetAsync(FeatureCollection features, CancellationToken cancellationToken = default)
        => await InvokeAsync<List<string>>("set", cancellationToken, features).ConfigureAwait(false) ?? new List<string>();

    /// <summary>Deletes features.</summary>
    public Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        => InvokeAsync("delete", cancellationToken, ids.ToArray());

    /// <summary>Deletes every feature.</summary>
    public Task DeleteAllAsync(CancellationToken cancellationToken = default) => InvokeAsync("deleteAll", cancellationToken);

    /// <summary>Selected features.</summary>
    public async Task<FeatureCollection> GetSelectedAsync(CancellationToken cancellationToken = default)
        => GeoJsonObject.Parse<FeatureCollection>((await InvokeAsync("getSelected", cancellationToken).ConfigureAwait(false)).GetRawText());

    /// <summary>Ids of the selected features.</summary>
    public async Task<IReadOnlyList<string>> GetSelectedIdsAsync(CancellationToken cancellationToken = default)
        => await InvokeAsync<List<string>>("getSelectedIds", cancellationToken).ConfigureAwait(false) ?? new List<string>();

    /// <summary>Ids of the features at a pixel position.</summary>
    public async Task<IReadOnlyList<string>> GetFeatureIdsAtAsync(ScreenPoint point, CancellationToken cancellationToken = default)
        => await InvokeAsync<List<string>>("getFeatureIdsAt", cancellationToken, point).ConfigureAwait(false) ?? new List<string>();

    /// <summary>Changes the mode (see <see cref="DrawModes"/>).</summary>
    /// <param name="mode">Mode name.</param>
    /// <param name="options">Mode options, e.g. <c>new { featureId = "…" }</c> for <c>direct_select</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ChangeModeAsync(string mode, object? options = null, CancellationToken cancellationToken = default)
        => InvokeAsync("changeMode", cancellationToken, mode, options);

    /// <summary>Current mode.</summary>
    public async Task<string?> GetModeAsync(CancellationToken cancellationToken = default)
        => await InvokeAsync<string>("getMode", cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the selection (like the trash button).</summary>
    public Task TrashAsync(CancellationToken cancellationToken = default) => InvokeAsync("trash", cancellationToken);
    /// <summary>Combines the selected features.</summary>
    public Task CombineFeaturesAsync(CancellationToken cancellationToken = default) => InvokeAsync("combineFeatures", cancellationToken);
    /// <summary>Splits the selected multi-features.</summary>
    public Task UncombineFeaturesAsync(CancellationToken cancellationToken = default) => InvokeAsync("uncombineFeatures", cancellationToken);

    /// <summary>Sets a property of a drawn feature.</summary>
    public Task SetFeaturePropertyAsync(string featureId, string property, object? value, CancellationToken cancellationToken = default)
        => InvokeAsync("setFeatureProperty", cancellationToken, featureId, property, value);

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine) => new[] { ScriptUrl };
    /// <inheritdoc />
    protected internal override IEnumerable<string> GetStylesheets(MapEngine engine) => new[] { StylesheetUrl };

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions(MapEngine engine)
    {
        var d = new JsonObject();
        Put(d, "displayControlsDefault", DisplayControlsDefault);
        if (Controls is { } c)
        {
            var co = new JsonObject();
            Put(co, "point", c.Point);
            Put(co, "line_string", c.LineString);
            Put(co, "polygon", c.Polygon);
            Put(co, "trash", c.Trash);
            Put(co, "combine_features", c.CombineFeatures);
            Put(co, "uncombine_features", c.UncombineFeatures);
            d["controls"] = co;
        }
        Put(d, "defaultMode", DefaultMode);
        Put(d, "keybindings", KeyBindings);
        Put(d, "boxSelect", BoxSelect);
        Put(d, "clickBuffer", ClickBuffer);
        Put(d, "userProperties", UserProperties);
        Put(d, "styles", Styles);
        foreach (var kv in AdditionalOptions) d[kv.Key] = kv.Value?.DeepClone();
        return new JsonObject { ["drawOptions"] = d };
    }

    /// <inheritdoc />
    protected override void OnEvent(string eventName, JsonElement data)
    {
        var args = new DrawEventArgs(eventName, data);
        (eventName switch
        {
            "create" => Created,
            "delete" => Deleted,
            "update" => Updated,
            "selectionchange" => SelectionChanged,
            "modechange" => ModeChanged,
            "combine" => Combined,
            "uncombine" => Uncombined,
            _ => null,
        })?.Invoke(this, args);
        base.OnEvent(eventName, data);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Controls;
using Mapbox.Net.Events;
using Mapbox.Net.GeoJson;

namespace Mapbox.Net.Plugins;

/// <summary>A geocoding request made by the search box (custom providers).</summary>
public sealed class GeocoderQuery
{
    internal GeocoderQuery(JsonElement data)
    {
        Text = MapEventArgs.Str(data, "query") ?? (data.TryGetProperty("query", out var q) ? q.ToString() : string.Empty);
        Limit = MapEventArgs.Num(data, "limit") is { } l ? (int)l : null;
        Language = data.TryGetProperty("language", out var lang) && lang.ValueKind != JsonValueKind.Null ? lang.ToString() : null;
        if (data.TryGetProperty("bbox", out var b) && b.ValueKind == JsonValueKind.Array && b.GetArrayLength() == 4)
            BBox = new LngLatBounds(b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble());
        if (data.TryGetProperty("proximity", out var p) && p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty("longitude", out var lon) && p.TryGetProperty("latitude", out var lat))
            Proximity = new LngLat(lon.GetDouble(), lat.GetDouble());
    }

    /// <summary>Search text (or "lng,lat" for reverse geocoding).</summary>
    public string Text { get; }
    /// <summary>Maximum number of results.</summary>
    public int? Limit { get; }
    /// <summary>Requested language(s).</summary>
    public string? Language { get; }
    /// <summary>Bounding box restriction.</summary>
    public LngLatBounds? BBox { get; }
    /// <summary>Bias results around this position.</summary>
    public LngLat? Proximity { get; }
}

/// <summary>A result returned by a custom geocoding provider.</summary>
/// <param name="PlaceName">Full display name.</param>
/// <param name="Position">Coordinates.</param>
/// <param name="Text">Short name. Null: <paramref name="PlaceName"/>.</param>
/// <param name="BBox">Optional bounds used to frame the result.</param>
/// <param name="Properties">Optional properties.</param>
public sealed record GeocoderResult(string PlaceName, LngLat Position, string? Text = null, LngLatBounds? BBox = null, object? Properties = null)
{
    internal JsonObject ToCarmen() => new()
    {
        ["type"] = "Feature",
        ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(Position.Longitude, Position.Latitude) },
        ["center"] = new JsonArray(Position.Longitude, Position.Latitude),
        ["place_name"] = PlaceName,
        ["text"] = Text ?? PlaceName,
        ["place_type"] = new JsonArray("place"),
        ["properties"] = Properties is null ? new JsonObject() : Bridge.BridgeJson.ToNode(Properties),
        ["bbox"] = BBox is { } b ? new JsonArray(b.West, b.South, b.East, b.North) : null,
    };
}

/// <summary>Arguments of <see cref="GeocoderPlugin.ResultSelected"/>.</summary>
public sealed class GeocoderResultEventArgs : EventArgs
{
    internal GeocoderResultEventArgs(JsonElement result)
    {
        Raw = result;
        PlaceName = MapEventArgs.Str(result, "place_name") ?? MapEventArgs.Str(result, "text") ?? string.Empty;
        if (result.TryGetProperty("center", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 2)
            Position = new LngLat(c[0].GetDouble(), c[1].GetDouble());
        else if (result.TryGetProperty("geometry", out var g) && g.TryGetProperty("coordinates", out var gc) && gc.ValueKind == JsonValueKind.Array)
            Position = new LngLat(gc[0].GetDouble(), gc[1].GetDouble());
        try { Feature = GeoJsonObject.Parse<Feature>(result.GetRawText()); } catch (JsonException) { Feature = null; }
    }

    /// <summary>Display name of the result.</summary>
    public string PlaceName { get; }
    /// <summary>Result position.</summary>
    public LngLat? Position { get; }
    /// <summary>Result as a GeoJSON feature.</summary>
    public Feature? Feature { get; }
    /// <summary>Raw result (Carmen GeoJSON).</summary>
    public JsonElement Raw { get; }
}

/// <summary>
/// Search box control. With the Mapbox engine it uses the Mapbox Geocoding API (mapbox-gl-geocoder).
/// With MapLibre it uses maplibre-gl-geocoder with either OpenStreetMap Nominatim (default, subject to its usage policy)
/// or your own provider (<see cref="SearchProvider"/>), e.g. Mapbox.Net.Services or any other geocoding service.
/// </summary>
public sealed class GeocoderPlugin : MapPlugin
{
    /// <summary>Creates the plugin.</summary>
    public GeocoderPlugin(ControlPosition? position = ControlPosition.TopLeft) => Position = position;

    /// <inheritdoc />
    public override string Name => "geocoder";

    /// <summary>Placeholder text.</summary>
    public string? Placeholder { get; set; }
    /// <summary>Language(s), e.g. <c>"fr"</c>.</summary>
    public string? Language { get; set; }
    /// <summary>ISO 3166 alpha-2 country codes, comma separated (e.g. <c>"fr,be"</c>).</summary>
    public string? Countries { get; set; }
    /// <summary>Feature types (Mapbox: <c>"address,poi"</c>).</summary>
    public string? Types { get; set; }
    /// <summary>Maximum number of results.</summary>
    public int? Limit { get; set; }
    /// <summary>Restricts the results to bounds.</summary>
    public LngLatBounds? BBox { get; set; }
    /// <summary>Bias results around a position.</summary>
    public LngLat? Proximity { get; set; }
    /// <summary>Fly to the selected result. Default: true.</summary>
    public bool? FlyTo { get; set; }
    /// <summary>Add a marker on the selected result. Default: true.</summary>
    public bool? Marker { get; set; }
    /// <summary>Collapsed until hovered. Default: false.</summary>
    public bool? Collapsed { get; set; }
    /// <summary>Minimum characters before searching. Default: 2.</summary>
    public int? MinLength { get; set; }
    /// <summary>Raw options merged into the geocoder options.</summary>
    public JsonObject AdditionalOptions { get; } = new();

    /// <summary>
    /// Custom search provider (MapLibre engine, or Mapbox engine when <see cref="UseProviderWithMapbox"/> is true).
    /// </summary>
    public Func<GeocoderQuery, CancellationToken, Task<IEnumerable<GeocoderResult>>>? SearchProvider { get; set; }

    /// <summary>Custom reverse geocoding provider (called with "lng,lat" queries).</summary>
    public Func<GeocoderQuery, CancellationToken, Task<IEnumerable<GeocoderResult>>>? ReverseProvider { get; set; }

    /// <summary>Use <see cref="SearchProvider"/> instead of the Mapbox Geocoding API with the Mapbox engine.</summary>
    public bool UseProviderWithMapbox { get; set; }

    /// <summary>Nominatim endpoint used by MapLibre when no provider is set.</summary>
    public string? NominatimUrl { get; set; }

    /// <summary>Script URL for the Mapbox engine.</summary>
    public string MapboxScriptUrl { get; set; } = PluginAssets.MapboxGeocoderScript;
    /// <summary>Stylesheet URL for the Mapbox engine.</summary>
    public string MapboxStylesheetUrl { get; set; } = PluginAssets.MapboxGeocoderStylesheet;
    /// <summary>Script URL for the MapLibre engine.</summary>
    public string MapLibreScriptUrl { get; set; } = PluginAssets.MapLibreGeocoderScript;
    /// <summary>Stylesheet URL for the MapLibre engine.</summary>
    public string MapLibreStylesheetUrl { get; set; } = PluginAssets.MapLibreGeocoderStylesheet;

    /// <summary>A result was selected.</summary>
    public event EventHandler<GeocoderResultEventArgs>? ResultSelected;
    /// <summary>Results were received.</summary>
    public event EventHandler<PluginEventArgs>? ResultsReceived;
    /// <summary>The input was cleared.</summary>
    public event EventHandler? Cleared;
    /// <summary>A geocoding error occurred.</summary>
    public event EventHandler<PluginEventArgs>? SearchFailed;

    /// <summary>Searches programmatically (fills the input and shows the results).</summary>
    public Task QueryAsync(string text, CancellationToken cancellationToken = default) => InvokeAsync("query", cancellationToken, text);
    /// <summary>Clears the input.</summary>
    public Task ClearAsync(CancellationToken cancellationToken = default) => InvokeAsync("clear", cancellationToken);
    /// <summary>Sets the input text without searching.</summary>
    public Task SetInputAsync(string text, CancellationToken cancellationToken = default) => InvokeAsync("setInput", cancellationToken, text);
    /// <summary>Changes the proximity bias.</summary>
    public Task SetProximityAsync(LngLat? position, CancellationToken cancellationToken = default) => InvokeAsync("setProximity", cancellationToken, position);
    /// <summary>Changes the language.</summary>
    public Task SetLanguageAsync(string language, CancellationToken cancellationToken = default) => InvokeAsync("setLanguage", cancellationToken, language);
    /// <summary>Changes the placeholder.</summary>
    public Task SetPlaceholderAsync(string text, CancellationToken cancellationToken = default) => InvokeAsync("setPlaceholder", cancellationToken, text);

    private bool UsesMapboxGeocoder(MapEngine engine) => engine == MapEngine.Mapbox && !(UseProviderWithMapbox && SearchProvider is not null);

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine)
        => new[] { UsesMapboxGeocoder(engine) ? MapboxScriptUrl : MapLibreScriptUrl };

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetStylesheets(MapEngine engine)
        => new[] { UsesMapboxGeocoder(engine) ? MapboxStylesheetUrl : MapLibreStylesheetUrl };

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions(MapEngine engine)
    {
        var g = new JsonObject();
        Put(g, "placeholder", Placeholder);
        Put(g, "language", Language);
        Put(g, "countries", Countries);
        Put(g, "types", Types);
        Put(g, "limit", Limit);
        if (BBox is { } b) g["bbox"] = new JsonArray(b.West, b.South, b.East, b.North);
        if (Proximity is { } p) g["proximity"] = new JsonObject { ["longitude"] = p.Longitude, ["latitude"] = p.Latitude };
        Put(g, "flyTo", FlyTo);
        Put(g, "marker", Marker);
        Put(g, "collapsed", Collapsed);
        Put(g, "minLength", MinLength);
        if (!UsesMapboxGeocoder(engine) && ReverseProvider is not null) g["reverseGeocode"] = true;
        foreach (var kv in AdditionalOptions) g[kv.Key] = kv.Value?.DeepClone();
        var o = new JsonObject
        {
            ["geocoderOptions"] = g,
            ["useHostProvider"] = SearchProvider is not null && (engine == MapEngine.MapLibre || UseProviderWithMapbox),
            ["reverse"] = ReverseProvider is not null,
        };
        Put(o, "nominatimUrl", NominatimUrl);
        return o;
    }

    /// <inheritdoc />
    protected internal override async Task<object?> OnHostCallAsync(string method, JsonElement data, CancellationToken cancellationToken)
    {
        var provider = method == "reverse" ? ReverseProvider : SearchProvider;
        if (provider is null) return new JsonObject { ["type"] = "FeatureCollection", ["features"] = new JsonArray() };
        var results = await provider(new GeocoderQuery(data), cancellationToken).ConfigureAwait(true);
        return new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["features"] = new JsonArray(results.Select(r => (JsonNode?)r.ToCarmen()).ToArray()),
        };
    }

    /// <inheritdoc />
    protected override void OnEvent(string eventName, JsonElement data)
    {
        switch (eventName)
        {
            case "result":
                if (data.TryGetProperty("result", out var r)) ResultSelected?.Invoke(this, new GeocoderResultEventArgs(r));
                break;
            case "results": ResultsReceived?.Invoke(this, new PluginEventArgs(eventName, data)); break;
            case "clear": Cleared?.Invoke(this, EventArgs.Empty); break;
            case "error": SearchFailed?.Invoke(this, new PluginEventArgs(eventName, data)); break;
        }
        base.OnEvent(eventName, data);
    }
}

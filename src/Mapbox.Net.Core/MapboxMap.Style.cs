using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Bridge;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;
using Mapbox.Net.Style;

namespace Mapbox.Net;

/// <summary>Options of an image added to the style.</summary>
public sealed class ImageOptions
{
    /// <summary>Pixel ratio of the image (2 for @2x images). Default: 1.</summary>
    public double? PixelRatio { get; set; }
    /// <summary>Signed distance field image: can be recolored with <c>icon-color</c>.</summary>
    public bool? Sdf { get; set; }
    /// <summary>Width in pixels to rasterize the image at (required for SVG without intrinsic size).</summary>
    public int? Width { get; set; }
    /// <summary>Height in pixels to rasterize the image at.</summary>
    public int? Height { get; set; }
    /// <summary>Stretchable horizontal ranges (for <c>icon-text-fit</c>).</summary>
    public double[][]? StretchX { get; set; }
    /// <summary>Stretchable vertical ranges.</summary>
    public double[][]? StretchY { get; set; }
    /// <summary>Content area <c>[x1, y1, x2, y2]</c> for <c>icon-text-fit</c>.</summary>
    public double[]? Content { get; set; }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        if (PixelRatio is { } pr) o["pixelRatio"] = pr;
        if (Sdf is { } sdf) o["sdf"] = sdf;
        if (StretchX is not null) o["stretchX"] = BridgeJson.ToNode(StretchX);
        if (StretchY is not null) o["stretchY"] = BridgeJson.ToNode(StretchY);
        if (Content is not null) o["content"] = BridgeJson.ToNode(Content);
        return o;
    }
}

/// <summary>A 3D light of the Mapbox Standard lighting model (<c>map.setLights</c>).</summary>
public sealed class Light3D
{
    /// <summary>Creates an ambient light.</summary>
    public static Light3D Ambient(string id, AmbientLightOptions properties) => new(id, "ambient", properties);
    /// <summary>Creates a directional light.</summary>
    public static Light3D Directional(string id, DirectionalLightOptions properties) => new(id, "directional", properties);
    /// <summary>Creates a flat light.</summary>
    public static Light3D Flat(string id, FlatLightOptions properties) => new(id, "flat", properties);

    private Light3D(string id, string type, StyleObject properties) { Id = id; Type = type; Properties = properties; }

    /// <summary>Light id.</summary>
    public string Id { get; }
    /// <summary>Light type.</summary>
    public string Type { get; }
    /// <summary>Light properties.</summary>
    public StyleObject Properties { get; }

    internal JsonObject ToJson() => new() { ["id"] = Id, ["type"] = Type, ["properties"] = Properties.ToJson() };
}

public sealed partial class MapboxMap
{
    // ------------------------------------------------------------------ style

    /// <summary>
    /// Changes the style. Sources, layers and images added through the API are re-added automatically
    /// (see <see cref="MapOptions.PreserveUserLayersOnStyleChange"/>).
    /// </summary>
    /// <param name="styleUrl">Style URL (see <see cref="MapStyles"/>).</param>
    /// <param name="diff">Try to apply the change as a diff instead of a full reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SetStyleAsync(string styleUrl, bool diff = true, CancellationToken cancellationToken = default)
        => InvokeAsync("setStyle", cancellationToken, styleUrl, new JsonObject { ["diff"] = diff });

    /// <summary>Changes the style from inline JSON.</summary>
    public Task SetStyleAsync(JsonObject style, bool diff = true, CancellationToken cancellationToken = default)
        => InvokeAsync("setStyle", cancellationToken, style, new JsonObject { ["diff"] = diff });

    /// <summary>Returns the current style as JSON.</summary>
    public async Task<JsonObject?> GetStyleAsync(CancellationToken cancellationToken = default)
        => JsonNode.Parse((await InvokeAsync("getStyle", cancellationToken).ConfigureAwait(false)).GetRawText()) as JsonObject;

    /// <summary>Ids of every layer of the style, bottom to top.</summary>
    public async Task<IReadOnlyList<string>> GetLayerIdsAsync(CancellationToken cancellationToken = default)
        => await CallAsync<List<string>>("map.layerIds", null, cancellationToken).ConfigureAwait(false) ?? new List<string>();

    /// <summary>
    /// Sets a configuration property of an imported style (Mapbox Standard), e.g.
    /// <c>SetConfigPropertyAsync("basemap", "lightPreset", "night")</c>.
    /// </summary>
    public Task SetConfigPropertyAsync(string importId, string name, object? value, CancellationToken cancellationToken = default)
        => InvokeAsync("setConfigProperty", cancellationToken, importId, name, value);

    /// <summary>Reads a configuration property of an imported style (Mapbox Standard).</summary>
    public Task<JsonElement> GetConfigPropertyAsync(string importId, string name, CancellationToken cancellationToken = default)
        => InvokeAsync("getConfigProperty", cancellationToken, importId, name);

    /// <summary>Replaces the configuration of an imported style (Mapbox Standard).</summary>
    public Task SetConfigAsync(string importId, JsonObject config, CancellationToken cancellationToken = default)
        => InvokeAsync("setConfig", cancellationToken, importId, config);

    /// <summary>Changes the map language (Mapbox v3: <c>"fr"</c>, <c>"auto"</c>…).</summary>
    public Task SetLanguageAsync(string? language, CancellationToken cancellationToken = default)
        => InvokeAsync("setLanguage", cancellationToken, language);

    /// <summary>Changes the worldview (Mapbox v3).</summary>
    public Task SetWorldviewAsync(string? worldview, CancellationToken cancellationToken = default)
        => InvokeAsync("setWorldview", cancellationToken, worldview);

    /// <summary>Changes the projection: <c>mercator</c>, <c>globe</c> (both engines), <c>equalEarth</c>, <c>naturalEarth</c>, <c>winkelTripel</c>, <c>albers</c>, <c>lambertConformalConic</c>, <c>equirectangular</c> (Mapbox).</summary>
    public Task SetProjectionAsync(string name, CancellationToken cancellationToken = default)
        => CallAsync("map.projection", new JsonObject { ["projection"] = new JsonObject { ["name"] = name } }, cancellationToken, Readiness.Loaded);

    // ------------------------------------------------------------------ sources

    /// <summary>Adds a source.</summary>
    public Task AddSourceAsync(Source source, CancellationToken cancellationToken = default)
        => InvokeAsync("addSource", cancellationToken, source.Id, source.ToJson());

    /// <summary>Removes a source (its layers must be removed first).</summary>
    public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        => InvokeAsync("removeSource", cancellationToken, sourceId);

    /// <summary>True if the style contains the source.</summary>
    public async Task<bool> HasSourceAsync(string sourceId, CancellationToken cancellationToken = default)
        => (await GetSourceAsync(sourceId, cancellationToken).ConfigureAwait(false)) is not null;

    /// <summary>Serialized definition of a source, or null.</summary>
    public async Task<JsonObject?> GetSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var json = await CallAsync("source.get", new JsonObject { ["id"] = sourceId }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return json.ValueKind == JsonValueKind.Object ? JsonNode.Parse(json.GetRawText()) as JsonObject : null;
    }

    /// <summary>True when all tiles of the source are loaded.</summary>
    public async Task<bool> IsSourceLoadedAsync(string sourceId, CancellationToken cancellationToken = default)
        => (await InvokeAsync("isSourceLoaded", cancellationToken, sourceId).ConfigureAwait(false)).ValueKind == JsonValueKind.True;

    /// <summary>Replaces the data of a GeoJSON source.</summary>
    /// <param name="sourceId">Source id.</param>
    /// <param name="data">A <see cref="GeoJsonObject"/>, a URL, a raw GeoJSON <see cref="JsonNode"/> or any serializable object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SetGeoJsonDataAsync(string sourceId, object data, CancellationToken cancellationToken = default)
        => InvokeSourceAsync(sourceId, "setData", cancellationToken, data);

    /// <summary>
    /// Applies a partial update to a GeoJSON source (<c>updateData</c>): <paramref name="diff"/> follows the engine's
    /// diff format (MapLibre: <c>{add, remove, update}</c>; Mapbox dynamic sources: features to upsert).
    /// </summary>
    public Task UpdateGeoJsonDataAsync(string sourceId, object diff, CancellationToken cancellationToken = default)
        => InvokeSourceAsync(sourceId, "updateData", cancellationToken, diff);

    /// <summary>Calls any method of a JavaScript source object (<c>setTiles</c>, <c>setUrl</c>, <c>updateImage</c>, <c>play</c>…).</summary>
    public Task<JsonElement> InvokeSourceAsync(string sourceId, string method, CancellationToken cancellationToken, params object?[] args)
        => CallAsync("source.invoke", new JsonObject { ["id"] = sourceId, ["method"] = method, ["args"] = BridgeJson.ToArray(args) }, cancellationToken, Readiness.Loaded);

    /// <summary>Changes the tile URLs of a vector / raster source.</summary>
    public Task SetSourceTilesAsync(string sourceId, IEnumerable<string> tiles, CancellationToken cancellationToken = default)
        => InvokeSourceAsync(sourceId, "setTiles", cancellationToken, tiles.ToArray());

    /// <summary>Changes the TileJSON URL of a vector / raster source.</summary>
    public Task SetSourceUrlAsync(string sourceId, string url, CancellationToken cancellationToken = default)
        => InvokeSourceAsync(sourceId, "setUrl", cancellationToken, url);

    /// <summary>Updates the image and / or corners of an image source.</summary>
    public Task UpdateImageSourceAsync(string sourceId, string url, LngLat[]? coordinates = null, CancellationToken cancellationToken = default)
    {
        var o = new JsonObject { ["url"] = url };
        if (coordinates is not null) o["coordinates"] = BridgeJson.ToNode(coordinates);
        return InvokeSourceAsync(sourceId, "updateImage", cancellationToken, o);
    }

    // ------------------------------------------------------------------ layers

    /// <summary>Adds a layer, optionally below another layer.</summary>
    /// <param name="layer">The layer (any generated layer class, or a <see cref="RawLayer"/>).</param>
    /// <param name="beforeLayerId">Id of an existing layer to insert below. Null: on top (or in <see cref="Layer.Slot"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddLayerAsync(Layer layer, string? beforeLayerId = null, CancellationToken cancellationToken = default)
        => InvokeAsync("addLayer", cancellationToken, layer.ToJson(), beforeLayerId);

    /// <summary>Adds a layer from raw style JSON.</summary>
    public Task AddLayerAsync(JsonObject layer, string? beforeLayerId = null, CancellationToken cancellationToken = default)
        => InvokeAsync("addLayer", cancellationToken, layer, beforeLayerId);

    /// <summary>Removes a layer.</summary>
    public Task RemoveLayerAsync(string layerId, CancellationToken cancellationToken = default)
        => InvokeAsync("removeLayer", cancellationToken, layerId);

    /// <summary>Removes a layer if it exists.</summary>
    public async Task<bool> TryRemoveLayerAsync(string layerId, CancellationToken cancellationToken = default)
    {
        if (!await HasLayerAsync(layerId, cancellationToken).ConfigureAwait(false)) return false;
        await RemoveLayerAsync(layerId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Removes a source if it exists.</summary>
    public async Task<bool> TryRemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        if (!await HasSourceAsync(sourceId, cancellationToken).ConfigureAwait(false)) return false;
        await RemoveSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Moves a layer below another one (or to the top when <paramref name="beforeLayerId"/> is null).</summary>
    public Task MoveLayerAsync(string layerId, string? beforeLayerId = null, CancellationToken cancellationToken = default)
        => InvokeAsync("moveLayer", cancellationToken, layerId, beforeLayerId);

    /// <summary>True if the style contains the layer.</summary>
    public async Task<bool> HasLayerAsync(string layerId, CancellationToken cancellationToken = default)
        => (await EvalAsync("return !!map.getLayer(args);", layerId, cancellationToken).ConfigureAwait(false)).ValueKind == JsonValueKind.True;

    /// <summary>Returns the style JSON of a layer, or null.</summary>
    public async Task<JsonObject?> GetLayerAsync(string layerId, CancellationToken cancellationToken = default)
    {
        var json = await EvalAsync(
            "var s = map.getStyle(); var l = s && s.layers ? s.layers.find(function (x) { return x.id === args; }) : null; return l || null;",
            layerId, cancellationToken).ConfigureAwait(false);
        return json.ValueKind == JsonValueKind.Object ? JsonNode.Parse(json.GetRawText()) as JsonObject : null;
    }

    /// <summary>Sets a paint property (value: constant, <see cref="Expression"/>, <see cref="StyleValue"/>, color…).</summary>
    public Task SetPaintPropertyAsync(string layerId, string name, object? value, CancellationToken cancellationToken = default)
        => InvokeAsync("setPaintProperty", cancellationToken, layerId, name, StyleValueHelper.ToNode(value));

    /// <summary>Sets a layout property.</summary>
    public Task SetLayoutPropertyAsync(string layerId, string name, object? value, CancellationToken cancellationToken = default)
        => InvokeAsync("setLayoutProperty", cancellationToken, layerId, name, StyleValueHelper.ToNode(value));

    /// <summary>Reads a paint property.</summary>
    public Task<JsonElement> GetPaintPropertyAsync(string layerId, string name, CancellationToken cancellationToken = default)
        => InvokeAsync("getPaintProperty", cancellationToken, layerId, name);

    /// <summary>Reads a layout property.</summary>
    public Task<JsonElement> GetLayoutPropertyAsync(string layerId, string name, CancellationToken cancellationToken = default)
        => InvokeAsync("getLayoutProperty", cancellationToken, layerId, name);

    /// <summary>Sets (or clears with null) the filter of a layer.</summary>
    public Task SetFilterAsync(string layerId, Expression? filter, CancellationToken cancellationToken = default)
        => InvokeAsync("setFilter", cancellationToken, layerId, filter);

    /// <summary>Sets the zoom range of a layer.</summary>
    public Task SetLayerZoomRangeAsync(string layerId, double minZoom, double maxZoom, CancellationToken cancellationToken = default)
        => InvokeAsync("setLayerZoomRange", cancellationToken, layerId, minZoom, maxZoom);

    /// <summary>Shows or hides a layer.</summary>
    public Task SetLayerVisibilityAsync(string layerId, bool visible, CancellationToken cancellationToken = default)
        => SetLayoutPropertyAsync(layerId, "visibility", visible ? "visible" : "none", cancellationToken);

    /// <summary>
    /// Applies every property currently set on <paramref name="layer"/> to the existing layer with the same id
    /// (paint, layout, filter and zoom range): a convenient way to update a layer from its typed object.
    /// </summary>
    public async Task UpdateLayerAsync(Layer layer, CancellationToken cancellationToken = default)
    {
        await EvalAsync(@"
var l = args;
Object.keys(l.layout || {}).forEach(function (k) { map.setLayoutProperty(l.id, k, l.layout[k]); });
Object.keys(l.paint || {}).forEach(function (k) { map.setPaintProperty(l.id, k, l.paint[k]); });
if (l.filter !== undefined) map.setFilter(l.id, l.filter);
if (l.minzoom !== undefined || l.maxzoom !== undefined) {
  var cur = map.getLayer(l.id);
  map.setLayerZoomRange(l.id, l.minzoom !== undefined ? l.minzoom : (cur.minzoom || 0), l.maxzoom !== undefined ? l.maxzoom : (cur.maxzoom || 24));
}
return null;", layer.ToJson(), cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ images

    /// <summary>Adds an image from a URL (http(s), data URL, SVG…) usable in <c>icon-image</c>, patterns…</summary>
    /// <param name="imageId">Image id.</param>
    /// <param name="url">Image URL.</param>
    /// <param name="options">Image options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddImageAsync(string imageId, string url, ImageOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync("map.addImage", new JsonObject
        {
            ["id"] = imageId,
            ["url"] = url,
            ["width"] = options?.Width,
            ["height"] = options?.Height,
            ["options"] = options?.ToJson() ?? new JsonObject(),
        }, cancellationToken, Readiness.Loaded);

    /// <summary>Adds an image from encoded bytes (PNG, JPEG, WebP, GIF, SVG).</summary>
    /// <param name="imageId">Image id.</param>
    /// <param name="data">Encoded image.</param>
    /// <param name="mimeType">MIME type (<c>image/png</c>, <c>image/svg+xml</c>…).</param>
    /// <param name="options">Image options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddImageAsync(string imageId, byte[] data, string mimeType = "image/png", ImageOptions? options = null, CancellationToken cancellationToken = default)
        => AddImageAsync(imageId, $"data:{mimeType};base64,{Convert.ToBase64String(data)}", options, cancellationToken);

    /// <summary>Adds an image from a stream.</summary>
    public async Task AddImageAsync(string imageId, Stream stream, string mimeType = "image/png", ImageOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        await AddImageAsync(imageId, ms.ToArray(), mimeType, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds an SVG image rendered at the given size.</summary>
    public Task AddSvgImageAsync(string imageId, string svg, int width, int height, ImageOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ImageOptions();
        options.Width ??= width;
        options.Height ??= height;
        return AddImageAsync(imageId, System.Text.Encoding.UTF8.GetBytes(svg), "image/svg+xml", options, cancellationToken);
    }

    /// <summary>Removes an image.</summary>
    public Task RemoveImageAsync(string imageId, CancellationToken cancellationToken = default)
        => InvokeAsync("removeImage", cancellationToken, imageId);

    /// <summary>True if the style contains the image.</summary>
    public async Task<bool> HasImageAsync(string imageId, CancellationToken cancellationToken = default)
        => (await InvokeAsync("hasImage", cancellationToken, imageId).ConfigureAwait(false)).ValueKind == JsonValueKind.True;

    // ------------------------------------------------------------------ 3D, atmosphere, lights

    /// <summary>Enables 3D terrain from a raster-dem source (null disables it).</summary>
    public Task SetTerrainAsync(TerrainOptions? terrain, CancellationToken cancellationToken = default)
        => InvokeAsync("setTerrain", cancellationToken, terrain?.ToJson());

    /// <summary>Sets fog / atmosphere (Mapbox). Null removes it.</summary>
    public Task SetFogAsync(FogOptions? fog, CancellationToken cancellationToken = default)
        => InvokeAsync("setFog", cancellationToken, fog?.ToJson());

    /// <summary>Sets the sky / atmosphere (MapLibre).</summary>
    public Task SetSkyAsync(SkyOptions? sky, CancellationToken cancellationToken = default)
        => InvokeAsync("setSky", cancellationToken, sky?.ToJson());

    /// <summary>Sets the classic global light.</summary>
    public Task SetLightAsync(LightOptions light, CancellationToken cancellationToken = default)
        => InvokeAsync("setLight", cancellationToken, light.ToJson());

    /// <summary>Sets the 3D lights (Mapbox v3). Null or empty restores the style lights.</summary>
    public Task SetLightsAsync(IEnumerable<Light3D>? lights, CancellationToken cancellationToken = default)
        => InvokeAsync("setLights", cancellationToken, lights is null ? null : new JsonArray(lights.Select(l => (JsonNode?)l.ToJson()).ToArray()));

    /// <summary>Sets the snow effect (Mapbox v3.9+). Null removes it.</summary>
    public Task SetSnowAsync(SnowOptions? snow, CancellationToken cancellationToken = default)
        => InvokeAsync("setSnow", cancellationToken, snow?.ToJson());

    /// <summary>Sets the rain effect (Mapbox v3.9+). Null removes it.</summary>
    public Task SetRainAsync(RainOptions? rain, CancellationToken cancellationToken = default)
        => InvokeAsync("setRain", cancellationToken, rain?.ToJson());
}

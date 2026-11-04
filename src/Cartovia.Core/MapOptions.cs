using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Cartovia.Controls;

namespace Cartovia;

/// <summary>Rendering engine running inside the WebView.</summary>
public enum MapEngine
{
    /// <summary>MapLibre GL JS (open source, BSD-3-Clause, no token; use any compatible style / tile provider). Default.</summary>
    MapLibre,

    /// <summary>Mapbox GL JS (requires a Mapbox access token; billed per map load by Mapbox).</summary>
    Mapbox,
}

/// <summary>Default versions and CDN locations of the JavaScript engines and plugins.</summary>
public static class EngineDefaults
{
    /// <summary>Default Mapbox GL JS version.</summary>
    public const string MapboxVersion = "3.34.2";

    /// <summary>Default MapLibre GL JS version (latest UMD build; MapLibre 6+ ships ES modules only).</summary>
    public const string MapLibreVersion = "5.24.0";

    /// <summary>Script URL of a Mapbox GL JS version.</summary>
    public static string MapboxScript(string version) => $"https://api.mapbox.com/mapbox-gl-js/v{version}/mapbox-gl.js";

    /// <summary>Stylesheet URL of a Mapbox GL JS version.</summary>
    public static string MapboxStylesheet(string version) => $"https://api.mapbox.com/mapbox-gl-js/v{version}/mapbox-gl.css";

    /// <summary>Script URL of a MapLibre GL JS version.</summary>
    public static string MapLibreScript(string version) => $"https://unpkg.com/maplibre-gl@{version}/dist/maplibre-gl.js";

    /// <summary>Stylesheet URL of a MapLibre GL JS version.</summary>
    public static string MapLibreStylesheet(string version) => $"https://unpkg.com/maplibre-gl@{version}/dist/maplibre-gl.css";
}

/// <summary>Well-known style URLs.</summary>
public static class MapStyles
{
    /// <summary>Mapbox Standard (3D, light presets, configurable). Mapbox engine.</summary>
    public const string MapboxStandard = "mapbox://styles/mapbox/standard";
    /// <summary>Mapbox Standard Satellite. Mapbox engine.</summary>
    public const string MapboxStandardSatellite = "mapbox://styles/mapbox/standard-satellite";
    /// <summary>Mapbox Streets. Mapbox engine.</summary>
    public const string MapboxStreets = "mapbox://styles/mapbox/streets-v12";
    /// <summary>Mapbox Outdoors. Mapbox engine.</summary>
    public const string MapboxOutdoors = "mapbox://styles/mapbox/outdoors-v12";
    /// <summary>Mapbox Light. Mapbox engine.</summary>
    public const string MapboxLight = "mapbox://styles/mapbox/light-v11";
    /// <summary>Mapbox Dark. Mapbox engine.</summary>
    public const string MapboxDark = "mapbox://styles/mapbox/dark-v11";
    /// <summary>Mapbox Satellite. Mapbox engine.</summary>
    public const string MapboxSatellite = "mapbox://styles/mapbox/satellite-v9";
    /// <summary>Mapbox Satellite Streets. Mapbox engine.</summary>
    public const string MapboxSatelliteStreets = "mapbox://styles/mapbox/satellite-streets-v12";
    /// <summary>Mapbox Navigation Day. Mapbox engine.</summary>
    public const string MapboxNavigationDay = "mapbox://styles/mapbox/navigation-day-v1";
    /// <summary>Mapbox Navigation Night. Mapbox engine.</summary>
    public const string MapboxNavigationNight = "mapbox://styles/mapbox/navigation-night-v1";

    /// <summary>OpenFreeMap Liberty (free, no key). MapLibre engine.</summary>
    public const string OpenFreeMapLiberty = "https://tiles.openfreemap.org/styles/liberty";
    /// <summary>OpenFreeMap Bright (free, no key). MapLibre engine.</summary>
    public const string OpenFreeMapBright = "https://tiles.openfreemap.org/styles/bright";
    /// <summary>OpenFreeMap Positron (free, no key). MapLibre engine.</summary>
    public const string OpenFreeMapPositron = "https://tiles.openfreemap.org/styles/positron";
    /// <summary>MapLibre demo tiles (world countries only). MapLibre engine.</summary>
    public const string MapLibreDemo = "https://demotiles.maplibre.org/style.json";

    /// <summary>A reasonable default style for an engine.</summary>
    public static string DefaultFor(MapEngine engine) => engine == MapEngine.Mapbox ? MapboxStandard : OpenFreeMapLiberty;
}

/// <summary>
/// Options used to create the map. Most values map 1:1 to the options of <c>new mapboxgl.Map(...)</c> /
/// <c>new maplibregl.Map(...)</c>. Anything not modelled can be passed through <see cref="AdditionalMapOptions"/>.
/// </summary>
public sealed class MapOptions
{
    // ---------------- engine & assets ----------------

    /// <summary>Rendering engine. Default: <see cref="MapEngine.MapLibre"/> (no account or token needed).</summary>
    public MapEngine Engine { get; set; } = MapEngine.MapLibre;

    /// <summary>Mapbox access token (required by the Mapbox engine and the Mapbox plugins).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Engine version used to build the default CDN URLs. Null: <see cref="EngineDefaults"/>.</summary>
    public string? EngineVersion { get; set; }

    /// <summary>Overrides the engine script URL (e.g. a local copy for offline use).</summary>
    public string? EngineScriptUrl { get; set; }

    /// <summary>Overrides the engine stylesheet URL.</summary>
    public string? EngineStylesheetUrl { get; set; }

    /// <summary>Additional scripts loaded after the engine (custom plugins, helpers…).</summary>
    public IList<string> AdditionalScripts { get; private set; } = new List<string>();

    /// <summary>Additional stylesheets.</summary>
    public IList<string> AdditionalStylesheets { get; private set; } = new List<string>();

    /// <summary>URL of the RTL text plugin (Arabic / Hebrew shaping). Null to skip.</summary>
    public string? RtlTextPluginUrl { get; set; }

    /// <summary>Number of web workers used by the engine. Null: engine default.</summary>
    public int? WorkerCount { get; set; }

    // ---------------- style ----------------

    /// <summary>Style URL (<c>mapbox://styles/...</c>, <c>https://...style.json</c>). Null: <see cref="MapStyles.DefaultFor"/>.</summary>
    public string? Style { get; set; }

    /// <summary>Inline style JSON. Takes precedence over <see cref="Style"/>.</summary>
    public JsonObject? StyleJson { get; set; }

    /// <summary>
    /// Mapbox Standard configuration per import, e.g. <c>{"basemap": {"lightPreset": "dusk", "showPointOfInterestLabels": false}}</c>.
    /// Mapbox engine only.
    /// </summary>
    public JsonObject? Config { get; set; }

    /// <summary>Map language (e.g. <c>"fr"</c>). Mapbox v3 / Standard styles.</summary>
    public string? Language { get; set; }

    /// <summary>Worldview used for disputed boundaries (e.g. <c>"US"</c>). Mapbox only.</summary>
    public string? Worldview { get; set; }

    /// <summary>Projection name: <c>mercator</c>, <c>globe</c>, <c>equalEarth</c>… (MapLibre: <c>mercator</c> / <c>globe</c>).</summary>
    public string? Projection { get; set; }

    // ---------------- camera ----------------

    /// <summary>Initial center. Default: (0, 0).</summary>
    public LngLat? Center { get; set; }

    /// <summary>Initial zoom. Default: 0.</summary>
    public double? Zoom { get; set; }

    /// <summary>Initial bearing in degrees.</summary>
    public double? Bearing { get; set; }

    /// <summary>Initial pitch in degrees.</summary>
    public double? Pitch { get; set; }

    /// <summary>Initial bounds to fit (overrides center / zoom).</summary>
    public LngLatBounds? Bounds { get; set; }

    /// <summary>Padding used with <see cref="Bounds"/>.</summary>
    public MapPadding? BoundsPadding { get; set; }

    /// <summary>Minimum zoom.</summary>
    public double? MinZoom { get; set; }

    /// <summary>Maximum zoom.</summary>
    public double? MaxZoom { get; set; }

    /// <summary>Minimum pitch.</summary>
    public double? MinPitch { get; set; }

    /// <summary>Maximum pitch.</summary>
    public double? MaxPitch { get; set; }

    /// <summary>Restricts panning to these bounds.</summary>
    public LngLatBounds? MaxBounds { get; set; }

    // ---------------- interaction ----------------

    /// <summary>When false, no mouse / keyboard interaction is handled. Default: true.</summary>
    public bool Interactive { get; set; } = true;

    /// <summary>Scroll wheel zoom. Default: true.</summary>
    public bool ScrollZoom { get; set; } = true;
    /// <summary>Shift + drag box zoom. Default: true.</summary>
    public bool BoxZoom { get; set; } = true;
    /// <summary>Right-click / ctrl + drag rotation. Default: true.</summary>
    public bool DragRotate { get; set; } = true;
    /// <summary>Drag to pan. Default: true.</summary>
    public bool DragPan { get; set; } = true;
    /// <summary>Keyboard shortcuts. Default: true.</summary>
    public bool Keyboard { get; set; } = true;
    /// <summary>Double click zoom. Default: true.</summary>
    public bool DoubleClickZoom { get; set; } = true;
    /// <summary>Touch zoom / rotate. Default: true.</summary>
    public bool TouchZoomRotate { get; set; } = true;
    /// <summary>Touch pitch. Default: true.</summary>
    public bool TouchPitch { get; set; } = true;
    /// <summary>Requires Ctrl to zoom with the wheel (useful in scrollable forms). Default: false.</summary>
    public bool CooperativeGestures { get; set; }

    // ---------------- rendering ----------------

    /// <summary>Show the attribution control. Default: true (required by most tile providers' terms).</summary>
    public bool AttributionControl { get; set; } = true;

    /// <summary>Custom attribution text or HTML.</summary>
    public string? CustomAttribution { get; set; }

    /// <summary>Position of the Mapbox logo.</summary>
    public ControlPosition? LogoPosition { get; set; }

    /// <summary>Render multiple copies of the world when zoomed out. Default: engine default (true).</summary>
    public bool? RenderWorldCopies { get; set; }

    /// <summary>Antialiasing (MSAA). Default: engine default.</summary>
    public bool? Antialias { get; set; }

    /// <summary>Keep the drawing buffer (required to read the canvas with <c>toDataURL</c>). Default: false.</summary>
    public bool? PreserveDrawingBuffer { get; set; }

    /// <summary>Label fade duration in milliseconds.</summary>
    public double? FadeDuration { get; set; }

    /// <summary>Font family used for CJK glyphs rendered locally (e.g. <c>"'Noto Sans', sans-serif"</c>).</summary>
    public string? LocalIdeographFontFamily { get; set; }

    /// <summary>Collision detection across sources. Default: engine default.</summary>
    public bool? CrossSourceCollisions { get; set; }

    /// <summary>Maximum number of tiles kept in cache.</summary>
    public int? MaxTileCacheSize { get; set; }

    /// <summary>Raw options merged last into the map constructor options (escape hatch for anything not modelled).</summary>
    public JsonObject AdditionalMapOptions { get; private set; } = new();

    // ---------------- controls added at startup ----------------

    /// <summary>Controls added when the map is created.</summary>
    public IList<MapControl> InitialControls { get; private set; } = new List<MapControl>();

    // ---------------- bridge behaviour ----------------

    /// <summary>
    /// Re-adds the sources, layers and images added through the API after <c>SetStyleAsync</c>
    /// (the engines otherwise drop them). Default: true.
    /// </summary>
    public bool PreserveUserLayersOnStyleChange { get; set; } = true;

    /// <summary>Minimum interval between two high-frequency events (move, mousemove, drag…). Default: 16 ms.</summary>
    public TimeSpan EventThrottle { get; set; } = TimeSpan.FromMilliseconds(16);

    /// <summary>Timeout of a call to the map engine. Default: 30 s.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Creates a shallow copy of the options.</summary>
    public MapOptions Clone()
    {
        var c = (MapOptions)MemberwiseClone();
        c.AdditionalScripts = new List<string>(AdditionalScripts);
        c.AdditionalStylesheets = new List<string>(AdditionalStylesheets);
        c.InitialControls = new List<MapControl>(InitialControls);
        c.AdditionalMapOptions = (JsonObject)AdditionalMapOptions.DeepClone();
        c.StyleJson = (JsonObject?)StyleJson?.DeepClone();
        c.Config = (JsonObject?)Config?.DeepClone();
        return c;
    }
}

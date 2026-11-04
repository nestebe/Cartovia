namespace Mapbox.Net.Plugins;

/// <summary>Default versions / CDN URLs of the JavaScript plugins. Each plugin exposes properties to override them.</summary>
public static class PluginAssets
{
    /// <summary>mapbox-gl-draw version.</summary>
    public const string DrawVersion = "1.5.2";
    /// <summary>mapbox-gl-geocoder version.</summary>
    public const string MapboxGeocoderVersion = "5.1.3";
    /// <summary>maplibre-gl-geocoder version.</summary>
    public const string MapLibreGeocoderVersion = "1.9.4";
    /// <summary>mapbox-gl-directions version.</summary>
    public const string DirectionsVersion = "4.3.3";
    /// <summary>Turf.js version.</summary>
    public const string TurfVersion = "7.4.0";

    /// <summary>mapbox-gl-draw script.</summary>
    public static string DrawScript { get; set; } = $"https://unpkg.com/@mapbox/mapbox-gl-draw@{DrawVersion}/dist/mapbox-gl-draw.js";
    /// <summary>mapbox-gl-draw stylesheet.</summary>
    public static string DrawStylesheet { get; set; } = $"https://unpkg.com/@mapbox/mapbox-gl-draw@{DrawVersion}/dist/mapbox-gl-draw.css";
    /// <summary>mapbox-gl-geocoder script.</summary>
    public static string MapboxGeocoderScript { get; set; } = $"https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-geocoder/v{MapboxGeocoderVersion}/mapbox-gl-geocoder.min.js";
    /// <summary>mapbox-gl-geocoder stylesheet.</summary>
    public static string MapboxGeocoderStylesheet { get; set; } = $"https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-geocoder/v{MapboxGeocoderVersion}/mapbox-gl-geocoder.css";
    /// <summary>maplibre-gl-geocoder script.</summary>
    public static string MapLibreGeocoderScript { get; set; } = $"https://unpkg.com/@maplibre/maplibre-gl-geocoder@{MapLibreGeocoderVersion}/dist/maplibre-gl-geocoder.js";
    /// <summary>maplibre-gl-geocoder stylesheet.</summary>
    public static string MapLibreGeocoderStylesheet { get; set; } = $"https://unpkg.com/@maplibre/maplibre-gl-geocoder@{MapLibreGeocoderVersion}/dist/maplibre-gl-geocoder.css";
    /// <summary>mapbox-gl-directions script.</summary>
    public static string DirectionsScript { get; set; } = $"https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-directions/v{DirectionsVersion}/mapbox-gl-directions.js";
    /// <summary>mapbox-gl-directions stylesheet.</summary>
    public static string DirectionsStylesheet { get; set; } = $"https://api.mapbox.com/mapbox-gl-js/plugins/mapbox-gl-directions/v{DirectionsVersion}/mapbox-gl-directions.css";
    /// <summary>Turf.js script.</summary>
    public static string TurfScript { get; set; } = $"https://unpkg.com/@turf/turf@{TurfVersion}/turf.min.js";
}

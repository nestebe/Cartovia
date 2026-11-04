using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.GeoJson;
using Cartovia.Mapbox.Services.Internal;

namespace Cartovia.Mapbox.Services;

/// <summary>Static Images API v1 (<c>/styles/v1/{owner}/{style}/static</c>): renders a map to an image.</summary>
public interface IStaticImagesService
{
    /// <summary>
    /// Builds the image URL. The URL contains the access token: it can be used directly as an image source,
    /// but do not log or share it.
    /// </summary>
    Uri BuildUrl(StaticImageRequest request);

    /// <summary>Downloads the image (PNG for vector styles, JPEG for raster-only styles).</summary>
    Task<byte[]> GetImageAsync(StaticImageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A static image request.</summary>
public sealed class StaticImageRequest
{
    /// <summary>Style as <c>owner/style_id</c> or <c>mapbox://styles/owner/style_id</c>. Default <c>mapbox/streets-v12</c>.</summary>
    public string Style { get; set; } = "mapbox/streets-v12";

    /// <summary>Image width in pixels, 1-1280.</summary>
    public int Width { get; set; } = 600;

    /// <summary>Image height in pixels, 1-1280.</summary>
    public int Height { get; set; } = 400;

    /// <summary>Render at 2x pixel density (<c>@2x</c>).</summary>
    public bool HighDensity { get; set; }

    /// <summary>Map center. When neither <see cref="Center"/> nor <see cref="Bounds"/> is set, the viewport is fitted to the overlays (<c>auto</c>).</summary>
    public LngLat? Center { get; set; }

    /// <summary>Zoom level 0-22 (with <see cref="Center"/>).</summary>
    public double Zoom { get; set; } = 12;

    /// <summary>Bearing 0-360 (with <see cref="Center"/>).</summary>
    public double Bearing { get; set; }

    /// <summary>Pitch 0-60 (with <see cref="Center"/>).</summary>
    public double Pitch { get; set; }

    /// <summary>Viewport bounding box (takes precedence over <see cref="Center"/>).</summary>
    public LngLatBounds? Bounds { get; set; }

    /// <summary>Overlays, drawn in order (later ones on top).</summary>
    public IList<StaticOverlay> Overlays { get; } = new List<StaticOverlay>();

    /// <summary>Padding around the overlays / bounds (<c>padding</c>, only with auto or bounds).</summary>
    public StaticImagePadding? Padding { get; set; }

    /// <summary>Show the attribution (<c>attribution</c>, default true). If false you must attribute elsewhere.</summary>
    public bool? Attribution { get; set; }

    /// <summary>Show the Mapbox logo (<c>logo</c>, default true).</summary>
    public bool? Logo { get; set; }

    /// <summary>Draws the overlays below this style layer (<c>before_layer</c>).</summary>
    public string? BeforeLayer { get; set; }

    /// <summary>Extra raw query parameters (e.g. <c>addlayer</c>, <c>setfilter</c>, <c>layer_id</c>).</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal (string Path, QueryBuilder Query) Build()
    {
        if (Width is < 1 or > 1280) throw new ArgumentOutOfRangeException(nameof(Width), Width, "Width must be between 1 and 1280.");
        if (Height is < 1 or > 1280) throw new ArgumentOutOfRangeException(nameof(Height), Height, "Height must be between 1 and 1280.");

        var style = (Style ?? string.Empty).Trim();
        if (style.StartsWith("mapbox://styles/", StringComparison.OrdinalIgnoreCase)) style = style.Substring("mapbox://styles/".Length);
        var parts = style.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Style must be 'owner/style_id' or 'mapbox://styles/owner/style_id'.", nameof(Style));

        string position;
        if (Bounds is { } b)
            position = "[" + Fmt.BBox(b) + "]";
        else if (Center is { } c)
        {
            position = Fmt.LngLat(c) + "," + Fmt.Num(Math.Round(Zoom, 2));
            if (Bearing != 0 || Pitch != 0) position += "," + Fmt.Num(Bearing);
            if (Pitch != 0) position += "," + Fmt.Num(Pitch);
        }
        else
        {
            if (Overlays.Count == 0) throw new InvalidOperationException("Set Center, Bounds or at least one overlay (auto viewport).");
            position = "auto";
        }

        var sb = new StringBuilder("styles/v1/")
            .Append(Uri.EscapeDataString(parts[0])).Append('/').Append(Uri.EscapeDataString(parts[1])).Append("/static/");
        if (Overlays.Count > 0) sb.Append(string.Join(",", Overlays.Select(o => o.ToUrlSegment()))).Append('/');
        sb.Append(position).Append('/').Append(Fmt.Int(Width)).Append('x').Append(Fmt.Int(Height));
        if (HighDensity) sb.Append("@2x");

        if (Padding is not null && Center is not null && Bounds is null)
            throw new InvalidOperationException("Padding can only be used with Bounds or the auto viewport.");

        var q = new QueryBuilder()
            .Add("padding", Padding?.ToString())
            .Add("attribution", Attribution)
            .Add("logo", Logo)
            .Add("before_layer", BeforeLayer)
            .AddRange(AdditionalParameters);
        return (sb.ToString(), q);
    }
}

/// <summary>Padding in pixels (top, right, bottom, left).</summary>
public sealed record StaticImagePadding(int Top, int Right, int Bottom, int Left)
{
    /// <summary>Same padding on every side.</summary>
    public StaticImagePadding(int all) : this(all, all, all, all) { }

    /// <inheritdoc />
    public override string ToString()
        => Top == Right && Right == Bottom && Bottom == Left
            ? Fmt.Int(Top)
            : string.Join(",", Fmt.Int(Top), Fmt.Int(Right), Fmt.Int(Bottom), Fmt.Int(Left));
}

/// <summary>Base class of static image overlays.</summary>
public abstract class StaticOverlay
{
    private protected StaticOverlay() { }

    /// <summary>Returns the overlay as it appears in the URL path (already percent-encoded).</summary>
    public abstract string ToUrlSegment();

    /// <inheritdoc />
    public override string ToString() => ToUrlSegment();
}

/// <summary>Size of a built-in marker.</summary>
public enum StaticMarkerSize
{
    /// <summary>Small pin (<c>pin-s</c>).</summary>
    Small,
    /// <summary>Large pin (<c>pin-l</c>).</summary>
    Large,
}

/// <summary>A built-in marker: <c>pin-s-label+color(lon,lat)</c>.</summary>
public sealed class StaticMarker : StaticOverlay
{
    /// <summary>Creates a marker.</summary>
    public StaticMarker(LngLat location, string? label = null, string? color = null, StaticMarkerSize size = StaticMarkerSize.Small)
    {
        Location = location;
        Label = label;
        Color = color;
        Size = size;
    }

    /// <summary>Location.</summary>
    public LngLat Location { get; set; }

    /// <summary>Label: a letter a-z, a number 0-99 or a Maki icon name.</summary>
    public string? Label { get; set; }

    /// <summary>Hex color (3 or 6 digits, with or without <c>#</c>).</summary>
    public string? Color { get; set; }

    /// <summary>Pin size.</summary>
    public StaticMarkerSize Size { get; set; }

    /// <inheritdoc />
    public override string ToUrlSegment()
    {
        var sb = new StringBuilder(Size == StaticMarkerSize.Large ? "pin-l" : "pin-s");
        if (!string.IsNullOrWhiteSpace(Label)) sb.Append('-').Append(Uri.EscapeDataString(Label!.Trim().ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(Color)) sb.Append('+').Append(Fmt.Color(Color!, nameof(Color)));
        return sb.Append('(').Append(Fmt.LngLat(Location)).Append(')').ToString();
    }
}

/// <summary>A custom image marker: <c>url-{encoded url}(lon,lat)</c>. The image must be PNG or JPEG, max 1024 px.</summary>
public sealed class StaticCustomMarker : StaticOverlay
{
    /// <summary>Creates a custom marker.</summary>
    public StaticCustomMarker(Uri imageUrl, LngLat location)
    {
        ImageUrl = imageUrl ?? throw new ArgumentNullException(nameof(imageUrl));
        Location = location;
    }

    /// <summary>Public URL of the image.</summary>
    public Uri ImageUrl { get; set; }

    /// <summary>Location.</summary>
    public LngLat Location { get; set; }

    /// <inheritdoc />
    public override string ToUrlSegment()
        => "url-" + Uri.EscapeDataString(ImageUrl.AbsoluteUri) + "(" + Fmt.LngLat(Location) + ")";
}

/// <summary>A GeoJSON overlay: <c>geojson({encoded geojson})</c>. simplestyle-spec properties (<c>stroke</c>, <c>fill</c>, <c>marker-color</c>…) are honoured.</summary>
public sealed class StaticGeoJsonOverlay : StaticOverlay
{
    /// <summary>Creates the overlay.</summary>
    public StaticGeoJsonOverlay(GeoJsonObject geoJson) => GeoJson = geoJson ?? throw new ArgumentNullException(nameof(geoJson));

    /// <summary>The GeoJSON (feature, feature collection or geometry).</summary>
    public GeoJsonObject GeoJson { get; set; }

    /// <inheritdoc />
    public override string ToUrlSegment() => "geojson(" + Uri.EscapeDataString(ServicesJson.ToJson(GeoJson)) + ")";
}

/// <summary>
/// A path overlay: <c>path-{strokeWidth}+{strokeColor}-{strokeOpacity}+{fillColor}-{fillOpacity}({polyline})</c>.
/// The coordinates are encoded as a precision-5 polyline. Close the ring to get a filled polygon.
/// </summary>
public sealed class StaticPath : StaticOverlay
{
    /// <summary>Creates a path.</summary>
    public StaticPath(IEnumerable<LngLat> coordinates)
    {
        Coordinates = (coordinates ?? throw new ArgumentNullException(nameof(coordinates))).ToList();
    }

    /// <summary>Creates a path from a line string.</summary>
    public StaticPath(LineString line) : this((line ?? throw new ArgumentNullException(nameof(line))).Coordinates.Select(p => (LngLat)p)) { }

    /// <summary>Coordinates of the path.</summary>
    public IList<LngLat> Coordinates { get; }

    /// <summary>Stroke width in pixels.</summary>
    public double? StrokeWidth { get; set; }

    /// <summary>Stroke color (hex).</summary>
    public string? StrokeColor { get; set; }

    /// <summary>Stroke opacity 0-1.</summary>
    public double? StrokeOpacity { get; set; }

    /// <summary>Fill color (hex).</summary>
    public string? FillColor { get; set; }

    /// <summary>Fill opacity 0-1.</summary>
    public double? FillOpacity { get; set; }

    /// <inheritdoc />
    public override string ToUrlSegment()
    {
        if (Coordinates.Count < 2) throw new InvalidOperationException("A path requires at least 2 coordinates.");
        var sb = new StringBuilder("path");
        if (StrokeWidth is { } w) sb.Append('-').Append(Fmt.Num(w));
        if (StrokeColor is not null) sb.Append('+').Append(Fmt.Color(StrokeColor, nameof(StrokeColor)));
        if (StrokeOpacity is { } so) sb.Append('-').Append(Fmt.Num(so));
        if (FillColor is not null) sb.Append('+').Append(Fmt.Color(FillColor, nameof(FillColor)));
        if (FillOpacity is { } fo) sb.Append('-').Append(Fmt.Num(fo));
        return sb.Append('(').Append(Uri.EscapeDataString(PolylineCodec.Encode(Coordinates, 5))).Append(')').ToString();
    }
}

internal sealed class StaticImagesService : IStaticImagesService
{
    private readonly MapboxHttp _http;

    public StaticImagesService(MapboxHttp http) => _http = http;

    public Uri BuildUrl(StaticImageRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var (path, query) = request.Build();
        return _http.BuildUri(path, query);
    }

    public Task<byte[]> GetImageAsync(StaticImageRequest request, CancellationToken cancellationToken = default)
        => _http.GetBytesAsync(BuildUrl(request), cancellationToken);
}

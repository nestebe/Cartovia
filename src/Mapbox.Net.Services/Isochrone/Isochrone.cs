using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Isochrone API v1 (<c>/isochrone/v1</c>): areas reachable within given times or distances.</summary>
public interface IIsochroneService
{
    /// <summary>Computes isochrone contours as GeoJSON polygons or lines.</summary>
    Task<IsochroneResponse> GetIsochronesAsync(IsochroneRequest request, CancellationToken cancellationToken = default);
}

/// <summary>An Isochrone API request. Set either <see cref="ContoursMinutes"/> or <see cref="ContoursMeters"/> (1 to 4 values).</summary>
public sealed class IsochroneRequest
{
    /// <summary>Creates a request.</summary>
    public IsochroneRequest(RoutingProfile profile, LngLat center)
    {
        Profile = profile;
        Center = center;
    }

    /// <summary>Routing profile.</summary>
    public RoutingProfile Profile { get; set; }

    /// <summary>Center of the isochrones.</summary>
    public LngLat Center { get; set; }

    /// <summary>Contour times in minutes, increasing, max 60 (<c>contours_minutes</c>).</summary>
    public IList<int> ContoursMinutes { get; set; } = new List<int>();

    /// <summary>Contour distances in meters, increasing, max 100000 (<c>contours_meters</c>).</summary>
    public IList<int> ContoursMeters { get; set; } = new List<int>();

    /// <summary>Hex colors, one per contour, with or without <c>#</c> (<c>contours_colors</c>).</summary>
    public IList<string> ContourColors { get; set; } = new List<string>();

    /// <summary>Return polygons instead of lines (<c>polygons</c>). Defaults to true in this client.</summary>
    public bool? Polygons { get; set; } = true;

    /// <summary>0..1, removes smaller contours (<c>denoise</c>).</summary>
    public double? Denoise { get; set; }

    /// <summary>Douglas-Peucker tolerance in meters (<c>generalize</c>).</summary>
    public double? Generalize { get; set; }

    /// <summary>Road types to avoid (<c>exclude</c>, driving profiles; motorway, toll, ferry, unpaved, cash_only_tolls).</summary>
    public RouteExclusions Exclude { get; set; }

    /// <summary>Departure time (<c>depart_at</c>).</summary>
    public DateTimeOffset? DepartAt { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        bool minutes = ContoursMinutes.Count > 0, meters = ContoursMeters.Count > 0;
        if (minutes == meters)
            throw new ArgumentException("Set exactly one of ContoursMinutes or ContoursMeters.");
        var contours = minutes ? ContoursMinutes : ContoursMeters;
        if (contours.Count > 4) throw new ArgumentException("At most 4 contours are allowed.");
        if (contours.Any(c => c <= 0)) throw new ArgumentException("Contour values must be positive.");
        if (ContourColors.Count > 0 && ContourColors.Count != contours.Count)
            throw new ArgumentException("The number of colors must match the number of contours.", nameof(ContourColors));

        var q = new QueryBuilder()
            .Add(minutes ? "contours_minutes" : "contours_meters", contours.Select(c => Fmt.Int(c)))
            .Add("contours_colors", ContourColors.Select(c => Fmt.Color(c, nameof(ContourColors))))
            .Add("polygons", Polygons)
            .Add("denoise", Denoise)
            .Add("generalize", Generalize);
        if (Exclude != RouteExclusions.None) q.Add("exclude", EnumText.Flags(Exclude));
        q.Add("depart_at", DepartAt).AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>Isochrone API response.</summary>
public sealed class IsochroneResponse
{
    internal IsochroneResponse(FeatureCollection features)
    {
        FeatureCollection = features;
        Contours = features.Features.Select(f => new IsochroneContour(f)).ToList();
    }

    /// <summary>The raw GeoJSON returned by the API, ready to be added as a map source.</summary>
    public FeatureCollection FeatureCollection { get; }

    /// <summary>Typed view of each contour.</summary>
    public IReadOnlyList<IsochroneContour> Contours { get; }
}

/// <summary>One isochrone contour.</summary>
public sealed class IsochroneContour
{
    internal IsochroneContour(Feature feature)
    {
        Feature = feature;
        Contour = ServicesJson.GetProperty<double?>(feature, "contour") ?? 0;
        Metric = ServicesJson.GetProperty<string>(feature, "metric");
        Color = ServicesJson.GetProperty<string>(feature, "color") ?? ServicesJson.GetProperty<string>(feature, "fillColor");
        Opacity = ServicesJson.GetProperty<double?>(feature, "opacity") ?? ServicesJson.GetProperty<double?>(feature, "fillOpacity");
    }

    /// <summary>The underlying GeoJSON feature.</summary>
    public Feature Feature { get; }

    /// <summary>Contour value (minutes or meters, see <see cref="Metric"/>).</summary>
    public double Contour { get; }

    /// <summary><c>time</c> or <c>distance</c>.</summary>
    public string? Metric { get; }

    /// <summary>Contour color (<c>#rrggbb</c>).</summary>
    public string? Color { get; }

    /// <summary>Suggested opacity.</summary>
    public double? Opacity { get; }

    /// <summary>The geometry (Polygon, MultiPolygon or LineString).</summary>
    public Geometry? Geometry => Feature.Geometry;
}

internal sealed class IsochroneService : IIsochroneService
{
    private readonly MapboxHttp _http;

    public IsochroneService(MapboxHttp http) => _http = http;

    public Task<IsochroneResponse> GetIsochronesAsync(IsochroneRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var path = "isochrone/v1/" + NavigationQuery.Profile(request.Profile) + "/" + Fmt.LngLat(request.Center);
        return _http.GetJsonAsync(path, query,
            root => new IsochroneResponse(ServicesJson.Deserialize<FeatureCollection>(root)), cancellationToken);
    }
}

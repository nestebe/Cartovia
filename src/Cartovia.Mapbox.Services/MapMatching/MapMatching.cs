using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.GeoJson;
using Cartovia.Mapbox.Services.Internal;

namespace Cartovia.Mapbox.Services;

/// <summary>Map Matching API v5 (<c>/matching/v5</c>): snaps a GPS trace to the road network.</summary>
public interface IMapMatchingService
{
    /// <summary>Matches 2 to 100 trace points to roads.</summary>
    /// <exception cref="MapboxApiException">HTTP error, or a response code other than <c>Ok</c> (e.g. <c>NoMatch</c>).</exception>
    Task<MapMatchingResponse> MatchAsync(MapMatchingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Rules the matcher may ignore.</summary>
[Flags]
public enum MapMatchingIgnore
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary>Access restrictions.</summary>
    Access = 1,
    /// <summary>One-way restrictions.</summary>
    Oneways = 2,
    /// <summary>Turn restrictions.</summary>
    Restrictions = 4,
}

/// <summary>A GPS trace point.</summary>
public sealed class TracePoint
{
    /// <summary>Creates a trace point.</summary>
    public TracePoint(LngLat location, DateTimeOffset? timestamp = null)
    {
        Location = location;
        Timestamp = timestamp;
    }

    /// <summary>The coordinate.</summary>
    public LngLat Location { get; set; }

    /// <summary>When the point was recorded (<c>timestamps</c>). Improves matching quality.</summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>GPS accuracy in meters, 0-50 (<c>radiuses</c>).</summary>
    public double? Radius { get; set; }

    /// <summary>Side of the road to approach (<c>approaches</c>).</summary>
    public Approach? Approach { get; set; }

    /// <summary>
    /// Whether the point splits the match into legs (<c>waypoints</c>). Defaults to true; set false to make it a
    /// silent point. The first and last points are always waypoints.
    /// </summary>
    public bool IsWaypoint { get; set; } = true;

    /// <summary>Custom waypoint name (<c>waypoint_names</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Implicit conversion from a coordinate.</summary>
    public static implicit operator TracePoint(LngLat location) => new(location);
}

/// <summary>A Map Matching request.</summary>
public sealed class MapMatchingRequest
{
    /// <summary>Creates a request.</summary>
    public MapMatchingRequest(RoutingProfile profile, IEnumerable<TracePoint> points)
    {
        Profile = profile;
        Points = (points ?? throw new ArgumentNullException(nameof(points))).ToList();
    }

    /// <summary>Creates a request from coordinates.</summary>
    public MapMatchingRequest(RoutingProfile profile, params LngLat[] coordinates)
        : this(profile, (coordinates ?? throw new ArgumentNullException(nameof(coordinates))).Select(c => new TracePoint(c))) { }

    /// <summary>Routing profile.</summary>
    public RoutingProfile Profile { get; set; }

    /// <summary>The trace (2 to 100 points).</summary>
    public IList<TracePoint> Points { get; }

    /// <summary>Return turn-by-turn steps (<c>steps</c>).</summary>
    public bool? Steps { get; set; }

    /// <summary>Overview geometry type (<c>overview</c>).</summary>
    public RouteOverview? Overview { get; set; }

    /// <summary>Per-segment annotations (<c>annotations</c>).</summary>
    public RouteAnnotations Annotations { get; set; }

    /// <summary>Removes clusters and re-samples traces (<c>tidy</c>).</summary>
    public bool? Tidy { get; set; }

    /// <summary>Rules to ignore (<c>ignore</c>, driving only).</summary>
    public MapMatchingIgnore Ignore { get; set; }

    /// <summary>Instruction language (<c>language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Emits instructions at roundabout exits (<c>roundabout_exits</c>).</summary>
    public bool? RoundaboutExits { get; set; }

    /// <summary>Returns banner instructions (<c>banner_instructions</c>).</summary>
    public bool? BannerInstructions { get; set; }

    /// <summary>Returns voice instructions (<c>voice_instructions</c>).</summary>
    public bool? VoiceInstructions { get; set; }

    /// <summary>Units of voice instructions (<c>voice_units</c>).</summary>
    public VoiceUnits? VoiceUnits { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        if (Points.Count < 2) throw new ArgumentException("The Map Matching API requires at least 2 coordinates.", nameof(Points));
        var q = new QueryBuilder()
            .Add("geometries", "geojson")
            .Add("steps", Steps)
            .Add("overview", Overview is { } o ? EnumText.Of(o) : null);
        if (Annotations != RouteAnnotations.None) q.Add("annotations", EnumText.Flags(Annotations));
        if (Points.Any(p => p.Radius is not null))
            q.Add("radiuses", Points.Select(p => p.Radius is { } r ? Fmt.Num(r) : string.Empty), ";");
        if (Points.Any(p => p.Timestamp is not null))
        {
            if (Points.Any(p => p.Timestamp is null))
                throw new ArgumentException("Either every trace point or none must have a timestamp.", nameof(Points));
            q.Add("timestamps", Points.Select(p => Fmt.Int(p.Timestamp!.Value.ToUnixTimeSeconds())), ";");
        }
        if (Points.Any(p => p.Approach is not null))
            q.Add("approaches", Points.Select(p => p.Approach is { } a ? EnumText.Of(a) : string.Empty), ";");

        var waypoints = Points.Select((p, i) => (p, i)).Where(x => x.p.IsWaypoint || x.i == 0 || x.i == Points.Count - 1).ToList();
        if (waypoints.Count != Points.Count)
            q.Add("waypoints", waypoints.Select(x => Fmt.Int(x.i)), ";");
        if (waypoints.Any(x => x.p.Name is not null))
            q.Add("waypoint_names", waypoints.Select(x => (x.p.Name ?? string.Empty).Replace(";", ",")), ";");

        q.Add("tidy", Tidy);
        if (Ignore != MapMatchingIgnore.None) q.Add("ignore", EnumText.Flags(Ignore));
        q.Add("language", Language)
            .Add("roundabout_exits", RoundaboutExits)
            .Add("banner_instructions", BannerInstructions)
            .Add("voice_instructions", VoiceInstructions)
            .Add("voice_units", VoiceUnits is { } vu ? EnumText.Of(vu) : null)
            .AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>Map Matching response.</summary>
public sealed class MapMatchingResponse
{
    /// <summary>Response code (<c>Ok</c>).</summary>
    public string Code { get; init; } = "Ok";

    /// <summary>Matched routes (the trace may be split into several matchings).</summary>
    public IReadOnlyList<Matching> Matchings { get; init; } = Array.Empty<Matching>();

    /// <summary>One entry per input point; null for points that could not be matched (outliers).</summary>
    public IReadOnlyList<Tracepoint?> Tracepoints { get; init; } = Array.Empty<Tracepoint?>();

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>The matchings as line features (with a <c>confidence</c> property).</summary>
    public FeatureCollection ToFeatureCollection() => RouteFeatures.Collection(Matchings);
}

/// <summary>A matched route.</summary>
public sealed class Matching : Route
{
    /// <summary>Confidence of the match, 0 (low) to 1 (high).</summary>
    public double Confidence { get; init; }

    /// <inheritdoc />
    public override Feature ToFeature()
    {
        var feature = base.ToFeature();
        feature.Properties["confidence"] = Confidence;
        return feature;
    }
}

/// <summary>An input trace point matched to the road network.</summary>
public sealed class Tracepoint : SnappedWaypoint
{
    /// <summary>Index of the matching containing this point.</summary>
    public int MatchingsIndex { get; init; }

    /// <summary>Index of the waypoint inside the matching.</summary>
    public int? WaypointIndex { get; init; }

    /// <summary>Number of alternative matches for this point.</summary>
    public int AlternativesCount { get; init; }
}

internal sealed class MapMatchingService : IMapMatchingService
{
    private readonly MapboxHttp _http;

    public MapMatchingService(MapboxHttp http) => _http = http;

    public Task<MapMatchingResponse> MatchAsync(MapMatchingRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var basePath = "matching/v5/" + NavigationQuery.Profile(request.Profile);
        var coordinates = Fmt.Coordinates(request.Points.Select(p => p.Location));
        var path = basePath + "/" + coordinates;

        MapMatchingResponse Parse(JsonElement root)
        {
            _http.EnsureOk(root, basePath);
            return ServicesJson.Deserialize<MapMatchingResponse>(root);
        }

        if (_http.GetUrlLength(path, query) <= MapboxHttp.MaxGetUrlLength)
            return _http.GetJsonAsync(path, query, Parse, cancellationToken);

        var form = new QueryBuilder().Add("coordinates", coordinates).AddRange(query.Items);
        return _http.PostFormAsync(basePath, form, Parse, cancellationToken);
    }
}

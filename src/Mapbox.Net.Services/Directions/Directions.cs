using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Directions API v5 (<c>/directions/v5</c>).</summary>
public interface IDirectionsService
{
    /// <summary>Computes routes between 2 to 25 waypoints. Geometries are returned as GeoJSON.</summary>
    /// <exception cref="MapboxApiException">HTTP error, or a response code other than <c>Ok</c> (e.g. <c>NoRoute</c>).</exception>
    Task<DirectionsResponse> GetRouteAsync(DirectionsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A Directions API request.</summary>
public sealed class DirectionsRequest
{
    /// <summary>Creates a request.</summary>
    public DirectionsRequest(RoutingProfile profile, IEnumerable<RouteWaypoint> waypoints)
    {
        Profile = profile;
        Waypoints = (waypoints ?? throw new ArgumentNullException(nameof(waypoints))).ToList();
    }

    /// <summary>Creates a request from coordinates.</summary>
    public DirectionsRequest(RoutingProfile profile, params LngLat[] coordinates)
        : this(profile, (coordinates ?? throw new ArgumentNullException(nameof(coordinates))).Select(c => new RouteWaypoint(c))) { }

    /// <summary>Routing profile.</summary>
    public RoutingProfile Profile { get; set; }

    /// <summary>Waypoints (2 to 25).</summary>
    public IList<RouteWaypoint> Waypoints { get; }

    /// <summary>Return up to two alternative routes (<c>alternatives</c>).</summary>
    public bool? Alternatives { get; set; }

    /// <summary>Return turn-by-turn steps (<c>steps</c>).</summary>
    public bool? Steps { get; set; }

    /// <summary>Overview geometry type (<c>overview</c>). Use <see cref="RouteOverview.Full"/> for annotations.</summary>
    public RouteOverview? Overview { get; set; }

    /// <summary>Per-segment annotations (<c>annotations</c>, requires <c>overview=full</c>).</summary>
    public RouteAnnotations Annotations { get; set; }

    /// <summary>Road types to avoid (<c>exclude</c>).</summary>
    public RouteExclusions Exclude { get; set; }

    /// <summary>Points to avoid (<c>exclude=point(lon lat)</c>, beta, max 50).</summary>
    public IList<LngLat> ExcludePoints { get; } = new List<LngLat>();

    /// <summary>Special lanes to use (<c>include</c>).</summary>
    public RouteIncludes Include { get; set; }

    /// <summary>Instruction language, e.g. <c>fr</c> (<c>language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Forces the route to keep going straight at waypoints (<c>continue_straight</c>).</summary>
    public bool? ContinueStraight { get; set; }

    /// <summary>Emits instructions at roundabout exits (<c>roundabout_exits</c>).</summary>
    public bool? RoundaboutExits { get; set; }

    /// <summary>Returns banner instructions (<c>banner_instructions</c>, requires steps).</summary>
    public bool? BannerInstructions { get; set; }

    /// <summary>Returns voice instructions (<c>voice_instructions</c>, requires steps).</summary>
    public bool? VoiceInstructions { get; set; }

    /// <summary>Units of voice instructions (<c>voice_units</c>).</summary>
    public VoiceUnits? VoiceUnits { get; set; }

    /// <summary>Departure time (<c>depart_at</c>, driving and driving-traffic).</summary>
    public DateTimeOffset? DepartAt { get; set; }

    /// <summary>Desired arrival time (<c>arrive_by</c>, driving only).</summary>
    public DateTimeOffset? ArriveBy { get; set; }

    /// <summary>Vehicle height in meters (<c>max_height</c>).</summary>
    public double? MaxHeight { get; set; }

    /// <summary>Vehicle width in meters (<c>max_width</c>).</summary>
    public double? MaxWidth { get; set; }

    /// <summary>Vehicle weight in metric tons (<c>max_weight</c>).</summary>
    public double? MaxWeight { get; set; }

    /// <summary>Walking speed in m/s (<c>walking_speed</c>, walking only).</summary>
    public double? WalkingSpeed { get; set; }

    /// <summary>Walkway preference -1..1 (<c>walkway_bias</c>, walking only).</summary>
    public double? WalkwayBias { get; set; }

    /// <summary>Alley preference -1..1 (<c>alley_bias</c>).</summary>
    public double? AlleyBias { get; set; }

    /// <summary>Radius in meters in which no maneuver is allowed at the start (<c>avoid_maneuver_radius</c>).</summary>
    public double? AvoidManeuverRadius { get; set; }

    /// <summary>Returns waypoints per route instead of per response (<c>waypoints_per_route</c>).</summary>
    public bool? WaypointsPerRoute { get; set; }

    /// <summary>Extra raw query parameters (for options not covered by this class).</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        if (Waypoints.Count is < 2 or > 25)
            throw new ArgumentException("The Directions API requires between 2 and 25 waypoints.", nameof(Waypoints));
        if (Waypoints[0].IsSilent || Waypoints[^1].IsSilent)
            throw new ArgumentException("The first and last waypoints cannot be silent.", nameof(Waypoints));

        var q = new QueryBuilder()
            .Add("geometries", "geojson")
            .Add("alternatives", Alternatives)
            .Add("steps", Steps)
            .Add("overview", Overview is { } o ? EnumText.Of(o) : null);
        if (Annotations != RouteAnnotations.None) q.Add("annotations", EnumText.Flags(Annotations));

        var exclude = EnumText.Flags(Exclude);
        exclude.AddRange(ExcludePoints.Select(p => "point(" + Fmt.Num(p.Longitude) + " " + Fmt.Num(p.Latitude) + ")"));
        q.Add("exclude", exclude);
        if (Include != RouteIncludes.None) q.Add("include", EnumText.Flags(Include));

        NavigationQuery.AddPerWaypoint(q, Waypoints, names: true, silent: true);

        q.Add("language", Language)
            .Add("continue_straight", ContinueStraight)
            .Add("roundabout_exits", RoundaboutExits)
            .Add("banner_instructions", BannerInstructions)
            .Add("voice_instructions", VoiceInstructions)
            .Add("voice_units", VoiceUnits is { } vu ? EnumText.Of(vu) : null)
            .Add("depart_at", DepartAt)
            .Add("arrive_by", ArriveBy)
            .Add("max_height", MaxHeight)
            .Add("max_width", MaxWidth)
            .Add("max_weight", MaxWeight)
            .Add("walking_speed", WalkingSpeed)
            .Add("walkway_bias", WalkwayBias)
            .Add("alley_bias", AlleyBias)
            .Add("avoid_maneuver_radius", AvoidManeuverRadius)
            .Add("waypoints_per_route", WaypointsPerRoute)
            .AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>Directions API response.</summary>
public sealed class DirectionsResponse
{
    /// <summary>Response code (<c>Ok</c>).</summary>
    public string Code { get; init; } = "Ok";

    /// <summary>Unique request identifier.</summary>
    public string? Uuid { get; init; }

    /// <summary>Routes, best first.</summary>
    public IReadOnlyList<Route> Routes { get; init; } = Array.Empty<Route>();

    /// <summary>Snapped input waypoints.</summary>
    public IReadOnlyList<SnappedWaypoint> Waypoints { get; init; } = Array.Empty<SnappedWaypoint>();

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>The best route, or null if there is none.</summary>
    [JsonIgnore] public Route? BestRoute => Routes.Count > 0 ? Routes[0] : null;

    /// <summary>All routes as line features (properties: <c>route_index</c>, <c>distance</c>, <c>duration</c>, <c>weight</c>).</summary>
    public FeatureCollection ToFeatureCollection() => RouteFeatures.Collection(Routes);

    /// <summary>The snapped waypoints as point features.</summary>
    public FeatureCollection WaypointsToFeatureCollection() => new(Waypoints.Select(w => w.ToFeature()));
}

/// <summary>Builds the semicolon-separated per-coordinate parameters.</summary>
internal static class NavigationQuery
{
    public static void AddPerWaypoint(QueryBuilder q, IList<RouteWaypoint> waypoints, bool names, bool silent)
    {
        if (waypoints.Any(w => w.Bearing is not null))
            q.Add("bearings", waypoints.Select(w => w.Bearing?.ToString() ?? string.Empty), ";");
        if (waypoints.Any(w => w.Radius is not null))
            q.Add("radiuses", waypoints.Select(w => w.Radius is { } r ? Fmt.Radius(r) : string.Empty), ";");
        if (waypoints.Any(w => w.Approach is not null))
            q.Add("approaches", waypoints.Select(w => w.Approach is { } a ? EnumText.Of(a) : string.Empty), ";");

        var stops = silent ? waypoints.Where(w => !w.IsSilent).ToList() : waypoints.ToList();
        if (silent && stops.Count != waypoints.Count)
            q.Add("waypoints", waypoints.Select((w, i) => (w, i)).Where(x => !x.w.IsSilent).Select(x => Fmt.Int(x.i)), ";");
        if (names && stops.Any(w => w.Name is not null))
            q.Add("waypoint_names", stops.Select(w => (w.Name ?? string.Empty).Replace(";", ",")), ";");
    }

    public static string Profile(RoutingProfile profile) => "mapbox/" + EnumText.Of(profile);
}

internal sealed class DirectionsService : IDirectionsService
{
    private readonly MapboxHttp _http;

    public DirectionsService(MapboxHttp http) => _http = http;

    public Task<DirectionsResponse> GetRouteAsync(DirectionsRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var basePath = "directions/v5/" + NavigationQuery.Profile(request.Profile);
        var coordinates = Fmt.Coordinates(request.Waypoints.Select(w => w.Location));
        var path = basePath + "/" + coordinates;

        DirectionsResponse Parse(JsonElement root)
        {
            _http.EnsureOk(root, basePath);
            return ServicesJson.Deserialize<DirectionsResponse>(root);
        }

        if (_http.GetUrlLength(path, query) <= MapboxHttp.MaxGetUrlLength)
            return _http.GetJsonAsync(path, query, Parse, cancellationToken);

        var form = new QueryBuilder().Add("coordinates", coordinates).AddRange(query.Items);
        return _http.PostFormAsync(basePath, form, Parse, cancellationToken);
    }
}

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

/// <summary>Optimization API v1 (<c>/optimized-trips/v1</c>): travelling-salesman style trip optimization.</summary>
public interface IOptimizationService
{
    /// <summary>Returns the fastest trip visiting 2 to 12 coordinates.</summary>
    /// <exception cref="MapboxApiException">HTTP error, or a response code other than <c>Ok</c> (e.g. <c>NoTrips</c>).</exception>
    Task<OptimizationResponse> OptimizeAsync(OptimizationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Which coordinate the trip starts at.</summary>
public enum TripSource
{
    /// <summary>Any coordinate (default).</summary>
    Any,
    /// <summary>The first coordinate.</summary>
    First,
}

/// <summary>Which coordinate the trip ends at.</summary>
public enum TripDestination
{
    /// <summary>Any coordinate (default).</summary>
    Any,
    /// <summary>The last coordinate.</summary>
    Last,
}

/// <summary>A pickup/drop-off constraint: the pickup must be visited before the drop-off.</summary>
/// <param name="PickupIndex">Index of the pickup coordinate.</param>
/// <param name="DropoffIndex">Index of the drop-off coordinate.</param>
public readonly record struct TripDistribution(int PickupIndex, int DropoffIndex);

/// <summary>An Optimization API v1 request.</summary>
public sealed class OptimizationRequest
{
    /// <summary>Creates a request.</summary>
    public OptimizationRequest(RoutingProfile profile, IEnumerable<RouteWaypoint> waypoints)
    {
        Profile = profile;
        Waypoints = (waypoints ?? throw new ArgumentNullException(nameof(waypoints))).ToList();
    }

    /// <summary>Creates a request from coordinates.</summary>
    public OptimizationRequest(RoutingProfile profile, params LngLat[] coordinates)
        : this(profile, (coordinates ?? throw new ArgumentNullException(nameof(coordinates))).Select(c => new RouteWaypoint(c))) { }

    /// <summary>Routing profile.</summary>
    public RoutingProfile Profile { get; set; }

    /// <summary>Coordinates to visit (2 to 12). Bearing, radius and approach are honoured.</summary>
    public IList<RouteWaypoint> Waypoints { get; }

    /// <summary>Return to the first location (<c>roundtrip</c>, API default true).</summary>
    public bool? Roundtrip { get; set; }

    /// <summary>Start coordinate (<c>source</c>).</summary>
    public TripSource? Source { get; set; }

    /// <summary>End coordinate (<c>destination</c>).</summary>
    public TripDestination? Destination { get; set; }

    /// <summary>Pickup/drop-off constraints (<c>distributions</c>).</summary>
    public IList<TripDistribution> Distributions { get; } = new List<TripDistribution>();

    /// <summary>Return turn-by-turn steps (<c>steps</c>).</summary>
    public bool? Steps { get; set; }

    /// <summary>Overview geometry type (<c>overview</c>).</summary>
    public RouteOverview? Overview { get; set; }

    /// <summary>Per-segment annotations (duration, distance, speed).</summary>
    public RouteAnnotations Annotations { get; set; }

    /// <summary>Instruction language (<c>language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        if (Waypoints.Count < 2) throw new ArgumentException("The Optimization API requires at least 2 coordinates.", nameof(Waypoints));
        var q = new QueryBuilder()
            .Add("geometries", "geojson")
            .Add("roundtrip", Roundtrip)
            .Add("source", Source is { } s ? EnumText.Of(s) : null)
            .Add("destination", Destination is { } d ? EnumText.Of(d) : null)
            .Add("steps", Steps)
            .Add("overview", Overview is { } o ? EnumText.Of(o) : null);
        if (Annotations != RouteAnnotations.None) q.Add("annotations", EnumText.Flags(Annotations));
        NavigationQuery.AddPerWaypoint(q, Waypoints, names: false, silent: false);
        if (Distributions.Count > 0)
            q.Add("distributions", Distributions.Select(x => Fmt.Int(x.PickupIndex) + "," + Fmt.Int(x.DropoffIndex)), ";");
        q.Add("language", Language).AddRange(AdditionalParameters);
        return q;
    }
}

/// <summary>Optimization API response.</summary>
public sealed class OptimizationResponse
{
    /// <summary>Response code (<c>Ok</c>).</summary>
    public string Code { get; init; } = "Ok";

    /// <summary>Input coordinates with their position in the optimized trip.</summary>
    public IReadOnlyList<OptimizedWaypoint> Waypoints { get; init; } = Array.Empty<OptimizedWaypoint>();

    /// <summary>Optimized trips (usually one).</summary>
    public IReadOnlyList<Route> Trips { get; init; } = Array.Empty<Route>();

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Waypoints sorted in visiting order.</summary>
    public IReadOnlyList<OptimizedWaypoint> GetVisitOrder() => Waypoints.OrderBy(w => w.TripsIndex).ThenBy(w => w.WaypointIndex).ToList();

    /// <summary>The trips as line features.</summary>
    public FeatureCollection ToFeatureCollection() => RouteFeatures.Collection(Trips);
}

/// <summary>An input coordinate of an optimized trip.</summary>
public sealed class OptimizedWaypoint : SnappedWaypoint
{
    /// <summary>Position of the waypoint in the trip.</summary>
    public int WaypointIndex { get; init; }

    /// <summary>Index of the trip containing the waypoint.</summary>
    public int TripsIndex { get; init; }
}

internal sealed class OptimizationService : IOptimizationService
{
    private readonly MapboxHttp _http;

    public OptimizationService(MapboxHttp http) => _http = http;

    public Task<OptimizationResponse> OptimizeAsync(OptimizationRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var basePath = "optimized-trips/v1/" + NavigationQuery.Profile(request.Profile);
        var path = basePath + "/" + Fmt.Coordinates(request.Waypoints.Select(w => w.Location));
        return _http.GetJsonAsync(path, query, root =>
        {
            _http.EnsureOk(root, basePath);
            return ServicesJson.Deserialize<OptimizationResponse>(root);
        }, cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Services.Internal;

namespace Mapbox.Net.Services;

/// <summary>Matrix API v1 (<c>/directions-matrix/v1</c>): travel times / distances between many points.</summary>
public interface IMatrixService
{
    /// <summary>Computes a duration and/or distance matrix.</summary>
    /// <exception cref="MapboxApiException">HTTP error, or a response code other than <c>Ok</c>.</exception>
    Task<MatrixResponse> GetMatrixAsync(MatrixRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Matrix annotations to return.</summary>
[Flags]
public enum MatrixAnnotations
{
    /// <summary>API default (durations only).</summary>
    Default = 0,
    /// <summary>Travel times in seconds.</summary>
    Duration = 1,
    /// <summary>Distances in meters.</summary>
    Distance = 2,
}

/// <summary>A Matrix API request. Up to 25 coordinates (10 for driving-traffic).</summary>
public sealed class MatrixRequest
{
    /// <summary>Creates a request.</summary>
    public MatrixRequest(RoutingProfile profile, IEnumerable<RouteWaypoint> points)
    {
        Profile = profile;
        Points = (points ?? throw new ArgumentNullException(nameof(points))).ToList();
    }

    /// <summary>Creates a request from coordinates.</summary>
    public MatrixRequest(RoutingProfile profile, params LngLat[] coordinates)
        : this(profile, (coordinates ?? throw new ArgumentNullException(nameof(coordinates))).Select(c => new RouteWaypoint(c))) { }

    /// <summary>Routing profile.</summary>
    public RoutingProfile Profile { get; set; }

    /// <summary>Input points (approach and bearing are honoured, radius and name are ignored).</summary>
    public IList<RouteWaypoint> Points { get; }

    /// <summary>Indices of <see cref="Points"/> used as sources (null = all).</summary>
    public IList<int>? Sources { get; set; }

    /// <summary>Indices of <see cref="Points"/> used as destinations (null = all).</summary>
    public IList<int>? Destinations { get; set; }

    /// <summary>Values to compute (<c>annotations</c>).</summary>
    public MatrixAnnotations Annotations { get; set; } = MatrixAnnotations.Duration | MatrixAnnotations.Distance;

    /// <summary>Speed in km/h used to estimate unroutable pairs instead of returning null (<c>fallback_speed</c>).</summary>
    public double? FallbackSpeed { get; set; }

    /// <summary>Departure time (<c>depart_at</c>, driving profiles).</summary>
    public DateTimeOffset? DepartAt { get; set; }

    /// <summary>Extra raw query parameters.</summary>
    public IDictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();

    internal QueryBuilder BuildQuery()
    {
        if (Points.Count < 2) throw new ArgumentException("The Matrix API requires at least 2 coordinates.", nameof(Points));
        var q = new QueryBuilder();
        if (Annotations != MatrixAnnotations.Default) q.Add("annotations", EnumText.Flags(Annotations));
        if (Points.Any(p => p.Approach is not null))
            q.Add("approaches", Points.Select(p => p.Approach is { } a ? EnumText.Of(a) : string.Empty), ";");
        if (Points.Any(p => p.Bearing is not null))
            q.Add("bearings", Points.Select(p => p.Bearing?.ToString() ?? string.Empty), ";");
        q.Add("sources", Indices(Sources, nameof(Sources)))
            .Add("destinations", Indices(Destinations, nameof(Destinations)))
            .Add("fallback_speed", FallbackSpeed)
            .Add("depart_at", DepartAt)
            .AddRange(AdditionalParameters);
        return q;
    }

    private string? Indices(IList<int>? list, string name)
    {
        if (list is null) return null;
        if (list.Count == 0) throw new ArgumentException("At least one index is required.", name);
        if (list.Any(i => i < 0 || i >= Points.Count)) throw new ArgumentOutOfRangeException(name, "Index out of range of Points.");
        return string.Join(";", list.Select(i => Fmt.Int(i)));
    }
}

/// <summary>Matrix API response. Matrices are indexed <c>[source][destination]</c>; null means no route.</summary>
public sealed class MatrixResponse
{
    /// <summary>Response code (<c>Ok</c>).</summary>
    public string Code { get; init; } = "Ok";

    /// <summary>Travel times in seconds.</summary>
    public IReadOnlyList<IReadOnlyList<double?>>? Durations { get; init; }

    /// <summary>Distances in meters.</summary>
    public IReadOnlyList<IReadOnlyList<double?>>? Distances { get; init; }

    /// <summary>Snapped sources.</summary>
    public IReadOnlyList<SnappedWaypoint> Sources { get; init; } = Array.Empty<SnappedWaypoint>();

    /// <summary>Snapped destinations.</summary>
    public IReadOnlyList<SnappedWaypoint> Destinations { get; init; } = Array.Empty<SnappedWaypoint>();

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Duration in seconds from source <paramref name="source"/> to destination <paramref name="destination"/>.</summary>
    public double? GetDuration(int source, int destination) => Durations?[source][destination];

    /// <summary>Distance in meters from source <paramref name="source"/> to destination <paramref name="destination"/>.</summary>
    public double? GetDistance(int source, int destination) => Distances?[source][destination];
}

internal sealed class MatrixService : IMatrixService
{
    private readonly MapboxHttp _http;

    public MatrixService(MapboxHttp http) => _http = http;

    public Task<MatrixResponse> GetMatrixAsync(MatrixRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var query = request.BuildQuery();
        var basePath = "directions-matrix/v1/" + NavigationQuery.Profile(request.Profile);
        var path = basePath + "/" + Fmt.Coordinates(request.Points.Select(p => p.Location));
        return _http.GetJsonAsync(path, query, root =>
        {
            _http.EnsureOk(root, basePath);
            return ServicesJson.Deserialize<MatrixResponse>(root);
        }, cancellationToken);
    }
}

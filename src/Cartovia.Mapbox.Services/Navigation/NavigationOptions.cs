using System;
using System.Globalization;
using Cartovia.Json;

namespace Cartovia.Mapbox.Services;

/// <summary>Mapbox routing profiles (the <c>mapbox/</c> prefix is added automatically).</summary>
public enum RoutingProfile
{
    /// <summary>Car travel using average conditions (<c>mapbox/driving</c>).</summary>
    Driving,
    /// <summary>Car travel using current and historic traffic (<c>mapbox/driving-traffic</c>).</summary>
    DrivingTraffic,
    /// <summary>Pedestrian and hiking travel (<c>mapbox/walking</c>).</summary>
    Walking,
    /// <summary>Bicycle travel (<c>mapbox/cycling</c>).</summary>
    Cycling,
}

/// <summary>Side of the road from which a waypoint must be approached.</summary>
public enum Approach
{
    /// <summary>Either side of the road (default).</summary>
    Unrestricted,
    /// <summary>The route must arrive on the driving side of the road.</summary>
    Curb,
}

/// <summary>Type of route overview geometry.</summary>
public enum RouteOverview
{
    /// <summary>Simplified geometry (API default).</summary>
    Simplified,
    /// <summary>Most detailed geometry available.</summary>
    Full,
    /// <summary>No overview geometry.</summary>
    [JsonValue("false")] None,
}

/// <summary>Per-segment annotations to return with each route leg.</summary>
[Flags]
public enum RouteAnnotations
{
    /// <summary>No annotation.</summary>
    None = 0,
    /// <summary>Distance between coordinates, in meters.</summary>
    Distance = 1,
    /// <summary>Duration between coordinates, in seconds.</summary>
    Duration = 2,
    /// <summary>Speed between coordinates, in m/s.</summary>
    Speed = 4,
    /// <summary>Traffic congestion level (driving-traffic only).</summary>
    Congestion = 8,
    /// <summary>Numeric congestion 0-100 (driving-traffic only).</summary>
    [JsonValue("congestion_numeric")] CongestionNumeric = 16,
    /// <summary>Posted speed limit (driving profiles, beta).</summary>
    [JsonValue("maxspeed")] MaxSpeed = 32,
    /// <summary>Closures (driving-traffic only).</summary>
    Closure = 64,
    /// <summary>EV state of charge (beta).</summary>
    [JsonValue("state_of_charge")] StateOfCharge = 128,
}

/// <summary>Road types to avoid. Applies to driving profiles (ferry also to cycling).</summary>
[Flags]
public enum RouteExclusions
{
    /// <summary>No exclusion.</summary>
    None = 0,
    /// <summary>Motorways.</summary>
    Motorway = 1,
    /// <summary>Toll roads.</summary>
    Toll = 2,
    /// <summary>Ferries.</summary>
    Ferry = 4,
    /// <summary>Unpaved roads.</summary>
    Unpaved = 8,
    /// <summary>Cash-only toll roads.</summary>
    [JsonValue("cash_only_tolls")] CashOnlyTolls = 16,
    /// <summary>Country borders (beta, Directions only).</summary>
    [JsonValue("country_border")] CountryBorder = 32,
    /// <summary>State borders (beta, Directions only).</summary>
    [JsonValue("state_border")] StateBorder = 64,
    /// <summary>Tunnels (beta, Directions only).</summary>
    Tunnel = 128,
}

/// <summary>Special lanes the route may use (driving profiles).</summary>
[Flags]
public enum RouteIncludes
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary>High-occupancy vehicle lanes with 2+ occupants.</summary>
    Hov2 = 1,
    /// <summary>High-occupancy vehicle lanes with 3+ occupants.</summary>
    Hov3 = 2,
    /// <summary>High-occupancy toll lanes.</summary>
    Hot = 4,
}

/// <summary>Unit system of voice instructions.</summary>
public enum VoiceUnits
{
    /// <summary>Imperial (default).</summary>
    Imperial,
    /// <summary>Metric.</summary>
    Metric,
    /// <summary>British imperial.</summary>
    [JsonValue("british_imperial")] BritishImperial,
}

/// <summary>A bearing filter: the route must leave the waypoint at <see cref="Angle"/> ± <see cref="Range"/> degrees.</summary>
/// <param name="Angle">Clockwise angle from true north, 0-360.</param>
/// <param name="Range">Allowed deviation, 0-180.</param>
public readonly record struct Bearing(double Angle, double Range)
{
    /// <inheritdoc />
    public override string ToString()
        => Angle.ToString("0.#########", CultureInfo.InvariantCulture) + "," + Range.ToString("0.#########", CultureInfo.InvariantCulture);
}

/// <summary>An input coordinate of a navigation request with its optional per-coordinate settings.</summary>
public sealed class RouteWaypoint
{
    /// <summary>Creates a waypoint.</summary>
    public RouteWaypoint(LngLat location) => Location = location;

    /// <summary>Creates a waypoint.</summary>
    public RouteWaypoint(double longitude, double latitude) => Location = new LngLat(longitude, latitude);

    /// <summary>The coordinate.</summary>
    public LngLat Location { get; set; }

    /// <summary>Bearing filter used to snap the coordinate to the road network (<c>bearings</c>).</summary>
    public Bearing? Bearing { get; set; }

    /// <summary>Maximum snapping distance in meters (<c>radiuses</c>); <see cref="double.PositiveInfinity"/> means unlimited.</summary>
    public double? Radius { get; set; }

    /// <summary>Side of the road to approach (<c>approaches</c>).</summary>
    public Approach? Approach { get; set; }

    /// <summary>Custom name used in arrival instructions (<c>waypoint_names</c>, Directions only).</summary>
    public string? Name { get; set; }

    /// <summary>
    /// When true the coordinate is a silent "via" point: the route passes through it without splitting into a new leg
    /// (Directions <c>waypoints</c> parameter). The first and last coordinates can't be silent.
    /// </summary>
    public bool IsSilent { get; set; }

    /// <summary>Implicit conversion from a coordinate.</summary>
    public static implicit operator RouteWaypoint(LngLat location) => new(location);

    /// <inheritdoc />
    public override string ToString() => Location.ToString();
}

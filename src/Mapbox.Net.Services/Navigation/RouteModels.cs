using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mapbox.Net.GeoJson;

namespace Mapbox.Net.Services;

/// <summary>A route returned by the Directions API (also used for Optimization trips and Map Matching matchings).</summary>
public class Route
{
    /// <summary>Distance in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Estimated travel time in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Typical travel time in seconds (driving-traffic only).</summary>
    public double? DurationTypical { get; init; }

    /// <summary>Weight of the route (used by the routing engine to select the best route).</summary>
    public double Weight { get; init; }

    /// <summary>Name of the weight metric (e.g. <c>auto</c>, <c>pedestrian</c>).</summary>
    public string? WeightName { get; init; }

    /// <summary>Route geometry (null when <c>overview=false</c>).</summary>
    public LineString? Geometry { get; init; }

    /// <summary>Legs between consecutive waypoints.</summary>
    public IReadOnlyList<RouteLeg> Legs { get; init; } = Array.Empty<RouteLeg>();

    /// <summary>Locale used for instructions.</summary>
    public string? VoiceLocale { get; init; }

    /// <summary>Members not mapped to a property (e.g. <c>refresh_ttl</c>, EV data).</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Total duration as a <see cref="TimeSpan"/>.</summary>
    [JsonIgnore] public TimeSpan DurationTime => TimeSpan.FromSeconds(Duration);

    /// <summary>Bounds of the route geometry, or null if it has no geometry.</summary>
    public LngLatBounds? GetBounds() => Geometry?.GetBounds();

    /// <summary>Returns the route as a GeoJSON feature with <c>distance</c>, <c>duration</c> and <c>weight</c> properties.</summary>
    public virtual Feature ToFeature()
    {
        var props = new JsonObject
        {
            ["distance"] = Distance,
            ["duration"] = Duration,
            ["weight"] = Weight,
        };
        if (WeightName is not null) props["weight_name"] = WeightName;
        if (DurationTypical is { } t) props["duration_typical"] = t;
        return new Feature { Geometry = Geometry, Properties = props };
    }
}

/// <summary>A route between two waypoints.</summary>
public sealed class RouteLeg
{
    /// <summary>Distance in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Travel time in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Typical travel time in seconds (driving-traffic only).</summary>
    public double? DurationTypical { get; init; }

    /// <summary>Weight of the leg.</summary>
    public double Weight { get; init; }

    /// <summary>Summary of the most significant roads used.</summary>
    public string? Summary { get; init; }

    /// <summary>Turn-by-turn steps (only when <c>steps=true</c>).</summary>
    public IReadOnlyList<RouteStep> Steps { get; init; } = Array.Empty<RouteStep>();

    /// <summary>Per-segment annotations (only when annotations are requested).</summary>
    public LegAnnotation? Annotation { get; init; }

    /// <summary>Silent waypoints along the leg.</summary>
    public IReadOnlyList<ViaWaypoint>? ViaWaypoints { get; init; }

    /// <summary>Members not mapped to a property (admins, incidents, closures…).</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>A silent waypoint on a leg.</summary>
public sealed class ViaWaypoint
{
    /// <summary>Index of the waypoint in the request coordinates.</summary>
    public int WaypointIndex { get; init; }

    /// <summary>Distance from the start of the leg, in meters.</summary>
    public double DistanceFromStart { get; init; }

    /// <summary>Index in the leg geometry.</summary>
    public int GeometryIndex { get; init; }
}

/// <summary>A turn-by-turn step.</summary>
public sealed class RouteStep
{
    /// <summary>Distance in meters.</summary>
    public double Distance { get; init; }

    /// <summary>Travel time in seconds.</summary>
    public double Duration { get; init; }

    /// <summary>Typical travel time in seconds.</summary>
    public double? DurationTypical { get; init; }

    /// <summary>Weight of the step.</summary>
    public double Weight { get; init; }

    /// <summary>Road name.</summary>
    public string? Name { get; init; }

    /// <summary>Road reference number (e.g. <c>A7</c>).</summary>
    public string? Ref { get; init; }

    /// <summary>Destinations of the road.</summary>
    public string? Destinations { get; init; }

    /// <summary>Exit numbers.</summary>
    public string? Exits { get; init; }

    /// <summary>Pronunciation hint of the road name.</summary>
    public string? Pronunciation { get; init; }

    /// <summary>Name of the rotary, if any.</summary>
    public string? RotaryName { get; init; }

    /// <summary>Mode of travel (<c>driving</c>, <c>ferry</c>, <c>walking</c>, <c>cycling</c>…).</summary>
    public string? Mode { get; init; }

    /// <summary>Driving side (<c>left</c>/<c>right</c>).</summary>
    public string? DrivingSide { get; init; }

    /// <summary>Step geometry.</summary>
    public LineString? Geometry { get; init; }

    /// <summary>The maneuver at the start of the step.</summary>
    public StepManeuver Maneuver { get; init; } = new();

    /// <summary>Intersections along the step.</summary>
    public IReadOnlyList<StepIntersection> Intersections { get; init; } = Array.Empty<StepIntersection>();

    /// <summary>Voice instructions (when <c>voice_instructions=true</c>).</summary>
    [JsonPropertyName("voiceInstructions")]
    public IReadOnlyList<VoiceInstruction>? VoiceInstructions { get; init; }

    /// <summary>Banner instructions (when <c>banner_instructions=true</c>).</summary>
    [JsonPropertyName("bannerInstructions")]
    public IReadOnlyList<BannerInstruction>? BannerInstructions { get; init; }

    /// <summary>Speed limit sign type (<c>mutcd</c> or <c>vienna</c>).</summary>
    [JsonPropertyName("speedLimitSign")]
    public string? SpeedLimitSign { get; init; }

    /// <summary>Speed limit unit (<c>km/h</c> or <c>mph</c>).</summary>
    [JsonPropertyName("speedLimitUnit")]
    public string? SpeedLimitUnit { get; init; }

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>A maneuver.</summary>
public sealed class StepManeuver
{
    /// <summary>Clockwise angle from north before the maneuver.</summary>
    public double BearingBefore { get; init; }

    /// <summary>Clockwise angle from north after the maneuver.</summary>
    public double BearingAfter { get; init; }

    /// <summary>Location of the maneuver.</summary>
    public LngLat Location { get; init; }

    /// <summary>Maneuver type: <c>turn</c>, <c>depart</c>, <c>arrive</c>, <c>merge</c>, <c>roundabout</c>, <c>fork</c>…</summary>
    public string? Type { get; init; }

    /// <summary>Direction modifier: <c>left</c>, <c>slight right</c>, <c>straight</c>, <c>uturn</c>…</summary>
    public string? Modifier { get; init; }

    /// <summary>Human-readable instruction in the requested language.</summary>
    public string? Instruction { get; init; }

    /// <summary>Roundabout exit number, if applicable.</summary>
    public int? Exit { get; init; }
}

/// <summary>An intersection along a step.</summary>
public sealed class StepIntersection
{
    /// <summary>Location of the intersection.</summary>
    public LngLat Location { get; init; }

    /// <summary>Bearings of the available roads.</summary>
    public IReadOnlyList<double> Bearings { get; init; } = Array.Empty<double>();

    /// <summary>Whether each road can be entered.</summary>
    public IReadOnlyList<bool> Entry { get; init; } = Array.Empty<bool>();

    /// <summary>Index of the incoming road in <see cref="Bearings"/>.</summary>
    public int? In { get; init; }

    /// <summary>Index of the outgoing road in <see cref="Bearings"/>.</summary>
    public int? Out { get; init; }

    /// <summary>Lanes available at the intersection.</summary>
    public IReadOnlyList<IntersectionLane>? Lanes { get; init; }

    /// <summary>Road classes (<c>toll</c>, <c>ferry</c>, <c>motorway</c>, <c>tunnel</c>…).</summary>
    public IReadOnlyList<string>? Classes { get; init; }

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>A lane at an intersection.</summary>
public sealed class IntersectionLane
{
    /// <summary>Whether the lane can be taken to follow the route.</summary>
    public bool Valid { get; init; }

    /// <summary>Whether the lane is the preferred one.</summary>
    public bool? Active { get; init; }

    /// <summary>Lane indications (<c>left</c>, <c>straight</c>, <c>right</c>…).</summary>
    public IReadOnlyList<string> Indications { get; init; } = Array.Empty<string>();

    /// <summary>The indication to follow when the lane is valid.</summary>
    public string? ValidIndication { get; init; }
}

/// <summary>A voice instruction.</summary>
public sealed class VoiceInstruction
{
    /// <summary>Distance before the end of the step at which the instruction should be spoken.</summary>
    [JsonPropertyName("distanceAlongGeometry")] public double DistanceAlongGeometry { get; init; }

    /// <summary>Plain-text announcement.</summary>
    [JsonPropertyName("announcement")] public string? Announcement { get; init; }

    /// <summary>SSML announcement.</summary>
    [JsonPropertyName("ssmlAnnouncement")] public string? SsmlAnnouncement { get; init; }
}

/// <summary>A banner instruction.</summary>
public sealed class BannerInstruction
{
    /// <summary>Distance before the end of the step at which the banner should be shown.</summary>
    [JsonPropertyName("distanceAlongGeometry")] public double DistanceAlongGeometry { get; init; }

    /// <summary>Primary banner.</summary>
    [JsonPropertyName("primary")] public BannerText? Primary { get; init; }

    /// <summary>Secondary banner.</summary>
    [JsonPropertyName("secondary")] public BannerText? Secondary { get; init; }

    /// <summary>Sub banner (lanes, next maneuver).</summary>
    [JsonPropertyName("sub")] public BannerText? Sub { get; init; }
}

/// <summary>Text of a banner instruction.</summary>
public sealed class BannerText
{
    /// <summary>Text.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Maneuver type.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Maneuver modifier.</summary>
    [JsonPropertyName("modifier")] public string? Modifier { get; init; }

    /// <summary>Roundabout degrees.</summary>
    [JsonPropertyName("degrees")] public double? Degrees { get; init; }

    /// <summary>Driving side.</summary>
    [JsonPropertyName("driving_side")] public string? DrivingSide { get; init; }

    /// <summary>Text components (raw JSON).</summary>
    [JsonPropertyName("components")] public JsonElement? Components { get; init; }
}

/// <summary>Per-segment annotations of a leg. Arrays have one entry per geometry segment.</summary>
public sealed class LegAnnotation
{
    /// <summary>Segment distances in meters.</summary>
    public IReadOnlyList<double>? Distance { get; init; }

    /// <summary>Segment durations in seconds.</summary>
    public IReadOnlyList<double>? Duration { get; init; }

    /// <summary>Segment speeds in m/s.</summary>
    public IReadOnlyList<double>? Speed { get; init; }

    /// <summary>Congestion levels (<c>unknown</c>, <c>low</c>, <c>moderate</c>, <c>heavy</c>, <c>severe</c>).</summary>
    public IReadOnlyList<string>? Congestion { get; init; }

    /// <summary>Numeric congestion 0-100 (null when unknown).</summary>
    public IReadOnlyList<int?>? CongestionNumeric { get; init; }

    /// <summary>Speed limits.</summary>
    [JsonPropertyName("maxspeed")] public IReadOnlyList<MaxSpeed>? MaxSpeed { get; init; }

    /// <summary>Members not mapped to a property (closure, state_of_charge…).</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>A speed limit annotation.</summary>
public sealed class MaxSpeed
{
    /// <summary>Speed limit value, in <see cref="Unit"/>.</summary>
    public double? Speed { get; init; }

    /// <summary><c>km/h</c> or <c>mph</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>True when the speed limit is unknown.</summary>
    public bool Unknown { get; init; }

    /// <summary>True when there is no speed limit.</summary>
    public bool None { get; init; }
}

/// <summary>An input coordinate snapped to the road network.</summary>
public class SnappedWaypoint
{
    /// <summary>Name of the street the coordinate snapped to.</summary>
    public string? Name { get; init; }

    /// <summary>Snapped location.</summary>
    public LngLat Location { get; init; }

    /// <summary>Distance in meters between the input coordinate and the snapped location.</summary>
    public double? Distance { get; init; }

    /// <summary>Members not mapped to a property.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>Returns the waypoint as a GeoJSON point feature with a <c>name</c> property.</summary>
    public Feature ToFeature()
        => new() { Geometry = new Point(Location), Properties = new JsonObject { ["name"] = Name ?? string.Empty } };
}

/// <summary>Helpers shared by navigation responses.</summary>
internal static class RouteFeatures
{
    public static FeatureCollection Collection(IEnumerable<Route> routes)
        => new(routes.Select((r, i) => { var f = r.ToFeature(); f.Properties["route_index"] = i; return f; }));
}

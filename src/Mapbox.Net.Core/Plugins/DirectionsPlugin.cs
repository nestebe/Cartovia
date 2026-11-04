using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Controls;

namespace Mapbox.Net.Plugins;

/// <summary>Routing profiles of the Mapbox Directions API.</summary>
public static class DirectionsProfiles
{
    /// <summary>Driving with live traffic.</summary>
    public const string DrivingTraffic = "mapbox/driving-traffic";
    /// <summary>Driving.</summary>
    public const string Driving = "mapbox/driving";
    /// <summary>Walking.</summary>
    public const string Walking = "mapbox/walking";
    /// <summary>Cycling.</summary>
    public const string Cycling = "mapbox/cycling";
}

/// <summary>
/// Interactive routing UI (origin / destination inputs, route display, turn-by-turn instructions)
/// based on mapbox-gl-directions. <b>Mapbox engine only.</b> For programmatic routing with any engine use the
/// Mapbox.Net.Services package and draw the route with a line layer.
/// </summary>
public sealed class DirectionsPlugin : MapPlugin
{
    /// <summary>Creates the plugin.</summary>
    public DirectionsPlugin(ControlPosition? position = ControlPosition.TopLeft) => Position = position;

    /// <inheritdoc />
    public override string Name => "directions";

    /// <summary>Routing profile (see <see cref="DirectionsProfiles"/>).</summary>
    public string? Profile { get; set; }
    /// <summary>Units: <c>metric</c> or <c>imperial</c>.</summary>
    public string? Unit { get; set; }
    /// <summary>Show alternative routes.</summary>
    public bool? Alternatives { get; set; }
    /// <summary>Color the route by traffic congestion.</summary>
    public bool? Congestion { get; set; }
    /// <summary>Instructions language.</summary>
    public string? Language { get; set; }
    /// <summary>Allow clicking the map to set points. Default: true.</summary>
    public bool? Interactive { get; set; }
    /// <summary>Show the origin / destination inputs. Default: true.</summary>
    public bool? ShowInputs { get; set; }
    /// <summary>Show the instructions panel. Default: true.</summary>
    public bool? ShowInstructions { get; set; }
    /// <summary>Show the profile switcher. Default: true.</summary>
    public bool? ShowProfileSwitcher { get; set; }
    /// <summary>Origin input placeholder.</summary>
    public string? PlaceholderOrigin { get; set; }
    /// <summary>Destination input placeholder.</summary>
    public string? PlaceholderDestination { get; set; }
    /// <summary>Raw options merged into the plugin options.</summary>
    public JsonObject AdditionalOptions { get; } = new();

    /// <summary>Script URL.</summary>
    public string ScriptUrl { get; set; } = PluginAssets.DirectionsScript;
    /// <summary>Stylesheet URL.</summary>
    public string StylesheetUrl { get; set; } = PluginAssets.DirectionsStylesheet;

    /// <summary>A route was computed (<see cref="PluginEventArgs.Data"/>.routes holds the Directions API routes).</summary>
    public event EventHandler<PluginEventArgs>? RouteCalculated;
    /// <summary>The origin changed.</summary>
    public event EventHandler<PluginEventArgs>? OriginChanged;
    /// <summary>The destination changed.</summary>
    public event EventHandler<PluginEventArgs>? DestinationChanged;
    /// <summary>Origin or destination cleared.</summary>
    public event EventHandler<PluginEventArgs>? Cleared;
    /// <summary>The profile changed.</summary>
    public event EventHandler<PluginEventArgs>? ProfileChanged;
    /// <summary>A routing error occurred.</summary>
    public event EventHandler<PluginEventArgs>? RoutingFailed;

    /// <summary>Sets the origin.</summary>
    public Task SetOriginAsync(LngLat position, CancellationToken cancellationToken = default) => InvokeAsync("setOrigin", cancellationToken, position);
    /// <summary>Sets the origin from a search text.</summary>
    public Task SetOriginAsync(string query, CancellationToken cancellationToken = default) => InvokeAsync("setOrigin", cancellationToken, query);
    /// <summary>Sets the destination.</summary>
    public Task SetDestinationAsync(LngLat position, CancellationToken cancellationToken = default) => InvokeAsync("setDestination", cancellationToken, position);
    /// <summary>Sets the destination from a search text.</summary>
    public Task SetDestinationAsync(string query, CancellationToken cancellationToken = default) => InvokeAsync("setDestination", cancellationToken, query);
    /// <summary>Inserts a waypoint.</summary>
    public Task AddWaypointAsync(int index, LngLat position, CancellationToken cancellationToken = default) => InvokeAsync("addWaypoint", cancellationToken, index, position);
    /// <summary>Replaces a waypoint.</summary>
    public Task SetWaypointAsync(int index, LngLat position, CancellationToken cancellationToken = default) => InvokeAsync("setWaypoint", cancellationToken, index, position);
    /// <summary>Removes a waypoint.</summary>
    public Task RemoveWaypointAsync(int index, CancellationToken cancellationToken = default) => InvokeAsync("removeWaypoint", cancellationToken, index);
    /// <summary>Removes the routes and points.</summary>
    public Task RemoveRoutesAsync(CancellationToken cancellationToken = default) => InvokeAsync("removeRoutes", cancellationToken);
    /// <summary>Swaps origin and destination.</summary>
    public Task ReverseAsync(CancellationToken cancellationToken = default) => InvokeAsync("reverse", cancellationToken);
    /// <summary>Enables or disables map clicks.</summary>
    public Task SetInteractiveAsync(bool interactive, CancellationToken cancellationToken = default) => InvokeAsync("interactive", cancellationToken, interactive);
    /// <summary>Origin feature.</summary>
    public Task<JsonElement> GetOriginAsync(CancellationToken cancellationToken = default) => InvokeAsync("getOrigin", cancellationToken);
    /// <summary>Destination feature.</summary>
    public Task<JsonElement> GetDestinationAsync(CancellationToken cancellationToken = default) => InvokeAsync("getDestination", cancellationToken);

    /// <inheritdoc />
    protected internal override void EnsureEngineSupported(MapEngine engine)
    {
        if (engine != MapEngine.Mapbox) throw new NotSupportedException("DirectionsPlugin requires the Mapbox engine.");
    }

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine) => new[] { ScriptUrl };
    /// <inheritdoc />
    protected internal override IEnumerable<string> GetStylesheets(MapEngine engine) => new[] { StylesheetUrl };

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions(MapEngine engine)
    {
        var d = new JsonObject();
        Put(d, "profile", Profile);
        Put(d, "unit", Unit);
        Put(d, "alternatives", Alternatives);
        Put(d, "congestion", Congestion);
        Put(d, "language", Language);
        Put(d, "interactive", Interactive);
        Put(d, "placeholderOrigin", PlaceholderOrigin);
        Put(d, "placeholderDestination", PlaceholderDestination);
        if (ShowInputs is not null || ShowInstructions is not null || ShowProfileSwitcher is not null)
        {
            var c = new JsonObject();
            Put(c, "inputs", ShowInputs);
            Put(c, "instructions", ShowInstructions);
            Put(c, "profileSwitcher", ShowProfileSwitcher);
            d["controls"] = c;
        }
        foreach (var kv in AdditionalOptions) d[kv.Key] = kv.Value?.DeepClone();
        return new JsonObject { ["directionsOptions"] = d };
    }

    /// <inheritdoc />
    protected override void OnEvent(string eventName, JsonElement data)
    {
        var args = new PluginEventArgs(eventName, data);
        (eventName switch
        {
            "route" => RouteCalculated,
            "origin" => OriginChanged,
            "destination" => DestinationChanged,
            "clear" => Cleared,
            "profile" => ProfileChanged,
            "error" => RoutingFailed,
            _ => null,
        })?.Invoke(this, args);
        base.OnEvent(eventName, data);
    }
}

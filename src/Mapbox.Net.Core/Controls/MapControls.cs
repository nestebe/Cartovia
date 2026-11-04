using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Mapbox.Net.Controls;

/// <summary>Corner of the map where a control is placed.</summary>
public enum ControlPosition
{
    /// <summary>Top left corner.</summary>
    TopLeft,
    /// <summary>Top right corner.</summary>
    TopRight,
    /// <summary>Bottom left corner.</summary>
    BottomLeft,
    /// <summary>Bottom right corner.</summary>
    BottomRight,
}

/// <summary>
/// Base class of the map controls (navigation, scale, geolocate, custom HTML…).
/// Add a control with <see cref="MapboxMap.AddControlAsync"/> or through <see cref="MapOptions.InitialControls"/>.
/// </summary>
public abstract class MapControl
{
    /// <summary>Creates a control of a given kind.</summary>
    protected MapControl(string kind, ControlPosition? position)
    {
        Kind = kind;
        Position = position;
    }

    /// <summary>Kind understood by the bridge (<c>navigation</c>, <c>scale</c>…).</summary>
    public string Kind { get; }

    /// <summary>Unique id of the control instance.</summary>
    public string Id { get; } = "ctrl-" + Guid.NewGuid().ToString("N");

    /// <summary>Corner where the control is placed. Null: engine default.</summary>
    public ControlPosition? Position { get; set; }

    /// <summary>The map the control is added to, if any.</summary>
    public MapboxMap? Map { get; internal set; }

    /// <summary>Builds the options passed to the JavaScript constructor.</summary>
    protected internal abstract JsonObject BuildOptions();

    /// <summary>Called when the control raises an event.</summary>
    protected internal virtual void OnEvent(string name, JsonElement data) { }

    /// <summary>Removes the control from its map.</summary>
    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => Map is null ? Task.CompletedTask : Map.RemoveControlAsync(this, cancellationToken);

    internal JsonObject ToDefinition() => new()
    {
        ["id"] = Id,
        ["kind"] = Kind,
        ["position"] = Position is { } p ? Json.MapboxJson.EnumToString(p) : null,
        ["options"] = BuildOptions(),
    };

    /// <summary>Invokes a method of the underlying JavaScript control.</summary>
    protected Task<JsonElement> InvokeControlAsync(string method, CancellationToken cancellationToken, params object?[] args)
    {
        if (Map is null) throw new InvalidOperationException("The control is not added to a map.");
        var a = new JsonArray();
        foreach (var arg in args) a.Add(Bridge.BridgeJson.ToNode(arg));
        return Map.CallAsync("control.update", new JsonObject { ["id"] = Id, ["method"] = method, ["args"] = a }, cancellationToken);
    }

    internal static void Put(JsonObject o, string name, object? value)
    {
        if (value is not null) o[name] = Bridge.BridgeJson.ToNode(value);
    }
}

/// <summary>Zoom buttons and compass.</summary>
public sealed class NavigationControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public NavigationControl(ControlPosition? position = ControlPosition.TopRight) : base("navigation", position) { }
    /// <summary>Show the compass button. Default: true.</summary>
    public bool? ShowCompass { get; set; }
    /// <summary>Show the zoom buttons. Default: true.</summary>
    public bool? ShowZoom { get; set; }
    /// <summary>Visualize the pitch by rotating the compass. Default: false.</summary>
    public bool? VisualizePitch { get; set; }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions()
    {
        var o = new JsonObject();
        Put(o, "showCompass", ShowCompass);
        Put(o, "showZoom", ShowZoom);
        Put(o, "visualizePitch", VisualizePitch);
        return o;
    }
}

/// <summary>Units of the scale control.</summary>
public enum ScaleUnit
{
    /// <summary>Metric units.</summary>
    Metric,
    /// <summary>Imperial units.</summary>
    Imperial,
    /// <summary>Nautical miles.</summary>
    Nautical,
}

/// <summary>Distance scale.</summary>
public sealed class ScaleControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public ScaleControl(ControlPosition? position = ControlPosition.BottomLeft) : base("scale", position) { }
    /// <summary>Maximum width in pixels. Default: 100.</summary>
    public double? MaxWidth { get; set; }
    /// <summary>Units. Default: metric.</summary>
    public ScaleUnit? Unit { get; set; }

    /// <summary>Changes the unit.</summary>
    public Task SetUnitAsync(ScaleUnit unit, CancellationToken cancellationToken = default)
        => InvokeControlAsync("setUnit", cancellationToken, unit);

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions()
    {
        var o = new JsonObject();
        Put(o, "maxWidth", MaxWidth);
        Put(o, "unit", Unit);
        return o;
    }
}

/// <summary>Toggles full screen display of the map (within the WebView).</summary>
public sealed class FullscreenControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public FullscreenControl(ControlPosition? position = ControlPosition.TopRight) : base("fullscreen", position) { }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions() => new();
}

/// <summary>Attribution (copyright) control.</summary>
public sealed class AttributionControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public AttributionControl(ControlPosition? position = ControlPosition.BottomRight) : base("attribution", position) { }
    /// <summary>Compact (collapsible) display.</summary>
    public bool? Compact { get; set; }
    /// <summary>Extra attribution text / HTML.</summary>
    public string? CustomAttribution { get; set; }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions()
    {
        var o = new JsonObject();
        Put(o, "compact", Compact);
        Put(o, "customAttribution", CustomAttribution);
        return o;
    }
}

/// <summary>
/// Locates the user with the Geolocation API. The host must allow geolocation
/// (see the <c>AllowGeolocation</c> property of the WPF / WinForms control).
/// </summary>
public sealed class GeolocateControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public GeolocateControl(ControlPosition? position = ControlPosition.TopRight) : base("geolocate", position) { }
    /// <summary>Keeps tracking the position. Default: false.</summary>
    public bool? TrackUserLocation { get; set; }
    /// <summary>Shows the user location dot. Default: true.</summary>
    public bool? ShowUserLocation { get; set; }
    /// <summary>Shows the accuracy circle. Default: true.</summary>
    public bool? ShowAccuracyCircle { get; set; }
    /// <summary>Shows the heading. Default: false.</summary>
    public bool? ShowUserHeading { get; set; }
    /// <summary>Requests high accuracy positions.</summary>
    public bool? EnableHighAccuracy { get; set; }
    /// <summary>Timeout of a position request.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Maximum zoom used when centering on the user.</summary>
    public double? MaxZoom { get; set; }

    /// <summary>Raised when a position is obtained.</summary>
    public event EventHandler<GeolocateEventArgs>? Geolocated;

    /// <summary>Raised when the position cannot be obtained.</summary>
    public event EventHandler<GeolocateErrorEventArgs>? GeolocateError;

    /// <summary>Requests the user position (same as clicking the button).</summary>
    public Task TriggerAsync(CancellationToken cancellationToken = default) => InvokeControlAsync("trigger", cancellationToken);

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions()
    {
        var o = new JsonObject();
        Put(o, "trackUserLocation", TrackUserLocation);
        Put(o, "showUserLocation", ShowUserLocation);
        Put(o, "showAccuracyCircle", ShowAccuracyCircle);
        Put(o, "showUserHeading", ShowUserHeading);
        if (EnableHighAccuracy is not null || Timeout is not null)
        {
            var p = new JsonObject();
            Put(p, "enableHighAccuracy", EnableHighAccuracy);
            if (Timeout is { } t) p["timeout"] = t.TotalMilliseconds;
            o["positionOptions"] = p;
        }
        if (MaxZoom is { } mz) o["fitBoundsOptions"] = new JsonObject { ["maxZoom"] = mz };
        return o;
    }

    /// <inheritdoc />
    protected internal override void OnEvent(string name, JsonElement data)
    {
        if (name == "geolocate")
        {
            var lngLat = data.GetProperty("lngLat").Deserialize<LngLat>(Json.MapboxJson.Options);
            double accuracy = data.TryGetProperty("accuracy", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : double.NaN;
            Geolocated?.Invoke(this, new GeolocateEventArgs(lngLat, accuracy));
        }
        else if (name == "geolocate.error")
        {
            GeolocateError?.Invoke(this, new GeolocateErrorEventArgs(data.TryGetProperty("message", out var m) ? m.GetString() ?? "" : ""));
        }
    }
}

/// <summary>Toggles 3D terrain. MapLibre GL JS only.</summary>
public sealed class TerrainControl : MapControl
{
    /// <summary>Creates the control for a raster-dem source.</summary>
    public TerrainControl(string source, double exaggeration = 1, ControlPosition? position = ControlPosition.TopRight) : base("terrain", position)
    {
        Source = source;
        Exaggeration = exaggeration;
    }
    /// <summary>Raster DEM source id.</summary>
    public string Source { get; set; }
    /// <summary>Exaggeration factor.</summary>
    public double Exaggeration { get; set; }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions() => new() { ["source"] = Source, ["exaggeration"] = Exaggeration };
}

/// <summary>Toggles the globe projection. MapLibre GL JS only.</summary>
public sealed class GlobeControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public GlobeControl(ControlPosition? position = ControlPosition.TopRight) : base("globe", position) { }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions() => new();
}

/// <summary>
/// A custom control rendered from HTML. Elements with a <c>data-command</c> attribute raise
/// <see cref="CommandInvoked"/> when clicked (buttons) or changed (inputs, selects, checkboxes).
/// </summary>
/// <example><code>
/// var ctrl = new HtmlControl("&lt;button data-command=\"home\" title=\"Home\"&gt;⌂&lt;/button&gt;");
/// ctrl.CommandInvoked += (s, e) => { if (e.Command == "home") map.FlyToAsync(...); };
/// await map.AddControlAsync(ctrl);
/// </code></example>
public sealed class HtmlControl : MapControl
{
    /// <summary>Creates the control.</summary>
    public HtmlControl(string html, ControlPosition? position = ControlPosition.TopRight) : base("html", position) => Html = html;
    /// <summary>HTML content.</summary>
    public string Html { get; private set; }
    /// <summary>Additional CSS class names.</summary>
    public string? CssClass { get; set; }
    /// <summary>Render with the standard control group frame. Default: true.</summary>
    public bool Group { get; set; } = true;

    /// <summary>Raised when an element with a <c>data-command</c> attribute is activated.</summary>
    public event EventHandler<ControlCommandEventArgs>? CommandInvoked;

    /// <summary>Replaces the HTML content.</summary>
    public async Task SetHtmlAsync(string html, CancellationToken cancellationToken = default)
    {
        Html = html;
        if (Map is not null) await Map.CallAsync("control.update", new JsonObject { ["id"] = Id, ["html"] = html }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected internal override JsonObject BuildOptions() => new()
    {
        ["html"] = Html,
        ["className"] = CssClass,
        ["group"] = Group,
    };

    /// <inheritdoc />
    protected internal override void OnEvent(string name, JsonElement data)
    {
        if (name != "control.command") return;
        CommandInvoked?.Invoke(this, new ControlCommandEventArgs(
            data.GetProperty("command").GetString() ?? string.Empty,
            data.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null));
    }
}

/// <summary>Arguments of <see cref="HtmlControl.CommandInvoked"/>.</summary>
public sealed class ControlCommandEventArgs : EventArgs
{
    internal ControlCommandEventArgs(string command, string? value) { Command = command; Value = value; }
    /// <summary>Value of the <c>data-command</c> attribute.</summary>
    public string Command { get; }
    /// <summary>Value of the input element, if any.</summary>
    public string? Value { get; }
}

/// <summary>Arguments of <see cref="GeolocateControl.Geolocated"/>.</summary>
public sealed class GeolocateEventArgs : EventArgs
{
    internal GeolocateEventArgs(LngLat position, double accuracy) { Position = position; Accuracy = accuracy; }
    /// <summary>User position.</summary>
    public LngLat Position { get; }
    /// <summary>Accuracy in meters.</summary>
    public double Accuracy { get; }
}

/// <summary>Arguments of <see cref="GeolocateControl.GeolocateError"/>.</summary>
public sealed class GeolocateErrorEventArgs : EventArgs
{
    internal GeolocateErrorEventArgs(string message) => Message = message;
    /// <summary>Error message.</summary>
    public string Message { get; }
}

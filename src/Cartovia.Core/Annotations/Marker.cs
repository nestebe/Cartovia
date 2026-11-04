using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Cartovia.Events;
using Cartovia.Style;

namespace Cartovia.Annotations;

/// <summary>Alignment of a marker relative to the map plane.</summary>
public enum MarkerAlignment
{
    /// <summary>Aligned to the map.</summary>
    Map,
    /// <summary>Aligned to the viewport.</summary>
    Viewport,
    /// <summary>Automatic.</summary>
    Auto,
}

/// <summary>Definition of a marker (a DOM element positioned on the map).</summary>
public sealed class MarkerOptions
{
    /// <summary>Creates options for a marker at a position.</summary>
    public MarkerOptions(LngLat position) => Position = position;

    /// <summary>Position.</summary>
    public LngLat Position { get; set; }
    /// <summary>Color of the default pin (ignored with <see cref="Html"/>).</summary>
    public string? Color { get; set; }
    /// <summary>Scale of the default pin.</summary>
    public double? Scale { get; set; }
    /// <summary>Custom HTML content replacing the default pin.</summary>
    public string? Html { get; set; }
    /// <summary>CSS class names of the marker element.</summary>
    public string? CssClass { get; set; }
    /// <summary>Tooltip (HTML <c>title</c>).</summary>
    public string? Tooltip { get; set; }
    /// <summary>CSS cursor over the marker (e.g. <c>pointer</c>).</summary>
    public string? Cursor { get; set; } = "pointer";
    /// <summary>Allows dragging the marker.</summary>
    public bool Draggable { get; set; }
    /// <summary>Rotation in degrees.</summary>
    public double? Rotation { get; set; }
    /// <summary>Rotation alignment.</summary>
    public MarkerAlignment? RotationAlignment { get; set; }
    /// <summary>Pitch alignment.</summary>
    public MarkerAlignment? PitchAlignment { get; set; }
    /// <summary>Part of the element placed at the position. Default: center (bottom for the default pin).</summary>
    public Anchor? Anchor { get; set; }
    /// <summary>Pixel offset.</summary>
    public ScreenPoint? Offset { get; set; }
    /// <summary>Popup opened when the marker is clicked.</summary>
    public PopupOptions? Popup { get; set; }
    /// <summary>Opacity when hidden behind terrain / 3D objects (Mapbox). </summary>
    public double? OccludedOpacity { get; set; }
    /// <summary>Arbitrary user data.</summary>
    public object? Tag { get; set; }

    internal JsonObject ToJson(string id)
    {
        var o = new JsonObject();
        if (Color is not null) o["color"] = Color;
        if (Scale is { } s) o["scale"] = s;
        if (CssClass is not null) o["className"] = CssClass;
        o["draggable"] = Draggable;
        if (Rotation is { } r) o["rotation"] = r;
        if (RotationAlignment is { } ra) o["rotationAlignment"] = BridgeJson.ToNode(ra);
        if (PitchAlignment is { } pa) o["pitchAlignment"] = BridgeJson.ToNode(pa);
        if (Anchor is { } an) o["anchor"] = BridgeJson.ToNode(an);
        if (Offset is { } off) o["offset"] = BridgeJson.ToNode(off);
        if (OccludedOpacity is { } oo) o["occludedOpacity"] = oo;
        return new JsonObject
        {
            ["id"] = id,
            ["lngLat"] = BridgeJson.ToNode(Position),
            ["options"] = o,
            ["html"] = Html,
            ["cursor"] = Cursor,
            ["tooltip"] = Tooltip,
            ["popup"] = Popup?.ToJson(),
        };
    }
}

/// <summary>Arguments of marker events.</summary>
public sealed class MarkerEventArgs : EventArgs
{
    internal MarkerEventArgs(Marker marker, LngLat position) { Marker = marker; Position = position; }
    /// <summary>The marker.</summary>
    public Marker Marker { get; }
    /// <summary>Marker position at the time of the event.</summary>
    public LngLat Position { get; }
}

/// <summary>A marker on the map. Created with <see cref="CartoviaMap.AddMarkerAsync(MarkerOptions, CancellationToken)"/>.</summary>
public sealed class Marker
{
    private readonly CartoviaMap _map;

    internal Marker(CartoviaMap map, MarkerOptions options)
    {
        _map = map;
        Options = options;
        Id = CartoviaMap.NewId("marker");
    }

    /// <summary>Unique id.</summary>
    public string Id { get; }

    /// <summary>Options (kept in sync with the setters of this class).</summary>
    public MarkerOptions Options { get; }

    /// <summary>Current position (updated while dragging).</summary>
    public LngLat Position => Options.Position;

    /// <summary>Arbitrary user data.</summary>
    public object? Tag { get => Options.Tag; set => Options.Tag = value; }

    /// <summary>True once removed.</summary>
    public bool IsRemoved { get; internal set; }

    /// <summary>The marker was clicked.</summary>
    public event EventHandler<MarkerEventArgs>? Click;
    /// <summary>The marker was double-clicked.</summary>
    public event EventHandler<MarkerEventArgs>? DoubleClick;
    /// <summary>The marker was right-clicked.</summary>
    public event EventHandler<MarkerEventArgs>? ContextMenu;
    /// <summary>The pointer entered the marker.</summary>
    public event EventHandler<MarkerEventArgs>? MouseEnter;
    /// <summary>The pointer left the marker.</summary>
    public event EventHandler<MarkerEventArgs>? MouseLeave;
    /// <summary>A drag starts.</summary>
    public event EventHandler<MarkerEventArgs>? DragStarted;
    /// <summary>Dragging (throttled).</summary>
    public event EventHandler<MarkerEventArgs>? Dragging;
    /// <summary>A drag ended.</summary>
    public event EventHandler<MarkerEventArgs>? DragEnded;

    /// <summary>Moves the marker.</summary>
    public async Task SetPositionAsync(LngLat position, CancellationToken cancellationToken = default)
    {
        await InvokeAsync("setLngLat", cancellationToken, position).ConfigureAwait(false);
        Options.Position = position;
    }

    /// <summary>Enables or disables dragging.</summary>
    public async Task SetDraggableAsync(bool draggable, CancellationToken cancellationToken = default)
    {
        await InvokeAsync("setDraggable", cancellationToken, draggable).ConfigureAwait(false);
        Options.Draggable = draggable;
    }

    /// <summary>Sets the rotation.</summary>
    public async Task SetRotationAsync(double rotation, CancellationToken cancellationToken = default)
    {
        await InvokeAsync("setRotation", cancellationToken, rotation).ConfigureAwait(false);
        Options.Rotation = rotation;
    }

    /// <summary>Sets the pixel offset.</summary>
    public async Task SetOffsetAsync(ScreenPoint offset, CancellationToken cancellationToken = default)
    {
        await InvokeAsync("setOffset", cancellationToken, offset).ConfigureAwait(false);
        Options.Offset = offset;
    }

    /// <summary>Replaces the custom HTML content (markers created with <see cref="MarkerOptions.Html"/>).</summary>
    public async Task SetHtmlAsync(string html, CancellationToken cancellationToken = default)
    {
        await _map.CallAsync("marker.update", new JsonObject { ["id"] = Id, ["html"] = html }, cancellationToken).ConfigureAwait(false);
        Options.Html = html;
    }

    /// <summary>Changes the tooltip.</summary>
    public async Task SetTooltipAsync(string? tooltip, CancellationToken cancellationToken = default)
    {
        await _map.CallAsync("marker.update", new JsonObject { ["id"] = Id, ["tooltip"] = tooltip ?? string.Empty }, cancellationToken).ConfigureAwait(false);
        Options.Tooltip = tooltip;
    }

    /// <summary>Attaches (or detaches with null) a popup opened on click.</summary>
    public async Task SetPopupAsync(PopupOptions? popup, CancellationToken cancellationToken = default)
    {
        await _map.CallAsync("marker.update", new JsonObject { ["id"] = Id, ["popup"] = popup?.ToJson() ?? (JsonNode?)JsonValue.Create(false) }, cancellationToken).ConfigureAwait(false);
        Options.Popup = popup;
    }

    /// <summary>Opens or closes the attached popup.</summary>
    public Task TogglePopupAsync(CancellationToken cancellationToken = default) => InvokeAsync("togglePopup", cancellationToken);

    /// <summary>Calls any method of the JavaScript marker.</summary>
    public Task<JsonElement> InvokeAsync(string method, CancellationToken cancellationToken, params object?[] args)
        => _map.CallAsync("marker.invoke", new JsonObject { ["id"] = Id, ["method"] = method, ["args"] = BridgeJson.ToArray(args) }, cancellationToken);

    /// <summary>Removes the marker.</summary>
    public Task RemoveAsync(CancellationToken cancellationToken = default) => _map.RemoveMarkerAsync(this, cancellationToken);

    internal void OnEvent(string name, JsonElement data)
    {
        var pos = MapEventArgs.Get<LngLat>(data, "lngLat") ?? Options.Position;
        if (name is "drag" or "dragend" or "dragstart") Options.Position = pos;
        var args = new MarkerEventArgs(this, pos);
        var handler = name switch
        {
            "click" => Click,
            "dblclick" => DoubleClick,
            "contextmenu" => ContextMenu,
            "mouseenter" => MouseEnter,
            "mouseleave" => MouseLeave,
            "dragstart" => DragStarted,
            "drag" => Dragging,
            "dragend" => DragEnded,
            _ => null,
        };
        handler?.Invoke(this, args);
    }
}

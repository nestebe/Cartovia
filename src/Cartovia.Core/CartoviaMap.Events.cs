using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Cartovia.Events;

namespace Cartovia;

/// <summary>Mouse events that can be bound to a layer.</summary>
public enum LayerEvent
{
    /// <summary><c>click</c></summary>
    Click,
    /// <summary><c>dblclick</c></summary>
    DoubleClick,
    /// <summary><c>contextmenu</c> (right click)</summary>
    ContextMenu,
    /// <summary><c>mousedown</c></summary>
    MouseDown,
    /// <summary><c>mouseup</c></summary>
    MouseUp,
    /// <summary><c>mousemove</c> over the layer (throttled)</summary>
    MouseMove,
    /// <summary><c>mouseenter</c>: the pointer enters a feature of the layer</summary>
    MouseEnter,
    /// <summary><c>mouseleave</c>: the pointer leaves the layer</summary>
    MouseLeave,
    /// <summary><c>mouseover</c></summary>
    MouseOver,
    /// <summary><c>mouseout</c></summary>
    MouseOut,
}

public sealed partial class CartoviaMap
{
    private readonly Dictionary<string, long> _globalSubs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, LayerSubscription> _layerSubs = new();
    private readonly Dictionary<string, Delegate?> _handlers = new(StringComparer.Ordinal);

    private sealed record LayerSubscription(string EventName, string LayerId, EventHandler<MapMouseEventArgs> Handler);

    // ------------------------------------------------------------------ lifecycle & style events

    /// <summary>The map and its style are loaded: calls can be made (they are queued until then anyway).</summary>
    public event EventHandler<MapCameraEventArgs>? Loaded { add => Add("load", value); remove => Remove("load", value); }

    /// <summary>A style finished loading (initially and after every <c>SetStyleAsync</c>).</summary>
    public event EventHandler<MapEventArgs>? StyleLoaded { add => Add("style.load", value); remove => Remove("style.load", value); }

    /// <summary>
    /// The sources / layers added through the API were re-added after a style change
    /// (see <see cref="MapOptions.PreserveUserLayersOnStyleChange"/>).
    /// </summary>
    public event EventHandler<MapEventArgs>? UserLayersRestored { add => Add("cartovia.stylerestored", value); remove => Remove("cartovia.stylerestored", value); }

    /// <summary>The map finished rendering and no animation is running.</summary>
    public event EventHandler<MapEventArgs>? Idle { add => Add("idle", value); remove => Remove("idle", value); }

    /// <summary>The map was resized.</summary>
    public event EventHandler<MapEventArgs>? Resized { add => Add("resize", value); remove => Remove("resize", value); }

    /// <summary>An error occurred (tile / style loading, invalid layer, fatal initialization error…).</summary>
    public event EventHandler<MapErrorEventArgs>? Error { add => Add("error", value); remove => Remove("error", value); }

    /// <summary>An image referenced by the style is missing; add it with <c>AddImageAsync</c>.</summary>
    public event EventHandler<StyleImageMissingEventArgs>? StyleImageMissing { add => Add("styleimagemissing", value); remove => Remove("styleimagemissing", value); }

    /// <summary>Any data (style or source) loaded or changed.</summary>
    public event EventHandler<MapDataEventArgs>? DataChanged { add => Add("data", value); remove => Remove("data", value); }

    /// <summary>Source data loaded or changed.</summary>
    public event EventHandler<MapDataEventArgs>? SourceDataChanged { add => Add("sourcedata", value); remove => Remove("sourcedata", value); }

    /// <summary>Style data loaded or changed.</summary>
    public event EventHandler<MapDataEventArgs>? StyleDataChanged { add => Add("styledata", value); remove => Remove("styledata", value); }

    /// <summary>A frame was rendered (high frequency, throttled).</summary>
    public event EventHandler<MapEventArgs>? Rendered { add => Add("render", value); remove => Remove("render", value); }

    // ------------------------------------------------------------------ mouse events

    /// <summary>Click on the map.</summary>
    public event EventHandler<MapMouseEventArgs>? Click { add => Add("click", value); remove => Remove("click", value); }
    /// <summary>Double click on the map.</summary>
    public event EventHandler<MapMouseEventArgs>? DoubleClick { add => Add("dblclick", value); remove => Remove("dblclick", value); }
    /// <summary>Right click on the map.</summary>
    public event EventHandler<MapMouseEventArgs>? ContextMenu { add => Add("contextmenu", value); remove => Remove("contextmenu", value); }
    /// <summary>Mouse button pressed.</summary>
    public event EventHandler<MapMouseEventArgs>? MouseDown { add => Add("mousedown", value); remove => Remove("mousedown", value); }
    /// <summary>Mouse button released.</summary>
    public event EventHandler<MapMouseEventArgs>? MouseUp { add => Add("mouseup", value); remove => Remove("mouseup", value); }
    /// <summary>Pointer moved (throttled, see <see cref="MapOptions.EventThrottle"/>).</summary>
    public event EventHandler<MapMouseEventArgs>? MouseMove { add => Add("mousemove", value); remove => Remove("mousemove", value); }
    /// <summary>Pointer entered the map.</summary>
    public event EventHandler<MapMouseEventArgs>? MouseOver { add => Add("mouseover", value); remove => Remove("mouseover", value); }
    /// <summary>Pointer left the map.</summary>
    public event EventHandler<MapMouseEventArgs>? MouseOut { add => Add("mouseout", value); remove => Remove("mouseout", value); }
    /// <summary>Mouse wheel (throttled).</summary>
    public event EventHandler<MapMouseEventArgs>? Wheel { add => Add("wheel", value); remove => Remove("wheel", value); }

    // ------------------------------------------------------------------ camera events

    /// <summary>A camera movement starts.</summary>
    public event EventHandler<MapCameraEventArgs>? MoveStarted { add => Add("movestart", value); remove => Remove("movestart", value); }
    /// <summary>The camera moves (throttled).</summary>
    public event EventHandler<MapCameraEventArgs>? Moving { add => Add("move", value); remove => Remove("move", value); }
    /// <summary>A camera movement ended.</summary>
    public event EventHandler<MapCameraEventArgs>? MoveEnded { add => Add("moveend", value); remove => Remove("moveend", value); }
    /// <summary>A zoom starts.</summary>
    public event EventHandler<MapCameraEventArgs>? ZoomStarted { add => Add("zoomstart", value); remove => Remove("zoomstart", value); }
    /// <summary>Zooming (throttled).</summary>
    public event EventHandler<MapCameraEventArgs>? Zooming { add => Add("zoom", value); remove => Remove("zoom", value); }
    /// <summary>A zoom ended.</summary>
    public event EventHandler<MapCameraEventArgs>? ZoomEnded { add => Add("zoomend", value); remove => Remove("zoomend", value); }
    /// <summary>A rotation starts.</summary>
    public event EventHandler<MapCameraEventArgs>? RotateStarted { add => Add("rotatestart", value); remove => Remove("rotatestart", value); }
    /// <summary>Rotating (throttled).</summary>
    public event EventHandler<MapCameraEventArgs>? Rotating { add => Add("rotate", value); remove => Remove("rotate", value); }
    /// <summary>A rotation ended.</summary>
    public event EventHandler<MapCameraEventArgs>? RotateEnded { add => Add("rotateend", value); remove => Remove("rotateend", value); }
    /// <summary>A pitch change starts.</summary>
    public event EventHandler<MapCameraEventArgs>? PitchStarted { add => Add("pitchstart", value); remove => Remove("pitchstart", value); }
    /// <summary>Pitching (throttled).</summary>
    public event EventHandler<MapCameraEventArgs>? Pitching { add => Add("pitch", value); remove => Remove("pitch", value); }
    /// <summary>A pitch change ended.</summary>
    public event EventHandler<MapCameraEventArgs>? PitchEnded { add => Add("pitchend", value); remove => Remove("pitchend", value); }
    /// <summary>A drag-pan starts.</summary>
    public event EventHandler<MapCameraEventArgs>? DragStarted { add => Add("dragstart", value); remove => Remove("dragstart", value); }
    /// <summary>Dragging (throttled).</summary>
    public event EventHandler<MapCameraEventArgs>? Dragging { add => Add("drag", value); remove => Remove("drag", value); }
    /// <summary>A drag-pan ended.</summary>
    public event EventHandler<MapCameraEventArgs>? DragEnded { add => Add("dragend", value); remove => Remove("dragend", value); }
    /// <summary>A box zoom (shift + drag) starts.</summary>
    public event EventHandler<MapCameraEventArgs>? BoxZoomStarted { add => Add("boxzoomstart", value); remove => Remove("boxzoomstart", value); }
    /// <summary>A box zoom ended.</summary>
    public event EventHandler<MapCameraEventArgs>? BoxZoomEnded { add => Add("boxzoomend", value); remove => Remove("boxzoomend", value); }
    /// <summary>A box zoom was cancelled.</summary>
    public event EventHandler<MapCameraEventArgs>? BoxZoomCancelled { add => Add("boxzoomcancel", value); remove => Remove("boxzoomcancel", value); }

    // ------------------------------------------------------------------ generic & layer subscriptions

    /// <summary>
    /// Subscribes to any engine event by name (<c>"sourcedataloading"</c>, <c>"webglcontextlost"</c>…).
    /// The arguments are the typed subclass matching the event when there is one.
    /// </summary>
    /// <returns>Dispose to unsubscribe.</returns>
    public IDisposable On(string eventName, EventHandler<MapEventArgs> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        Add(eventName, handler);
        return new ActionDisposable(() => Remove(eventName, handler));
    }

    /// <summary>Subscribes to a mouse event on the features of a layer.</summary>
    /// <param name="layerId">Layer id.</param>
    /// <param name="eventType">Event type.</param>
    /// <param name="handler">Handler; <see cref="MapMouseEventArgs.Features"/> holds the features under the pointer.</param>
    /// <returns>Dispose to unsubscribe.</returns>
    public IDisposable OnLayer(string layerId, LayerEvent eventType, EventHandler<MapMouseEventArgs> handler)
        => OnLayer(layerId, LayerEventName(eventType), handler);

    /// <summary>Subscribes to a mouse event (by engine name) on the features of a layer.</summary>
    public IDisposable OnLayer(string layerId, string eventName, EventHandler<MapMouseEventArgs> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        long sid = NextId();
        _layerSubs[sid] = new LayerSubscription(eventName, layerId, handler);
        if (_initSent)
        {
            FireAndForget(CallAsync("events.on", new JsonObject { ["sid"] = sid, ["name"] = eventName, ["layer"] = layerId },
                CancellationToken.None, Readiness.Created), $"Subscription to '{eventName}' on layer '{layerId}'");
        }
        return new ActionDisposable(() =>
        {
            if (_layerSubs.TryRemove(sid, out _) && !_disposed && _created.Task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                FireAndForget(CallAsync("events.off", new JsonObject { ["sid"] = sid }, CancellationToken.None, Readiness.Created), "Unsubscription");
        });
    }

    private static string LayerEventName(LayerEvent e) => e switch
    {
        LayerEvent.Click => "click",
        LayerEvent.DoubleClick => "dblclick",
        LayerEvent.ContextMenu => "contextmenu",
        LayerEvent.MouseDown => "mousedown",
        LayerEvent.MouseUp => "mouseup",
        LayerEvent.MouseMove => "mousemove",
        LayerEvent.MouseEnter => "mouseenter",
        LayerEvent.MouseLeave => "mouseleave",
        LayerEvent.MouseOver => "mouseover",
        LayerEvent.MouseOut => "mouseout",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    private void Add(string name, Delegate? handler)
    {
        if (handler is null) return;
        bool subscribe = false;
        long sid = 0;
        lock (_gate)
        {
            _handlers.TryGetValue(name, out var existing);
            _handlers[name] = Delegate.Combine(existing, handler);
            if (!_globalSubs.ContainsKey(name) && !name.StartsWith("cartovia.", StringComparison.Ordinal))
            {
                sid = NextId();
                _globalSubs[name] = sid;
                subscribe = _initSent;
            }
        }
        if (subscribe)
        {
            FireAndForget(CallAsync("events.on", new JsonObject { ["sid"] = sid, ["name"] = name }, CancellationToken.None, Readiness.Created),
                $"Subscription to '{name}'");
        }
    }

    private void Remove(string name, Delegate? handler)
    {
        if (handler is null) return;
        lock (_gate)
        {
            if (_handlers.TryGetValue(name, out var existing)) _handlers[name] = Delegate.Remove(existing, handler);
        }
    }

    // ------------------------------------------------------------------ dispatch

    private void DispatchEvent(JsonElement root)
    {
        var name = MapEventArgs.Str(root, "name") ?? string.Empty;
        var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;

        var target = MapEventArgs.Str(root, "target");
        if (target is not null)
        {
            DispatchTargetEvent(name, target, data);
            return;
        }

        var layer = MapEventArgs.Str(root, "layer");
        if (layer is not null)
        {
            if (root.TryGetProperty("sid", out var sidProp) && _layerSubs.TryGetValue(sidProp.GetInt64(), out var sub))
                sub.Handler(this, new MapMouseEventArgs(name, data, layer));
            return;
        }

        switch (name)
        {
            case "load": OnLoadedInternal(data); break;
            case "moveend": Camera = CameraState.FromJson(data); break;
        }
        RaiseTyped(name, CreateArgs(name, data));
    }

    private void RaiseTyped(string name, MapEventArgs args)
    {
        Delegate? handlers;
        lock (_gate) _handlers.TryGetValue(name, out handlers);
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList())
        {
            switch (h)
            {
                case EventHandler<MapEventArgs> g: g(this, args); break;
                case EventHandler<MapMouseEventArgs> m when args is MapMouseEventArgs ma: m(this, ma); break;
                case EventHandler<MapCameraEventArgs> c when args is MapCameraEventArgs ca: c(this, ca); break;
                case EventHandler<MapErrorEventArgs> e when args is MapErrorEventArgs ea: e(this, ea); break;
                case EventHandler<MapDataEventArgs> dd when args is MapDataEventArgs da: dd(this, da); break;
                case EventHandler<StyleImageMissingEventArgs> s when args is StyleImageMissingEventArgs sa: s(this, sa); break;
            }
        }
    }

    private static readonly HashSet<string> MouseEventNames = new(StringComparer.Ordinal)
    {
        "click", "dblclick", "contextmenu", "mousedown", "mouseup", "mousemove", "mouseover", "mouseout",
        "mouseenter", "mouseleave", "wheel", "touchstart", "touchend", "touchmove", "touchcancel",
    };

    internal static MapEventArgs CreateArgs(string name, JsonElement data)
    {
        if (MouseEventNames.Contains(name)) return new MapMouseEventArgs(name, data);
        if (name == "load" || IsCameraEvent(name)) return new MapCameraEventArgs(name, data);
        return name switch
        {
            "error" => new MapErrorEventArgs(name, data),
            "styleimagemissing" => new StyleImageMissingEventArgs(name, data),
            "data" or "dataloading" or "sourcedata" or "sourcedataloading" or "styledata" or "styledataloading" => new MapDataEventArgs(name, data),
            _ => new MapEventArgs(name, data),
        };
    }

    private static bool IsCameraEvent(string name)
    {
        foreach (var prefix in new[] { "move", "zoom", "rotate", "pitch", "drag", "boxzoom" })
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = name.Substring(prefix.Length);
            if (rest is "" or "start" or "end" or "cancel") return true;
        }
        return false;
    }

    private void DispatchTargetEvent(string name, string target, JsonElement data)
    {
        if (name.StartsWith("marker.", StringComparison.Ordinal))
        {
            if (_markers.TryGetValue(target, out var marker)) marker.OnEvent(name.Substring(7), data);
        }
        else if (name.StartsWith("popup.", StringComparison.Ordinal))
        {
            if (_popups.TryGetValue(target, out var popup)) popup.OnEvent(name.Substring(6), data);
        }
        else if (name == "plugin")
        {
            if (_plugins.TryGetValue(target, out var plugin))
                plugin.RaiseEvent(MapEventArgs.Str(data, "event") ?? string.Empty, data.TryGetProperty("data", out var pd) ? pd.Clone() : default);
        }
        else if (name == "interaction")
        {
            if (_interactions.TryGetValue(target, out var interaction)) interaction(this, new InteractionEventArgs(data));
        }
        else if (_controls.TryGetValue(target, out var control))
        {
            control.OnEvent(name, data);
        }
    }
}

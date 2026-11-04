using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Bridge;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.Json;

namespace Cartovia;

/// <summary>Kinds of Mapbox v3 interactions (featureset events of Mapbox Standard and other styles).</summary>
public enum InteractionType
{
    /// <summary>Click.</summary>
    Click,
    /// <summary>Double click.</summary>
    [JsonValue("dblclick")] DoubleClick,
    /// <summary>Right click.</summary>
    [JsonValue("contextmenu")] ContextMenu,
    /// <summary>Pointer enters a feature.</summary>
    [JsonValue("mouseenter")] MouseEnter,
    /// <summary>Pointer leaves a feature.</summary>
    [JsonValue("mouseleave")] MouseLeave,
    /// <summary>Pointer moves over a feature.</summary>
    [JsonValue("mousemove")] MouseMove,
    /// <summary>Mouse button pressed.</summary>
    [JsonValue("mousedown")] MouseDown,
    /// <summary>Mouse button released.</summary>
    [JsonValue("mouseup")] MouseUp,
}

/// <summary>Definition of a Mapbox v3 interaction (<c>map.addInteraction</c>, Mapbox GL JS 3.9+).</summary>
public sealed class InteractionOptions
{
    /// <summary>Creates an interaction.</summary>
    public InteractionOptions(InteractionType type) => Type = type;

    /// <summary>Event type.</summary>
    public InteractionType Type { get; }
    /// <summary>Featureset id (e.g. <c>poi</c>, <c>buildings</c>, <c>place-labels</c> in Mapbox Standard).</summary>
    public string? FeaturesetId { get; set; }
    /// <summary>Import id of the featureset (e.g. <c>basemap</c>).</summary>
    public string? ImportId { get; set; }
    /// <summary>Targets a layer instead of a featureset.</summary>
    public string? LayerId { get; set; }
    /// <summary>Filter expression.</summary>
    public Style.Expression? Filter { get; set; }
    /// <summary>Stops propagation to interactions below. Default: true.</summary>
    public bool StopPropagation { get; set; } = true;
}

/// <summary>Arguments of an interaction.</summary>
public sealed class InteractionEventArgs : EventArgs
{
    internal InteractionEventArgs(JsonElement data)
    {
        Data = data;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("feature", out var f) && f.ValueKind == JsonValueKind.Object)
            Feature = QueriedFeature.FromJson(f);
        LngLat = MapEventArgs.Get<LngLat>(data, "lngLat");
        Point = MapEventArgs.Get<ScreenPoint>(data, "point");
    }

    /// <summary>Raw payload.</summary>
    public JsonElement Data { get; }
    /// <summary>Target feature (with featureset namespace / id).</summary>
    public QueriedFeature? Feature { get; }
    /// <summary>Pointer position.</summary>
    public LngLat? LngLat { get; }
    /// <summary>Pointer pixel position.</summary>
    public ScreenPoint? Point { get; }
}

public sealed partial class CartoviaMap
{
    private readonly ConcurrentDictionary<string, Marker> _markers = new();
    private readonly ConcurrentDictionary<string, Popup> _popups = new();
    private readonly ConcurrentDictionary<string, MapControl> _controls = new();
    private readonly ConcurrentDictionary<string, EventHandler<InteractionEventArgs>> _interactions = new();

    // ------------------------------------------------------------------ markers

    /// <summary>Markers currently on the map.</summary>
    public IReadOnlyCollection<Marker> Markers => _markers.Values.ToList();

    /// <summary>Adds a marker.</summary>
    public async Task<Marker> AddMarkerAsync(MarkerOptions options, CancellationToken cancellationToken = default)
    {
        var marker = new Marker(this, options ?? throw new ArgumentNullException(nameof(options)));
        _markers[marker.Id] = marker;
        try
        {
            await CreateMarkerAsync(marker, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _markers.TryRemove(marker.Id, out _);
            throw;
        }
        return marker;
    }

    /// <summary>Adds a default pin marker.</summary>
    public Task<Marker> AddMarkerAsync(LngLat position, string? color = null, string? popupHtml = null, CancellationToken cancellationToken = default)
        => AddMarkerAsync(new MarkerOptions(position)
        {
            Color = color,
            Popup = popupHtml is null ? null : new PopupOptions { Html = popupHtml },
        }, cancellationToken);

    internal Task CreateMarkerAsync(Marker marker, CancellationToken cancellationToken)
        => CallAsync("marker.add", marker.Options.ToJson(marker.Id), cancellationToken);

    internal async Task RemoveMarkerAsync(Marker marker, CancellationToken cancellationToken)
    {
        if (!_markers.TryRemove(marker.Id, out _)) return;
        marker.IsRemoved = true;
        await CallAsync("marker.remove", new JsonObject { ["id"] = marker.Id }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes every marker.</summary>
    public async Task ClearMarkersAsync(CancellationToken cancellationToken = default)
    {
        foreach (var m in _markers.Values.ToList()) await RemoveMarkerAsync(m, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ popups

    /// <summary>Opens a standalone popup.</summary>
    public async Task<Popup> ShowPopupAsync(PopupOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Position is null && !options.TrackPointer) throw new ArgumentException("A popup needs a Position (or TrackPointer).", nameof(options));
        var popup = new Popup(this, options);
        _popups[popup.Id] = popup;
        var json = options.ToJson();
        json["id"] = popup.Id;
        await CallAsync("popup.add", json, cancellationToken).ConfigureAwait(false);
        popup.IsOpen = true;
        return popup;
    }

    /// <summary>Opens a standalone HTML popup at a position.</summary>
    public Task<Popup> ShowPopupAsync(LngLat position, string html, CancellationToken cancellationToken = default)
        => ShowPopupAsync(new PopupOptions { Position = position, Html = html }, cancellationToken);

    internal async Task ClosePopupAsync(Popup popup, CancellationToken cancellationToken)
    {
        if (!_popups.TryRemove(popup.Id, out _)) return;
        popup.IsOpen = false;
        await CallAsync("popup.remove", new JsonObject { ["id"] = popup.Id }, cancellationToken).ConfigureAwait(false);
    }

    internal void ForgetPopup(Popup popup) => _popups.TryRemove(popup.Id, out _);

    /// <summary>Closes every standalone popup.</summary>
    public async Task CloseAllPopupsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var p in _popups.Values.ToList()) await ClosePopupAsync(p, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ controls

    /// <summary>Controls currently on the map.</summary>
    public IReadOnlyCollection<MapControl> Controls => _controls.Values.ToList();

    /// <summary>Adds a control.</summary>
    public async Task<T> AddControlAsync<T>(T control, CancellationToken cancellationToken = default) where T : MapControl
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (control.Map is not null && control.Map != this) throw new InvalidOperationException("The control already belongs to another map.");
        control.Map = this;
        _controls[control.Id] = control;
        try
        {
            await CallAsync("control.add", control.ToDefinition(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _controls.TryRemove(control.Id, out _);
            control.Map = null;
            throw;
        }
        return control;
    }

    /// <summary>Removes a control.</summary>
    public async Task RemoveControlAsync(MapControl control, CancellationToken cancellationToken = default)
    {
        if (!_controls.TryRemove(control.Id, out _)) return;
        control.Map = null;
        await CallAsync("control.remove", new JsonObject { ["id"] = control.Id }, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ interactions (Mapbox v3)

    /// <summary>
    /// Adds a Mapbox v3 interaction, e.g. a click on the POIs of the Standard style:
    /// <c>AddInteractionAsync(new InteractionOptions(InteractionType.Click) { FeaturesetId = "poi", ImportId = "basemap" }, handler)</c>.
    /// </summary>
    /// <returns>Dispose to remove the interaction.</returns>
    public async Task<IDisposable> AddInteractionAsync(InteractionOptions options, EventHandler<InteractionEventArgs> handler, CancellationToken cancellationToken = default)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var id = NewId("interaction");
        JsonObject? target = null;
        if (options.FeaturesetId is not null)
        {
            target = new JsonObject { ["featuresetId"] = options.FeaturesetId };
            if (options.ImportId is not null) target["importId"] = options.ImportId;
        }
        else if (options.LayerId is not null)
        {
            target = new JsonObject { ["layerId"] = options.LayerId };
        }
        _interactions[id] = handler;
        await CallAsync("interaction.add", new JsonObject
        {
            ["id"] = id,
            ["type"] = CartoviaJson.EnumToString(options.Type),
            ["target"] = target,
            ["filter"] = options.Filter?.ToJsonNode(),
            ["stopPropagation"] = options.StopPropagation,
        }, cancellationToken).ConfigureAwait(false);
        return new ActionDisposable(() =>
        {
            if (_interactions.TryRemove(id, out _) && !_disposed)
                FireAndForget(CallAsync("interaction.remove", new JsonObject { ["id"] = id }, CancellationToken.None), "Interaction removal");
        });
    }
}

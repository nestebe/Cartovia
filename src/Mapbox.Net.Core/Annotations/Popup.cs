using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Bridge;
using Mapbox.Net.Style;

namespace Mapbox.Net.Annotations;

/// <summary>Definition of a popup.</summary>
public sealed class PopupOptions
{
    /// <summary>Position (not needed for popups attached to a marker).</summary>
    public LngLat? Position { get; set; }
    /// <summary>HTML content (sanitize untrusted content yourself).</summary>
    public string? Html { get; set; }
    /// <summary>Plain text content (used when <see cref="Html"/> is null).</summary>
    public string? Text { get; set; }
    /// <summary>Show a close button. Default: true.</summary>
    public bool? CloseButton { get; set; }
    /// <summary>Close when the map is clicked. Default: true.</summary>
    public bool? CloseOnClick { get; set; }
    /// <summary>Close when the map moves. Default: false.</summary>
    public bool? CloseOnMove { get; set; }
    /// <summary>Focus the popup when opened. Default: true.</summary>
    public bool? FocusAfterOpen { get; set; }
    /// <summary>Anchor of the popup relative to its position. Null: automatic.</summary>
    public Anchor? Anchor { get; set; }
    /// <summary>Distance offset in pixels.</summary>
    public double? Offset { get; set; }
    /// <summary>CSS max width (e.g. <c>"300px"</c>, <c>"none"</c>). Default: 240px.</summary>
    public string? MaxWidth { get; set; }
    /// <summary>CSS class names.</summary>
    public string? CssClass { get; set; }
    /// <summary>Follow the mouse pointer instead of a fixed position (standalone popups).</summary>
    public bool TrackPointer { get; set; }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        if (CloseButton is { } cb) o["closeButton"] = cb;
        if (CloseOnClick is { } cc) o["closeOnClick"] = cc;
        if (CloseOnMove is { } cm) o["closeOnMove"] = cm;
        if (FocusAfterOpen is { } fo) o["focusAfterOpen"] = fo;
        if (Anchor is { } a) o["anchor"] = BridgeJson.ToNode(a);
        if (Offset is { } off) o["offset"] = off;
        if (MaxWidth is not null) o["maxWidth"] = MaxWidth;
        if (CssClass is not null) o["className"] = CssClass;
        return new JsonObject
        {
            ["lngLat"] = Position is { } p ? BridgeJson.ToNode(p) : null,
            ["html"] = Html,
            ["text"] = Html is null ? Text : null,
            ["options"] = o,
            ["trackPointer"] = TrackPointer,
        };
    }
}

/// <summary>A standalone popup. Created with <see cref="MapboxMap.ShowPopupAsync(PopupOptions, System.Threading.CancellationToken)"/>.</summary>
public sealed class Popup
{
    private readonly MapboxMap _map;

    internal Popup(MapboxMap map, PopupOptions options)
    {
        _map = map;
        Options = options;
        Id = MapboxMap.NewId("popup");
    }

    /// <summary>Unique id.</summary>
    public string Id { get; }

    /// <summary>Options.</summary>
    public PopupOptions Options { get; }

    /// <summary>True while the popup is displayed.</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>The popup was closed (by the user or by code).</summary>
    public event EventHandler? Closed;

    /// <summary>Replaces the content with HTML.</summary>
    public Task SetHtmlAsync(string html, CancellationToken cancellationToken = default)
    {
        Options.Html = html;
        return InvokeAsync("setHTML", cancellationToken, html);
    }

    /// <summary>Replaces the content with text.</summary>
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Options.Text = text; Options.Html = null;
        return InvokeAsync("setText", cancellationToken, text);
    }

    /// <summary>Moves the popup.</summary>
    public Task SetPositionAsync(LngLat position, CancellationToken cancellationToken = default)
    {
        Options.Position = position;
        return InvokeAsync("setLngLat", cancellationToken, position);
    }

    /// <summary>Calls any method of the JavaScript popup.</summary>
    public Task<JsonElement> InvokeAsync(string method, CancellationToken cancellationToken, params object?[] args)
        => _map.CallAsync("popup.invoke", new JsonObject { ["id"] = Id, ["method"] = method, ["args"] = BridgeJson.ToArray(args) }, cancellationToken);

    /// <summary>Closes and removes the popup.</summary>
    public Task CloseAsync(CancellationToken cancellationToken = default) => _map.ClosePopupAsync(this, cancellationToken);

    internal void OnEvent(string name, JsonElement data)
    {
        if (name == "open") IsOpen = true;
        else if (name == "close")
        {
            IsOpen = false;
            _map.ForgetPopup(this);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}

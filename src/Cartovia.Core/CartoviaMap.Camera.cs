using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Cartovia.Events;
using Cartovia.GeoJson;

namespace Cartovia;

/// <summary>Target camera of a camera operation. Null members keep their current value.</summary>
public sealed class CameraOptions
{
    /// <summary>Center.</summary>
    public LngLat? Center { get; set; }
    /// <summary>Zoom level.</summary>
    public double? Zoom { get; set; }
    /// <summary>Bearing in degrees (0 = north up).</summary>
    public double? Bearing { get; set; }
    /// <summary>Pitch in degrees (0 = looking straight down).</summary>
    public double? Pitch { get; set; }
    /// <summary>Padding applied to the viewport.</summary>
    public MapPadding? Padding { get; set; }
    /// <summary>Geographic point kept fixed during zoom / rotation.</summary>
    public LngLat? Around { get; set; }

    internal void WriteTo(JsonObject o)
    {
        if (Center is { } c) o["center"] = BridgeJson.ToNode(c);
        if (Zoom is { } z) o["zoom"] = z;
        if (Bearing is { } b) o["bearing"] = b;
        if (Pitch is { } p) o["pitch"] = p;
        if (Padding is { } pad) o["padding"] = BridgeJson.ToNode(pad);
        if (Around is { } a) o["around"] = BridgeJson.ToNode(a);
    }
}

/// <summary>Animation settings of a camera transition.</summary>
public sealed class AnimationOptions
{
    /// <summary>Duration. Null: engine default.</summary>
    public TimeSpan? Duration { get; set; }
    /// <summary>When false, the animation is skipped if the user prefers reduced motion. Default: true here.</summary>
    public bool Essential { get; set; } = true;
    /// <summary>When false, the transition is instantaneous.</summary>
    public bool? Animate { get; set; }
    /// <summary>Pixel offset of the target center.</summary>
    public ScreenPoint? Offset { get; set; }
    /// <summary>Zoom "curve" of <c>flyTo</c> (default 1.42).</summary>
    public double? Curve { get; set; }
    /// <summary>Average speed of <c>flyTo</c> (default 1.2).</summary>
    public double? Speed { get; set; }
    /// <summary>Maximum duration of <c>flyTo</c>.</summary>
    public TimeSpan? MaxDuration { get; set; }
    /// <summary>Peak zoom of <c>flyTo</c>.</summary>
    public double? MinZoom { get; set; }

    internal void WriteTo(JsonObject o)
    {
        if (Duration is { } d) o["duration"] = d.TotalMilliseconds;
        o["essential"] = Essential;
        if (Animate is { } a) o["animate"] = a;
        if (Offset is { } off) o["offset"] = BridgeJson.ToNode(off);
        if (Curve is { } c) o["curve"] = c;
        if (Speed is { } s) o["speed"] = s;
        if (MaxDuration is { } md) o["maxDuration"] = md.TotalMilliseconds;
        if (MinZoom is { } mz) o["minZoom"] = mz;
    }
}

/// <summary>Options of <see cref="CartoviaMap.FitBoundsAsync"/>.</summary>
public sealed class FitBoundsOptions
{
    /// <summary>Padding around the bounds, in pixels.</summary>
    public MapPadding? Padding { get; set; }
    /// <summary>Maximum zoom level.</summary>
    public double? MaxZoom { get; set; }
    /// <summary>Bearing to apply.</summary>
    public double? Bearing { get; set; }
    /// <summary>Pitch to apply.</summary>
    public double? Pitch { get; set; }
    /// <summary>Use <c>easeTo</c> instead of <c>flyTo</c>.</summary>
    public bool? Linear { get; set; }
    /// <summary>Animation settings.</summary>
    public AnimationOptions? Animation { get; set; }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        if (Padding is { } p) o["padding"] = BridgeJson.ToNode(p);
        if (MaxZoom is { } mz) o["maxZoom"] = mz;
        if (Bearing is { } b) o["bearing"] = b;
        if (Pitch is { } pi) o["pitch"] = pi;
        if (Linear is { } l) o["linear"] = l;
        Animation?.WriteTo(o);
        return o;
    }
}

/// <summary>Interaction handlers that can be enabled or disabled at runtime.</summary>
public enum MapInteraction
{
    /// <summary>Scroll wheel zoom.</summary>
    ScrollZoom,
    /// <summary>Shift + drag box zoom.</summary>
    BoxZoom,
    /// <summary>Right-click / ctrl + drag rotation.</summary>
    DragRotate,
    /// <summary>Drag to pan.</summary>
    DragPan,
    /// <summary>Keyboard.</summary>
    Keyboard,
    /// <summary>Double click zoom.</summary>
    DoubleClickZoom,
    /// <summary>Touch zoom / rotate.</summary>
    TouchZoomRotate,
    /// <summary>Touch pitch.</summary>
    TouchPitch,
}

public sealed partial class CartoviaMap
{
    /// <summary>Reads the current camera from the engine.</summary>
    public async Task<CameraState> GetCameraAsync(CancellationToken cancellationToken = default)
    {
        var json = await CallAsync("map.camera", null, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return Camera = CameraState.FromJson(json);
    }

    /// <summary>Moves the camera instantly.</summary>
    public Task<CameraState> JumpToAsync(CameraOptions camera, CancellationToken cancellationToken = default)
        => AnimateAsync("jumpTo", camera, null, true, cancellationToken);

    /// <summary>Animates the camera along a straight path.</summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="animation">Animation settings.</param>
    /// <param name="waitForCompletion">When true, the task completes at the end of the animation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The camera at completion (or right after the start when not waiting).</returns>
    public Task<CameraState> EaseToAsync(CameraOptions camera, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => AnimateAsync("easeTo", camera, animation, waitForCompletion, cancellationToken);

    /// <summary>Animates the camera along a "flight" path (zooms out then in).</summary>
    public Task<CameraState> FlyToAsync(CameraOptions camera, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => AnimateAsync("flyTo", camera, animation, waitForCompletion, cancellationToken);

    /// <summary>Flies to a position.</summary>
    public Task<CameraState> FlyToAsync(LngLat center, double? zoom = null, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => FlyToAsync(new CameraOptions { Center = center, Zoom = zoom }, animation, waitForCompletion, cancellationToken);

    /// <summary>Pans to a position.</summary>
    public Task<CameraState> PanToAsync(LngLat center, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => EaseToAsync(new CameraOptions { Center = center }, animation, waitForCompletion, cancellationToken);

    /// <summary>Animates to a zoom level.</summary>
    public Task<CameraState> ZoomToAsync(double zoom, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => EaseToAsync(new CameraOptions { Zoom = zoom }, animation, waitForCompletion, cancellationToken);

    /// <summary>Zooms in by one level.</summary>
    public async Task<CameraState> ZoomInAsync(CancellationToken cancellationToken = default)
        => await ZoomToAsync((await GetCameraAsync(cancellationToken).ConfigureAwait(false)).Zoom + 1, cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>Zooms out by one level.</summary>
    public async Task<CameraState> ZoomOutAsync(CancellationToken cancellationToken = default)
        => await ZoomToAsync((await GetCameraAsync(cancellationToken).ConfigureAwait(false)).Zoom - 1, cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>Animates to a bearing.</summary>
    public Task<CameraState> RotateToAsync(double bearing, AnimationOptions? animation = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => EaseToAsync(new CameraOptions { Bearing = bearing }, animation, waitForCompletion, cancellationToken);

    /// <summary>Rotates back to north (and optionally resets the pitch).</summary>
    public Task<CameraState> ResetNorthAsync(bool resetPitch = false, CancellationToken cancellationToken = default)
        => EaseToAsync(new CameraOptions { Bearing = 0, Pitch = resetPitch ? 0 : null }, cancellationToken: cancellationToken);

    /// <summary>Sets the center instantly.</summary>
    public Task<CameraState> SetCenterAsync(LngLat center, CancellationToken cancellationToken = default) => JumpToAsync(new CameraOptions { Center = center }, cancellationToken);
    /// <summary>Sets the zoom instantly.</summary>
    public Task<CameraState> SetZoomAsync(double zoom, CancellationToken cancellationToken = default) => JumpToAsync(new CameraOptions { Zoom = zoom }, cancellationToken);
    /// <summary>Sets the bearing instantly.</summary>
    public Task<CameraState> SetBearingAsync(double bearing, CancellationToken cancellationToken = default) => JumpToAsync(new CameraOptions { Bearing = bearing }, cancellationToken);
    /// <summary>Sets the pitch instantly.</summary>
    public Task<CameraState> SetPitchAsync(double pitch, CancellationToken cancellationToken = default) => JumpToAsync(new CameraOptions { Pitch = pitch }, cancellationToken);

    /// <summary>Moves the camera so that the bounds fit the viewport.</summary>
    public async Task<CameraState> FitBoundsAsync(LngLatBounds bounds, FitBoundsOptions? options = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
    {
        var json = await CallAsync("map.animate", new JsonObject
        {
            ["method"] = "fitBounds",
            ["bounds"] = BridgeJson.ToNode(bounds),
            ["options"] = (options ?? new FitBoundsOptions()).ToJson(),
            ["wait"] = waitForCompletion,
        }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return Camera = CameraState.FromJson(json);
    }

    /// <summary>Fits the camera to a set of coordinates.</summary>
    public Task<CameraState> FitToAsync(IEnumerable<LngLat> points, FitBoundsOptions? options = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
        => FitBoundsAsync(LngLatBounds.FromPoints(points), options, waitForCompletion, cancellationToken);

    /// <summary>Fits the camera to a GeoJSON object (geometry, feature or collection).</summary>
    /// <exception cref="ArgumentException">The object has no coordinates.</exception>
    public Task<CameraState> FitToAsync(GeoJsonObject geoJson, FitBoundsOptions? options = null, bool waitForCompletion = true, CancellationToken cancellationToken = default)
    {
        IEnumerable<Position> positions = geoJson switch
        {
            Geometry g => g.GetPositions(),
            Feature f => f.Geometry?.GetPositions() ?? Enumerable.Empty<Position>(),
            FeatureCollection fc => fc.Features.Where(x => x.Geometry is not null).SelectMany(x => x.Geometry!.GetPositions()),
            _ => Enumerable.Empty<Position>(),
        };
        var list = positions.Select(p => (LngLat)p).ToList();
        if (list.Count == 0) throw new ArgumentException("The GeoJSON object has no coordinates.", nameof(geoJson));
        return FitBoundsAsync(LngLatBounds.FromPoints(list), options, waitForCompletion, cancellationToken);
    }

    /// <summary>Computes the camera that would fit the bounds, without moving.</summary>
    public async Task<CameraState?> CameraForBoundsAsync(LngLatBounds bounds, FitBoundsOptions? options = null, CancellationToken cancellationToken = default)
    {
        var json = await InvokeAsync("cameraForBounds", cancellationToken, bounds, (options ?? new FitBoundsOptions()).ToJson()).ConfigureAwait(false);
        return json.ValueKind == System.Text.Json.JsonValueKind.Object ? CameraState.FromJson(json) : null;
    }

    /// <summary>Stops any running camera animation.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => InvokeAsync("stop", cancellationToken);

    /// <summary>Visible geographic bounds.</summary>
    public async Task<LngLatBounds> GetBoundsAsync(CancellationToken cancellationToken = default)
        => (await GetCameraAsync(cancellationToken).ConfigureAwait(false)).Bounds ?? default;

    /// <summary>Restricts panning to bounds (null removes the restriction).</summary>
    public Task SetMaxBoundsAsync(LngLatBounds? bounds, CancellationToken cancellationToken = default)
        => InvokeAsync("setMaxBounds", cancellationToken, bounds);

    /// <summary>Sets the minimum zoom.</summary>
    public Task SetMinZoomAsync(double? zoom, CancellationToken cancellationToken = default) => InvokeAsync("setMinZoom", cancellationToken, zoom);
    /// <summary>Sets the maximum zoom.</summary>
    public Task SetMaxZoomAsync(double? zoom, CancellationToken cancellationToken = default) => InvokeAsync("setMaxZoom", cancellationToken, zoom);
    /// <summary>Sets the minimum pitch.</summary>
    public Task SetMinPitchAsync(double? pitch, CancellationToken cancellationToken = default) => InvokeAsync("setMinPitch", cancellationToken, pitch);
    /// <summary>Sets the maximum pitch.</summary>
    public Task SetMaxPitchAsync(double? pitch, CancellationToken cancellationToken = default) => InvokeAsync("setMaxPitch", cancellationToken, pitch);
    /// <summary>Sets the viewport padding.</summary>
    public Task SetPaddingAsync(MapPadding padding, CancellationToken cancellationToken = default) => InvokeAsync("setPadding", cancellationToken, padding);

    /// <summary>Converts a geographic position to a pixel position.</summary>
    public async Task<ScreenPoint> ProjectAsync(LngLat lngLat, CancellationToken cancellationToken = default)
        => Json.CartoviaJson.Deserialize<ScreenPoint>(await InvokeAsync("project", cancellationToken, lngLat).ConfigureAwait(false));

    /// <summary>Converts a pixel position to a geographic position.</summary>
    public async Task<LngLat> UnprojectAsync(ScreenPoint point, CancellationToken cancellationToken = default)
        => Json.CartoviaJson.Deserialize<LngLat>(await InvokeAsync("unproject", cancellationToken, point).ConfigureAwait(false));

    /// <summary>Resizes the map to its container (done automatically on container resize).</summary>
    public Task ResizeAsync(CancellationToken cancellationToken = default) => InvokeAsync("resize", cancellationToken);

    /// <summary>Forces a repaint.</summary>
    public Task TriggerRepaintAsync(CancellationToken cancellationToken = default) => InvokeAsync("triggerRepaint", cancellationToken);

    /// <summary>Enables or disables an interaction handler.</summary>
    public Task SetInteractionEnabledAsync(MapInteraction interaction, bool enabled, CancellationToken cancellationToken = default)
    {
        var name = interaction.ToString();
        name = char.ToLowerInvariant(name[0]) + name.Substring(1);
        return CallAsync("map.handler", new JsonObject { ["name"] = name, ["enabled"] = enabled }, cancellationToken, Readiness.Loaded);
    }

    private async Task<CameraState> AnimateAsync(string method, CameraOptions camera, AnimationOptions? animation, bool wait, CancellationToken cancellationToken)
    {
        var options = new JsonObject();
        camera.WriteTo(options);
        animation?.WriteTo(options);
        var json = await CallAsync("map.animate", new JsonObject
        {
            ["method"] = method,
            ["options"] = options,
            ["wait"] = wait,
        }, cancellationToken, Readiness.Loaded).ConfigureAwait(false);
        return Camera = CameraState.FromJson(json);
    }
}

# Map and camera

## Lifecycle

`CartoviaMap` is created by the control and is usable immediately: calls made before the map is loaded are queued
and run as soon as the `load` event fires.

```csharp
map.Loaded += (s, e) => Console.WriteLine($"Loaded at zoom {e.Camera.Zoom}");
await map.WhenLoadedAsync();                 // or await the load explicitly
bool ready = map.IsLoaded;
string? version = map.EngineVersion;         // e.g. "5.24.0"
CameraState? last = map.Camera;              // last known camera (updated after every move)
```

If the map cannot be created (engine script not reachable, missing token, no WebGL), pending calls fail with a
`MapInitializationException` and `map.Error` is raised with `IsFatal = true`.

## MapOptions

Options are applied when the map is created. Change them later and call `ReinitializeAsync()` to recreate the map in
place (controls, plugins and markers are restored).

```csharp
var o = mapView.Options;
o.Engine = MapEngine.MapLibre;
o.Style = MapStyles.OpenFreeMapPositron;
o.Center = new LngLat(4.8357, 45.7640);
o.Zoom = 12;
o.Pitch = 30;
o.MinZoom = 3;
o.MaxBounds = new LngLatBounds(-5.5, 41, 10, 51.5);
o.Projection = "globe";
o.ScrollZoom = true;
o.CooperativeGestures = false;               // true: Ctrl + wheel to zoom
o.AttributionControl = true;
o.CustomAttribution = "© My company";
o.InitialControls.Add(new NavigationControl());
o.AdditionalMapOptions["hash"] = false;      // any constructor option not modelled
await map.ReinitializeAsync();               // only needed after load
```

| Group | Members |
|---|---|
| Engine and assets | `Engine`, `AccessToken`, `EngineVersion`, `EngineScriptUrl`, `EngineStylesheetUrl`, `AdditionalScripts`, `AdditionalStylesheets`, `RtlTextPluginUrl`, `WorkerCount` |
| Style | `Style`, `StyleJson`, `Config` (Mapbox Standard), `Language`, `Worldview`, `Projection` |
| Camera | `Center`, `Zoom`, `Bearing`, `Pitch`, `Bounds`, `BoundsPadding`, `MinZoom`, `MaxZoom`, `MinPitch`, `MaxPitch`, `MaxBounds` |
| Interaction | `Interactive`, `ScrollZoom`, `BoxZoom`, `DragRotate`, `DragPan`, `Keyboard`, `DoubleClickZoom`, `TouchZoomRotate`, `TouchPitch`, `CooperativeGestures` |
| Rendering | `AttributionControl`, `CustomAttribution`, `LogoPosition`, `RenderWorldCopies`, `Antialias`, `PreserveDrawingBuffer`, `FadeDuration`, `LocalIdeographFontFamily`, `CrossSourceCollisions`, `MaxTileCacheSize` |
| Bridge | `PreserveUserLayersOnStyleChange` (default true), `EventThrottle` (default 16 ms), `CallTimeout` (default 30 s) |

## Moving the camera

Animated methods complete **when the animation ends** (pass `waitForCompletion: false` to return immediately) and
return the final `CameraState`.

```csharp
// Instant
await map.JumpToAsync(new CameraOptions { Center = new LngLat(2.35, 48.85), Zoom = 12 });
await map.SetZoomAsync(14);

// Straight animation
await map.EaseToAsync(new CameraOptions { Bearing = 45, Pitch = 60 },
    new AnimationOptions { Duration = TimeSpan.FromSeconds(1.5) });

// "Flight" animation
var final = await map.FlyToAsync(new CameraOptions { Center = new LngLat(-74.006, 40.713), Zoom = 15, Pitch = 60 },
    new AnimationOptions { Duration = TimeSpan.FromSeconds(6), Curve = 1.6 });

// Shortcuts
await map.FlyToAsync(new LngLat(139.767, 35.681), zoom: 13);
await map.PanToAsync(new LngLat(2.29, 48.86));
await map.ZoomInAsync();
await map.RotateToAsync(90);
await map.ResetNorthAsync(resetPitch: true);
await map.StopAsync();                                  // stop any running animation
```

### Fitting content

```csharp
await map.FitBoundsAsync(new LngLatBounds(-5, 42, 8, 51), new FitBoundsOptions { Padding = 40, MaxZoom = 12 });
await map.FitToAsync(myFeatureCollection, new FitBoundsOptions { Padding = new MapPadding(20, 20, 20, 320) });
await map.FitToAsync(new[] { new LngLat(2.35, 48.85), new LngLat(4.83, 45.76) });

CameraState? preview = await map.CameraForBoundsAsync(bounds);   // compute without moving
```

`MapPadding` converts implicitly from a number (uniform padding).

### Reading the camera and converting coordinates

```csharp
CameraState camera = await map.GetCameraAsync();   // Center, Zoom, Bearing, Pitch, Bounds
LngLatBounds visible = await map.GetBoundsAsync();
ScreenPoint px = await map.ProjectAsync(new LngLat(2.35, 48.85));
LngLat geo = await map.UnprojectAsync(new ScreenPoint(200, 150));
```

### Limits and padding

```csharp
await map.SetMaxBoundsAsync(new LngLatBounds(-5.5, 41, 10, 51.5));
await map.SetMinZoomAsync(4);
await map.SetMaxPitchAsync(70);
await map.SetPaddingAsync(new MapPadding(0, 0, 0, 300));   // e.g. leave room for a side panel
```

## Projection, language and style configuration

```csharp
await map.SetProjectionAsync("globe");          // "mercator", "globe"; Mapbox also: equalEarth, naturalEarth…
await map.SetLanguageAsync("fr");               // Mapbox v3
await map.SetConfigPropertyAsync("basemap", "lightPreset", "dusk");   // Mapbox Standard
await map.SetConfigPropertyAsync("basemap", "showPointOfInterestLabels", false);
```

## Interaction handlers

```csharp
await map.SetInteractionEnabledAsync(MapInteraction.ScrollZoom, false);
await map.SetInteractionEnabledAsync(MapInteraction.DragRotate, false);
```

## Coordinates

`LngLat` is a `readonly record struct` (longitude first, like GeoJSON). It converts from tuples and strings:

```csharp
LngLat a = (2.35, 48.85);
LngLat b = LngLatTypeConverter.Parse("4.8357, 45.7640");
double meters = a.DistanceTo(b);
var bounds = LngLatBounds.FromPoints(new[] { a, b }).Extend(new LngLat(5.37, 43.29));
```

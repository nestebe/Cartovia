# Events

Events are raised on the UI thread. Subscribing to an event the first time automatically subscribes to it in the
engine; high-frequency events (`MouseMove`, `Moving`, `Zooming`, `Dragging`, `Rendered`, `Wheel`…) are throttled by
`MapOptions.EventThrottle` (16 ms by default).

## Map events

```csharp
map.Loaded += (s, e) => Console.WriteLine($"Loaded, zoom {e.Camera.Zoom}");
map.Click += (s, e) => Console.WriteLine($"{e.LngLat} at {e.Point}, button {e.Button}, shift {e.ShiftKey}");
map.ContextMenu += (s, e) => ShowMenuAt(e.LngLat);
map.MouseMove += (s, e) => coordinates.Text = e.LngLat.ToString();
map.MoveEnded += (s, e) =>
{
    if (e.IsUserInitiated) SaveViewport(e.Camera.Center, e.Camera.Zoom, e.Camera.Bounds);
};
map.Error += (s, e) => logger.LogWarning("Map error {Message} ({Status}) {Url}", e.Message, e.Status, e.SourceId);
map.Log += (s, e) => logger.Log(e.Level == MapLogLevel.Error ? LogLevel.Error : LogLevel.Information, e.Message);
```

| Category | Events | Arguments |
|---|---|---|
| Lifecycle | `Loaded`, `StyleLoaded`, `UserLayersRestored`, `Idle`, `Resized`, `Rendered` | `MapCameraEventArgs` / `MapEventArgs` |
| Errors | `Error` | `MapErrorEventArgs` (`Message`, `Status`, `Url`, `SourceId`, `IsFatal`) |
| Data | `DataChanged`, `SourceDataChanged`, `StyleDataChanged`, `StyleImageMissing` | `MapDataEventArgs`, `StyleImageMissingEventArgs` |
| Mouse | `Click`, `DoubleClick`, `ContextMenu`, `MouseDown`, `MouseUp`, `MouseMove`, `MouseOver`, `MouseOut`, `Wheel` | `MapMouseEventArgs` |
| Camera | `MoveStarted`, `Moving`, `MoveEnded`, `ZoomStarted`, `Zooming`, `ZoomEnded`, `RotateStarted`, `Rotating`, `RotateEnded`, `PitchStarted`, `Pitching`, `PitchEnded`, `DragStarted`, `Dragging`, `DragEnded`, `BoxZoomStarted`, `BoxZoomEnded`, `BoxZoomCancelled` | `MapCameraEventArgs` |
| Diagnostics | `Log` (script errors, bridge warnings), `Initializing` | `MapLogEventArgs` |

`MapMouseEventArgs`: `LngLat`, `Point`, `Button`, `AltKey`, `CtrlKey`, `ShiftKey`, `WheelDelta`, `Features`, `Feature`, `LayerId`.
`MapCameraEventArgs`: `Camera` (`Center`, `Zoom`, `Bearing`, `Pitch`, `Bounds`) and `IsUserInitiated`.

## Layer events

Bind mouse events to the features of a layer; the features under the pointer are included.

```csharp
IDisposable sub = map.OnLayer("cities", LayerEvent.Click, (s, e) =>
{
    var city = e.Feature!;
    Console.WriteLine($"{city.GetProperty<string>("name")} ({city.Source}/{city.LayerId}) id={city.Id}");
});

map.OnLayer("cities", LayerEvent.MouseEnter, (s, e) => tooltip.Show(e.Feature?.GetProperty<string>("name")));
map.OnLayer("cities", LayerEvent.MouseLeave, (s, e) => tooltip.Hide());

sub.Dispose();   // unsubscribe
```

`LayerEvent`: `Click`, `DoubleClick`, `ContextMenu`, `MouseDown`, `MouseUp`, `MouseMove`, `MouseEnter`, `MouseLeave`,
`MouseOver`, `MouseOut`.

## Any engine event

```csharp
IDisposable sub = map.On("webglcontextlost", (s, e) => Console.WriteLine("WebGL context lost"));
map.On("sourcedataloading", (s, e) => Console.WriteLine(((MapDataEventArgs)e).SourceId));
```

The arguments are the typed subclass when one matches the event (mouse, camera, data, error), otherwise
`MapEventArgs` with the raw JSON in `Data`.

## Mapbox interactions (featuresets)

Mapbox GL JS 3.9+ can target featuresets of the Standard style (POIs, buildings, place labels…):

```csharp
IDisposable poiClick = await map.AddInteractionAsync(
    new InteractionOptions(InteractionType.Click) { FeaturesetId = "poi", ImportId = "basemap" },
    (s, e) => Console.WriteLine($"POI: {e.Feature?.GetProperty<string>("name")}"));

await map.AddInteractionAsync(
    new InteractionOptions(InteractionType.MouseEnter) { FeaturesetId = "buildings", ImportId = "basemap" },
    (s, e) => status.Text = $"Building {e.Feature?.Id} at {e.LngLat}");

poiClick.Dispose();   // remove the interaction
```

## Events from markers, popups, controls and plugins

They are raised on their own objects:

```csharp
marker.DragEnded += (s, e) => Console.WriteLine(e.Position);
popup.Closed += (s, e) => Console.WriteLine("closed");
htmlControl.CommandInvoked += (s, e) => Console.WriteLine(e.Command);
draw.Created += (s, e) => Console.WriteLine(e.Features.Count);
```

See [Markers, popups and controls](annotations-and-controls.md) and [Plugins](plugins.md).

# Markers, popups and controls

## Markers

Markers are HTML elements positioned on the map: ideal for a few interactive points. For thousands of points, use a
GeoJSON source with a circle or symbol layer instead.

```csharp
// Default pin
Marker pin = await map.AddMarkerAsync(new LngLat(2.3522, 48.8566), color: "#e11d48", popupHtml: "<b>Paris</b>");

// Full options
Marker truck = await map.AddMarkerAsync(new MarkerOptions(new LngLat(4.8357, 45.7640))
{
    Html = "<div style='width:28px;height:28px;border-radius:50%;background:#2563eb;border:3px solid white'></div>",
    Anchor = Anchor.Center,
    Draggable = true,
    Tooltip = "Truck 42",
    Rotation = 45,
    RotationAlignment = MarkerAlignment.Map,
    Popup = new PopupOptions { Html = "<b>Truck 42</b><br/>Driver: Alice", Offset = 18 },
    Tag = truckId,                                   // your own data
});

truck.Click += (s, e) => Console.WriteLine($"{e.Marker.Tag} clicked");
truck.DragEnded += (s, e) => Console.WriteLine($"moved to {e.Position}");

await truck.SetPositionAsync(new LngLat(4.84, 45.77));   // live tracking
await truck.SetRotationAsync(90);
await truck.SetHtmlAsync("<div class='truck alert'></div>");
await truck.TogglePopupAsync();
await truck.RemoveAsync();

IReadOnlyCollection<Marker> all = map.Markers;
await map.ClearMarkersAsync();
```

Marker events: `Click`, `DoubleClick`, `ContextMenu`, `MouseEnter`, `MouseLeave`, `DragStarted`, `Dragging`, `DragEnded`.

## Popups

```csharp
Popup popup = await map.ShowPopupAsync(new LngLat(2.29, 48.86), "<b>Eiffel Tower</b>");

Popup info = await map.ShowPopupAsync(new PopupOptions
{
    Position = new LngLat(2.35, 48.85),
    Html = "<h3>Details</h3><p>…</p>",
    MaxWidth = "320px",
    CloseOnClick = false,
    Anchor = Anchor.Bottom,
    CssClass = "my-popup",
});
info.Closed += (s, e) => Console.WriteLine("closed by the user");
await info.SetHtmlAsync("<p>Updated</p>");
await info.CloseAsync();
await map.CloseAllPopupsAsync();
```

HTML content is not sanitized: escape user-provided text yourself, or use `Text` instead of `Html`.

For popups on layer clicks, see `ShowPopupOnClickAsync` in [Extensions](extensions.md#popups-on-click).

## Built-in controls

```csharp
await map.AddControlAsync(new NavigationControl(ControlPosition.TopRight) { VisualizePitch = true });
await map.AddControlAsync(new ScaleControl { Unit = ScaleUnit.Metric, MaxWidth = 120 });
await map.AddControlAsync(new FullscreenControl());
await map.AddControlAsync(new AttributionControl { Compact = true, CustomAttribution = "© My data" });

var locate = await map.AddControlAsync(new GeolocateControl { TrackUserLocation = true, EnableHighAccuracy = true });
locate.Geolocated += (s, e) => Console.WriteLine($"{e.Position} ± {e.Accuracy} m");
await locate.TriggerAsync();

// MapLibre only
await map.AddControlAsync(new GlobeControl());
await map.AddControlAsync(new TerrainControl("dem", exaggeration: 1.5));
```

`GeolocateControl` requires `HostOptions.AllowGeolocation = true` and the Windows location service.
Controls can also be added before loading through `MapOptions.InitialControls`.

```csharp
await control.RemoveAsync();
IReadOnlyCollection<MapControl> controls = map.Controls;
```

## Custom HTML controls

`HtmlControl` renders HTML in the map's control area. Elements with a `data-command` attribute raise
`CommandInvoked`: clicks for buttons, changes for inputs, selects and checkboxes.

```csharp
var toolbar = new HtmlControl("""
    <button data-command="home" title="Home">⌂</button>
    <button data-command="layers" title="Layers">≡</button>
    <select data-command="basemap">
      <option value="liberty">Liberty</option>
      <option value="positron">Positron</option>
    </select>
    """, ControlPosition.TopLeft);

toolbar.CommandInvoked += async (s, e) =>
{
    switch (e.Command)
    {
        case "home": await map.FlyToAsync(new LngLat(2.35, 48.85), 11); break;
        case "basemap": await map.SetStyleAsync(e.Value == "positron" ? MapStyles.OpenFreeMapPositron : MapStyles.OpenFreeMapLiberty); break;
    }
};
await map.AddControlAsync(toolbar);
await toolbar.SetHtmlAsync("<button data-command='home'>Home</button>");
```

# Plugins

Plugins load extra JavaScript in the page and expose a typed C# API. Assets come from public CDNs by default; every
plugin has `ScriptUrl` / `StylesheetUrl` properties (and `PluginAssets` holds the defaults) to point to local copies.

```csharp
var draw = await map.AddPluginAsync(new DrawPlugin());
await draw.RemoveAsync();                    // or map.RemovePluginAsync(draw)
DrawPlugin? same = map.GetPlugin<DrawPlugin>();
```

| Plugin | Engines | Purpose |
|---|---|---|
| `DrawPlugin` | both | Draw and edit points, lines and polygons (mapbox-gl-draw) |
| `GeocoderPlugin` | both | Search box (Mapbox Geocoding, Nominatim or your own provider) |
| `DirectionsPlugin` | Mapbox | Interactive routing UI (mapbox-gl-directions) |
| `TurfPlugin` | both | Geospatial analysis with Turf.js |
| `ScriptPlugin` / `MapPlugin` | both | Your own plugins |

## Draw

```csharp
var draw = new DrawPlugin(ControlPosition.TopLeft)
{
    DisplayControlsDefault = false,
    Controls = new DrawControls { Polygon = true, LineString = true, Point = true, Trash = true },
};
draw.Created += (s, e) => Save(e.Features);
draw.Updated += (s, e) => Console.WriteLine($"{e.Action}: {e.Features.Count} feature(s)");
draw.Deleted += (s, e) => Console.WriteLine("deleted");
draw.SelectionChanged += (s, e) => Console.WriteLine($"{e.Features.Count} selected");
await map.AddPluginAsync(draw);

await draw.ChangeModeAsync(DrawModes.DrawPolygon);         // start drawing from code
FeatureCollection all = await draw.GetAllAsync();
IReadOnlyList<string> ids = await draw.AddAsync(existingPolygon);   // edit an existing shape
await draw.ChangeModeAsync(DrawModes.DirectSelect, new { featureId = ids[0] });
await draw.SetFeaturePropertyAsync(ids[0], "name", "Zone A");
await draw.DeleteAllAsync();
```

Modes: `DrawModes.SimpleSelect`, `DirectSelect`, `DrawPoint`, `DrawLineString`, `DrawPolygon`.
Custom draw styles: `draw.Styles` (array of layer JSON); any other option: `draw.AdditionalOptions`.

## Geocoder (search box)

```csharp
var search = new GeocoderPlugin(ControlPosition.TopLeft)
{
    Placeholder = "Search an address…",
    Language = "fr",
    Countries = "fr,be",
    Limit = 6,
    Proximity = new LngLat(2.35, 48.85),
};
search.ResultSelected += (s, e) => Console.WriteLine($"{e.PlaceName} {e.Position}");
await map.AddPluginAsync(search);

await search.QueryAsync("Lyon");          // search from code
await search.ClearAsync();
```

Providers:

- **Mapbox engine**: the Mapbox Geocoding API (uses the map token).
- **MapLibre engine**: OpenStreetMap **Nominatim** by default. Respect its
  [usage policy](https://operations.osmfoundation.org/policies/nominatim/) (light use only), or plug your own provider:

```csharp
search.SearchProvider = async (query, ct) =>
{
    var results = await myGeocodingService.SearchAsync(query.Text, query.Limit ?? 5, ct);
    return results.Select(r => new GeocoderResult(r.Label, new LngLat(r.Lon, r.Lat)));
};
// search.UseProviderWithMapbox = true;   // also use it with the Mapbox engine
```

## Directions (Mapbox)

```csharp
var directions = new DirectionsPlugin
{
    Profile = DirectionsProfiles.DrivingTraffic,
    Unit = "metric",
    Language = "fr",
    Alternatives = true,
};
directions.RouteCalculated += (s, e) => Console.WriteLine(e.Data.GetProperty("routes").GetArrayLength());
await map.AddPluginAsync(directions);
await directions.SetOriginAsync(new LngLat(2.35, 48.85));
await directions.SetDestinationAsync("Lyon");
```

Throws `NotSupportedException` with MapLibre. For routing without the UI (any engine), use
[`Cartovia.Mapbox.Services`](mapbox-services.md) or your own routing service and draw the result with a `LineLayer`.

## Turf

```csharp
var turf = await map.AddPluginAsync(new TurfPlugin());

Feature buffer = await turf.BufferAsync(new Point(2.35, 48.85), 2, TurfUnits.Kilometers);
double area = await turf.AreaAsync(buffer);                          // m²
double length = await turf.LengthAsync(route, TurfUnits.Kilometers);
double distance = await turf.DistanceAsync(new LngLat(2.35, 48.85), new LngLat(4.83, 45.76));
bool inside = await turf.BooleanPointInPolygonAsync(new LngLat(2.35, 48.85), polygon);
FeatureCollection within = await turf.PointsWithinPolygonAsync(points, polygon);

// Any other Turf function
JsonElement result = await turf.CallAsync("lineIntersect", lineA, lineB);
```

## Writing your own plugin

### Quick: `ScriptPlugin`

Define the plugin in JavaScript with `Cartovia.definePlugin(name, factory)`. The factory receives a context `ctx`
(`map`, `lib`, `engine`, `emit(event, data)`, `callHost(method, data)`…) and returns commands.

```csharp
var measure = new ScriptPlugin("measure", """
    Cartovia.definePlugin('measure', function (ctx, options) {
      var total = 0, last = null;
      function onClick(e) {
        if (last) total += e.lngLat.distanceTo(last);
        last = e.lngLat;
        ctx.emit('distance', { meters: total });
      }
      ctx.map.on('click', onClick);
      return {
        commands: { reset: function () { total = 0; last = null; } },
        dispose: function () { ctx.map.off('click', onClick); }
      };
    });
    """);
measure.EventReceived += (s, e) => Console.WriteLine($"{e.Data.GetProperty("meters").GetDouble():0} m");
await map.AddPluginAsync(measure);
await measure.CallAsync("reset");
```

JavaScript can call back into .NET with `ctx.callHost(method, data)` (handled by `HostCallHandler`), which returns a
promise of the .NET result:

```csharp
measure.HostCallHandler = async (method, data, ct) => method switch
{
    "lookup" => await database.FindAsync(data.GetProperty("id").GetString()!, ct),
    _ => null,
};
```

### Reusable: subclass `MapPlugin`

```csharp
public sealed class WindPlugin : MapPlugin
{
    public override string Name => "wind";
    public string ScriptUrl { get; set; } = "https://unpkg.com/some-wind-layer@1.0.0/dist/wind.js";
    public event EventHandler<double>? SpeedChanged;

    public Task SetDateAsync(DateTime date) => InvokeAsync("setDate", date.ToString("O"));

    protected override IEnumerable<string> GetScripts(MapEngine engine) => new[] { ScriptUrl };
    protected override string? GetDefinitionScript() => File.ReadAllText("wind-plugin.js");
    protected override JsonObject BuildOptions(MapEngine engine) => new() { ["particles"] = 4000 };
    protected override void EnsureEngineSupported(MapEngine engine) { /* throw NotSupportedException if needed */ }

    protected override void OnEvent(string eventName, JsonElement data)
    {
        if (eventName == "speed") SpeedChanged?.Invoke(this, data.GetProperty("value").GetDouble());
        base.OnEvent(eventName, data);
    }
}
```

`GetScripts`, `GetStylesheets`, `GetDefinitionScript`, `BuildOptions`, `EnsureEngineSupported` and `OnHostCallAsync`
are `protected internal` in `MapPlugin`; override them with the same accessibility in your assembly
(`protected override` works from another assembly). Plugins are re-created automatically when the map is re-initialized.

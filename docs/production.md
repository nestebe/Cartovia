# Production guide

Everything you need when a map goes from a demo to an application used every day: authenticated tile servers,
large datasets, offline tilesets, custom rendering, error handling and testing.

## Authenticated tile servers (request rules)

Request rules modify the requests of the engine (styles, TileJSON, tiles, sprites, glyphs, images) whose URL matches a
pattern. Use them for API keys, bearer tokens, or cookies.

```csharp
mapView.Options.RequestRules.Add(
    new RequestRule("https://tiles.mycompany.com/*")
        .WithHeader("Authorization", "Bearer " + await tokens.GetAsync()));

mapView.Options.RequestRules.Add(
    new RequestRule("https://api.maptiler.com/*").WithQueryParameter("key", mapTilerKey));

// Only tiles, regular expression
mapView.Options.RequestRules.Add(
    new RequestRule(@"regex:^https://[a-c]\.tiles\.example\.com/")
        .ForResourceTypes(RequestResourceType.Tile)
        .WithHeader("X-Client", "desktop"));
```

Patterns: `*` matches any characters (case-insensitive, whole URL); prefix with `regex:` for a JavaScript regular
expression. Rules are applied in order; headers and query parameters of every matching rule are combined.

### Refreshing an expiring token

```csharp
async Task RefreshAsync()
{
    var token = await tokens.RefreshAsync();
    await map.SetRequestRulesAsync(new[]
    {
        new RequestRule("https://tiles.mycompany.com/*").WithHeader("Authorization", "Bearer " + token),
    });
}
```

New requests use the new rules immediately; tiles already loaded are kept.

> Custom headers make tile requests CORS "non-simple": the server must answer the preflight
> (`Access-Control-Allow-Origin` and `Access-Control-Allow-Headers`). Query parameters don't need it.
> The page origin is `https://cartovia.local`.

## Large datasets

Sending a 50 MB GeoJSON string through the WebView message channel blocks the UI while the page parses it.
Cartovia avoids that automatically: GeoJSON larger than `MapOptions.LargeDataThreshold` (512 KB by default) is
**served to the engine through a URL** of the page origin (`https://cartovia.local/data/…`); the engine downloads and
parses it in a web worker.

```csharp
await map.AddSourceAsync(new GeoJsonSource("parcels", hugeFeatureCollection));   // served by URL automatically
await map.SetGeoJsonDataAsync("parcels", updatedCollection);                       // same

mapView.Options.LargeDataThreshold = 0;   // disable (always inline)
```

### Files and raw data

```csharp
// Stream a file from disk (never loaded in .NET memory)
await map.AddSourceAsync(new GeoJsonSource("cadastre", map.PublishFile(@"D:\data\cadastre.geojson")));
await map.SetGeoJsonDataFromFileAsync("cadastre", @"D:\data\cadastre-2026.geojson");

// Serve bytes (images, JSON…) and get a URL usable anywhere in the style
string iconUrl = map.PublishData("icons/truck.png", truckPng, "image/png");
await map.AddImageAsync("truck", iconUrl);

map.UnpublishData(iconUrl);
```

`map.CanPublishData` is true with the WPF, WinForms and Avalonia controls (the WebView2 host implements
`IMapDataHost`, including HTTP `Range` requests).

### Other performance tips

- Prefer **one GeoJSON source with many features** over many markers: markers are DOM elements, fine for dozens,
  not for thousands. Use circle / symbol layers instead.
- Use **feature state** (`SetFeatureStateAsync`, `EnableHoverStateAsync`) to highlight features instead of re-uploading data.
- Use **clustering** (`AddClusteredPointsAsync`) or **vector tiles** / **PMTiles** for hundreds of thousands of features.
- High-frequency events (`MouseMove`, `Moving`…) are throttled by `MapOptions.EventThrottle` (16 ms); raise it if your
  handlers are expensive, and only subscribe to the events you need.
- Batch style changes with `UpdateLayerAsync(layer)` or `EvalAsync` when you change many properties at once.

## Offline tilesets with PMTiles (MapLibre)

[PMTiles](https://docs.protomaps.com/pmtiles/) packs a whole tileset (vector or raster) into one file read with HTTP
range requests: ideal for offline maps or for hosting tiles on a plain file server / CDN.

```csharp
var pmtiles = await map.AddPluginAsync(new PmTilesPlugin());

// A local file served by the host…
string url = map.PublishFile(@"C:\maps\france.pmtiles", "application/octet-stream");
// …or a remote archive: "https://cdn.example.com/france.pmtiles"

PmTilesHeader header = await pmtiles.GetHeaderAsync(url);
await map.AddSourceAsync(new VectorSource("france", PmTilesPlugin.Url(url)) { MaxZoom = header.MaxZoom });
await map.AddLayerAsync(new LineLayer("roads", "france", sourceLayer: "roads") { LineColor = "#64748b" });
await map.FitBoundsAsync(header.Bounds);
```

For a fully offline map, also serve the engine, the style, the sprites and the glyphs locally
(see [Advanced topics](advanced.md#offline-and-self-hosted-assets)).

## Custom WebGL layers

Draw anything with WebGL (custom shaders, three.js, deck.gl…) inside the map's rendering pipeline:

```csharp
await map.LoadAssetsAsync(new[] { "https://unpkg.com/three@0.170.0/build/three.min.js" });
await map.AddCustomLayerAsync("pulse", """
    {
      onAdd: function (map, gl) {
        this.start = performance.now();
        // compile shaders, create buffers…
      },
      render: function (gl, args) {
        // args.defaultProjectionData / matrix (engine-specific): draw here
        map.triggerRepaint();   // animate
      }
    }
    """, threeDimensional: false);
```

`map`, `lib` (maplibregl / mapboxgl) and `Cartovia` are in scope. Custom layers are not restored after a style change
(re-add them in `StyleLoaded`). Errors such as a missing `render` function throw `MapException`.

## Error handling

| Situation | What happens |
|---|---|
| Invalid call (unknown layer / source, invalid property value, unknown method) | The awaited call throws `MapException` with the engine message (`Command`, `ScriptStack`) |
| Engine cannot start (CDN unreachable, missing token, no WebGL) | Pending calls throw `MapInitializationException`; `Error` is raised with `IsFatal = true` |
| Tile / style network error | `Error` event (`Status`, `Url`, `SourceId`); the call that triggered it is not affected |
| No answer in time | `TimeoutException` after `MapOptions.CallTimeout` (30 s) |
| Renderer process crash | The page is reloaded and the map re-created; controls, plugins and markers are restored |
| Script error in the page | `map.Log` with `MapLogLevel.Error` |

Log `map.Error` and `map.Log` in production: they are the first place to look when a layer does not appear.

## Testing your integration

- Unit-test your view models against an `IMapHost` test double: `CartoviaMap` only needs two members
  (`PostMessageAsync` and `MessageReceived`). `tests/Cartovia.Core.Tests/FakeMapHost.cs` shows a complete fake that
  answers calls and raises events.
- End-to-end: `tests/Cartovia.Integration.Tests` runs the real page and MapLibre GL JS in headless Edge / Chrome with
  Playwright (`PlaywrightMapHost` emulates the WebView2 messaging API). Copy the harness to test your own layers and
  interactions without a UI.

## Versioning and upgrades

- Packages follow semantic versioning; `dotnet pack` validates the public API against the last published release.
- Engine and plugin versions are pinned (`EngineDefaults`, `PluginAssets`); upgrade them deliberately, run the
  end-to-end tests, and set `MapOptions.EngineVersion` / `EngineScriptUrl` to override them per application.
- See the [changelog](../CHANGELOG.md).

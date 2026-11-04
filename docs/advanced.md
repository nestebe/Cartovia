# Advanced topics

## Calling any engine method

Not everything needs a typed wrapper: `InvokeAsync` calls any method of the JavaScript map object.

```csharp
await map.InvokeAsync("setPadding", new MapPadding(0, 0, 0, 300));
double zoom = await map.InvokeAsync<double>("getZoom");
bool moving = await map.InvokeAsync<bool>("isMoving");

// Sources, markers and popups have the same escape hatch
await map.InvokeSourceAsync("video", "play", CancellationToken.None);
await marker.InvokeAsync("setOpacity", CancellationToken.None, "0.5");
```

Arguments are converted to JSON: layers, sources, expressions, GeoJSON, `LngLat`, `MapPadding`, enums, colors,
anonymous objects and `JsonNode` are all supported.

## Running JavaScript

`EvalAsync` runs a function body receiving `map`, `lib` (maplibregl / mapboxgl), `Cartovia` and `args`.
Use `return`; promises are awaited.

```csharp
int layerCount = await map.EvalAsync<int>("return map.getStyle().layers.length;");
string[] ids = await map.EvalAsync<string[]>(
    "return map.getStyle().layers.filter(l => l.type === args).map(l => l.id);", "symbol") ?? Array.Empty<string>();

await map.LoadAssetsAsync(new[] { "https://unpkg.com/some-lib@1.0.0/dist/lib.js" });
```

## Calling .NET from JavaScript

```csharp
using var registration = map.RegisterHostCallback("getUser", async (data, ct) =>
    await users.FindAsync(data.GetProperty("id").GetInt32(), ct));
```

```js
const user = await Cartovia.callHost('getUser', { id: 42 });
```

## Raw bridge commands

`map.CallAsync(command, args)` sends a raw command of `bridge.js` (see `src/Cartovia.Core/Web/bridge.js`). The typed
API is built on these commands; prefer it whenever possible.

## WebView2 options

Set them before the control is loaded (`mapView.HostOptions`):

| Option | Default | Purpose |
|---|---|---|
| `UserDataFolder` | `%LOCALAPPDATA%\<app>\Cartovia\WebView2` | Cache and storage; must be writable |
| `BrowserExecutableFolder` | Evergreen runtime | Fixed-version runtime folder |
| `AdditionalBrowserArguments` | none | Chromium switches, e.g. `--ignore-gpu-blocklist` |
| `Language` | system | Browser UI language |
| `EnableDevTools` | false | F12 and `OpenDevTools()` |
| `AllowGeolocation` | false | Geolocation permission for `GeolocateControl` |
| `EnableContextMenu` | false | Browser context menu |
| `OpenLinksInBrowser` | true | Links (attribution, popups) open in the default browser |
| `LocalAssetsFolder` | none | Served at `https://assets.cartovia.local/` |
| `DefaultBackgroundColor` | transparent | Color shown before the first frame |

`mapView.Host` (`WebView2MapHost`) gives access to `CoreWebView2`, `Reload()`, `CaptureAsync()`, `PrintToPdfAsync()`
and the `ProcessFailed` event (the page is reloaded automatically after a renderer crash).

## Offline and self-hosted assets

```powershell
./tools/download-offline-assets.ps1 -Destination ./assets
```

```csharp
mapView.HostOptions.LocalAssetsFolder = Path.Combine(AppContext.BaseDirectory, "assets");
mapView.Options.EngineScriptUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.js";
mapView.Options.EngineStylesheetUrl = WebView2HostOptions.LocalAssetsBaseUrl + "maplibre-gl.css";

PluginAssets.DrawScript = WebView2HostOptions.LocalAssetsBaseUrl + "mapbox-gl-draw.js";
PluginAssets.DrawStylesheet = WebView2HostOptions.LocalAssetsBaseUrl + "mapbox-gl-draw.css";
```

Styles, tiles, sprites and glyphs can be served from the same folder (`https://assets.cartovia.local/style.json`).
Check your tile provider's terms before caching its tiles.

## Engine versions

```csharp
mapView.Options.EngineVersion = "5.24.0";                       // CDN URL built from EngineDefaults
mapView.Options.EngineScriptUrl = "https://my-cdn/maplibre-gl.js";  // or any URL
```

MapLibre GL JS 6+ is distributed as ES modules only; Cartovia uses the latest 5.x UMD build by default.

## Errors and timeouts

- Engine errors in a call throw `MapException` (`Command`, `ScriptStack`).
- Initialization failures throw `MapInitializationException` and raise `Error` with `IsFatal = true`.
- Calls time out after `MapOptions.CallTimeout` (30 s) with a `TimeoutException`; every method accepts a `CancellationToken`.
- Script errors and bridge warnings are reported through `map.Log`.

## Threading

All methods can be called from any thread; messages are marshalled to the UI thread. Events are raised on the UI
thread. Awaiting with `ConfigureAwait(false)` inside library code is safe.

## Hosting in another UI framework

`CartoviaMap` only needs an `IMapHost` (send a JSON string, raise a message event). `Cartovia.WebView2` provides
`MapViewController` for any WebView2-based framework; for another web view, serve the embedded page
(`MapWebAssets.Open("index.html")`) and relay messages with `chrome.webview`-compatible semantics.

## WebView2 runtime

```csharp
if (!WebView2Environments.IsRuntimeAvailable())
    Process.Start(new ProcessStartInfo(WebView2Environments.RuntimeDownloadUrl) { UseShellExecute = true });
string? version = WebView2Environments.GetRuntimeVersion();
```

For installers, Microsoft provides the Evergreen bootstrapper and a fixed-version runtime
(`HostOptions.BrowserExecutableFolder`).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Blank map, `Error` with `IsFatal` | Engine script not reachable (proxy, offline): use local assets |
| "A Mapbox access token is required" | Set `AccessToken` or switch to `Engine = MapEngine.MapLibre` |
| 401 / 403 on Mapbox tiles | Token scopes or URL restriction: allow `https://cartovia.local` |
| Labels missing for a layer | `TextFont` not available in the style glyphs |
| Layers disappear after `SetStyleAsync` | Only layers added through Cartovia are restored; check `PreserveUserLayersOnStyleChange` |
| WPF content hidden behind the map | Airspace: use `UseCompositionControl = true` |
| Geolocation does nothing | `HostOptions.AllowGeolocation = true` and Windows location enabled |
| Debugging the page | `HostOptions.EnableDevTools = true`, then `mapView.OpenDevTools()` |

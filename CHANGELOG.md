# Changelog

All notable changes to Cartovia are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).
`dotnet pack` validates the public API against the last published version (package validation), so breaking changes
cannot ship in a minor version by accident.

## [Unreleased]

## [1.2.0] — 2026-11-04

### Added
- **Cartovia.WinUI**: WinUI 3 (Windows App SDK) map control.
- **Cartovia.Maui**: .NET MAUI map control (Windows; other platforms restore and show a placeholder).
- **Request rules** (`MapOptions.RequestRules`, `CartoviaMap.SetRequestRulesAsync`): add headers (e.g. `Authorization`),
  query parameters or credentials to engine requests matching a URL pattern — private tile servers, expiring tokens.
- **Large data**: GeoJSON above `MapOptions.LargeDataThreshold` (512 KB) is served to the engine through a URL of the
  page origin and parsed in a web worker instead of travelling in a message. New `PublishData`, `PublishFile`,
  `SetGeoJsonDataFromFileAsync` and the `IMapDataHost` host capability (implemented by the WebView2 host, with HTTP
  `Range` support).
- **PMTiles** (`PmTilesPlugin`, MapLibre): single-file vector / raster tilesets from a server or a local file.
- **Custom WebGL layers** (`AddCustomLayerAsync`).
- End-to-end test suite running the real bridge and MapLibre GL JS in headless Edge / Chrome (Playwright), plus
  Mapbox GL JS tests enabled by the `MAPBOX_ACCESS_TOKEN` environment variable.
- Documentation guides for WinUI, MAUI, request rules, large data, PMTiles, custom layers and testing.

### Changed
- Style errors reported by the engine through its `error` event during a call (unknown source, invalid property
  value…) now fail the call with a `MapException` instead of succeeding silently.

## [1.1.0] — 2026-11-04

### Added
- **Cartovia.Avalonia**: Avalonia 12 map control (`NativeControlHost` + WebView2), .NET 8 / 10.
- Avalonia showcase sample and guide.

## [1.0.0] — 2026-11-04

First release.

### Added
- **Cartovia.Core**: UI-agnostic typed API over MapLibre GL JS and Mapbox GL JS — camera, sources, every style layer
  and property generated from the official specifications, expressions, markers, popups, controls, Mapbox v3
  interactions, 40+ typed events, GeoJSON model, plugins (Draw, Geocoder, Directions, Turf, custom plugins) and
  extensions (clusters, heatmap, hover / selection, click popups, 3D buildings, terrain, raster overlays).
- **Cartovia.WebView2**: shared WebView2 host (embedded page, navigation, permissions, screenshots, PDF).
- **Cartovia.Wpf** and **Cartovia.WinForms** controls.
- **Cartovia.Mapbox.Services**: Mapbox REST clients (Geocoding, Search Box, Directions, Matrix, Isochrone,
  Optimization, Map Matching, Static Images, Tilequery).

[Unreleased]: https://github.com/nestebe/Cartovia/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/nestebe/Cartovia/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/nestebe/Cartovia/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/nestebe/Cartovia/releases/tag/v1.0.0

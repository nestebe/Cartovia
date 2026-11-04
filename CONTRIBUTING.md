# Contributing to Cartovia

Thanks for your interest! Issues and pull requests are welcome.

## Build and test

Requirements: Windows 10/11, the .NET 10 SDK (with the .NET 6 / 8 targeting packs restored automatically), the
WebView2 runtime, Microsoft Edge or Google Chrome for the end-to-end tests, and the `maui-windows` workload for the
MAUI projects.

```bash
dotnet build Cartovia.sln
dotnet test Cartovia.sln
```

| Test project | What it covers | Needs |
|---|---|---|
| `tests/Cartovia.Core.Tests` | C# API, JSON protocol, layers, expressions, GeoJSON (fake host) | nothing |
| `tests/Cartovia.Mapbox.Services.Tests` | Mapbox REST clients (fake HTTP handler) | nothing |
| `tests/Cartovia.Integration.Tests` | Real `bridge.js` + MapLibre GL JS in headless Edge / Chrome | network (engine and plugin CDNs) |
| Mapbox end-to-end tests (same project) | Mapbox GL JS, Standard style, Mapbox plugins | `MAPBOX_ACCESS_TOKEN` |

## Layout

| Path | Content |
|---|---|
| `src/Cartovia.Core` | Typed API, JSON bridge, embedded page (`Web/index.html`, `bridge.js`, `plugins.js`) |
| `src/Cartovia.Core/Style/Generated` | Layers, enums, style options and expression operators **generated** from the style specs |
| `src/Cartovia.WebView2` | COM WebView2 host shared by WPF, WinForms and Avalonia |
| `src/Cartovia.Wpf`, `Cartovia.WinForms`, `Cartovia.Avalonia`, `Cartovia.WinUI`, `Cartovia.Maui` | UI controls |
| `src/Cartovia.Mapbox.Services` | Mapbox REST clients |
| `tools/` | Style-spec generator, offline assets downloader |
| `docs/` | Guides |

## Guidelines

- Don't edit `Style/Generated/*.g.cs` by hand: update `tools/generate_style_spec.py` and run
  `python tools/generate_style_spec.py --mapbox-version <v> --maplibre-version <v>`.
- Public API changes must stay backward compatible within a major version: `dotnet pack` runs package validation
  against the last published release.
- Every public member needs XML documentation (the build treats missing docs as warnings; keep it at zero).
- New bridge commands need an end-to-end test in `tests/Cartovia.Integration.Tests`.
- Add an entry to `CHANGELOG.md` under *Unreleased*.

## Releasing

Bump `VersionPrefix` in `Directory.Build.props`, update `CHANGELOG.md`, then push a `vX.Y.Z` tag: the CI builds, tests,
publishes to GitHub Packages and nuget.org (Trusted Publishing), and creates the GitHub release.

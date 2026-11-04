# Mapbox.Net.Core

UI-agnostic core of **Mapbox.Net**: a strongly-typed .NET API over **Mapbox GL JS** and **MapLibre GL JS**
running in a web view. Targets .NET 6, 8 and 10.

> Looking for a ready-to-use control? Install **Mapbox.Net.Wpf** or **Mapbox.Net.WinForms**.
> This package is referenced automatically.

## What's inside

- `MapboxMap`: camera (`FlyToAsync`, `FitBoundsAsync`, `JumpToAsync`…), style, sources, layers, images,
  terrain / fog / lights / sky / snow / rain, feature queries, feature state, clusters, markers, popups,
  controls, Mapbox v3 interactions and 40+ typed events. Calls are queued until the map is loaded.
- **Every style layer and property**, generated from the official Mapbox and MapLibre style specifications:
  `FillLayer`, `LineLayer`, `SymbolLayer`, `CircleLayer`, `HeatmapLayer`, `FillExtrusionLayer`, `RasterLayer`,
  `HillshadeLayer`, `ModelLayer`, `SkyLayer`, `ColorReliefLayer`… with XML docs stating which engine supports them.
- Expressions: `Expr.Get`, `Expr.Interpolate`, `Expr.Match`, `Expr.Case`, `Expr.Step`… plus every operator of the spec.
- A GeoJSON model (`Point`, `LineString`, `Polygon`, `Feature`, `FeatureCollection`…) with System.Text.Json converters.
- Plugins: `DrawPlugin`, `GeocoderPlugin`, `DirectionsPlugin`, `TurfPlugin`, and `MapPlugin` / `ScriptPlugin` to write your own.
- Escape hatches: `InvokeAsync("anyMapMethod", args)`, `EvalAsync("return map.getStyle().layers.length;")`, `CallAsync`.

## Example

```csharp
await map.AddSourceAsync(new GeoJsonSource("cities", cities) { GenerateId = true });
await map.AddLayerAsync(new CircleLayer("cities", "cities")
{
    CircleRadius = Expr.Interpolate(Interpolation.Linear, Expr.Get("population"), (100_000, 4), (2_000_000, 18)),
    CircleColor = Expr.Case("#2563eb", (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), "#f97316")),
});
await map.EnableHoverStateAsync("cities", "cities");
await map.ShowPopupOnClickAsync("cities", "<b>{name}</b><br/>{population} inhabitants");
map.OnLayer("cities", LayerEvent.Click, (s, e) => Console.WriteLine(e.Feature?.GetProperty<string>("name")));
```

To host the map in another UI framework, implement `Mapbox.Net.Bridge.IMapHost` (two members) over any web view
that can serve the embedded page (`MapWebAssets`).

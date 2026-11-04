# Expressions

Expressions compute a style value from feature properties, zoom level, feature state… They are built with the static
`Expr` class and can be used wherever a style value is expected.

```csharp
var layer = new CircleLayer("cities", "cities")
{
    // radius grows with the population
    CircleRadius = Expr.Interpolate(Interpolation.Linear, Expr.Get("population"), (10_000, 3), (2_000_000, 20)),
    // color by category, gray by default
    CircleColor = Expr.Match(Expr.Get("type"), "#9ca3af",
        ("capital", "#dc2626"),
        (new[] { "prefecture", "subprefecture" }, "#2563eb")),
    // highlighted when hovered (feature state)
    CircleStrokeWidth = Expr.Case(1, (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), 4)),
};
```

## Data

| Helper | JSON |
|---|---|
| `Expr.Get("name")` | `["get", "name"]` |
| `Expr.Has("name")` | `["has", "name"]` |
| `Expr.Id()` | `["id"]` |
| `Expr.GeometryType()` | `["geometry-type"]` |
| `Expr.FeatureState("hover")` | `["feature-state", "hover"]` |
| `Expr.Zoom()` | `["zoom"]` |
| `Expr.Literal(new[] { 1, 2 })` | `["literal", [1, 2]]` |

## Decisions

```csharp
Expr.Equal(Expr.Get("type"), "park");                       // ==, NotEqual, GreaterThan, LessOrEqual…
Expr.All(Expr.Has("name"), Expr.GreaterThan(Expr.Get("pop"), 1000));
Expr.Any(cond1, cond2);
Expr.Not(Expr.Has("hidden"));
Expr.In(Expr.Get("country"), "FR", "BE", "CH");

Expr.Case("gray",
    (Expr.GreaterThan(Expr.Get("speed"), 90), "red"),
    (Expr.GreaterThan(Expr.Get("speed"), 50), "orange"));

Expr.Match(Expr.Get("kind"), "#999", ("bus", "#2563eb"), ("tram", "#16a34a"));
Expr.Coalesce(Expr.Get("name_fr"), Expr.Get("name"));
```

## Ramps

```csharp
// Continuous: linear, exponential or cubic-bezier interpolation
Expr.Interpolate(Interpolation.Exponential(1.5), Expr.Zoom(), (10, 1), (18, 12));
Expr.InterpolateZoom((5, 1), (15, 8));                      // linear over zoom
Expr.InterpolateHcl(Interpolation.Linear, Expr.Get("t"), (0, "blue"), (30, "red"));

// Discrete steps
Expr.Step(Expr.Get("point_count"), "#51bbd6", (100, "#f1f075"), (750, "#f28cb1"));
```

## Text, math and colors

```csharp
Expr.Concat(Expr.Get("name"), " (", Expr.ToStringOf(Expr.Get("population")), ")");
Expr.Format(
    new Expr.FormatSection(Expr.Get("name"), FontScale: 1.2),
    new Expr.FormatSection("\n"),
    new Expr.FormatSection(Expr.Get("type"), FontScale: 0.8, TextColor: "#6b7280"));

Expr.Get("area") / 1_000_000;                               // C# operators: + - * / % !
Expr.ToNumber(Expr.Get("value"), 0);
Expr.Rgba(255, 0, 0, Expr.Get("alpha"));
Expr.Image(Expr.Concat("icon-", Expr.Get("kind")));

// Variables
Expr.Let(Expr.Var("d") * 2, ("d", Expr.Get("density")));
```

## Every other operator

All operators of both specifications are generated as `Expr.<Name>(params object?[] args)` with their documentation
(`Expr.Distance`, `Expr.Within`, `Expr.Slice`, `Expr.Upcase`, `Expr.Config`, `Expr.MeasureLight`, `Expr.GlobalState`…).
Use `Expr.Op("operator", args…)` or `Expression.Parse(json)` for anything else:

```csharp
var e1 = Expr.Op("index-of", "foo", Expr.Get("name"));
var e2 = Expression.Parse("""["slice", ["get", "code"], 0, 2]""");
```

Arrays passed as arguments are automatically wrapped in `["literal", …]`, except match labels.

## Filters

Filters are boolean expressions:

```csharp
layer.Filter = Expr.All(
    Expr.Equal(Expr.GeometryType(), "Polygon"),
    Expr.GreaterOrEqual(Expr.Get("height"), 20));
await map.SetFilterAsync("buildings", Expr.LessThan(Expr.Get("year"), 1900));
```

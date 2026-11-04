using System.Linq;
using System.Text.Json.Nodes;
using Mapbox.Net.Bridge;
using Mapbox.Net.GeoJson;
using Mapbox.Net.Json;
using Mapbox.Net.Style;
using Xunit;

namespace Mapbox.Net.Core.Tests;

public class StyleAndGeoJsonTests
{
    [Fact]
    public void Layer_SerializesPaintLayoutFilterAndTransitions()
    {
        var layer = new SymbolLayer("labels", "places", "place_label")
        {
            MinZoom = 5,
            Filter = Expr.Equal(Expr.Get("class"), "city"),
            TextField = Expr.Get("name"),
            TextFont = new[] { "Open Sans Bold" },
            TextAnchor = Anchor.TopLeft,
            TextOffset = (0, 1.5),
            TextColor = System.Drawing.Color.FromArgb(128, 255, 0, 0),
            TextHaloWidth = 1,
            TextOpacityTransition = StyleTransition.FromMilliseconds(300),
            Visibility = Visibility.Visible,
        };
        var json = layer.ToJson();

        Assert.Equal("symbol", json["type"]!.GetValue<string>());
        Assert.Equal("place_label", json["source-layer"]!.GetValue<string>());
        Assert.Equal("""["==",["get","class"],"city"]""", json["filter"]!.ToJsonString());
        var layout = json["layout"]!;
        Assert.Equal("""["get","name"]""", layout["text-field"]!.ToJsonString());
        Assert.Equal("""["Open Sans Bold"]""", layout["text-font"]!.ToJsonString());
        Assert.Equal("top-left", layout["text-anchor"]!.GetValue<string>());
        Assert.Equal("[0,1.5]", layout["text-offset"]!.ToJsonString());
        Assert.Equal("visible", layout["visibility"]!.GetValue<string>());
        var paint = json["paint"]!;
        Assert.Equal("rgba(255,0,0,0.502)", paint["text-color"]!.GetValue<string>());
        Assert.Equal(300, paint["text-opacity-transition"]!["duration"]!.GetValue<double>());
    }

    [Fact]
    public void Layer_GenericSetters_AndRawLayer()
    {
        var raw = new RawLayer("custom", "line", "src");
        raw.SetPaint("line-color", "#000").SetLayout("line-cap", LineCap.Round).SetPaint("line-width", Expr.InterpolateZoom((5, 1), (15, 8)));
        var json = raw.ToJson();
        Assert.Equal("round", json["layout"]!["line-cap"]!.GetValue<string>());
        Assert.Equal("""["interpolate",["linear"],["zoom"],5,1,15,8]""", json["paint"]!["line-width"]!.ToJsonString());
    }

    [Fact]
    public void Expressions_MatchCaseStepLiteralAndOperators()
    {
        var match = Expr.Match(Expr.Get("type"), "#999", ("park", "#0f0"), (new[] { "lake", "river" }, "#00f"));
        Assert.Equal("""["match",["get","type"],"park","#0f0",["lake","river"],"#00f","#999"]""", match.ToJson());

        var cs = Expr.Case(1.0, (Expr.Equal(Expr.FeatureState("hover"), true), 2.0));
        Assert.Equal("""["case",["==",["feature-state","hover"],true],2,1]""", cs.ToJson());

        var step = Expr.Step(Expr.Get("count"), "small", (10, "medium"), (100, "large"));
        Assert.Equal("""["step",["get","count"],"small",10,"medium",100,"large"]""", step.ToJson());

        Assert.Equal("""["in",["get","k"],["literal",["a","b"]]]""", Expr.In(Expr.Get("k"), "a", "b").ToJson());
        Assert.Equal("""["*",["get","v"],2]""", (Expr.Get("v") * 2).ToJson());
        Assert.Equal("""["interpolate",["exponential",1.5],["zoom"],0,0,10,1]""",
            Expr.Interpolate(Interpolation.Exponential(1.5), Expr.Zoom(), (0, 0), (10, 1)).ToJson());
        // Generated operator
        Assert.Equal("""["distance",{"type":"Point","coordinates":[0,0]}]""",
            Expr.Distance(JsonNode.Parse("""{"type":"Point","coordinates":[0,0]}""")).ToJson());
        Assert.Equal("""["get","x"]""", Expression.Parse("""["get","x"]""").ToJson());
    }

    [Fact]
    public void Sources_Serialize()
    {
        var fc = new FeatureCollection().Add(new Feature(new Point(1, 2), new { name = "A" }, id: 1));
        var src = new GeoJsonSource("pts", fc) { Cluster = true, ClusterRadius = 40, PromoteId = "name" }.ToJson();
        Assert.Equal("geojson", src["type"]!.GetValue<string>());
        Assert.True(src["cluster"]!.GetValue<bool>());
        Assert.Equal("FeatureCollection", src["data"]!["type"]!.GetValue<string>());

        var raster = new RasterSource("osm", "https://tile.openstreetmap.org/{z}/{x}/{y}.png") { TileSize = 256, Attribution = "© OSM" }.ToJson();
        Assert.Equal("https://tile.openstreetmap.org/{z}/{x}/{y}.png", raster["tiles"]![0]!.GetValue<string>());

        var dem = RasterDemSource.MapboxTerrain().ToJson();
        Assert.Equal("raster-dem", dem["type"]!.GetValue<string>());

        var url = new GeoJsonSource("remote", "https://example.com/data.geojson").ToJson();
        Assert.Equal("https://example.com/data.geojson", url["data"]!.GetValue<string>());
    }

    [Fact]
    public void StyleOptions_Serialize()
    {
        var fog = new FogOptions { Color = "white", HorizonBlend = 0.1, Range = new[] { 0.5, 10.0 } }.ToJson();
        Assert.Equal("white", fog["color"]!.GetValue<string>());
        Assert.Equal("[0.5,10]", fog["range"]!.ToJsonString());
        var terrain = new TerrainOptions { Source = "dem", Exaggeration = 1.5 }.ToJson();
        Assert.Equal("dem", terrain["source"]!.GetValue<string>());
    }

    [Fact]
    public void GeoJson_RoundTrips()
    {
        const string json = """{"type":"FeatureCollection","features":[{"type":"Feature","id":"a","geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]},"properties":{"n":1}},{"type":"Feature","geometry":{"type":"MultiPoint","coordinates":[[1,2,3],[4,5]]},"properties":{}}]}""";
        var fc = GeoJsonObject.Parse<FeatureCollection>(json);
        Assert.Equal(2, fc.Features.Count);
        Assert.IsType<Polygon>(fc.Features[0].Geometry);
        Assert.Equal(3, ((MultiPoint)fc.Features[1].Geometry!).Coordinates[0].Altitude);
        Assert.Equal(1, fc.Features[0].GetProperty<int>("n"));
        Assert.Equal(new LngLatBounds(0, 0, 4, 5), fc.GetBounds());

        var again = GeoJsonObject.Parse<FeatureCollection>(fc.ToJson());
        Assert.Equal(fc.ToJson(), again.ToJson());
    }

    [Fact]
    public void GeoJson_Helpers()
    {
        var circle = Polygon.Circle(new LngLat(2.35, 48.85), 1000);
        var bounds = circle.GetBounds()!.Value;
        Assert.InRange(new LngLat(bounds.East, 48.85).DistanceTo(new LngLat(2.35, 48.85)), 990, 1010);

        var line = new LineString(new LngLat(0, 0), new LngLat(0, 1));
        Assert.InRange(line.GetLength(), 111_000, 111_400);

        var rect = Polygon.FromBounds(new LngLatBounds(0, 0, 1, 1));
        Assert.Equal(5, rect.Coordinates[0].Count);
    }

    [Fact]
    public void LngLat_JsonAndTypeConverter()
    {
        Assert.Equal("[2.35,48.85]", MapboxJson.Serialize(new LngLat(2.35, 48.85)));
        Assert.Equal(new LngLat(1, 2), MapboxJson.Deserialize<LngLat>("""{"lng":1,"lat":2}"""));
        Assert.Equal(new LngLat(1, 2), MapboxJson.Deserialize<LngLat>("[1,2]"));
        Assert.Equal(new LngLat(2.35, 48.85), LngLatTypeConverter.Parse("2.35, 48.85"));
        var conv = System.ComponentModel.TypeDescriptor.GetConverter(typeof(LngLat));
        Assert.Equal(new LngLat(-1.5, 3), conv.ConvertFromInvariantString("-1.5;3"));
        Assert.Equal(new LngLatBounds(1, 2, 3, 4), MapboxJson.Deserialize<LngLatBounds>("""{"_sw":{"lng":1,"lat":2},"_ne":{"lng":3,"lat":4}}"""));
    }

    [Fact]
    public void Enums_AreKebabCase()
    {
        Assert.Equal("top-left", MapboxJson.EnumToString(Controls.ControlPosition.TopLeft));
        Assert.Equal("viewport-glyph", MapboxJson.EnumToString(TextRotationAlignment.ViewportGlyph));
        Assert.Equal("\"line-center\"", MapboxJson.Serialize(SymbolPlacement.LineCenter));
        Assert.Equal(SymbolPlacement.LineCenter, MapboxJson.Deserialize<SymbolPlacement>("\"line-center\""));
        Assert.Equal("nauticalmiles", MapboxJson.EnumToString(Plugins.TurfUnits.NauticalMiles));
    }

    [Fact]
    public void BridgeJson_ConvertsAnonymousObjectsAndPadding()
    {
        Assert.Equal("""{"hover":true}""", BridgeJson.ToNode(new { hover = true })!.ToJsonString());
        Assert.Equal("""{"top":10,"right":10,"bottom":10,"left":10}""", BridgeJson.ToNode((MapPadding)10)!.ToJsonString());
    }

    [Fact]
    public void Feature_AnonymousProperties()
    {
        var f = new Feature(new Point(0, 0), new { name = "x", population = 12 });
        Assert.Equal("x", f.GetProperty<string>("name"));
        f.SetProperty("population", 13);
        Assert.Equal(13, f.GetProperty<int>("population"));
    }

    [Fact]
    public void EmbeddedWebAssets_ArePresent()
    {
        foreach (var asset in new[] { "index.html", "bridge.js", "plugins.js" })
        {
            using var s = MapWebAssets.Open(asset);
            Assert.NotNull(s);
            Assert.True(s!.Length > 100);
        }
        Assert.Null(MapWebAssets.Open("missing.js"));
        Assert.StartsWith("text/javascript", MapWebAssets.GetContentType("bridge.js"));
    }

    [Fact]
    public void LayerFactory_CreatesTypedLayers()
    {
        Assert.IsType<FillExtrusionLayer>(Layer.CreateOfType("fill-extrusion", "a"));
        Assert.IsType<ColorReliefLayer>(Layer.CreateOfType("color-relief", "b"));
        Assert.IsType<RawLayer>(Layer.CreateOfType("unknown", "c"));
    }
}

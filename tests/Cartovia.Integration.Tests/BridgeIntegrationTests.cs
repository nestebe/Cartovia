using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.GeoJson;
using Cartovia.Plugins;
using Cartovia.Style;
using Microsoft.Playwright;
using Xunit;

namespace Cartovia.Integration.Tests;

/// <summary>
/// End-to-end tests: C# API → JSON → bridge.js → real MapLibre GL JS in headless Edge → events back to C#.
/// Requires Microsoft Edge and network access to the engine / plugin CDNs.
/// </summary>
[Collection("browser")]
public sealed class BridgeIntegrationTests
{
    private readonly BrowserFixture _fixture;

    public BridgeIntegrationTests(BrowserFixture fixture) => _fixture = fixture;

    private static readonly Polygon Square = Polygon.FromBounds(new LngLatBounds(2.30, 48.82, 2.40, 48.88));

    private Task<(PlaywrightMapHost Host, CartoviaMap Map)> CreateAsync(Action<MapOptions>? configure = null)
        => PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options(configure));

    private static async Task AddSquareAsync(CartoviaMap map, string id = "zone")
    {
        await map.AddSourceAsync(new GeoJsonSource(id, new FeatureCollection().Add(new Feature(Square, new { name = "Square" }, id: 1))));
        await map.AddLayerAsync(new FillLayer(id + "-fill", id)
        {
            FillColor = Expr.Case("#3b82f6", (Expr.Equal(Expr.Coalesce(Expr.FeatureState("hover"), false), true), "#f97316")),
        });
    }

    [Fact]
    public async Task Loads_ReportsVersionAndInitialCamera()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        await map.WhenLoadedAsync().WithTimeout(40);

        Assert.StartsWith("5.", map.EngineVersion);
        var camera = await map.GetCameraAsync();
        Assert.Equal(10, camera.Zoom, 3);
        Assert.Equal(2.35, camera.Center.Longitude, 3);
        Assert.Equal(10, await map.InvokeAsync<double>("getZoom"), 3);
        Assert.Equal(1, await map.EvalAsync<int>("return map.getStyle().layers.length;"));
    }

    [Fact]
    public async Task SourcesLayers_AreRendered_AndQueryable()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        await AddSquareAsync(map);
        await map.IdleAsync();

        Assert.True(await map.HasSourceAsync("zone"));
        Assert.True(await map.HasLayerAsync("zone-fill"));
        Assert.Contains("zone-fill", await map.GetLayerIdsAsync());

        var hits = await map.QueryRenderedFeaturesAsync(new ScreenPoint(400, 300), new QueryOptions { Layers = new[] { "zone-fill" } });
        var feature = Assert.Single(hits);
        Assert.Equal("Square", feature.GetProperty<string>("name"));
        Assert.Equal("zone-fill", feature.LayerId);
        Assert.IsType<Polygon>(feature.Geometry);

        await map.SetPaintPropertyAsync("zone-fill", "fill-opacity", 0.5);
        Assert.Equal(0.5, (await map.GetPaintPropertyAsync("zone-fill", "fill-opacity")).GetDouble());
        await map.SetLayerVisibilityAsync("zone-fill", false);
        Assert.Equal("none", (await map.GetLayoutPropertyAsync("zone-fill", "visibility")).GetString());
    }

    [Fact]
    public async Task MouseEvents_ReachMapAndLayerHandlers()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        await AddSquareAsync(map);
        await map.IdleAsync();

        var mapClick = new TaskCompletionSource<MapMouseEventArgs>();
        var layerClick = new TaskCompletionSource<MapMouseEventArgs>();
        map.Click += (_, e) => mapClick.TrySetResult(e);
        using var sub = map.OnLayer("zone-fill", LayerEvent.Click, (_, e) => layerClick.TrySetResult(e));
        await Task.Delay(300); // subscriptions reach the page

        await host.Page.Mouse.ClickAsync(400, 300);

        var m = await mapClick.Task.WithTimeout();
        Assert.Equal(2.35, m.LngLat.Longitude, 2);
        Assert.Equal(Cartovia.Events.MouseButton.Left, m.Button);
        var l = await layerClick.Task.WithTimeout();
        Assert.Equal("zone-fill", l.LayerId);
        Assert.Equal("Square", l.Feature!.GetProperty<string>("name"));
    }

    [Fact]
    public async Task CameraAnimations_CompleteAndRaiseEvents()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        var moveEnd = new TaskCompletionSource<MapCameraEventArgs>();
        map.MoveEnded += (_, e) => { if (Math.Abs(e.Camera.Zoom - 5) < 0.01) moveEnd.TrySetResult(e); };

        var final = await map.FlyToAsync(new CameraOptions { Center = new LngLat(4.83, 45.76), Zoom = 5 },
            new AnimationOptions { Duration = TimeSpan.FromMilliseconds(300) });
        Assert.Equal(5, final.Zoom, 2);
        Assert.Equal(4.83, final.Center.Longitude, 2);
        Assert.False((await moveEnd.Task.WithTimeout()).IsUserInitiated);

        var fitted = await map.FitBoundsAsync(new LngLatBounds(2.30, 48.82, 2.40, 48.88), new FitBoundsOptions { Animation = new AnimationOptions { Animate = false } });
        Assert.InRange(fitted.Zoom, 10, 14);
        var px = await map.ProjectAsync(fitted.Center);
        Assert.Equal(400, px.X, 0);
    }

    [Fact]
    public async Task StyleChange_RestoresUserSourcesAndLayers()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        await AddSquareAsync(map);
        var restored = new TaskCompletionSource<bool>();
        map.UserLayersRestored += (_, _) => restored.TrySetResult(true);

        await map.SetStyleAsync(TestStyles.Background("#111827"), diff: false);
        await restored.Task.WithTimeout();

        Assert.True(await map.HasLayerAsync("zone-fill"));
        Assert.Equal("#111827", (await map.GetPaintPropertyAsync("bg", "background-color")).GetString());
    }

    [Fact]
    public async Task FeatureState_RoundTrips()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        await AddSquareAsync(map);
        var id = new FeatureIdentifier("zone", 1);
        await map.SetFeatureStateAsync(id, new { hover = true });
        var state = await map.GetFeatureStateAsync(id);
        Assert.True(state!["hover"]!.GetValue<bool>());
        await map.RemoveFeatureStateAsync(id, "hover");
        Assert.Null((await map.GetFeatureStateAsync(id))?["hover"]);
    }

    [Fact]
    public async Task MarkersPopupsAndHtmlControls_RaiseEvents()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;

        var marker = await map.AddMarkerAsync(new MarkerOptions(new LngLat(2.35, 48.85)) { Color = "#e11d48", Tooltip = "pin" });
        var clicked = new TaskCompletionSource<bool>();
        marker.Click += (_, _) => clicked.TrySetResult(true);
        await host.Page.ClickAsync(".maplibregl-marker");
        Assert.True(await clicked.Task.WithTimeout());

        await marker.SetPositionAsync(new LngLat(2.36, 48.86));
        var pos = await marker.InvokeAsync("getLngLat", default);
        Assert.Equal(2.36, pos[0].GetDouble(), 3);

        var popup = await map.ShowPopupAsync(new LngLat(2.35, 48.85), "<b id='pop'>Hello</b>");
        Assert.Equal("Hello", await host.Page.InnerTextAsync("#pop"));
        await popup.CloseAsync();
        Assert.Equal(0, await host.Page.Locator("#pop").CountAsync());

        var control = await map.AddControlAsync(new HtmlControl("<button id='go' data-command='go'>Go</button>"));
        var command = new TaskCompletionSource<string>();
        control.CommandInvoked += (_, e) => command.TrySetResult(e.Command);
        await host.Page.ClickAsync("#go");
        Assert.Equal("go", await command.Task.WithTimeout());

        await marker.RemoveAsync();
        Assert.Equal(0, await host.Page.Locator(".maplibregl-marker").CountAsync());
    }

    [Fact]
    public async Task Clusters_ExpansionZoomAndLeaves()
    {
        var (host, map) = await CreateAsync(o => o.Zoom = 3);
        await using var _ = host;
        var rnd = new Random(1);
        var points = new FeatureCollection(Enumerable.Range(0, 200).Select(i =>
            new Feature(new Point(2.3 + rnd.NextDouble() * 0.1, 48.8 + rnd.NextDouble() * 0.1), new { i })));
        var layers = await map.AddClusteredPointsAsync("pts", points);
        await map.IdleAsync();

        var clusters = await map.QueryRenderedFeaturesAsync(new QueryOptions { Layers = new[] { layers.ClusterLayerId } });
        Assert.NotEmpty(clusters);
        long clusterId = clusters[0].GetProperty<long>("cluster_id");
        double zoom = await map.GetClusterExpansionZoomAsync("pts", clusterId);
        Assert.True(zoom > 3);
        var leaves = await map.GetClusterLeavesAsync("pts", clusterId, limit: 5);
        Assert.Equal(5, leaves.Count);
    }

    [Fact]
    public async Task RequestRules_AddHeadersAndQueryParameters_AndCanBeUpdated()
    {
        var headers = new ConcurrentQueue<(string Url, string? Header)>();
        var (host, map) = await CreateAsync(o => o.RequestRules.Add(
            new RequestRule("https://tiles.test/*").WithHeader("X-Api-Key", "secret-1").WithQueryParameter("token", "abc")));
        await using var _ = host;

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        await host.Page.RouteAsync("https://tiles.test/**", async route =>
        {
            route.Request.Headers.TryGetValue("x-api-key", out var h);
            headers.Enqueue((route.Request.Url, h));
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200, ContentType = "image/png", BodyBytes = png,
                Headers = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = "*",
                    ["Access-Control-Allow-Headers"] = "*",
                },
            });
        });

        await map.AddSourceAsync(new RasterSource("r", "https://tiles.test/{z}/{x}/{y}.png") { TileSize = 256 });
        await map.AddLayerAsync(new RasterLayer("r", "r"));
        await map.IdleAsync();

        var first = headers.FirstOrDefault(h => h.Header is not null && !h.Url.Contains("OPTIONS"));
        Assert.Equal("secret-1", first.Header);
        Assert.Contains("token=abc", first.Url);

        await map.SetRequestRulesAsync(new[] { new RequestRule("https://tiles.test/*").WithHeader("X-Api-Key", "secret-2") });
        await map.JumpToAsync(new CameraOptions { Zoom = 12 });
        await map.IdleAsync();
        Assert.Contains(headers, h => h.Header == "secret-2" && !h.Url.Contains("token="));
    }

    [Fact]
    public async Task Plugins_DrawTurfAndScript()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;

        var draw = await map.AddPluginAsync(new DrawPlugin());
        var ids = await draw.AddAsync(Square);
        Assert.Single(ids);
        var all = await draw.GetAllAsync();
        Assert.Single(all.Features);
        await draw.ChangeModeAsync(DrawModes.DrawPolygon);
        Assert.Equal(DrawModes.DrawPolygon, await draw.GetModeAsync());

        var turf = await map.AddPluginAsync(new TurfPlugin());
        double area = await turf.AreaAsync(Square);
        Assert.InRange(area, 45_000_000, 52_000_000);   // ~7.3 km x 6.7 km = ~48.8 km²
        var buffer = await turf.BufferAsync(new Point(2.35, 48.85), 1, TurfUnits.Kilometers);
        Assert.IsType<Polygon>(buffer.Geometry);

        var script = new ScriptPlugin("echo", """
            Cartovia.definePlugin('echo', function (ctx) {
              return { commands: {
                hello: function (n) { return 'Hello ' + n; },
                ask: function () { return ctx.callHost('double', { v: 21 }); }
              } };
            });
            """)
        {
            HostCallHandler = (method, data, ct) => Task.FromResult<object?>(data.GetProperty("v").GetInt32() * 2),
        };
        await map.AddPluginAsync(script);
        Assert.Equal("Hello you", await script.CallAsync<string>("hello", "you"));
        Assert.Equal(42, await script.CallAsync<int>("ask"));
    }

    [Fact]
    public async Task EngineErrors_SurfaceAsMapException()
    {
        var (host, map) = await CreateAsync();
        await using var _ = host;
        var ex = await Assert.ThrowsAsync<MapException>(() => map.InvokeAsync("noSuchMethod"));
        Assert.Contains("noSuchMethod", ex.Message);
        var layerError = await Assert.ThrowsAsync<MapException>(() => map.AddLayerAsync(new FillLayer("x", "missing-source")));
        Assert.Contains("missing-source", layerError.Message);
        await Assert.ThrowsAsync<MapException>(() => map.SetPaintPropertyAsync("bg", "background-color", 42));
    }
}

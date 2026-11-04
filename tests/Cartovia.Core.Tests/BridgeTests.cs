using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.Plugins;
using Cartovia.Style;
using Xunit;

namespace Cartovia.Core.Tests;

public class BridgeTests
{
    [Fact]
    public void Ready_SendsInitWithEngineAssetsAndDefaultSubscriptions()
    {
        var host = new FakeMapHost();
        var options = new MapOptions { Engine = MapEngine.Mapbox, AccessToken = "pk.test", Center = new LngLat(2.35, 48.85), Zoom = 11, ScrollZoom = false };
        options.InitialControls.Add(new NavigationControl());
        using var map = new CartoviaMap(host, options);

        host.Ready();

        var init = host.Sent.Single(m => m["t"]!.GetValue<string>() == "init")["options"]!.AsObject();
        Assert.Equal("mapbox", init["engine"]!.GetValue<string>());
        Assert.Equal("pk.test", init["accessToken"]!.GetValue<string>());
        Assert.Contains(EngineDefaults.MapboxVersion, init["engineScript"]!.GetValue<string>());
        var mo = init["mapOptions"]!.AsObject();
        Assert.Equal(MapStyles.MapboxStandard, mo["style"]!.GetValue<string>());
        Assert.Equal("[2.35,48.85]", mo["center"]!.ToJsonString());
        Assert.False(mo["scrollZoom"]!.GetValue<bool>());
        var subs = init["subscriptions"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("load", subs);
        Assert.Contains("moveend", subs);
        Assert.Single(init["controls"]!.AsArray());
        Assert.Equal("navigation", init["controls"]![0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task RequestRules_AreSentAtInitAndUpdatedAtRuntime()
    {
        var host = new FakeMapHost { Responder = _ => null };
        var options = new MapOptions();
        options.RequestRules.Add(new RequestRule("https://tiles.example.com/*")
            .WithHeader("Authorization", "Bearer a").WithQueryParameter("key", "k").ForResourceTypes(RequestResourceType.Tile));
        using var map = new CartoviaMap(host, options);
        host.Ready();

        var rule = host.Sent.Single(m => m["t"]!.GetValue<string>() == "init")["options"]!["requestRules"]![0]!;
        Assert.Equal("https://tiles.example.com/*", rule["pattern"]!.GetValue<string>());
        Assert.Equal("Bearer a", rule["headers"]!["Authorization"]!.GetValue<string>());
        Assert.Equal("k", rule["query"]!["key"]!.GetValue<string>());
        Assert.Equal("Tile", rule["types"]![0]!.GetValue<string>());

        host.Created();
        await map.SetRequestRulesAsync(new[] { new RequestRule("https://x/*") { Credentials = RequestCredentials.Include } });
        var update = host.Calls("requests.setRules").Single()["args"]!["rules"]![0]!;
        Assert.Equal("include", update["credentials"]!.GetValue<string>());
        Assert.Single(map.Options.RequestRules);
    }

    [Fact]
    public void MapLibre_UsesMapLibreAssetsAndAttributionObject()
    {
        var host = new FakeMapHost();
        using var map = new CartoviaMap(host, new MapOptions { Engine = MapEngine.MapLibre, CustomAttribution = "© Me", Projection = "globe" });
        host.Ready();
        var init = host.Sent.Single(m => m["t"]!.GetValue<string>() == "init")["options"]!.AsObject();
        Assert.Contains("maplibre-gl@" + EngineDefaults.MapLibreVersion, init["engineScript"]!.GetValue<string>());
        Assert.Equal("© Me", init["mapOptions"]!["attributionControl"]!["customAttribution"]!.GetValue<string>());
        Assert.Equal("globe", init["projection"]!.GetValue<string>());
        Assert.Null(init["mapOptions"]!["projection"]);
    }

    [Fact]
    public async Task Calls_AreQueuedUntilLoad()
    {
        var host = new FakeMapHost { Responder = _ => null };
        using var map = new CartoviaMap(host, new MapOptions());
        host.Ready();

        var task = map.SetZoomAsync(5);
        await Task.Delay(50);
        Assert.Empty(host.Calls("map.animate"));
        Assert.False(task.IsCompleted);

        host.Created();
        host.Load();
        await task;
        var call = host.Calls("map.animate").Single();
        Assert.Equal("jumpTo", call["args"]!["method"]!.GetValue<string>());
        Assert.Equal(5, call["args"]!["options"]!["zoom"]!.GetValue<double>());
    }

    [Fact]
    public async Task ErrorResult_ThrowsMapException()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => throw new InvalidOperationException("Layer 'x' does not exist");
        var ex = await Assert.ThrowsAsync<MapException>(() => map.RemoveLayerAsync("x"));
        Assert.Contains("does not exist", ex.Message);
        Assert.Equal("map.invoke", ex.Command);
    }

    [Fact]
    public async Task Fatal_FailsLoadingAndRaisesFatalError()
    {
        var host = new FakeMapHost();
        using var map = new CartoviaMap(host, new MapOptions());
        MapErrorEventArgs? error = null;
        map.Error += (_, e) => error = e;
        host.Ready();
        host.Receive(new JsonObject { ["t"] = "fatal", ["message"] = "A Mapbox access token is required" });

        await Assert.ThrowsAsync<MapInitializationException>(() => map.WhenLoadedAsync());
        Assert.NotNull(error);
        Assert.True(error!.IsFatal);
    }

    [Fact]
    public async Task Calls_TimeOut()
    {
        var host = new FakeMapHost();
        using var map = new CartoviaMap(host, new MapOptions { CallTimeout = TimeSpan.FromMilliseconds(100) });
        host.Ready(); host.Created(); host.Load();
        await Assert.ThrowsAsync<TimeoutException>(() => map.InvokeAsync("getZoom"));
    }

    [Fact]
    public void Load_SetsCameraAndRaisesLoaded()
    {
        var host = new FakeMapHost();
        using var map = new CartoviaMap(host, new MapOptions());
        MapCameraEventArgs? loaded = null;
        map.Loaded += (_, e) => loaded = e;
        host.Ready(); host.Created(); host.Load();

        Assert.True(map.IsLoaded);
        Assert.NotNull(loaded);
        Assert.Equal(10, loaded!.Camera.Zoom);
        Assert.Equal(new LngLat(2.35, 48.85), map.Camera!.Center);
        Assert.Equal(48, map.Camera.Bounds!.Value.South);
    }

    [Fact]
    public async Task SubscribingAfterInit_SendsEventsOn_AndDispatchesTypedArgs()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        MapMouseEventArgs? click = null;
        map.Click += (_, e) => click = e;

        var on = await host.WaitForCallAsync("events.on");
        Assert.Equal("click", on["args"]!["name"]!.GetValue<string>());

        host.Event("click", new JsonObject
        {
            ["lngLat"] = new JsonArray(1.5, 43.6), ["point"] = new JsonArray(10, 20), ["button"] = 0, ["shiftKey"] = true,
        });
        Assert.NotNull(click);
        Assert.Equal(new LngLat(1.5, 43.6), click!.LngLat);
        Assert.Equal(new ScreenPoint(10, 20), click.Point);
        Assert.Equal(MouseButton.Left, click.Button);
        Assert.True(click.ShiftKey);
    }

    [Fact]
    public async Task LayerEvents_AreRoutedBySubscriptionId_AndCarryFeatures()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        MapMouseEventArgs? args = null;
        var sub = map.OnLayer("cities", LayerEvent.Click, (_, e) => args = e);
        var on = await host.WaitForCallAsync("events.on");
        var sid = on["args"]!["sid"]!.GetValue<long>();

        host.Event("click", new JsonObject
        {
            ["lngLat"] = new JsonArray(2, 48),
            ["features"] = new JsonArray(new JsonObject
            {
                ["type"] = "Feature", ["id"] = 7,
                ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(2, 48) },
                ["properties"] = new JsonObject { ["name"] = "Paris" },
                ["layer"] = new JsonObject { ["id"] = "cities", ["type"] = "circle" },
                ["source"] = "cities-src",
            }),
        }, sid: sid, layer: "cities");

        Assert.NotNull(args);
        Assert.Equal("cities", args!.LayerId);
        Assert.Equal("Paris", args.Feature!.GetProperty<string>("name"));
        Assert.Equal("cities-src", args.Feature.Source);
        Assert.Equal("circle", args.Feature.LayerType);

        sub.Dispose();
        var off = await host.WaitForCallAsync("events.off");
        Assert.Equal(sid, off["args"]!["sid"]!.GetValue<long>());
    }

    [Fact]
    public async Task Markers_SendDefinition_AndReceiveDragEvents()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        var marker = await map.AddMarkerAsync(new MarkerOptions(new LngLat(1, 2))
        {
            Color = "red", Draggable = true, Anchor = Anchor.Bottom,
            Popup = new PopupOptions { Html = "<b>Hi</b>" },
        });

        var add = host.Calls("marker.add").Single()["args"]!;
        Assert.Equal(marker.Id, add["id"]!.GetValue<string>());
        Assert.Equal("bottom", add["options"]!["anchor"]!.GetValue<string>());
        Assert.Equal("<b>Hi</b>", add["popup"]!["html"]!.GetValue<string>());

        LngLat? dropped = null;
        marker.DragEnded += (_, e) => dropped = e.Position;
        host.Event("marker.dragend", new JsonObject { ["lngLat"] = new JsonArray(3, 4) }, target: marker.Id);
        Assert.Equal(new LngLat(3, 4), dropped);
        Assert.Equal(new LngLat(3, 4), marker.Position);

        await marker.RemoveAsync();
        Assert.Single(host.Calls("marker.remove"));
        Assert.Empty(map.Markers);
    }

    [Fact]
    public async Task HtmlControl_RaisesCommands()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        var control = await map.AddControlAsync(new HtmlControl("<button data-command='home'>⌂</button>"));
        string? command = null;
        control.CommandInvoked += (_, e) => command = e.Command;
        host.Event("control.command", new JsonObject { ["command"] = "home" }, target: control.Id);
        Assert.Equal("home", command);
    }

    [Fact]
    public async Task ScriptPlugin_CreatesInvokesAndAnswersHostCalls()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = m => m["cmd"]!.GetValue<string>() == "plugin.invoke" ? JsonValue.Create("Hello world") : null;
        var plugin = new ScriptPlugin("hello", "Cartovia.definePlugin('hello', function () { return {}; });")
        {
            HostCallHandler = (method, data, ct) => Task.FromResult<object?>(new { echo = method + ":" + data.GetProperty("x").GetInt32() }),
        };
        await map.AddPluginAsync(plugin);

        var create = host.Calls("plugin.create").Single()["args"]!;
        Assert.Equal("hello", create["name"]!.GetValue<string>());
        Assert.Contains("definePlugin", create["source"]!.GetValue<string>());

        Assert.Equal("Hello world", await plugin.CallAsync<string>("greet", "world"));

        host.Receive(new JsonObject { ["t"] = "hostCall", ["id"] = 99, ["name"] = $"plugin:{plugin.InstanceId}:compute", ["data"] = new JsonObject { ["x"] = 4 } });
        JsonObject? reply = null;
        for (int i = 0; i < 100 && reply is null; i++)
        {
            reply = host.Sent.FirstOrDefault(m => m["t"]!.GetValue<string>() == "hostResult");
            if (reply is null) await Task.Delay(10);
        }
        Assert.NotNull(reply);
        Assert.True(reply!["ok"]!.GetValue<bool>());
        Assert.Equal("compute:4", reply["value"]!["echo"]!.GetValue<string>());
    }

    [Fact]
    public async Task DirectionsPlugin_RejectsMapLibre()
    {
        using var map = FakeMapHost.CreateLoadedMap(out _, new MapOptions { Engine = MapEngine.MapLibre });
        await Assert.ThrowsAsync<NotSupportedException>(() => map.AddPluginAsync(new DirectionsPlugin()));
    }

    [Fact]
    public async Task Draw_ParsesCreatedFeatures()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        var draw = await map.AddPluginAsync(new DrawPlugin { DisplayControlsDefault = false, Controls = new DrawControls { Polygon = true, Trash = true } });
        var opts = host.Calls("plugin.create").Single()["args"]!["options"]!;
        Assert.True(opts["drawOptions"]!["controls"]!["polygon"]!.GetValue<bool>());
        Assert.Equal("top-left", opts["position"]!.GetValue<string>());

        DrawEventArgs? created = null;
        draw.Created += (_, e) => created = e;
        host.Event("plugin", new JsonObject
        {
            ["event"] = "create",
            ["data"] = new JsonObject
            {
                ["features"] = new JsonArray(new JsonObject
                {
                    ["type"] = "Feature", ["id"] = "abc", ["properties"] = new JsonObject(),
                    ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(1, 2) },
                }),
            },
        }, target: draw.InstanceId);
        Assert.NotNull(created);
        Assert.IsType<GeoJson.Point>(created!.Features.Single().Geometry);
    }

    [Fact]
    public async Task AddLayer_SerializesTypedLayer()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => null;
        await map.AddLayerAsync(new CircleLayer("pts", "src") { CircleRadius = 6, CircleColor = "#ff0000" }, "labels");
        var args = host.Calls("map.invoke").Single()["args"]!;
        Assert.Equal("addLayer", args["method"]!.GetValue<string>());
        Assert.Equal("pts", args["args"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("labels", args["args"]![1]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryRenderedFeatures_ParsesFeatures()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        host.Responder = _ => new JsonArray(new JsonObject
        {
            ["type"] = "Feature", ["id"] = 1, ["properties"] = new JsonObject { ["v"] = 3 },
            ["geometry"] = new JsonObject { ["type"] = "LineString", ["coordinates"] = new JsonArray(new JsonArray(0, 0), new JsonArray(1, 1)) },
            ["layer"] = new JsonObject { ["id"] = "roads", ["type"] = "line" }, ["sourceLayer"] = "road",
        });
        var features = await map.QueryRenderedFeaturesAsync(new ScreenPoint(5, 5), new QueryOptions { Layers = new[] { "roads" } });
        Assert.Single(features);
        Assert.Equal("roads", features[0].LayerId);
        Assert.Equal("road", features[0].SourceLayer);
        Assert.Equal(3, features[0].GetProperty<int>("v"));
        var call = host.Calls("query.rendered").Single()["args"]!;
        Assert.Equal("[5,5]", call["geometry"]!.ToJsonString());
        Assert.Equal("roads", call["options"]!["layers"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task PageReload_FailsPendingCallsAndReinitializes()
    {
        using var map = FakeMapHost.CreateLoadedMap(out var host);
        var pending = map.InvokeAsync("getZoom");
        host.Ready();
        await Assert.ThrowsAsync<MapException>(() => pending);
        Assert.Equal(2, host.Sent.Count(m => m["t"]!.GetValue<string>() == "init"));
        Assert.False(map.IsLoaded);
    }
}

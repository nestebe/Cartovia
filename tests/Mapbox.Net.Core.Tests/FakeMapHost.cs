using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Bridge;

namespace Mapbox.Net.Core.Tests;

/// <summary>In-memory host simulating the JavaScript side of the bridge.</summary>
internal sealed class FakeMapHost : IMapHost
{
    public ConcurrentQueue<JsonObject> Sent { get; } = new();

    /// <summary>Optional automatic responder for calls: returns the value to send back (or throws to send an error).</summary>
    public Func<JsonObject, JsonNode?>? Responder { get; set; }

    public event EventHandler<MapHostMessageEventArgs>? MessageReceived;

    public Task PostMessageAsync(string json)
    {
        var msg = JsonNode.Parse(json)!.AsObject();
        Sent.Enqueue(msg);
        if (Responder is not null && msg["t"]?.GetValue<string>() == "call")
        {
            var id = msg["id"]!.GetValue<long>();
            try
            {
                var value = Responder(msg);
                Receive(new JsonObject { ["t"] = "result", ["id"] = id, ["ok"] = true, ["value"] = value });
            }
            catch (Exception ex)
            {
                Receive(new JsonObject { ["t"] = "result", ["id"] = id, ["ok"] = false, ["error"] = new JsonObject { ["message"] = ex.Message } });
            }
        }
        return Task.CompletedTask;
    }

    public void Receive(JsonObject message) => MessageReceived?.Invoke(this, new MapHostMessageEventArgs(message.ToJsonString()));

    public void Ready() => Receive(new JsonObject { ["t"] = "ready" });

    public void Created() => Receive(new JsonObject { ["t"] = "created", ["version"] = "test" });

    public void Event(string name, JsonNode? data = null, string? target = null, long? sid = null, string? layer = null)
    {
        var msg = new JsonObject { ["t"] = "event", ["name"] = name, ["data"] = data ?? new JsonObject() };
        if (target is not null) msg["target"] = target;
        if (sid is not null) msg["sid"] = sid;
        if (layer is not null) msg["layer"] = layer;
        Receive(msg);
    }

    public void Load() => Event("load", new JsonObject
    {
        ["center"] = new JsonArray(2.35, 48.85), ["zoom"] = 10, ["bearing"] = 0, ["pitch"] = 0,
        ["bounds"] = new JsonArray(new JsonArray(2, 48), new JsonArray(3, 49)),
    });

    public JsonObject[] Calls(string cmd) => Sent.Where(m => m["t"]?.GetValue<string>() == "call" && m["cmd"]?.GetValue<string>() == cmd).ToArray();

    public async Task<JsonObject> WaitForCallAsync(string cmd, int timeoutMs = 2000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (!cts.IsCancellationRequested)
        {
            var c = Calls(cmd).LastOrDefault();
            if (c is not null) return c;
            await Task.Delay(5);
        }
        throw new TimeoutException($"No '{cmd}' call was sent.");
    }

    public static MapboxMap CreateLoadedMap(out FakeMapHost host, MapOptions? options = null)
    {
        host = new FakeMapHost();
        var map = new MapboxMap(host, options ?? new MapOptions { Engine = MapEngine.MapLibre });
        host.Ready();
        host.Created();
        host.Load();
        return map;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mapbox.Net.Bridge;
using Mapbox.Net.Controls;
using Mapbox.Net.Json;

namespace Mapbox.Net.Plugins;

/// <summary>
/// Base class of map plugins. A plugin loads JavaScript / CSS assets, defines a JavaScript factory with
/// <c>MapboxNet.definePlugin(name, function (ctx, options) { … return { commands: {…}, dispose() {…} }; })</c>,
/// receives events emitted with <c>ctx.emit(event, data)</c> and can answer calls made with <c>ctx.callHost(method, data)</c>.
/// </summary>
/// <remarks>
/// Built-in plugins: <see cref="DrawPlugin"/>, <see cref="GeocoderPlugin"/>, <see cref="DirectionsPlugin"/>, <see cref="TurfPlugin"/>.
/// To write your own, override <see cref="Name"/>, <see cref="GetDefinitionScript"/>, <see cref="GetScripts"/> and
/// <see cref="BuildOptions"/>, then expose typed methods calling <see cref="InvokeAsync(string, object?[])"/>.
/// </remarks>
public abstract class MapPlugin
{
    /// <summary>Unique instance id.</summary>
    public string InstanceId { get; } = "plugin-" + Guid.NewGuid().ToString("N");

    /// <summary>Plugin name, as registered with <c>MapboxNet.definePlugin</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Map the plugin is attached to.</summary>
    public MapboxMap? Map { get; internal set; }

    /// <summary>Corner where the plugin control (if any) is placed.</summary>
    public ControlPosition? Position { get; set; }

    /// <summary>Information returned by the JavaScript factory (<c>info</c> member), if any.</summary>
    public JsonElement Info { get; internal set; }

    /// <summary>Raised for every event emitted by the JavaScript part of the plugin.</summary>
    public event EventHandler<PluginEventArgs>? EventReceived;

    /// <summary>Scripts to load before the plugin is created (CDN or local URLs).</summary>
    protected internal virtual IEnumerable<string> GetScripts(MapEngine engine) => Array.Empty<string>();

    /// <summary>Stylesheets to load.</summary>
    protected internal virtual IEnumerable<string> GetStylesheets(MapEngine engine) => Array.Empty<string>();

    /// <summary>
    /// JavaScript code defining the plugin (<c>MapboxNet.definePlugin(...)</c>), or null when the definition is
    /// already present in the page (built-in plugins, <see cref="MapOptions.AdditionalScripts"/>).
    /// </summary>
    protected internal virtual string? GetDefinitionScript() => null;

    /// <summary>Options passed to the JavaScript factory.</summary>
    protected internal virtual JsonObject BuildOptions(MapEngine engine) => new();

    /// <summary>Throws <see cref="NotSupportedException"/> when the engine is not supported.</summary>
    protected internal virtual void EnsureEngineSupported(MapEngine engine) { }

    /// <summary>Handles an event emitted by the JavaScript part. Call the base implementation to raise <see cref="EventReceived"/>.</summary>
    protected virtual void OnEvent(string eventName, JsonElement data)
        => EventReceived?.Invoke(this, new PluginEventArgs(eventName, data));

    /// <summary>Answers a <c>ctx.callHost(method, data)</c> call made by the JavaScript part.</summary>
    protected internal virtual Task<object?> OnHostCallAsync(string method, JsonElement data, CancellationToken cancellationToken)
        => throw new NotSupportedException($"The plugin '{Name}' does not handle host calls ('{method}').");

    /// <summary>Invokes a command of the JavaScript plugin instance.</summary>
    protected Task<JsonElement> InvokeAsync(string command, params object?[] args) => InvokeAsync(command, CancellationToken.None, args);

    /// <summary>Invokes a command of the JavaScript plugin instance.</summary>
    protected Task<JsonElement> InvokeAsync(string command, CancellationToken cancellationToken, params object?[] args)
    {
        var map = Map ?? throw new InvalidOperationException($"The plugin '{Name}' is not added to a map.");
        return map.CallAsync("plugin.invoke", new JsonObject
        {
            ["instanceId"] = InstanceId,
            ["command"] = command,
            ["args"] = BridgeJson.ToArray(args),
        }, cancellationToken);
    }

    /// <summary>Invokes a command and converts the result.</summary>
    protected async Task<T?> InvokeAsync<T>(string command, CancellationToken cancellationToken, params object?[] args)
        => MapboxJson.Deserialize<T>(await InvokeAsync(command, cancellationToken, args).ConfigureAwait(false));

    /// <summary>Removes the plugin from its map.</summary>
    public Task RemoveAsync(CancellationToken cancellationToken = default)
        => Map is null ? Task.CompletedTask : Map.RemovePluginAsync(this, cancellationToken);

    internal void RaiseEvent(string eventName, JsonElement data) => OnEvent(eventName, data);

    internal JsonObject BuildCreateMessage(MapEngine engine)
    {
        var options = BuildOptions(engine);
        if (Position is { } p) options["position"] = MapboxJson.EnumToString(p);
        return new JsonObject
        {
            ["instanceId"] = InstanceId,
            ["name"] = Name,
            ["scripts"] = new JsonArray(GetScripts(engine).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["stylesheets"] = new JsonArray(GetStylesheets(engine).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["source"] = GetDefinitionScript(),
            ["options"] = options,
        };
    }

    /// <summary>Helper writing an optional option.</summary>
    protected static void Put(JsonObject o, string name, object? value)
    {
        if (value is not null) o[name] = BridgeJson.ToNode(value);
    }
}

/// <summary>An event emitted by a plugin.</summary>
public sealed class PluginEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public PluginEventArgs(string eventName, JsonElement data) { EventName = eventName; Data = data; }
    /// <summary>Event name.</summary>
    public string EventName { get; }
    /// <summary>Payload.</summary>
    public JsonElement Data { get; }
}

/// <summary>
/// A plugin entirely defined by JavaScript, for quick integrations without writing a subclass.
/// </summary>
/// <example><code>
/// var plugin = new ScriptPlugin("hello", "MapboxNet.definePlugin('hello', function (ctx, o) {" +
///     "  return { commands: { greet: function (n) { return 'Hello ' + n; } } }; });");
/// await map.AddPluginAsync(plugin);
/// var text = await plugin.CallAsync&lt;string&gt;("greet", "world");
/// </code></example>
public sealed class ScriptPlugin : MapPlugin
{
    private readonly string _name;
    private readonly string? _definition;

    /// <summary>Creates the plugin.</summary>
    /// <param name="name">Name used in <c>MapboxNet.definePlugin</c>.</param>
    /// <param name="definitionScript">Definition code (null if already loaded through <see cref="Scripts"/>).</param>
    public ScriptPlugin(string name, string? definitionScript)
    {
        _name = name;
        _definition = definitionScript;
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <summary>Scripts loaded before the definition.</summary>
    public IList<string> Scripts { get; } = new List<string>();

    /// <summary>Stylesheets.</summary>
    public IList<string> Stylesheets { get; } = new List<string>();

    /// <summary>Options passed to the factory.</summary>
    public JsonObject Options { get; } = new();

    /// <summary>Handler of <c>ctx.callHost(method, data)</c> calls.</summary>
    public Func<string, JsonElement, CancellationToken, Task<object?>>? HostCallHandler { get; set; }

    /// <summary>Calls a command.</summary>
    public Task<JsonElement> CallAsync(string command, params object?[] args) => InvokeAsync(command, args);

    /// <summary>Calls a command and converts the result.</summary>
    public Task<T?> CallAsync<T>(string command, params object?[] args) => InvokeAsync<T>(command, CancellationToken.None, args);

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine) => Scripts;
    /// <inheritdoc />
    protected internal override IEnumerable<string> GetStylesheets(MapEngine engine) => Stylesheets;
    /// <inheritdoc />
    protected internal override string? GetDefinitionScript() => _definition;
    /// <inheritdoc />
    protected internal override JsonObject BuildOptions(MapEngine engine) => (JsonObject)Options.DeepClone();

    /// <inheritdoc />
    protected internal override Task<object?> OnHostCallAsync(string method, JsonElement data, CancellationToken cancellationToken)
        => HostCallHandler is null ? base.OnHostCallAsync(method, data, cancellationToken) : HostCallHandler(method, data, cancellationToken);
}

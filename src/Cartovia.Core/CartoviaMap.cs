using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Annotations;
using Cartovia.Bridge;
using Cartovia.Controls;
using Cartovia.Events;
using Cartovia.Json;
using Cartovia.Plugins;

namespace Cartovia;

/// <summary>
/// Strongly-typed, UI-agnostic controller of a Mapbox GL JS / MapLibre GL JS map running in a web view.
/// Every method is asynchronous and can be called before the map is loaded: calls are queued until the
/// <see cref="Loaded"/> event. Events are raised on the UI thread of the host.
/// </summary>
/// <remarks>
/// Use the WPF / WinForms <c>MapView</c> controls, which create and expose an instance through their <c>Map</c> property.
/// </remarks>
public sealed partial class CartoviaMap : IDisposable
{
    private readonly IMapHost _host;
    private readonly ConcurrentDictionary<long, PendingCall> _pending = new();
    private readonly object _gate = new();
    private long _nextId;
    private TaskCompletionSource<bool> _created = NewTcs();
    private TaskCompletionSource<bool> _loaded = NewTcs();
    private bool _initSent;
    private bool _hasLoadedOnce;
    private bool _disposed;

    private sealed record PendingCall(string Command, TaskCompletionSource<JsonElement> Completion);

    /// <summary>Creates a map controller bound to a host.</summary>
    /// <param name="host">The web view host.</param>
    /// <param name="options">Map options (can be changed until the page is ready, or applied later with <see cref="ReinitializeAsync"/>).</param>
    public CartoviaMap(IMapHost host, MapOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Options = options ?? new MapOptions();
        foreach (var name in AlwaysSubscribed) _globalSubs[name] = NextId();
        _host.MessageReceived += OnHostMessage;
    }

    private static readonly string[] AlwaysSubscribed = { "load", "style.load", "error", "moveend" };

    /// <summary>Options used to (re)initialize the map.</summary>
    public MapOptions Options { get; private set; }

    /// <summary>Engine currently used.</summary>
    public MapEngine Engine => Options.Engine;

    /// <summary>Version of the loaded engine (e.g. <c>3.34.2</c>), once created.</summary>
    public string? EngineVersion { get; private set; }

    /// <summary>True once the map has fired its <c>load</c> event.</summary>
    public bool IsLoaded => _loaded.Task.Status == TaskStatus.RanToCompletion;

    /// <summary>Last known camera (updated on load and at the end of every movement).</summary>
    public CameraState? Camera { get; private set; }

    /// <summary>Raised when the page signals it is ready, right before the map is (re)initialized.</summary>
    public event EventHandler? Initializing;

    /// <summary>Raised for every message logged by the page (script errors, warnings).</summary>
    public event EventHandler<MapLogEventArgs>? Log;

    /// <summary>Waits until the map is loaded.</summary>
    /// <exception cref="MapInitializationException">The map could not be created.</exception>
    public Task WhenLoadedAsync(CancellationToken cancellationToken = default)
        => cancellationToken.CanBeCanceled ? WaitAsync(_loaded.Task, cancellationToken) : _loaded.Task;

    // ------------------------------------------------------------------ lifecycle

    /// <summary>
    /// Applies new options by recreating the map in the same page (engine, token, style… can change).
    /// Controls, plugins and markers added through this instance are restored automatically.
    /// </summary>
    public Task ReinitializeAsync(MapOptions? options = null)
    {
        ThrowIfDisposed();
        if (options is not null) Options = options;
        ResetLifecycle();
        return SendInitAsync();
    }

    private void ResetLifecycle()
    {
        lock (_gate)
        {
            if (_created.Task.IsCompleted) _created = NewTcs();
            if (_loaded.Task.IsCompleted) _loaded = NewTcs();
        }
    }

    private Task SendInitAsync()
    {
        _initSent = true;
        Initializing?.Invoke(this, EventArgs.Empty);
        var msg = new JsonObject { ["t"] = "init", ["options"] = BuildInitOptions() };
        return _host.PostMessageAsync(msg.ToJsonString());
    }

    internal JsonObject BuildInitOptions()
    {
        var o = Options;
        bool mapbox = o.Engine == MapEngine.Mapbox;
        string version = o.EngineVersion ?? (mapbox ? EngineDefaults.MapboxVersion : EngineDefaults.MapLibreVersion);
        string script = o.EngineScriptUrl ?? (mapbox ? EngineDefaults.MapboxScript(version) : EngineDefaults.MapLibreScript(version));
        string css = o.EngineStylesheetUrl ?? (mapbox ? EngineDefaults.MapboxStylesheet(version) : EngineDefaults.MapLibreStylesheet(version));

        var mo = new JsonObject();
        mo["style"] = o.StyleJson is not null ? o.StyleJson.DeepClone() : JsonValue.Create(o.Style ?? MapStyles.DefaultFor(o.Engine));
        void Put(string name, object? value) { if (value is not null) mo[name] = BridgeJson.ToNode(value); }
        Put("center", o.Center);
        Put("zoom", o.Zoom);
        Put("bearing", o.Bearing);
        Put("pitch", o.Pitch);
        Put("bounds", o.Bounds);
        if (o.Bounds is not null && o.BoundsPadding is not null) mo["fitBoundsOptions"] = new JsonObject { ["padding"] = BridgeJson.ToNode(o.BoundsPadding) };
        Put("minZoom", o.MinZoom);
        Put("maxZoom", o.MaxZoom);
        Put("minPitch", o.MinPitch);
        Put("maxPitch", o.MaxPitch);
        Put("maxBounds", o.MaxBounds);
        mo["interactive"] = o.Interactive;
        if (!o.ScrollZoom) mo["scrollZoom"] = false;
        if (!o.BoxZoom) mo["boxZoom"] = false;
        if (!o.DragRotate) mo["dragRotate"] = false;
        if (!o.DragPan) mo["dragPan"] = false;
        if (!o.Keyboard) mo["keyboard"] = false;
        if (!o.DoubleClickZoom) mo["doubleClickZoom"] = false;
        if (!o.TouchZoomRotate) mo["touchZoomRotate"] = false;
        if (!o.TouchPitch) mo["touchPitch"] = false;
        if (o.CooperativeGestures) mo["cooperativeGestures"] = true;
        if (!o.AttributionControl) mo["attributionControl"] = false;
        else if (o.CustomAttribution is not null)
        {
            if (mapbox) mo["customAttribution"] = o.CustomAttribution;
            else mo["attributionControl"] = new JsonObject { ["customAttribution"] = o.CustomAttribution };
        }
        Put("logoPosition", o.LogoPosition);
        Put("renderWorldCopies", o.RenderWorldCopies);
        Put("antialias", o.Antialias);
        Put("preserveDrawingBuffer", o.PreserveDrawingBuffer);
        Put("fadeDuration", o.FadeDuration);
        Put("localIdeographFontFamily", o.LocalIdeographFontFamily);
        Put("crossSourceCollisions", o.CrossSourceCollisions);
        Put("maxTileCacheSize", o.MaxTileCacheSize);
        if (mapbox)
        {
            Put("projection", o.Projection);
            Put("language", o.Language);
            Put("worldview", o.Worldview);
            Put("config", o.Config);
        }
        foreach (var kv in o.AdditionalMapOptions) mo[kv.Key] = kv.Value?.DeepClone();

        var subs = new JsonArray();
        lock (_gate)
        {
            foreach (var kv in _globalSubs) subs.Add(new JsonObject { ["sid"] = kv.Value, ["name"] = kv.Key });
            foreach (var kv in _layerSubs) subs.Add(new JsonObject { ["sid"] = kv.Key, ["name"] = kv.Value.EventName, ["layer"] = kv.Value.LayerId });
        }

        var controls = new JsonArray();
        foreach (var c in o.InitialControls.Concat(_controls.Values).Distinct().ToList())
        {
            c.Map = this;
            _controls[c.Id] = c;
            controls.Add(c.ToDefinition());
        }

        return new JsonObject
        {
            ["engine"] = mapbox ? "mapbox" : "maplibre",
            ["accessToken"] = o.AccessToken,
            ["engineScript"] = script,
            ["stylesheets"] = new JsonArray(new[] { css }.Concat(o.AdditionalStylesheets).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["scripts"] = new JsonArray(o.AdditionalScripts.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["rtlTextPluginUrl"] = o.RtlTextPluginUrl,
            ["workerCount"] = o.WorkerCount,
            ["projection"] = mapbox ? null : o.Projection,
            ["preserveUserLayers"] = o.PreserveUserLayersOnStyleChange,
            ["eventThrottleMs"] = Math.Max(0, o.EventThrottle.TotalMilliseconds),
            ["mapOptions"] = mo,
            ["subscriptions"] = subs,
            ["controls"] = controls,
        };
    }

    // ------------------------------------------------------------------ calls

    /// <summary>Waiting policy of a bridge call.</summary>
    internal enum Readiness { None, Created, Loaded }

    /// <summary>Sends a raw bridge command (advanced; prefer the typed API).</summary>
    /// <param name="command">Bridge command name (see <c>bridge.js</c>).</param>
    /// <param name="args">Arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<JsonElement> CallAsync(string command, JsonObject? args, CancellationToken cancellationToken = default)
        => CallAsync(command, args, cancellationToken, Readiness.Loaded);

    internal async Task<JsonElement> CallAsync(string command, JsonObject? args, CancellationToken cancellationToken, Readiness readiness)
    {
        ThrowIfDisposed();
        if (readiness == Readiness.Loaded) await WaitAsync(_loaded.Task, cancellationToken).ConfigureAwait(false);
        else if (readiness == Readiness.Created) await WaitAsync(_created.Task, cancellationToken).ConfigureAwait(false);

        long id = NextId();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = new PendingCall(command, tcs);

        using var timeout = new CancellationTokenSource(Options.CallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var registration = linked.Token.Register(() =>
        {
            if (!_pending.TryRemove(id, out var p)) return;
            if (cancellationToken.IsCancellationRequested) p.Completion.TrySetCanceled(cancellationToken);
            else p.Completion.TrySetException(new TimeoutException($"The map command '{command}' timed out after {Options.CallTimeout.TotalSeconds:0.#} s."));
        });

        var message = new JsonObject { ["t"] = "call", ["id"] = id, ["cmd"] = command, ["args"] = args ?? new JsonObject() };
        try
        {
            await _host.PostMessageAsync(message.ToJsonString()).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        return await tcs.Task.ConfigureAwait(false);
    }

    internal async Task<T?> CallAsync<T>(string command, JsonObject? args, CancellationToken cancellationToken, Readiness readiness = Readiness.Loaded)
    {
        var result = await CallAsync(command, args, cancellationToken, readiness).ConfigureAwait(false);
        return CartoviaJson.Deserialize<T>(result);
    }

    /// <summary>
    /// Calls any method of the JavaScript map object: <c>map[method](...args)</c>.
    /// Covers the whole Mapbox / MapLibre API, including methods without a typed wrapper.
    /// </summary>
    /// <example><code>await map.InvokeAsync("setPadding", new MapPadding(0, 0, 0, 300));</code></example>
    public Task<JsonElement> InvokeAsync(string method, params object?[] args)
        => InvokeAsync(method, CancellationToken.None, args);

    /// <summary>Calls any method of the JavaScript map object.</summary>
    public Task<JsonElement> InvokeAsync(string method, CancellationToken cancellationToken, params object?[] args)
        => CallAsync("map.invoke", new JsonObject { ["method"] = method, ["args"] = BridgeJson.ToArray(args) }, cancellationToken, Readiness.Loaded);

    /// <summary>Calls any method of the JavaScript map object and converts the result.</summary>
    public async Task<T?> InvokeAsync<T>(string method, params object?[] args)
        => CartoviaJson.Deserialize<T>(await InvokeAsync(method, CancellationToken.None, args).ConfigureAwait(false));

    /// <summary>
    /// Evaluates JavaScript in the page. The code is the body of a function receiving <c>map</c>, <c>lib</c>
    /// (mapboxgl / maplibregl), <c>Cartovia</c> and <c>args</c>; use <c>return</c> to send back a value (promises are awaited).
    /// </summary>
    /// <example><code>var count = await map.EvalAsync&lt;int&gt;("return map.getStyle().layers.length;");</code></example>
    public async Task<T?> EvalAsync<T>(string code, object? args = null, CancellationToken cancellationToken = default)
        => CartoviaJson.Deserialize<T>(await EvalAsync(code, args, cancellationToken).ConfigureAwait(false));

    /// <summary>Evaluates JavaScript in the page (see <see cref="EvalAsync{T}"/>).</summary>
    public Task<JsonElement> EvalAsync(string code, object? args = null, CancellationToken cancellationToken = default)
        => CallAsync("map.eval", new JsonObject { ["code"] = code, ["args"] = BridgeJson.ToNode(args) }, cancellationToken, Readiness.Loaded);

    /// <summary>Loads extra scripts / stylesheets in the page.</summary>
    public Task LoadAssetsAsync(IEnumerable<string>? scripts, IEnumerable<string>? stylesheets = null, bool isModule = false, CancellationToken cancellationToken = default)
        => CallAsync("assets.load", new JsonObject
        {
            ["scripts"] = new JsonArray((scripts ?? Array.Empty<string>()).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["stylesheets"] = new JsonArray((stylesheets ?? Array.Empty<string>()).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["module"] = isModule,
        }, cancellationToken, Readiness.Created);

    // ------------------------------------------------------------------ host callbacks (JS -> .NET calls)

    private readonly ConcurrentDictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _hostCallbacks = new();

    /// <summary>
    /// Registers a .NET function callable from JavaScript with <c>await Cartovia.callHost(name, data)</c>.
    /// </summary>
    public IDisposable RegisterHostCallback(string name, Func<JsonElement, CancellationToken, Task<object?>> callback)
    {
        _hostCallbacks[name] = callback ?? throw new ArgumentNullException(nameof(callback));
        return new ActionDisposable(() => _hostCallbacks.TryRemove(name, out _));
    }

    private async void HandleHostCall(long id, string name, JsonElement data)
    {
        JsonObject reply;
        try
        {
            object? value;
            if (name.StartsWith("plugin:", StringComparison.Ordinal))
            {
                var parts = name.Split(new[] { ':' }, 3);
                if (parts.Length < 3 || !_plugins.TryGetValue(parts[1], out var plugin)) throw new InvalidOperationException("Unknown plugin instance.");
                value = await plugin.OnHostCallAsync(parts[2], data, CancellationToken.None).ConfigureAwait(true);
            }
            else if (_hostCallbacks.TryGetValue(name, out var cb))
            {
                value = await cb(data, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                throw new InvalidOperationException($"No host callback named '{name}' is registered.");
            }
            reply = new JsonObject { ["t"] = "hostResult", ["id"] = id, ["ok"] = true, ["value"] = BridgeJson.ToNode(value) };
        }
        catch (Exception ex)
        {
            reply = new JsonObject { ["t"] = "hostResult", ["id"] = id, ["ok"] = false, ["error"] = new JsonObject { ["message"] = ex.Message } };
        }
        try { await _host.PostMessageAsync(reply.ToJsonString()).ConfigureAwait(false); }
        catch (Exception ex) { RaiseLog(MapLogLevel.Error, "Unable to answer a host call: " + ex.Message); }
    }

    // ------------------------------------------------------------------ incoming messages

    private void OnHostMessage(object? sender, MapHostMessageEventArgs e)
    {
        if (_disposed) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(e.Json); }
        catch (JsonException) { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("t", out var tProp)) return;
            switch (tProp.GetString())
            {
                case "ready":
                    OnPageReady();
                    break;
                case "created":
                    EngineVersion = MapEventArgs.Str(root, "version");
                    _created.TrySetResult(true);
                    break;
                case "fatal":
                    OnFatal(MapEventArgs.Str(root, "message") ?? "The map could not be created.");
                    break;
                case "result":
                    CompleteCall(root);
                    break;
                case "event":
                    DispatchEvent(root);
                    break;
                case "log":
                    RaiseLog(MapEventArgs.Str(root, "level") switch { "error" => MapLogLevel.Error, "warn" => MapLogLevel.Warning, _ => MapLogLevel.Info },
                             MapEventArgs.Str(root, "message") ?? string.Empty);
                    break;
                case "hostCall":
                    HandleHostCall(root.GetProperty("id").GetInt64(), MapEventArgs.Str(root, "name") ?? string.Empty,
                                   root.TryGetProperty("data", out var d) ? d.Clone() : default);
                    break;
            }
        }
    }

    private void OnPageReady()
    {
        if (_initSent)
        {
            // The page was reloaded: everything living in JavaScript is gone.
            ResetLifecycle();
            foreach (var p in _pending.Values) p.Completion.TrySetException(new MapException("The map page was reloaded.", p.Command));
            _pending.Clear();
        }
        _ = SendInitAsync().ContinueWith(t => RaiseLog(MapLogLevel.Error, "Unable to initialize the map: " + t.Exception?.GetBaseException().Message),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private void OnFatal(string message)
    {
        var ex = new MapInitializationException(message);
        _created.TrySetException(ex);
        _loaded.TrySetException(ex);
        // Observe the exceptions so that unobserved-task handlers are not triggered.
        _ = _created.Task.Exception;
        _ = _loaded.Task.Exception;
        using var doc = JsonDocument.Parse(new JsonObject { ["message"] = message }.ToJsonString());
        RaiseLog(MapLogLevel.Error, message);
        RaiseTyped("error", new MapErrorEventArgs("error", doc.RootElement.Clone(), isFatal: true));
    }

    private void CompleteCall(JsonElement root)
    {
        long id = root.GetProperty("id").GetInt64();
        if (!_pending.TryRemove(id, out var call)) return;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
        {
            call.Completion.TrySetResult(root.TryGetProperty("value", out var v) ? v.Clone() : default);
        }
        else
        {
            var err = root.TryGetProperty("error", out var e) ? e : default;
            call.Completion.TrySetException(new MapException(
                MapEventArgs.Str(err, "message") ?? "Unknown error", call.Command, MapEventArgs.Str(err, "stack")));
        }
    }

    private void OnLoadedInternal(JsonElement data)
    {
        Camera = CameraState.FromJson(data);
        bool reload = _hasLoadedOnce;
        _hasLoadedOnce = true;
        _loaded.TrySetResult(true);
        if (reload) _ = RestoreAfterReinitAsync();
    }

    private async Task RestoreAfterReinitAsync()
    {
        try
        {
            foreach (var plugin in _plugins.Values.ToList()) await CreatePluginAsync(plugin, CancellationToken.None).ConfigureAwait(true);
            foreach (var marker in _markers.Values.ToList()) await CreateMarkerAsync(marker, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RaiseLog(MapLogLevel.Warning, "Unable to restore plugins / markers after re-initialization: " + ex.Message);
        }
    }

    private void RaiseLog(MapLogLevel level, string message) => Log?.Invoke(this, new MapLogEventArgs(level, message));

    internal void FireAndForget(Task task, string what)
        => task.ContinueWith(t => RaiseLog(MapLogLevel.Warning, what + " failed: " + t.Exception?.GetBaseException().Message),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    // ------------------------------------------------------------------ helpers

    internal long NextId() => Interlocked.Increment(ref _nextId);

    internal static string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private static TaskCompletionSource<bool> NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitAsync(Task task, CancellationToken cancellationToken)
    {
        if (task.IsCompleted || !cancellationToken.CanBeCanceled) { await task.ConfigureAwait(false); return; }
        var cancel = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancel.TrySetCanceled(cancellationToken)))
        {
            await (await Task.WhenAny(task, cancel.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CartoviaMap));
    }

    /// <summary>Detaches from the host and cancels pending calls.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.MessageReceived -= OnHostMessage;
        foreach (var p in _pending.Values) p.Completion.TrySetCanceled();
        _pending.Clear();
    }

    internal sealed class ActionDisposable : IDisposable
    {
        private Action? _action;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}

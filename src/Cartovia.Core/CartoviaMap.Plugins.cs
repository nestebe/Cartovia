using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Plugins;

namespace Cartovia;

public sealed partial class CartoviaMap
{
    private readonly ConcurrentDictionary<string, MapPlugin> _plugins = new();

    /// <summary>Plugins attached to the map.</summary>
    public IReadOnlyCollection<MapPlugin> Plugins => _plugins.Values.ToList();

    /// <summary>Adds a plugin (loads its assets and creates it in the page).</summary>
    /// <exception cref="NotSupportedException">The plugin does not support the current engine.</exception>
    public async Task<T> AddPluginAsync<T>(T plugin, CancellationToken cancellationToken = default) where T : MapPlugin
    {
        if (plugin is null) throw new ArgumentNullException(nameof(plugin));
        if (plugin.Map is not null) throw new InvalidOperationException("The plugin is already attached to a map.");
        plugin.EnsureEngineSupported(Engine);
        plugin.Map = this;
        _plugins[plugin.InstanceId] = plugin;
        try
        {
            await CreatePluginAsync(plugin, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _plugins.TryRemove(plugin.InstanceId, out _);
            plugin.Map = null;
            throw;
        }
        return plugin;
    }

    internal async Task CreatePluginAsync(MapPlugin plugin, CancellationToken cancellationToken)
    {
        // Loading plugin assets can take a while on slow networks: no call timeout here beyond the caller's token.
        plugin.Info = await CallAsync("plugin.create", plugin.BuildCreateMessage(Engine), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a plugin.</summary>
    public async Task RemovePluginAsync(MapPlugin plugin, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryRemove(plugin.InstanceId, out _)) return;
        plugin.Map = null;
        await CallAsync("plugin.dispose", new JsonObject { ["instanceId"] = plugin.InstanceId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first plugin of a type, or null.</summary>
    public T? GetPlugin<T>() where T : MapPlugin => _plugins.Values.OfType<T>().FirstOrDefault();
}

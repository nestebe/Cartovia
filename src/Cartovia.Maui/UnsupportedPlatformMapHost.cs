#if !WINDOWS
using System;
using System.Threading.Tasks;
using Cartovia.Bridge;

namespace Cartovia.Maui;

/// <summary>Host used on platforms without WebView2: the map fails to initialize with a clear message.</summary>
internal sealed class UnsupportedPlatformMapHost : IMapHost
{
    private const string Fatal = "{\"t\":\"fatal\",\"message\":\"The Cartovia map requires Windows (WebView2).\"}";

    public event EventHandler<MapHostMessageEventArgs>? MessageReceived;

    public void Start() => MessageReceived?.Invoke(this, new MapHostMessageEventArgs("{\"t\":\"ready\"}"));

    public Task PostMessageAsync(string json)
    {
        if (json.Contains("\"t\":\"init\"", StringComparison.Ordinal))
            MessageReceived?.Invoke(this, new MapHostMessageEventArgs(Fatal));
        return Task.CompletedTask;
    }
}
#endif

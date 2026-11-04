using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Cartovia.Bridge;

/// <summary>
/// Abstraction of the web view hosting the map page. Implemented by Cartovia.WebView2
/// (and by test doubles). Implementations must be safe to call from any thread.
/// </summary>
public interface IMapHost
{
    /// <summary>Sends a JSON message to the page.</summary>
    Task PostMessageAsync(string json);

    /// <summary>Raised (on the UI thread) when the page sends a JSON message.</summary>
    event EventHandler<MapHostMessageEventArgs>? MessageReceived;
}

/// <summary>A message received from the page.</summary>
public sealed class MapHostMessageEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapHostMessageEventArgs(string json) => Json = json;

    /// <summary>The raw JSON message.</summary>
    public string Json { get; }
}

/// <summary>Access to the HTML / JavaScript files of the map page embedded in this assembly.</summary>
public static class MapWebAssets
{
    /// <summary>Virtual host name used to serve the page.</summary>
    public const string HostName = "cartovia.local";

    /// <summary>URL of the map page.</summary>
    public const string PageUrl = "https://" + HostName + "/index.html";

    /// <summary>Opens an embedded asset (<c>index.html</c>, <c>bridge.js</c>, <c>plugins.js</c>).</summary>
    /// <returns>The stream, or null if the asset does not exist.</returns>
    public static Stream? Open(string path)
    {
        path = path.TrimStart('/').Replace('/', '.');
        if (path.Length == 0) path = "index.html";
        return typeof(MapWebAssets).GetTypeInfo().Assembly.GetManifestResourceStream("Cartovia.Web." + path);
    }

    /// <summary>MIME type of an asset path.</summary>
    public static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".pbf" or ".mvt" => "application/x-protobuf",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }
}

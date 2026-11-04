using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Cartovia.GeoJson;
using Cartovia.Json;
using Cartovia.Style;

namespace Cartovia;

public sealed partial class CartoviaMap
{
    private readonly ConcurrentDictionary<string, string> _publishedGeoJson = new(StringComparer.Ordinal);
    private long _publishCounter;

    /// <summary>True when the host can serve data through URLs (<see cref="IMapDataHost"/>; WebView2 hosts can).</summary>
    public bool CanPublishData => _host is IMapDataHost;

    /// <summary>
    /// Serves bytes to the page and returns their URL (use it as a GeoJSON source URL, an image URL, a PMTiles URL…).
    /// </summary>
    /// <exception cref="NotSupportedException">The host cannot serve data (<see cref="CanPublishData"/> is false).</exception>
    public string PublishData(string name, byte[] data, string contentType = "application/json")
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        return DataHost.PublishData(name, () => new MemoryStream(data, writable: false), contentType);
    }

    /// <summary>
    /// Serves a local file to the page (streamed on every request, never loaded in memory) and returns its URL.
    /// </summary>
    /// <example><code>
    /// await map.AddSourceAsync(new GeoJsonSource("parcels", map.PublishFile(@"C:\data\parcels.geojson")));
    /// await map.AddSourceAsync(new VectorSource("osm", "pmtiles://" + map.PublishFile(@"C:\data\france.pmtiles")));
    /// </code></example>
    public string PublishFile(string path, string? contentType = null)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("File not found.", full);
        var name = "files/" + Interlocked.Increment(ref _publishCounter) + "/" + Uri.EscapeDataString(Path.GetFileName(full));
        return DataHost.PublishData(name, () => new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true),
            contentType ?? MapWebAssets.GetContentType(full));
    }

    /// <summary>Stops serving data published with <see cref="PublishData"/> / <see cref="PublishFile"/>.</summary>
    public void UnpublishData(string urlOrName)
    {
        var name = urlOrName;
        var marker = "/data/";
        int i = urlOrName.IndexOf(marker, StringComparison.Ordinal);
        if (i >= 0) name = Uri.UnescapeDataString(urlOrName.Substring(i + marker.Length));
        (_host as IMapDataHost)?.UnpublishData(name);
    }

    /// <summary>Replaces the data of a GeoJSON source with the content of a local file (streamed, not sent as a message).</summary>
    public Task SetGeoJsonDataFromFileAsync(string sourceId, string path, CancellationToken cancellationToken = default)
        => SetGeoJsonDataAsync(sourceId, PublishFile(path, "application/geo+json"), cancellationToken);

    private IMapDataHost DataHost => _host as IMapDataHost
        ?? throw new NotSupportedException("The map host cannot serve data (IMapDataHost). Use a WebView2-based control.");

    /// <summary>
    /// Large GeoJSON objects are served through a URL when the host supports it (see <see cref="MapOptions.LargeDataThreshold"/>).
    /// Returns the JSON node or the URL string to send to the engine.
    /// </summary>
    internal JsonNode? PrepareGeoJsonData(string sourceId, object data)
    {
        if (data is string url) return JsonValue.Create(url);
        if (_host is not IMapDataHost dataHost || Options.LargeDataThreshold <= 0 || data is not (GeoJsonObject or JsonNode))
            return BridgeJson.ToNode(data);

        byte[] bytes = data is GeoJsonObject g ? Encoding.UTF8.GetBytes(g.ToJson()) : Encoding.UTF8.GetBytes(((JsonNode)data).ToJsonString());
        if (bytes.Length < Options.LargeDataThreshold) return JsonNode.Parse(bytes);

        var name = $"geojson/{Uri.EscapeDataString(sourceId)}-{Interlocked.Increment(ref _publishCounter)}.json";
        var published = dataHost.PublishData(name, () => new MemoryStream(bytes, writable: false), "application/geo+json");
        if (_publishedGeoJson.TryGetValue(sourceId, out var previous)) dataHost.UnpublishData(previous);
        _publishedGeoJson[sourceId] = name;
        return JsonValue.Create(published);
    }
}

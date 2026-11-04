using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Events;

namespace Cartovia.Plugins;

/// <summary>Header of a PMTiles archive.</summary>
/// <param name="MinZoom">Minimum zoom.</param>
/// <param name="MaxZoom">Maximum zoom.</param>
/// <param name="Bounds">Geographic bounds.</param>
/// <param name="Center">Suggested center.</param>
/// <param name="CenterZoom">Suggested zoom.</param>
/// <param name="TileType">Tile type code (1 = MVT vector, 2 = PNG, 3 = JPEG, 4 = WebP, 5 = AVIF).</param>
public sealed record PmTilesHeader(double MinZoom, double MaxZoom, LngLatBounds Bounds, LngLat Center, double CenterZoom, int TileType)
{
    /// <summary>True for vector (MVT) archives.</summary>
    public bool IsVector => TileType == 1;
}

/// <summary>
/// Reads <a href="https://docs.protomaps.com/pmtiles/">PMTiles</a> archives (single-file vector or raster tilesets) over
/// HTTP range requests: from a web server, a CDN, or a local file served with <see cref="CartoviaMap.PublishFile"/>.
/// <b>MapLibre engine only.</b> Use <c>pmtiles://</c> URLs in sources once the plugin is added.
/// </summary>
/// <example><code>
/// await map.AddPluginAsync(new PmTilesPlugin());
/// var url = PmTilesPlugin.Url(map.PublishFile(@"C:\data\region.pmtiles"));
/// await map.AddSourceAsync(new VectorSource("region", url));
/// </code></example>
public sealed class PmTilesPlugin : MapPlugin
{
    /// <inheritdoc />
    public override string Name => "pmtiles";

    /// <summary>Script URL.</summary>
    public string ScriptUrl { get; set; } = PluginAssets.PmTilesScript;

    /// <summary>Builds the source URL of an archive: <c>pmtiles://https://…/file.pmtiles</c>.</summary>
    public static string Url(string archiveUrl) => archiveUrl.StartsWith("pmtiles://", StringComparison.OrdinalIgnoreCase) ? archiveUrl : "pmtiles://" + archiveUrl;

    /// <summary>Reads the header of an archive (zoom range, bounds, tile type…).</summary>
    public async Task<PmTilesHeader> GetHeaderAsync(string archiveUrl, CancellationToken cancellationToken = default)
    {
        var url = archiveUrl.StartsWith("pmtiles://", StringComparison.OrdinalIgnoreCase) ? archiveUrl.Substring(10) : archiveUrl;
        var h = await InvokeAsync("header", cancellationToken, url).ConfigureAwait(false);
        return new PmTilesHeader(
            MapEventArgs.Num(h, "minZoom") ?? 0,
            MapEventArgs.Num(h, "maxZoom") ?? 0,
            MapEventArgs.Get<LngLatBounds>(h, "bounds") ?? default,
            MapEventArgs.Get<LngLat>(h, "center") ?? default,
            MapEventArgs.Num(h, "centerZoom") ?? 0,
            (int)(MapEventArgs.Num(h, "tileType") ?? 0));
    }

    /// <summary>Reads the JSON metadata of an archive (vector layers, attribution…).</summary>
    public Task<JsonElement> GetMetadataAsync(string archiveUrl, CancellationToken cancellationToken = default)
        => InvokeAsync("metadata", cancellationToken, archiveUrl.StartsWith("pmtiles://", StringComparison.OrdinalIgnoreCase) ? archiveUrl.Substring(10) : archiveUrl);

    /// <inheritdoc />
    protected internal override void EnsureEngineSupported(MapEngine engine)
    {
        if (engine != MapEngine.MapLibre) throw new NotSupportedException("PmTilesPlugin requires the MapLibre engine.");
    }

    /// <inheritdoc />
    protected internal override IEnumerable<string> GetScripts(MapEngine engine) => new[] { ScriptUrl };
}

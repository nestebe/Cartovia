using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cartovia.GeoJson;
using Cartovia.Plugins;
using Cartovia.Style;
using Xunit;

namespace Cartovia.Integration.Tests;

/// <summary>Large data, PMTiles, custom WebGL layers.</summary>
[Collection("browser")]
public sealed class ProductionFeaturesTests
{
    private readonly BrowserFixture _fixture;

    public ProductionFeaturesTests(BrowserFixture fixture) => _fixture = fixture;

    private static FeatureCollection ManyPoints(int count)
    {
        var rnd = new Random(3);
        return new FeatureCollection(Enumerable.Range(0, count).Select(i =>
            new Feature(new Point(2.30 + rnd.NextDouble() * 0.1, 48.82 + rnd.NextDouble() * 0.06), new { i, label = "point-" + i })));
    }

    [Fact]
    public async Task LargeGeoJson_IsServedThroughAUrl()
    {
        var (host, map) = await PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options());
        await using var _ = host;
        Assert.True(map.CanPublishData);

        await map.AddSourceAsync(new GeoJsonSource("big", ManyPoints(20_000)));   // ~2 MB > threshold
        await map.AddLayerAsync(new CircleLayer("big", "big") { CircleRadius = 2 });
        await map.IdleAsync();
        Assert.Contains(host.ServedData, p => p.StartsWith("/data/geojson/big-", StringComparison.Ordinal));
        Assert.NotEmpty(await map.QuerySourceFeaturesAsync("big"));
        int servedBefore = host.ServedData.Count;

        // Small data still goes inline.
        await map.SetGeoJsonDataAsync("big", ManyPoints(10));
        await map.IdleAsync();
        Assert.Equal(servedBefore, host.ServedData.Count);
        Assert.Equal(10, (await map.QuerySourceFeaturesAsync("big")).Select(f => f.GetProperty<int>("i")).Distinct().Count());

        // From a file (streamed by the host).
        var file = Path.Combine(Path.GetTempPath(), "cartovia-" + Guid.NewGuid().ToString("N") + ".geojson");
        await File.WriteAllTextAsync(file, ManyPoints(500).ToJson());
        try
        {
            await map.SetGeoJsonDataFromFileAsync("big", file);
            await map.IdleAsync();
            Assert.NotEmpty(await map.QueryRenderedFeaturesAsync(new QueryOptions { Layers = new[] { "big" } }));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task PmTiles_ReadsALocalArchiveWithRangeRequests()
    {
        var (host, map) = await PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options());
        await using var _ = host;
        var pmtiles = await map.AddPluginAsync(new PmTilesPlugin());
        var url = map.PublishFile(Path.Combine(AppContext.BaseDirectory, "Data", "test_fixture_1.pmtiles"), "application/octet-stream");

        var header = await pmtiles.GetHeaderAsync(url);
        Assert.True(header.IsVector);
        Assert.Equal(0, header.MinZoom);

        await map.AddSourceAsync(new VectorSource("fixture", PmTilesPlugin.Url(url)));
        Assert.True(await map.HasSourceAsync("fixture"));
    }

    [Fact]
    public async Task CustomWebGlLayer_Renders()
    {
        var (host, map) = await PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options());
        await using var _ = host;
        await map.AddCustomLayerAsync("custom", """
            {
              onAdd: function (map, gl) { window.__customAdded = true; },
              render: function (gl, args) { window.__renders = (window.__renders || 0) + 1; }
            }
            """);
        await map.IdleAsync();
        Assert.True(await map.EvalAsync<bool>("return !!window.__customAdded;"));
        Assert.True(await map.EvalAsync<int>("return window.__renders || 0;") > 0);
        Assert.True(await map.HasLayerAsync("custom"));
        await Assert.ThrowsAsync<MapException>(() => map.AddCustomLayerAsync("bad", "{ onAdd: function () {} }"));
    }

    [Fact]
    public async Task PmTiles_RejectsMapbox()
    {
        var (host, map) = await PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options(o => o.Engine = MapEngine.Mapbox));
        await using var _ = host;
        await Assert.ThrowsAsync<NotSupportedException>(() => map.AddPluginAsync(new PmTilesPlugin()));
    }
}

using System;
using System.Threading.Tasks;
using Cartovia.Plugins;
using Cartovia.Style;
using Xunit;

namespace Cartovia.Integration.Tests;

/// <summary>Runs only when the MAPBOX_ACCESS_TOKEN environment variable is set.</summary>
public sealed class MapboxFactAttribute : FactAttribute
{
    public static string? Token => Environment.GetEnvironmentVariable("MAPBOX_ACCESS_TOKEN");

    public MapboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Token)) Skip = "Set MAPBOX_ACCESS_TOKEN to run the Mapbox GL JS tests.";
    }
}

/// <summary>End-to-end tests against the real Mapbox GL JS engine and Mapbox services.</summary>
[Collection("browser")]
public sealed class MapboxIntegrationTests
{
    private readonly BrowserFixture _fixture;

    public MapboxIntegrationTests(BrowserFixture fixture) => _fixture = fixture;

    private Task<(PlaywrightMapHost Host, CartoviaMap Map)> CreateAsync(string style)
        => PlaywrightMapHost.CreateAsync(_fixture.Browser, TestStyles.Options(o =>
        {
            o.Engine = MapEngine.Mapbox;
            o.AccessToken = MapboxFactAttribute.Token;
            o.StyleJson = null;
            o.Style = style;
            o.AttributionControl = true;
        }));

    [MapboxFact]
    public async Task Streets_LoadsWithVectorTiles()
    {
        var (host, map) = await CreateAsync(MapStyles.MapboxStreets);
        await using var _ = host;
        await map.WhenLoadedAsync().WithTimeout(60);
        Assert.StartsWith("3.", map.EngineVersion);
        await map.IdleAsync();
        var features = await map.QueryRenderedFeaturesAsync();
        Assert.NotEmpty(features);
        await map.Add3DBuildingsAsync();
        Assert.True(await map.HasLayerAsync("cartovia-3d-buildings"));
    }

    [MapboxFact]
    public async Task Standard_ConfigLightsFogSnowAndInteractions()
    {
        var (host, map) = await CreateAsync(MapStyles.MapboxStandard);
        await using var _ = host;
        await map.WhenLoadedAsync().WithTimeout(60);

        await map.SetConfigPropertyAsync("basemap", "lightPreset", "dusk");
        Assert.Equal("dusk", (await map.GetConfigPropertyAsync("basemap", "lightPreset")).GetString());
        await map.SetFogAsync(new FogOptions { Color = "white", HorizonBlend = 0.1 });
        await map.SetSnowAsync(new SnowOptions { Density = 0.5 });
        await map.SetSnowAsync(null);
        await map.SetLightsAsync(new[]
        {
            Light3D.Ambient("ambient", new AmbientLightOptions { Intensity = 0.5 }),
            Light3D.Directional("sun", new DirectionalLightOptions { Direction = new[] { 200.0, 40.0 } }),
        });
        using var interaction = await map.AddInteractionAsync(
            new InteractionOptions(InteractionType.Click) { FeaturesetId = "poi", ImportId = "basemap" }, (_, _) => { });
        await map.SetProjectionAsync("globe");
        await map.EnableTerrainAsync(1.2);
        await map.IdleAsync();
    }

    [MapboxFact]
    public async Task MapboxPlugins_GeocoderDirectionsDraw()
    {
        var (host, map) = await CreateAsync(MapStyles.MapboxStreets);
        await using var _ = host;
        await map.WhenLoadedAsync().WithTimeout(60);

        var geocoder = await map.AddPluginAsync(new GeocoderPlugin { Language = "fr" });
        await geocoder.SetInputAsync("Paris");
        Assert.Equal(1, await host.Page.Locator(".mapboxgl-ctrl-geocoder").CountAsync());

        var directions = await map.AddPluginAsync(new DirectionsPlugin());
        await directions.SetOriginAsync(new LngLat(2.35, 48.85));
        Assert.Equal(1, await host.Page.Locator(".mapboxgl-ctrl-directions").CountAsync());

        var draw = await map.AddPluginAsync(new DrawPlugin());
        Assert.Single(await draw.AddAsync(new GeoJson.Point(2.35, 48.85)));
    }
}

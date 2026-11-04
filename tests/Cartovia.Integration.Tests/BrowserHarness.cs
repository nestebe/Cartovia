using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Microsoft.Playwright;
using Xunit;

namespace Cartovia.Integration.Tests;

/// <summary>Shared headless Microsoft Edge instance (WebGL through SwiftShader).</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        var args = new[] { "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist" };
        // Microsoft Edge, then Google Chrome, then the Playwright Chromium build (pwsh bin/…/playwright.ps1 install chromium).
        PlaywrightException? last = null;
        foreach (var channel in new[] { "msedge", "chrome", null })
        {
            try
            {
                Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = channel, Headless = true, Args = args });
                return;
            }
            catch (PlaywrightException ex) { last = ex; }
        }
        throw new InvalidOperationException("No Chromium-based browser found (Edge, Chrome or Playwright Chromium).", last);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}

[CollectionDefinition("browser")]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture> { }

/// <summary>
/// <see cref="IMapHost"/> over a Playwright page: serves the embedded page from https://cartovia.local and emulates
/// the <c>window.chrome.webview</c> messaging API of WebView2.
/// </summary>
public sealed class PlaywrightMapHost : IMapHost, IMapDataHost, IAsyncDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Func<Stream> Open, string ContentType)> _data = new();

    public string PublishData(string name, Func<Stream> open, string contentType)
    {
        _data[name] = (open, contentType);
        return "https://cartovia.local/data/" + name;
    }

    public void UnpublishData(string name) => _data.TryRemove(name, out _);

    /// <summary>Paths of the published data actually requested by the page.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> ServedData { get; } = new();

    public IPage Page { get; }
    public IBrowserContext Context { get; }

    public event EventHandler<MapHostMessageEventArgs>? MessageReceived;

    private PlaywrightMapHost(IBrowserContext context, IPage page)
    {
        Context = context;
        Page = page;
    }

    public static async Task<(PlaywrightMapHost Host, CartoviaMap Map)> CreateAsync(IBrowser browser, MapOptions options)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 800, Height = 600 },
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        var host = new PlaywrightMapHost(context, page);

        await page.ExposeFunctionAsync("__cartoviaToHost", (string json) =>
        {
            host.MessageReceived?.Invoke(host, new MapHostMessageEventArgs(json));
        });
        await page.AddInitScriptAsync("""
            window.chrome = window.chrome || {};
            window.chrome.webview = {
              postMessage: function (m) { window.__cartoviaToHost(m); },
              addEventListener: function (type, f) { if (type === 'message') window.__cartoviaListener = f; }
            };
            """);
        await page.RouteAsync("https://cartovia.local/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.StartsWith("/data/", StringComparison.Ordinal))
            {
                if (!host._data.TryGetValue(Uri.UnescapeDataString(path.Substring(6)), out var entry)
                    && !host._data.TryGetValue(path.Substring(6), out entry))
                {
                    await route.FulfillAsync(new RouteFulfillOptions { Status = 404 });
                    return;
                }
                host.ServedData.Enqueue(path);
                var data = entry.Open();
                route.Request.Headers.TryGetValue("range", out var range);
                if (DataRange.TryParse(range, data, out long start, out long end, out long total) && start >= 0)
                {
                    var part = DataRange.Read(data, start, end);
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 206, ContentType = entry.ContentType, BodyBytes = part,
                        Headers = new System.Collections.Generic.Dictionary<string, string>
                        {
                            ["Content-Range"] = $"bytes {start}-{start + part.Length - 1}/{total}",
                            ["Accept-Ranges"] = "bytes",
                        },
                    });
                    return;
                }
                using (data)
                {
                    using var all = new MemoryStream();
                    await data.CopyToAsync(all);
                    await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = entry.ContentType, BodyBytes = all.ToArray() });
                }
                return;
            }
            using var stream = MapWebAssets.Open(path);
            if (stream is null) { await route.FulfillAsync(new RouteFulfillOptions { Status = 404 }); return; }
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = MapWebAssets.GetContentType(path),
                BodyBytes = ms.ToArray(),
            });
        });

        var map = new CartoviaMap(host, options);   // subscribes before the page posts "ready"
        await page.GotoAsync(MapWebAssets.PageUrl);
        return (host, map);
    }

    public Task PostMessageAsync(string json)
        => Page.EvaluateAsync("m => window.__cartoviaListener && window.__cartoviaListener({ data: m })", json);

    public async ValueTask DisposeAsync() => await Context.CloseAsync();
}

/// <summary>Inline styles and helpers (no network tiles, so tests are deterministic).</summary>
public static class TestStyles
{
    public static JsonObject Background(string color = "#e5e7eb") => new()
    {
        ["version"] = 8,
        ["sources"] = new JsonObject(),
        ["layers"] = new JsonArray(new JsonObject
        {
            ["id"] = "bg", ["type"] = "background", ["paint"] = new JsonObject { ["background-color"] = color },
        }),
    };

    public static MapOptions Options(Action<MapOptions>? configure = null)
    {
        var o = new MapOptions
        {
            Engine = MapEngine.MapLibre,
            StyleJson = Background(),
            Center = new LngLat(2.35, 48.85),
            Zoom = 10,
            CallTimeout = TimeSpan.FromSeconds(45),
            AttributionControl = false,
        };
        configure?.Invoke(o);
        return o;
    }

    public static async Task<T> WithTimeout<T>(this Task<T> task, int seconds = 20)
    {
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task) throw new TimeoutException("The expected event did not happen in time.");
        return await task;
    }

    public static async Task WithTimeout(this Task task, int seconds = 20)
    {
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task) throw new TimeoutException("The expected operation did not complete in time.");
        await task;
    }

    /// <summary>Waits for the map to be idle (all rendering done).</summary>
    public static async Task IdleAsync(this CartoviaMap map)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (map.On("idle", (_, _) => tcs.TrySetResult(true)))
        {
            await map.TriggerRepaintAsync();
            await tcs.Task.WithTimeout();
        }
    }
}

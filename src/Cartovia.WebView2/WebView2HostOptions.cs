using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Cartovia.WebView2;

/// <summary>Options of the WebView2 instance hosting the map.</summary>
public sealed class WebView2HostOptions
{
    /// <summary>
    /// WebView2 user data folder (cache, cookies, local storage). Null: <see cref="WebView2Environments.DefaultUserDataFolder"/>
    /// (in %LOCALAPPDATA%, safe for apps installed in Program Files).
    /// </summary>
    public string? UserDataFolder { get; set; }

    /// <summary>Folder of a fixed-version WebView2 runtime. Null: the installed Evergreen runtime.</summary>
    public string? BrowserExecutableFolder { get; set; }

    /// <summary>Additional Chromium command line arguments (e.g. <c>--ignore-gpu-blocklist</c>).</summary>
    public string? AdditionalBrowserArguments { get; set; }

    /// <summary>UI language of the browser (affects <c>navigator.language</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Allows the F12 developer tools and <see cref="WebView2MapHost.OpenDevTools"/>. Default: false.</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>Allows the page to use the Geolocation API (GeolocateControl). Default: false.</summary>
    public bool AllowGeolocation { get; set; }

    /// <summary>Shows the browser context menu. Default: false.</summary>
    public bool EnableContextMenu { get; set; }

    /// <summary>Opens links clicked in the map (attribution, popups) in the default browser. Default: true.</summary>
    public bool OpenLinksInBrowser { get; set; } = true;

    /// <summary>
    /// Local folder served at <see cref="LocalAssetsBaseUrl"/> (offline engine / plugin copies, data files, sprites…).
    /// </summary>
    public string? LocalAssetsFolder { get; set; }

    /// <summary>Base URL of <see cref="LocalAssetsFolder"/>.</summary>
    public const string LocalAssetsBaseUrl = "https://assets.cartovia.local/";

    /// <summary>Background color shown before the map renders. Default: transparent.</summary>
    public System.Drawing.Color DefaultBackgroundColor { get; set; } = System.Drawing.Color.Transparent;
}

/// <summary>Creates and shares WebView2 environments.</summary>
public static class WebView2Environments
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Download page of the WebView2 Evergreen runtime.</summary>
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Default user data folder: <c>%LOCALAPPDATA%\&lt;EntryAssembly&gt;\Cartovia\WebView2</c>.</summary>
    public static string DefaultUserDataFolder
    {
        get
        {
            var app = Assembly.GetEntryAssembly()?.GetName().Name ?? "Cartovia";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), app, "Cartovia", "WebView2");
        }
    }

    /// <summary>Version of the installed WebView2 runtime, or null if it is not installed.</summary>
    public static string? GetRuntimeVersion(string? browserExecutableFolder = null)
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(browserExecutableFolder); }
        catch (WebView2RuntimeNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    /// <summary>True when a WebView2 runtime is available.</summary>
    public static bool IsRuntimeAvailable(string? browserExecutableFolder = null) => GetRuntimeVersion(browserExecutableFolder) is not null;

    /// <summary>
    /// Returns a shared environment for the options (WebView2 requires identical options for a given user data folder).
    /// </summary>
    public static Task<CoreWebView2Environment> GetAsync(WebView2HostOptions options)
    {
        var folder = options.UserDataFolder ?? DefaultUserDataFolder;
        var key = string.Join("|", folder, options.BrowserExecutableFolder, options.AdditionalBrowserArguments, options.Language);
        var lazy = Cache.GetOrAdd(key, _ => new Lazy<Task<CoreWebView2Environment>>(() =>
        {
            Directory.CreateDirectory(folder);
            var envOptions = new CoreWebView2EnvironmentOptions(options.AdditionalBrowserArguments, options.Language);
            return CoreWebView2Environment.CreateAsync(options.BrowserExecutableFolder, folder, envOptions);
        }));
        var task = lazy.Value;
        // Do not cache failures (e.g. runtime installed afterwards).
        task.ContinueWith(t => Cache.TryRemove(key, out _), TaskContinuationOptions.OnlyOnFaulted);
        return task;
    }
}

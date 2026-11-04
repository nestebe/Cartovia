using System;
using System.IO;
using System.Reflection;

namespace Cartovia.WinUI;

/// <summary>Options of the WinUI WebView2 hosting the map. Must be set before the control is loaded.</summary>
public sealed class WinUIHostOptions
{
    /// <summary>
    /// WebView2 user data folder. Null: <c>%LOCALAPPDATA%\&lt;EntryAssembly&gt;\Cartovia\WebView2</c>
    /// (safe for unpackaged apps installed in Program Files).
    /// </summary>
    public string? UserDataFolder { get; set; }

    /// <summary>Folder of a fixed-version WebView2 runtime. Null: the installed Evergreen runtime.</summary>
    public string? BrowserExecutableFolder { get; set; }

    /// <summary>Additional Chromium command line arguments.</summary>
    public string? AdditionalBrowserArguments { get; set; }

    /// <summary>Allows the F12 developer tools and <see cref="WinUIMapHost.OpenDevTools"/>. Default: false.</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>Allows the page to use the Geolocation API (GeolocateControl). Default: false.</summary>
    public bool AllowGeolocation { get; set; }

    /// <summary>Shows the browser context menu. Default: false.</summary>
    public bool EnableContextMenu { get; set; }

    /// <summary>Opens links clicked in the map (attribution, popups) in the default browser. Default: true.</summary>
    public bool OpenLinksInBrowser { get; set; } = true;

    /// <summary>Local folder served at <see cref="LocalAssetsBaseUrl"/> (offline engine copies, data, sprites…).</summary>
    public string? LocalAssetsFolder { get; set; }

    /// <summary>Base URL of <see cref="LocalAssetsFolder"/>.</summary>
    public const string LocalAssetsBaseUrl = "https://assets.cartovia.local/";

    /// <summary>Download page of the WebView2 Evergreen runtime.</summary>
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Default user data folder.</summary>
    public static string DefaultUserDataFolder
    {
        get
        {
            var app = Assembly.GetEntryAssembly()?.GetName().Name ?? "Cartovia";
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), app, "Cartovia", "WebView2");
        }
    }
}

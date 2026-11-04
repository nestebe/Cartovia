using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Cartovia.WinUI;

/// <summary>
/// Shared logic of the WinUI and MAUI map controls: owns the <see cref="CartoviaMap"/> and the
/// <see cref="WinUIMapHost"/>, and initializes a WinUI <see cref="WebView2"/>.
/// </summary>
public sealed class WinUIMapController : IDisposable
{
    private bool _initializing;

    /// <summary>Creates the controller. <see cref="Map"/> is usable immediately (calls are queued until load).</summary>
    public WinUIMapController(MapOptions? mapOptions = null, WinUIHostOptions? hostOptions = null)
    {
        Host = new WinUIMapHost(hostOptions);
        Map = new CartoviaMap(Host, mapOptions ?? new MapOptions());
    }

    /// <summary>The map controller.</summary>
    public CartoviaMap Map { get; }

    /// <summary>The WebView2 host.</summary>
    public WinUIMapHost Host { get; }

    /// <summary>True once the WebView2 is initialized.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Raised when the WebView2 cannot be initialized (missing runtime, invalid user data folder…).</summary>
    public event EventHandler<WinUIHostErrorEventArgs>? InitializationFailed;

    /// <summary>Initializes the WebView2 control and attaches the map host. Call on the UI thread.</summary>
    /// <returns>True on success.</returns>
    public async Task<bool> InitializeAsync(WebView2 webView)
    {
        if (IsInitialized || _initializing) return IsInitialized;
        _initializing = true;
        try
        {
            if (!IsRuntimeAvailable(Host.Options.BrowserExecutableFolder))
            {
                InitializationFailed?.Invoke(this, new WinUIHostErrorEventArgs(
                    "The Microsoft Edge WebView2 Runtime is not installed. Download it from " + WinUIHostOptions.RuntimeDownloadUrl,
                    isRuntimeMissing: true));
                return false;
            }
            await Host.AttachAsync(webView);
            IsInitialized = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or COMException)
        {
            InitializationFailed?.Invoke(this, new WinUIHostErrorEventArgs("Unable to initialize WebView2: " + ex.Message, ex));
            return false;
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>True when a WebView2 runtime is available.</summary>
    public static bool IsRuntimeAvailable(string? browserExecutableFolder = null)
    {
        try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString(browserExecutableFolder ?? string.Empty)); }
        catch (Exception) { return false; }
    }

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public Task<byte[]> CaptureAsync() => Host.CaptureAsync();

    /// <summary>Captures the visible map into a stream.</summary>
    public Task CaptureAsync(Stream destination, bool jpeg = false) => Host.CaptureAsync(destination, jpeg);

    /// <summary>Prints the map to a PDF file.</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true) => Host.PrintToPdfAsync(path, landscape);

    /// <summary>Opens the developer tools.</summary>
    public void OpenDevTools() => Host.OpenDevTools();

    /// <inheritdoc />
    public void Dispose()
    {
        Map.Dispose();
        Host.Dispose();
    }
}

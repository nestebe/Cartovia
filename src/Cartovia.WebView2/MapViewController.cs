using System;
using System.IO;
using System.Threading.Tasks;
using Cartovia.Events;
using Microsoft.Web.WebView2.Core;

namespace Cartovia.WebView2;

/// <summary>
/// Shared logic of the WPF and WinForms map controls: owns the <see cref="CartoviaMap"/> and the
/// <see cref="WebView2MapHost"/>, initializes the WebView2 and keeps the camera in sync.
/// Use it to build a control for another UI framework hosting a WebView2.
/// </summary>
public sealed class MapViewController : IDisposable
{
    private bool _initializing;

    /// <summary>Creates the controller. <see cref="Map"/> is usable immediately (calls are queued until load).</summary>
    public MapViewController(MapOptions? mapOptions = null, WebView2HostOptions? hostOptions = null)
    {
        Host = new WebView2MapHost(hostOptions);
        Map = new CartoviaMap(Host, mapOptions ?? new MapOptions());
    }

    /// <summary>The map controller.</summary>
    public CartoviaMap Map { get; }

    /// <summary>The WebView2 host.</summary>
    public WebView2MapHost Host { get; }

    /// <summary>True once the WebView2 is initialized.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Raised when the WebView2 cannot be initialized (missing runtime, invalid user data folder…).</summary>
    public event EventHandler<MapHostErrorEventArgs>? InitializationFailed;

    /// <summary>
    /// Initializes the WebView2 with a shared environment and attaches the map host.
    /// </summary>
    /// <param name="ensureCoreWebView2">Control-specific initialization, e.g. <c>env =&gt; webView.EnsureCoreWebView2Async(env)</c>,
    /// returning the <see cref="CoreWebView2"/>.</param>
    /// <returns>True on success.</returns>
    public async Task<bool> InitializeAsync(Func<CoreWebView2Environment, Task<CoreWebView2>> ensureCoreWebView2)
    {
        if (IsInitialized || _initializing) return IsInitialized;
        _initializing = true;
        try
        {
            if (!WebView2Environments.IsRuntimeAvailable(Host.Options.BrowserExecutableFolder))
            {
                InitializationFailed?.Invoke(this, new MapHostErrorEventArgs(
                    "The Microsoft Edge WebView2 Runtime is not installed. Download it from " + WebView2Environments.RuntimeDownloadUrl,
                    isRuntimeMissing: true));
                return false;
            }
            var env = await WebView2Environments.GetAsync(Host.Options).ConfigureAwait(true);
            var core = await ensureCoreWebView2(env).ConfigureAwait(true);
            Host.Attach(core);
            IsInitialized = true;
            return true;
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            InitializationFailed?.Invoke(this, new MapHostErrorEventArgs(ex.Message, ex, isRuntimeMissing: true));
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            InitializationFailed?.Invoke(this, new MapHostErrorEventArgs("Unable to initialize WebView2: " + ex.Message, ex));
            return false;
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public Task<byte[]> CaptureAsync() => Host.CaptureAsync();

    /// <summary>Captures the visible map into a stream.</summary>
    public Task CaptureAsync(Stream destination, bool jpeg = false) => Host.CaptureAsync(destination, jpeg);

    /// <summary>Prints the map to a PDF file.</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true) => Host.PrintToPdfAsync(path, landscape);

    /// <summary>Opens the developer tools (requires <see cref="WebView2HostOptions.EnableDevTools"/>).</summary>
    public void OpenDevTools() => Host.OpenDevTools();

    /// <summary>Reloads the page and recreates the map.</summary>
    public void Reload() => Host.Reload();

    /// <inheritdoc />
    public void Dispose()
    {
        Map.Dispose();
        Host.Dispose();
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace Cartovia.WinUI;

/// <summary>Arguments of host errors (initialization failure, browser process failure).</summary>
public sealed class WinUIHostErrorEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public WinUIHostErrorEventArgs(string message, Exception? exception = null, bool isRuntimeMissing = false)
    {
        Message = message;
        Exception = exception;
        IsRuntimeMissing = isRuntimeMissing;
    }

    /// <summary>Description of the problem.</summary>
    public string Message { get; }
    /// <summary>Underlying exception.</summary>
    public Exception? Exception { get; }
    /// <summary>True when the WebView2 runtime is not installed.</summary>
    public bool IsRuntimeMissing { get; }
}

/// <summary>
/// <see cref="IMapHost"/> implementation over the WinUI 3 <see cref="WebView2"/> control: serves the embedded map page
/// from <c>https://cartovia.local/</c>, relays web messages on the UI thread, keeps navigation inside the page,
/// handles permissions, and offers screenshots / PDF export. Can be created before the control and attached later.
/// </summary>
public sealed class WinUIMapHost : IMapHost, IDisposable
{
    private readonly TaskCompletionSource<CoreWebView2> _attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DispatcherQueue? _dispatcher;
    private CoreWebView2? _core;
    private bool _disposed;

    /// <summary>Creates a host (call <see cref="AttachAsync"/> once the control is in the visual tree).</summary>
    public WinUIMapHost(WinUIHostOptions? options = null) => Options = options ?? new WinUIHostOptions();

    /// <summary>Host options.</summary>
    public WinUIHostOptions Options { get; }

    /// <summary>The attached WebView2 core, if any.</summary>
    public CoreWebView2? CoreWebView2 => _core;

    /// <inheritdoc />
    public event EventHandler<MapHostMessageEventArgs>? MessageReceived;

    /// <summary>The browser process failed (the page is reloaded automatically when possible).</summary>
    public event EventHandler<WinUIHostErrorEventArgs>? ProcessFailed;

    /// <summary>
    /// Initializes the WebView2 control with the host options, attaches to it and navigates to the map page.
    /// Must be called on the UI thread.
    /// </summary>
    public async Task AttachAsync(WebView2 webView)
    {
        if (_core is not null) throw new InvalidOperationException("The host is already attached.");
        _dispatcher = DispatcherQueue.GetForCurrentThread() ?? webView.DispatcherQueue;

        var folder = Options.UserDataFolder ?? WinUIHostOptions.DefaultUserDataFolder;
        Directory.CreateDirectory(folder);
        var envOptions = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = Options.AdditionalBrowserArguments ?? string.Empty };
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(Options.BrowserExecutableFolder ?? string.Empty, folder, envOptions);
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2;
        _core = core;

        var s = core.Settings;
        s.IsWebMessageEnabled = true;
        s.AreDevToolsEnabled = Options.EnableDevTools;
        s.AreDefaultContextMenusEnabled = Options.EnableContextMenu;
        s.AreBrowserAcceleratorKeysEnabled = Options.EnableDevTools;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreHostObjectsAllowed = false;

        core.AddWebResourceRequestedFilter("https://" + MapWebAssets.HostName + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        if (!string.IsNullOrEmpty(Options.LocalAssetsFolder))
            core.SetVirtualHostNameToFolderMapping("assets." + MapWebAssets.HostName, Options.LocalAssetsFolder, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.ProcessFailed += OnProcessFailed;

        core.Navigate(MapWebAssets.PageUrl);
        _attached.TrySetResult(core);
    }

    /// <summary>Reloads the map page (the map is recreated with its options).</summary>
    public void Reload() => _core?.Navigate(MapWebAssets.PageUrl);

    /// <summary>Opens the developer tools (requires <see cref="WinUIHostOptions.EnableDevTools"/>).</summary>
    public void OpenDevTools() => _core?.OpenDevToolsWindow();

    /// <inheritdoc />
    public async Task PostMessageAsync(string json)
    {
        var core = await _attached.Task.ConfigureAwait(false);
        if (_disposed) return;
        await RunOnUiAsync(() => { if (!_disposed) core.PostWebMessageAsString(json); return Task.CompletedTask; }).ConfigureAwait(false);
    }

    /// <summary>Captures the visible map (markers, popups and controls included) into a stream.</summary>
    public async Task CaptureAsync(Stream destination, bool jpeg = false)
    {
        var core = await _attached.Task.ConfigureAwait(false);
        await RunOnUiAsync(async () =>
        {
            using var ras = new InMemoryRandomAccessStream();
            await core.CapturePreviewAsync(jpeg ? CoreWebView2CapturePreviewImageFormat.Jpeg : CoreWebView2CapturePreviewImageFormat.Png, ras);
            ras.Seek(0);
            using var input = ras.AsStreamForRead();
            await input.CopyToAsync(destination);
        }).ConfigureAwait(false);
    }

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public async Task<byte[]> CaptureAsync()
    {
        using var ms = new MemoryStream();
        await CaptureAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>Prints the map page to a PDF file.</summary>
    public async Task<bool> PrintToPdfAsync(string path, bool landscape = true)
    {
        var core = await _attached.Task.ConfigureAwait(false);
        bool result = false;
        await RunOnUiAsync(async () =>
        {
            var settings = core.Environment.CreatePrintSettings();
            settings.Orientation = landscape ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            result = await core.PrintToPdfAsync(path, settings);
        }).ConfigureAwait(false);
        return result;
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) return action();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _dispatcher.TryEnqueue(async () =>
        {
            try { await action(); tcs.TrySetResult(true); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        if (!queued) tcs.TrySetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return tcs.Task;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (Exception) { json = e.WebMessageAsJson; }
        if (!string.IsNullOrEmpty(json)) MessageReceived?.Invoke(this, new MapHostMessageEventArgs(json));
    }

    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (!string.Equals(uri.Host, MapWebAssets.HostName, StringComparison.OrdinalIgnoreCase)) return;
        var env = sender.Environment;
        using var stream = MapWebAssets.Open(uri.AbsolutePath);
        if (stream is null)
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }
        // WinRT responses need a seekable IRandomAccessStream: copy the embedded asset in memory.
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        e.Response = env.CreateWebResourceResponse(copy.AsRandomAccessStream(), 200, "OK",
            $"Content-Type: {MapWebAssets.GetContentType(uri.AbsolutePath)}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *");
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("https://" + MapWebAssets.HostName + "/", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    private void OpenExternally(string uri)
    {
        if (!Options.OpenLinksInBrowser) return;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeMailto)) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { /* no browser registered */ }
    }

    private void OnPermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = e.PermissionKind == CoreWebView2PermissionKind.Geolocation && Options.AllowGeolocation
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ProcessFailed?.Invoke(this, new WinUIHostErrorEventArgs($"WebView2 process failure: {e.ProcessFailedKind} ({e.Reason})."));
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
            or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            try { Reload(); } catch (Exception) { /* the WebView is gone */ }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var core = _core;
        if (core is null) return;
        try
        {
            core.WebResourceRequested -= OnWebResourceRequested;
            core.WebMessageReceived -= OnWebMessageReceived;
            core.NavigationStarting -= OnNavigationStarting;
            core.NewWindowRequested -= OnNewWindowRequested;
            core.PermissionRequested -= OnPermissionRequested;
            core.ProcessFailed -= OnProcessFailed;
        }
        catch (Exception) { /* already closed */ }
    }
}

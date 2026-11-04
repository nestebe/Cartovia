using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cartovia.Bridge;
using Microsoft.Web.WebView2.Core;

namespace Cartovia.WebView2;

/// <summary>Arguments of <see cref="WebView2MapHost.ProcessFailed"/> and initialization failures.</summary>
public sealed class MapHostErrorEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapHostErrorEventArgs(string message, Exception? exception = null, bool isRuntimeMissing = false)
    {
        Message = message;
        Exception = exception;
        IsRuntimeMissing = isRuntimeMissing;
    }

    /// <summary>Description of the problem.</summary>
    public string Message { get; }
    /// <summary>Underlying exception.</summary>
    public Exception? Exception { get; }
    /// <summary>True when the WebView2 runtime is not installed (see <see cref="WebView2Environments.RuntimeDownloadUrl"/>).</summary>
    public bool IsRuntimeMissing { get; }
}

/// <summary>
/// <see cref="IMapHost"/> implementation over a <see cref="CoreWebView2"/>: serves the embedded map page from
/// <c>https://cartovia.local/</c>, relays web messages, keeps navigation inside the map page, handles permissions,
/// and offers screenshots / PDF export. Can be created before the WebView exists and attached later.
/// </summary>
public sealed class WebView2MapHost : IMapHost, IDisposable
{
    private readonly TaskCompletionSource<CoreWebView2> _attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? _uiContext;
    private int _uiThreadId;
    private CoreWebView2? _core;
    private bool _disposed;

    /// <summary>Creates a host (call <see cref="Attach"/> once the WebView is initialized).</summary>
    public WebView2MapHost(WebView2HostOptions? options = null) => Options = options ?? new WebView2HostOptions();

    /// <summary>Host options.</summary>
    public WebView2HostOptions Options { get; }

    /// <summary>The attached WebView2 core, if any.</summary>
    public CoreWebView2? CoreWebView2 => _core;

    /// <inheritdoc />
    public event EventHandler<MapHostMessageEventArgs>? MessageReceived;

    /// <summary>The browser process crashed or became unresponsive (the page is reloaded automatically).</summary>
    public event EventHandler<MapHostErrorEventArgs>? ProcessFailed;

    /// <summary>
    /// Attaches the host to an initialized WebView2 and navigates to the map page. Must be called on the UI thread.
    /// </summary>
    public void Attach(CoreWebView2 core)
    {
        if (_core is not null) throw new InvalidOperationException("The host is already attached.");
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _uiContext = SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;

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
        s.AreDefaultScriptDialogsEnabled = true;

        core.AddWebResourceRequestedFilter("https://" + MapWebAssets.HostName + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        if (!string.IsNullOrEmpty(Options.LocalAssetsFolder))
        {
            core.SetVirtualHostNameToFolderMapping("assets." + MapWebAssets.HostName, Options.LocalAssetsFolder,
                CoreWebView2HostResourceAccessKind.Allow);
        }
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

    /// <summary>Opens the developer tools (requires <see cref="WebView2HostOptions.EnableDevTools"/>).</summary>
    public void OpenDevTools() => _core?.OpenDevToolsWindow();

    /// <inheritdoc />
    public async Task PostMessageAsync(string json)
    {
        var core = await _attached.Task.ConfigureAwait(false);
        if (_disposed) return;
        await RunOnUiAsync(() => { if (!_disposed) core.PostWebMessageAsString(json); }).ConfigureAwait(false);
    }

    /// <summary>Captures the visible map (including markers, popups and controls) as PNG or JPEG.</summary>
    public async Task CaptureAsync(Stream destination, bool jpeg = false)
    {
        var core = await _attached.Task.ConfigureAwait(false);
        await RunOnUiAsync(() => core.CapturePreviewAsync(
            jpeg ? CoreWebView2CapturePreviewImageFormat.Jpeg : CoreWebView2CapturePreviewImageFormat.Png, destination)).ConfigureAwait(false);
    }

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public async Task<byte[]> CaptureAsync()
    {
        using var ms = new MemoryStream();
        await CaptureAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>Prints the map page to a PDF file.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="landscape">Landscape orientation.</param>
    /// <returns>True on success.</returns>
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

    private Task RunOnUiAsync(Action action) => RunOnUiAsync(() => { action(); return Task.CompletedTask; });

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (_uiContext is null || Environment.CurrentManagedThreadId == _uiThreadId) return action();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(async _ =>
        {
            try { await action(); tcs.TrySetResult(true); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }, null);
        return tcs.Task;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { json = e.WebMessageAsJson; }
        if (!string.IsNullOrEmpty(json)) MessageReceived?.Invoke(this, new MapHostMessageEventArgs(json!));
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        if (!string.Equals(uri.Host, MapWebAssets.HostName, StringComparison.OrdinalIgnoreCase)) return;
        var stream = MapWebAssets.Open(uri.AbsolutePath);
        var env = _core!.Environment;
        e.Response = stream is null
            ? env.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain")
            : env.CreateWebResourceResponse(stream, 200, "OK",
                $"Content-Type: {MapWebAssets.GetContentType(uri.AbsolutePath)}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *");
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("https://" + MapWebAssets.HostName + "/", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
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

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = e.PermissionKind == CoreWebView2PermissionKind.Geolocation && Options.AllowGeolocation
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ProcessFailed?.Invoke(this, new MapHostErrorEventArgs($"WebView2 process failure: {e.ProcessFailedKind} ({e.Reason})."));
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
        catch (Exception) { /* already disposed by the control */ }
    }
}

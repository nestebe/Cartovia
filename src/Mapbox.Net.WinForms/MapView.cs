using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mapbox.Net.Events;
using Mapbox.Net.WebView2;

namespace Mapbox.Net.WinForms;

/// <summary>
/// Windows Forms map control hosting Mapbox GL JS or MapLibre GL JS in WebView2.
/// The full API is available through <see cref="Map"/>.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(MapLoaded))]
[Description("Mapbox GL JS / MapLibre GL JS map hosted in WebView2.")]
public class MapView : UserControl
{
    private readonly MapViewController _controller;
    private Microsoft.Web.WebView2.WinForms.WebView2? _webView;
    private bool _initStarted;

    /// <summary>Creates the control.</summary>
    public MapView()
    {
        _controller = new MapViewController(new MapOptions(), new WebView2HostOptions());
        _controller.InitializationFailed += OnInitializationFailed;
        Map.Loaded += OnMapLoaded;
        Map.MoveEnded += OnMoveEnded;
        Map.Click += OnMapClick;
        BackColor = Color.FromArgb(0xE5, 0xE7, 0xEB);
        Size = new Size(400, 300);
    }

    // ------------------------------------------------------------------ API

    /// <summary>The map API: camera, sources, layers, markers, plugins, events…</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapboxMap Map => _controller.Map;

    /// <summary>The WebView2 host (screenshots, PDF, dev tools, reload).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WebView2MapHost Host => _controller.Host;

    /// <summary>Map options used at initialization (the designer properties override the matching members).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapOptions Options => Map.Options;

    /// <summary>WebView2 options. Must be set before the control handle is created.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WebView2HostOptions HostOptions => _controller.Host.Options;

    /// <summary>True once the map is loaded.</summary>
    [Browsable(false)]
    public bool IsMapLoaded => Map.IsLoaded;

    /// <summary>Rendering engine. Changing it after load recreates the map.</summary>
    [Category("Map"), DefaultValue(MapEngine.Mapbox)]
    public MapEngine Engine
    {
        get => Map.Options.Engine;
        set
        {
            if (Map.Options.Engine == value) return;
            Map.Options.Engine = value;
            ReinitializeIfStarted();
        }
    }

    /// <summary>Mapbox access token. Changing it after load recreates the map.</summary>
    [Category("Map"), DefaultValue(null)]
    public string? AccessToken
    {
        get => Map.Options.AccessToken;
        set
        {
            if (Map.Options.AccessToken == value) return;
            Map.Options.AccessToken = value;
            ReinitializeIfStarted();
        }
    }

    /// <summary>Style URL (see <see cref="MapStyles"/>). Null: engine default.</summary>
    [Category("Map"), DefaultValue(null)]
    public string? MapStyle
    {
        get => Map.Options.Style;
        set
        {
            Map.Options.Style = value;
            if (value is not null && Map.IsLoaded) Map.FireAndForget(Map.SetStyleAsync(value), "Style change");
        }
    }

    /// <summary>Map center.</summary>
    [Category("Camera")]
    public LngLat Center
    {
        get => Map.Camera?.Center ?? Map.Options.Center ?? default;
        set
        {
            Map.Options.Center = value;
            if (Map.IsLoaded) Map.FireAndForget(Map.JumpToAsync(new CameraOptions { Center = value }), "Camera update");
        }
    }

    /// <summary>Zoom level.</summary>
    [Category("Camera"), DefaultValue(1.0)]
    public double Zoom
    {
        get => Map.Camera?.Zoom ?? Map.Options.Zoom ?? 1;
        set
        {
            Map.Options.Zoom = value;
            if (Map.IsLoaded) Map.FireAndForget(Map.JumpToAsync(new CameraOptions { Zoom = value }), "Camera update");
        }
    }

    /// <summary>Bearing in degrees.</summary>
    [Category("Camera"), DefaultValue(0.0)]
    public double Bearing
    {
        get => Map.Camera?.Bearing ?? Map.Options.Bearing ?? 0;
        set
        {
            Map.Options.Bearing = value;
            if (Map.IsLoaded) Map.FireAndForget(Map.JumpToAsync(new CameraOptions { Bearing = value }), "Camera update");
        }
    }

    /// <summary>Pitch in degrees.</summary>
    [Category("Camera"), DefaultValue(0.0)]
    public double Pitch
    {
        get => Map.Camera?.Pitch ?? Map.Options.Pitch ?? 0;
        set
        {
            Map.Options.Pitch = value;
            if (Map.IsLoaded) Map.FireAndForget(Map.JumpToAsync(new CameraOptions { Pitch = value }), "Camera update");
        }
    }

    private bool ShouldSerializeCenter() => Map.Options.Center is not null;
    private void ResetCenter() => Map.Options.Center = null;

    /// <summary>Raised when the map is loaded.</summary>
    [Category("Map")]
    public event EventHandler<MapCameraEventArgs>? MapLoaded;

    /// <summary>Raised on map clicks.</summary>
    [Category("Map")]
    public event EventHandler<MapMouseEventArgs>? MapClick;

    /// <summary>Raised at the end of every camera movement.</summary>
    [Category("Map")]
    public event EventHandler<MapCameraEventArgs>? CameraChanged;

    /// <summary>Raised when WebView2 cannot be initialized (e.g. runtime not installed).</summary>
    [Category("Map")]
    public event EventHandler<MapHostErrorEventArgs>? InitializationFailed;

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public Task<byte[]> CaptureAsync() => _controller.CaptureAsync();

    /// <summary>Saves a PNG (or JPEG) screenshot of the map.</summary>
    public async Task SaveScreenshotAsync(string path)
    {
        using var fs = File.Create(path);
        await _controller.CaptureAsync(fs, jpeg: path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(true);
    }

    /// <summary>Prints the map to a PDF file.</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true) => _controller.PrintToPdfAsync(path, landscape);

    /// <summary>Opens the developer tools (requires <c>HostOptions.EnableDevTools</c>).</summary>
    public void OpenDevTools() => _controller.OpenDevTools();

    /// <summary>Applies the current options by recreating the map.</summary>
    public Task ReinitializeAsync() => Map.ReinitializeAsync();

    // ------------------------------------------------------------------ initialization

    private bool IsDesignMode => DesignMode || LicenseManager.UsageMode == LicenseUsageMode.Designtime;

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (IsDesignMode || _initStarted) return;
        _initStarted = true;
        BeginInvoke(new Action(async () => await InitializeWebViewAsync()));
    }

    private async Task InitializeWebViewAsync()
    {
        _webView = new Microsoft.Web.WebView2.WinForms.WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = HostOptions.DefaultBackgroundColor == Color.Transparent ? BackColor : HostOptions.DefaultBackgroundColor,
        };
        Controls.Add(_webView);
        var webView = _webView;
        await _controller.InitializeAsync(async env =>
        {
            await webView.EnsureCoreWebView2Async(env);
            return webView.CoreWebView2;
        });
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsDesignMode) return;
        TextRenderer.DrawText(e.Graphics, "Mapbox.Net MapView", Font, ClientRectangle, Color.DimGray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void ReinitializeIfStarted()
    {
        if (_controller.IsInitialized) Map.FireAndForget(Map.ReinitializeAsync(), "Map re-initialization");
    }

    private void OnInitializationFailed(object? sender, MapHostErrorEventArgs e)
    {
        if (_webView is not null) { Controls.Remove(_webView); _webView.Dispose(); _webView = null; }
        var label = new LinkLabel
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = e.IsRuntimeMissing ? e.Message.Replace(WebView2Environments.RuntimeDownloadUrl, string.Empty).Trim() + Environment.NewLine + "Download the WebView2 Runtime" : e.Message,
        };
        if (e.IsRuntimeMissing)
        {
            const string linkText = "Download the WebView2 Runtime";
            label.LinkArea = new LinkArea(label.Text.Length - linkText.Length, linkText.Length);
            label.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(WebView2Environments.RuntimeDownloadUrl) { UseShellExecute = true }); } catch (Exception) { /* ignore */ }
            };
        }
        Controls.Add(label);
        InitializationFailed?.Invoke(this, e);
    }

    private void OnMapLoaded(object? sender, MapCameraEventArgs e) => MapLoaded?.Invoke(this, e);

    private void OnMoveEnded(object? sender, MapCameraEventArgs e) => CameraChanged?.Invoke(this, e);

    private void OnMapClick(object? sender, MapMouseEventArgs e) => MapClick?.Invoke(this, e);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Dispose();
            _webView?.Dispose();
        }
        base.Dispose(disposing);
    }
}

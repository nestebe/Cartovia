using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform;
using Cartovia.Events;
using Cartovia.WebView2;
using Microsoft.Web.WebView2.Core;

namespace Cartovia.Avalonia;

/// <summary>
/// Avalonia map control hosting MapLibre GL JS or Mapbox GL JS in WebView2 (Windows).
/// The full API is available through <see cref="Map"/>; the camera and the main options are bindable.
/// </summary>
/// <example><code>
/// &lt;carto:MapView MapStyle="https://tiles.openfreemap.org/styles/liberty"
///                Center="{Binding Center}" Zoom="{Binding Zoom}" /&gt;
/// </code></example>
/// <remarks>
/// The map is a native child window: Avalonia content cannot be drawn over it. Use map-side overlays
/// (markers, popups, <see cref="Controls.HtmlControl"/>) or place Avalonia content next to the map.
/// </remarks>
public class MapView : NativeControlHost, IDisposable
{
    private readonly MapViewController _controller;
    private CoreWebView2Controller? _webView;
    private IntPtr _hwnd;
    private bool _syncingFromMap;
    private bool _initialized;
    private bool _disposed;
    private IntPtr _parkingHwnd;
    private Window? _window;

    /// <summary>Creates the control.</summary>
    public MapView()
    {
        _controller = new MapViewController(new MapOptions(), new WebView2HostOptions());
        _controller.InitializationFailed += (_, e) => InitializationFailed?.Invoke(this, e);
        Map.Loaded += OnMapLoaded;
        Map.MoveEnded += OnMoveEnded;
        Map.Click += OnMapClick;
    }

    // ------------------------------------------------------------------ API

    /// <summary>The map API: camera, sources, layers, markers, plugins, events…</summary>
    public CartoviaMap Map => _controller.Map;

    /// <summary>The WebView2 host (screenshots, PDF, dev tools, reload).</summary>
    public WebView2MapHost Host => _controller.Host;

    /// <summary>Map options used at initialization (the styled properties override the matching members).</summary>
    public MapOptions Options => Map.Options;

    /// <summary>WebView2 options. Must be set before the control is attached to a window.</summary>
    public WebView2HostOptions HostOptions => _controller.Host.Options;

    /// <summary>Raised when the map is loaded.</summary>
    public event EventHandler<MapCameraEventArgs>? MapLoaded;

    /// <summary>Raised at the end of every camera movement.</summary>
    public event EventHandler<MapCameraEventArgs>? CameraChanged;

    /// <summary>Raised when WebView2 cannot be initialized (runtime missing, unsupported OS…).</summary>
    public event EventHandler<MapHostErrorEventArgs>? InitializationFailed;

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public Task<byte[]> CaptureAsync() => _controller.CaptureAsync();

    /// <summary>Saves a PNG (or JPEG) screenshot of the map.</summary>
    public async Task SaveScreenshotAsync(string path)
    {
        using var fs = File.Create(path);
        await _controller.CaptureAsync(fs, jpeg: path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Prints the map to a PDF file.</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true) => _controller.PrintToPdfAsync(path, landscape);

    /// <summary>Opens the developer tools (requires <c>HostOptions.EnableDevTools</c>).</summary>
    public void OpenDevTools() => _controller.OpenDevTools();

    /// <summary>Applies the current options and styled properties by recreating the map.</summary>
    public Task ReinitializeAsync()
    {
        ApplyPropertiesToOptions();
        return Map.ReinitializeAsync();
    }

    // ------------------------------------------------------------------ styled properties

    /// <summary>Defines <see cref="Engine"/>.</summary>
    public static readonly StyledProperty<MapEngine> EngineProperty =
        AvaloniaProperty.Register<MapView, MapEngine>(nameof(Engine), MapEngine.MapLibre);

    /// <summary>Rendering engine. Changing it after load recreates the map.</summary>
    public MapEngine Engine { get => GetValue(EngineProperty); set => SetValue(EngineProperty, value); }

    /// <summary>Defines <see cref="AccessToken"/>.</summary>
    public static readonly StyledProperty<string?> AccessTokenProperty =
        AvaloniaProperty.Register<MapView, string?>(nameof(AccessToken));

    /// <summary>Mapbox access token. Changing it after load recreates the map.</summary>
    public string? AccessToken { get => GetValue(AccessTokenProperty); set => SetValue(AccessTokenProperty, value); }

    /// <summary>Defines <see cref="MapStyle"/>.</summary>
    public static readonly StyledProperty<string?> MapStyleProperty =
        AvaloniaProperty.Register<MapView, string?>(nameof(MapStyle));

    /// <summary>Style URL (see <see cref="MapStyles"/>). Null: engine default.</summary>
    public string? MapStyle { get => GetValue(MapStyleProperty); set => SetValue(MapStyleProperty, value); }

    /// <summary>Defines <see cref="Center"/>.</summary>
    public static readonly StyledProperty<LngLat> CenterProperty =
        AvaloniaProperty.Register<MapView, LngLat>(nameof(Center), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Map center (two-way: updated when the user moves the map).</summary>
    public LngLat Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

    /// <summary>Defines <see cref="Zoom"/>.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(Zoom), 1.0, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Zoom level (two-way).</summary>
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>Defines <see cref="Bearing"/>.</summary>
    public static readonly StyledProperty<double> BearingProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(Bearing), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Bearing in degrees (two-way).</summary>
    public double Bearing { get => GetValue(BearingProperty); set => SetValue(BearingProperty, value); }

    /// <summary>Defines <see cref="Pitch"/>.</summary>
    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(Pitch), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Pitch in degrees (two-way).</summary>
    public double Pitch { get => GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    /// <summary>Defines <see cref="IsMapLoaded"/>.</summary>
    public static readonly DirectProperty<MapView, bool> IsMapLoadedProperty =
        AvaloniaProperty.RegisterDirect<MapView, bool>(nameof(IsMapLoaded), o => o.IsMapLoaded);

    private bool _isMapLoaded;

    /// <summary>True once the map is loaded.</summary>
    public bool IsMapLoaded { get => _isMapLoaded; private set => SetAndRaise(IsMapLoadedProperty, ref _isMapLoaded, value); }

    /// <summary>Defines <see cref="ClickCommand"/>.</summary>
    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<MapView, ICommand?>(nameof(ClickCommand));

    /// <summary>Command executed on map clicks, with the <see cref="MapMouseEventArgs"/> as parameter.</summary>
    public ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>Defines <see cref="LoadedCommand"/>.</summary>
    public static readonly StyledProperty<ICommand?> LoadedCommandProperty =
        AvaloniaProperty.Register<MapView, ICommand?>(nameof(LoadedCommand));

    /// <summary>Command executed when the map is loaded, with the <see cref="CartoviaMap"/> as parameter.</summary>
    public ICommand? LoadedCommand { get => GetValue(LoadedCommandProperty); set => SetValue(LoadedCommandProperty, value); }

    // ------------------------------------------------------------------ native hosting

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            InitializationFailed?.Invoke(this, new MapHostErrorEventArgs("Cartovia.Avalonia currently requires Windows (WebView2)."));
            return base.CreateNativeControlCore(parent);
        }

        _hwnd = Native.CreateWindowEx(0, "Static", string.Empty,
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS,
            0, 0, Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height),
            parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_window is null && TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.Closed += (_, _) => Dispose();
        }

        if (!_initialized)
        {
            _initialized = true;
            ApplyPropertiesToOptions();
            _ = InitializeWebViewAsync(_hwnd);
        }
        else if (_webView is not null)
        {
            // Re-attached to another window: move the WebView to the new host window.
            _webView.ParentWindow = _hwnd;
            UpdateWebViewBounds();
            _webView.IsVisible = true;
        }
        return new PlatformHandle(_hwnd, "HWND");
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (control.Handle == _hwnd && OperatingSystem.IsWindows())
        {
            if (_webView is not null)
            {
                // Keep the WebView alive while the control is out of the visual tree (tabs, re-templating):
                // move it to a hidden window, it is moved back in CreateNativeControlCore.
                if (_parkingHwnd == IntPtr.Zero)
                    _parkingHwnd = Native.CreateWindowEx(0, "Static", string.Empty, Native.WS_POPUP, 0, 0, 1, 1,
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _webView.IsVisible = false;
                _webView.ParentWindow = _parkingHwnd;
            }
            Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        else
        {
            base.DestroyNativeControlCore(control);
        }
    }

    /// <summary>
    /// Chromium switches applied by default: Windows' native occlusion detection mistakes the WebView hosted under
    /// Avalonia's composition window for hidden and stops rendering frames, so it is disabled.
    /// </summary>
    public const string DefaultBrowserArguments = "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows";

    private async Task InitializeWebViewAsync(IntPtr hwnd)
    {
        var hostOptions = HostOptions;
        var args = hostOptions.AdditionalBrowserArguments;
        if (args is null || !args.Contains("CalculateNativeWinOcclusion"))
            hostOptions.AdditionalBrowserArguments = string.IsNullOrWhiteSpace(args) ? DefaultBrowserArguments : DefaultBrowserArguments + " " + args;
        await _controller.InitializeAsync(async env =>
        {
            _webView = await env.CreateCoreWebView2ControllerAsync(hwnd);
            var bg = hostOptions.DefaultBackgroundColor;
            _webView.DefaultBackgroundColor = bg;
            UpdateWebViewBounds();
            _webView.IsVisible = true;
            return _webView.CoreWebView2;
        });
    }

    private void UpdateWebViewBounds()
    {
        if (_webView is null) return;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        int h = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        _webView.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateWebViewBounds();
        }
        else if (change.Property == CenterProperty || change.Property == ZoomProperty
                 || change.Property == BearingProperty || change.Property == PitchProperty)
        {
            OnCameraPropertyChanged(change);
        }
        else if (change.Property == MapStyleProperty)
        {
            if (change.NewValue is string style)
            {
                Map.Options.Style = style;
                if (Map.IsLoaded) Map.FireAndForget(Map.SetStyleAsync(style), "Style change");
            }
        }
        else if (change.Property == EngineProperty || change.Property == AccessTokenProperty)
        {
            if (!_controller.IsInitialized) return;
            ApplyPropertiesToOptions();
            Map.FireAndForget(Map.ReinitializeAsync(), "Map re-initialization");
        }
    }

    private void ApplyPropertiesToOptions()
    {
        var o = Map.Options;
        o.Engine = Engine;
        if (AccessToken is not null) o.AccessToken = AccessToken;
        if (MapStyle is not null) o.Style = MapStyle;
        if (IsSet(CenterProperty)) o.Center = Center;
        if (IsSet(ZoomProperty)) o.Zoom = Zoom;
        if (IsSet(BearingProperty)) o.Bearing = Bearing;
        if (IsSet(PitchProperty)) o.Pitch = Pitch;
    }

    // ------------------------------------------------------------------ camera sync

    private void OnCameraPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (_syncingFromMap || !Map.IsLoaded) return;
        var camera = new CameraOptions();
        if (change.Property == CenterProperty) camera.Center = (LngLat)change.NewValue!;
        else if (change.Property == ZoomProperty) camera.Zoom = (double)change.NewValue!;
        else if (change.Property == BearingProperty) camera.Bearing = (double)change.NewValue!;
        else if (change.Property == PitchProperty) camera.Pitch = (double)change.NewValue!;
        Map.FireAndForget(Map.JumpToAsync(camera), "Camera update");
    }

    private void SyncCamera(CameraState camera)
    {
        _syncingFromMap = true;
        try
        {
            SetCurrentValue(CenterProperty, camera.Center);
            SetCurrentValue(ZoomProperty, camera.Zoom);
            SetCurrentValue(BearingProperty, camera.Bearing);
            SetCurrentValue(PitchProperty, camera.Pitch);
        }
        finally
        {
            _syncingFromMap = false;
        }
    }

    private void OnMapLoaded(object? sender, MapCameraEventArgs e)
    {
        IsMapLoaded = true;
        SyncCamera(e.Camera);
        MapLoaded?.Invoke(this, e);
        if (LoadedCommand?.CanExecute(Map) == true) LoadedCommand.Execute(Map);
    }

    private void OnMoveEnded(object? sender, MapCameraEventArgs e)
    {
        SyncCamera(e.Camera);
        CameraChanged?.Invoke(this, e);
    }

    private void OnMapClick(object? sender, MapMouseEventArgs e)
    {
        if (ClickCommand?.CanExecute(e) == true) ClickCommand.Execute(e);
    }

    /// <summary>Releases WebView2 and the map (done automatically when the window closes).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _webView?.Close(); } catch (Exception) { /* already closed */ }
        _webView = null;
        _controller.Dispose();
        if (_parkingHwnd != IntPtr.Zero && OperatingSystem.IsWindows()) Native.DestroyWindow(_parkingHwnd);
        _parkingHwnd = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CLIPCHILDREN = 0x02000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hwnd);
    }
}

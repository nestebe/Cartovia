using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Mapbox.Net.Events;
using Mapbox.Net.WebView2;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mapbox.Net.Wpf;

/// <summary>
/// WPF map control hosting Mapbox GL JS or MapLibre GL JS in WebView2.
/// The full API is available through <see cref="Map"/>; the camera and the main options are bindable.
/// </summary>
/// <example><code>
/// &lt;mbx:MapView x:Name="Map" Engine="Mapbox" AccessToken="{Binding Token}"
///              MapStyle="mapbox://styles/mapbox/standard" Center="2.35, 48.85" Zoom="11" /&gt;
/// </code></example>
public class MapView : UserControl, IDisposable
{
    private readonly MapViewController _controller;
    private readonly Grid _root = new();
    private IWebView2? _webView;
    private bool _syncingFromMap;
    private bool _initialized;
    private bool _disposed;

    static MapView()
    {
        BackgroundProperty.OverrideMetadata(typeof(MapView), new FrameworkPropertyMetadata(Brushes.Transparent));
        FocusableProperty.OverrideMetadata(typeof(MapView), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates the control.</summary>
    public MapView()
    {
        _controller = new MapViewController(new MapOptions(), new WebView2HostOptions());
        _controller.InitializationFailed += OnInitializationFailed;
        Map.Loaded += OnMapLoaded;
        Map.MoveEnded += OnMoveEnded;
        Map.Click += OnMapClick;
        Content = _root;
        Loaded += OnControlLoaded;

        if (DesignerProperties.GetIsInDesignMode(this))
        {
            _root.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            _root.Children.Add(new TextBlock
            {
                Text = "Mapbox.Net MapView",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
            });
        }
    }

    // ------------------------------------------------------------------ API

    /// <summary>The map API: camera, sources, layers, markers, plugins, events…</summary>
    public MapboxMap Map => _controller.Map;

    /// <summary>The WebView2 host (screenshots, PDF, dev tools, reload).</summary>
    public WebView2MapHost Host => _controller.Host;

    /// <summary>
    /// Map options used at initialization. Bindable properties (<see cref="Engine"/>, <see cref="AccessToken"/>, <see cref="MapStyle"/>,
    /// camera…) override the matching members.
    /// </summary>
    public MapOptions Options => Map.Options;

    /// <summary>WebView2 options. Must be set before the control is loaded.</summary>
    public WebView2HostOptions HostOptions => _controller.Host.Options;

    /// <summary>
    /// Uses <see cref="WebView2CompositionControl"/> instead of the HWND-based WebView2, which allows WPF content to be
    /// drawn over the map, transparency and transforms (slightly slower). Must be set before the control is loaded.
    /// </summary>
    public bool UseCompositionControl { get; set; }

    /// <summary>Raised when the map is loaded.</summary>
    public event EventHandler<MapCameraEventArgs>? MapLoaded;

    /// <summary>Raised at the end of every camera movement.</summary>
    public event EventHandler<MapCameraEventArgs>? CameraChanged;

    /// <summary>Raised when WebView2 cannot be initialized (e.g. runtime not installed).</summary>
    public event EventHandler<MapHostErrorEventArgs>? InitializationFailed;

    /// <summary>Captures the visible map as PNG bytes.</summary>
    public Task<byte[]> CaptureAsync() => _controller.CaptureAsync();

    /// <summary>Saves a PNG screenshot of the map.</summary>
    public async Task SaveScreenshotAsync(string path)
    {
        using var fs = File.Create(path);
        await _controller.CaptureAsync(fs, jpeg: path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(true);
    }

    /// <summary>Prints the map to a PDF file.</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true) => _controller.PrintToPdfAsync(path, landscape);

    /// <summary>Opens the developer tools (requires <c>HostOptions.EnableDevTools</c>).</summary>
    public void OpenDevTools() => _controller.OpenDevTools();

    /// <summary>Applies the current options and bindable properties by recreating the map.</summary>
    public Task ReinitializeAsync()
    {
        ApplyPropertiesToOptions();
        return Map.ReinitializeAsync();
    }

    // ------------------------------------------------------------------ dependency properties

    /// <summary>Identifies <see cref="Engine"/>.</summary>
    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(MapEngine), typeof(MapView), new PropertyMetadata(MapEngine.Mapbox, OnReinitializingPropertyChanged));

    /// <summary>Rendering engine. Changing it after load recreates the map.</summary>
    public MapEngine Engine { get => (MapEngine)GetValue(EngineProperty); set => SetValue(EngineProperty, value); }

    /// <summary>Identifies <see cref="AccessToken"/>.</summary>
    public static readonly DependencyProperty AccessTokenProperty = DependencyProperty.Register(
        nameof(AccessToken), typeof(string), typeof(MapView), new PropertyMetadata(null, OnReinitializingPropertyChanged));

    /// <summary>Mapbox access token. Changing it after load recreates the map.</summary>
    public string? AccessToken { get => (string?)GetValue(AccessTokenProperty); set => SetValue(AccessTokenProperty, value); }

    /// <summary>Identifies <see cref="MapStyle"/>.</summary>
    public static readonly DependencyProperty MapStyleProperty = DependencyProperty.Register(
        nameof(MapStyle), typeof(string), typeof(MapView), new PropertyMetadata(null, OnMapStyleChanged));

    /// <summary>Style URL (see <see cref="MapStyles"/>). Null: engine default.</summary>
    public string? MapStyle { get => (string?)GetValue(MapStyleProperty); set => SetValue(MapStyleProperty, value); }

    /// <summary>Identifies <see cref="Center"/>.</summary>
    public static readonly DependencyProperty CenterProperty = DependencyProperty.Register(
        nameof(Center), typeof(LngLat), typeof(MapView),
        new FrameworkPropertyMetadata(default(LngLat), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCameraPropertyChanged));

    /// <summary>Map center (two-way: updated when the user moves the map).</summary>
    public LngLat Center { get => (LngLat)GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

    /// <summary>Identifies <see cref="Zoom"/>.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(MapView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCameraPropertyChanged));

    /// <summary>Zoom level (two-way).</summary>
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>Identifies <see cref="Bearing"/>.</summary>
    public static readonly DependencyProperty BearingProperty = DependencyProperty.Register(
        nameof(Bearing), typeof(double), typeof(MapView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCameraPropertyChanged));

    /// <summary>Bearing in degrees (two-way).</summary>
    public double Bearing { get => (double)GetValue(BearingProperty); set => SetValue(BearingProperty, value); }

    /// <summary>Identifies <see cref="Pitch"/>.</summary>
    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(MapView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCameraPropertyChanged));

    /// <summary>Pitch in degrees (two-way).</summary>
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    private static readonly DependencyPropertyKey IsMapLoadedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMapLoaded), typeof(bool), typeof(MapView), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsMapLoaded"/>.</summary>
    public static readonly DependencyProperty IsMapLoadedProperty = IsMapLoadedPropertyKey.DependencyProperty;

    /// <summary>True once the map is loaded.</summary>
    public bool IsMapLoaded => (bool)GetValue(IsMapLoadedProperty);

    /// <summary>Identifies <see cref="ClickCommand"/>.</summary>
    public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.Register(
        nameof(ClickCommand), typeof(ICommand), typeof(MapView), new PropertyMetadata(null));

    /// <summary>Command executed on map clicks, with the <see cref="MapMouseEventArgs"/> as parameter.</summary>
    public ICommand? ClickCommand { get => (ICommand?)GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>Identifies <see cref="LoadedCommand"/>.</summary>
    public static readonly DependencyProperty LoadedCommandProperty = DependencyProperty.Register(
        nameof(LoadedCommand), typeof(ICommand), typeof(MapView), new PropertyMetadata(null));

    /// <summary>Command executed when the map is loaded, with the <see cref="MapboxMap"/> as parameter.</summary>
    public ICommand? LoadedCommand { get => (ICommand?)GetValue(LoadedCommandProperty); set => SetValue(LoadedCommandProperty, value); }

    // ------------------------------------------------------------------ initialization

    private async void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _disposed || DesignerProperties.GetIsInDesignMode(this)) return;
        _initialized = true;
        ApplyPropertiesToOptions();

        var bg = HostOptions.DefaultBackgroundColor;
        if (UseCompositionControl)
        {
            var c = new WebView2CompositionControl { DefaultBackgroundColor = bg };
            _webView = c;
            _root.Children.Add(c);
        }
        else
        {
            var c = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = bg };
            _webView = c;
            _root.Children.Add(c);
        }

        var webView = _webView;
        await _controller.InitializeAsync(async env =>
        {
            await webView.EnsureCoreWebView2Async(env);
            return webView.CoreWebView2;
        });
    }

    private void ApplyPropertiesToOptions()
    {
        var o = Map.Options;
        o.Engine = Engine;
        if (AccessToken is not null) o.AccessToken = AccessToken;
        if (MapStyle is not null) o.Style = MapStyle;
        if (ReadLocalValue(CenterProperty) != DependencyProperty.UnsetValue || IsBound(CenterProperty)) o.Center = Center;
        if (ReadLocalValue(ZoomProperty) != DependencyProperty.UnsetValue || IsBound(ZoomProperty)) o.Zoom = Zoom;
        if (ReadLocalValue(BearingProperty) != DependencyProperty.UnsetValue || IsBound(BearingProperty)) o.Bearing = Bearing;
        if (ReadLocalValue(PitchProperty) != DependencyProperty.UnsetValue || IsBound(PitchProperty)) o.Pitch = Pitch;
    }

    private bool IsBound(DependencyProperty dp) => System.Windows.Data.BindingOperations.IsDataBound(this, dp);

    private void OnInitializationFailed(object? sender, MapHostErrorEventArgs e)
    {
        _root.Children.Clear();
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        };
        text.Inlines.Add(new Run(e.Message));
        if (e.IsRuntimeMissing)
        {
            text.Inlines.Add(new LineBreak());
            var link = new Hyperlink(new Run("Download the WebView2 Runtime")) { NavigateUri = new Uri(WebView2Environments.RuntimeDownloadUrl) };
            link.RequestNavigate += (_, a) =>
            {
                try { Process.Start(new ProcessStartInfo(a.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception) { /* ignore */ }
            };
            text.Inlines.Add(link);
        }
        _root.Children.Add(text);
        InitializationFailed?.Invoke(this, e);
    }

    // ------------------------------------------------------------------ map -> control

    private void OnMapLoaded(object? sender, MapCameraEventArgs e)
    {
        SetValue(IsMapLoadedPropertyKey, true);
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

    // ------------------------------------------------------------------ control -> map

    private static void OnCameraPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (view._syncingFromMap || !view.Map.IsLoaded) return;
        var camera = new CameraOptions();
        if (e.Property == CenterProperty) camera.Center = (LngLat)e.NewValue;
        else if (e.Property == ZoomProperty) camera.Zoom = (double)e.NewValue;
        else if (e.Property == BearingProperty) camera.Bearing = (double)e.NewValue;
        else if (e.Property == PitchProperty) camera.Pitch = (double)e.NewValue;
        view.Map.FireAndForget(view.Map.JumpToAsync(camera), "Camera update");
    }

    private static void OnMapStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (e.NewValue is string style)
        {
            view.Map.Options.Style = style;
            if (view.Map.IsLoaded) view.Map.FireAndForget(view.Map.SetStyleAsync(style), "Style change");
        }
    }

    private static void OnReinitializingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (!view._initialized) return;
        view.ApplyPropertiesToOptions();
        if (view._controller.IsInitialized) view.Map.FireAndForget(view.Map.ReinitializeAsync(), "Map re-initialization");
    }

    /// <summary>Releases the WebView2 and the map.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        (_webView as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}

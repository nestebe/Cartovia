using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Cartovia.Events;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cartovia.WinUI;

/// <summary>
/// WinUI 3 map control hosting MapLibre GL JS or Mapbox GL JS in the WinUI <see cref="WebView2"/>.
/// The full API is available through <see cref="Map"/>; the camera and the main options are bindable.
/// </summary>
/// <example><code>
/// &lt;carto:MapView MapStyle="https://tiles.openfreemap.org/styles/liberty"
///                Longitude="2.35" Latitude="48.85" Zoom="12" /&gt;
/// </code></example>
/// <remarks>
/// WinUI XAML has no type converters for custom structs: in XAML, set the center with <see cref="Longitude"/> and
/// <see cref="Latitude"/>; in code, use <see cref="Center"/>. All three stay in sync.
/// </remarks>
public sealed class MapView : UserControl, IDisposable
{
    private readonly WinUIMapController _controller;
    private readonly Grid _root = new();
    private WebView2? _webView;
    private bool _syncing;
    private bool _initStarted;
    private bool _disposed;

    /// <summary>Creates the control.</summary>
    public MapView()
    {
        _controller = new WinUIMapController(new MapOptions(), new WinUIHostOptions());
        _controller.InitializationFailed += OnInitializationFailed;
        Map.Loaded += OnMapLoaded;
        Map.MoveEnded += OnMoveEnded;
        Map.Click += OnMapClick;
        Content = _root;
        Loaded += OnControlLoaded;
    }

    // ------------------------------------------------------------------ API

    /// <summary>The map API: camera, sources, layers, markers, plugins, events…</summary>
    public CartoviaMap Map => _controller.Map;

    /// <summary>The WebView2 host (screenshots, PDF, dev tools, reload).</summary>
    public WinUIMapHost Host => _controller.Host;

    /// <summary>Map options used at initialization (dependency properties override the matching members).</summary>
    public MapOptions Options => Map.Options;

    /// <summary>WebView2 options. Must be set before the control is loaded.</summary>
    public WinUIHostOptions HostOptions => _controller.Host.Options;

    /// <summary>Raised when the map is loaded.</summary>
    public event EventHandler<MapCameraEventArgs>? MapLoaded;

    /// <summary>Raised at the end of every camera movement.</summary>
    public event EventHandler<MapCameraEventArgs>? CameraChanged;

    /// <summary>Raised when WebView2 cannot be initialized (e.g. runtime not installed).</summary>
    public event EventHandler<WinUIHostErrorEventArgs>? InitializationFailed;

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

    /// <summary>Applies the current options and dependency properties by recreating the map.</summary>
    public Task ReinitializeAsync()
    {
        ApplyPropertiesToOptions();
        return Map.ReinitializeAsync();
    }

    // ------------------------------------------------------------------ dependency properties

    /// <summary>Identifies <see cref="Engine"/>.</summary>
    public static readonly DependencyProperty EngineProperty = DependencyProperty.Register(
        nameof(Engine), typeof(MapEngine), typeof(MapView), new PropertyMetadata(MapEngine.MapLibre, OnReinitializingChanged));

    /// <summary>Rendering engine. Changing it after load recreates the map.</summary>
    public MapEngine Engine { get => (MapEngine)GetValue(EngineProperty); set => SetValue(EngineProperty, value); }

    /// <summary>Identifies <see cref="AccessToken"/>.</summary>
    public static readonly DependencyProperty AccessTokenProperty = DependencyProperty.Register(
        nameof(AccessToken), typeof(string), typeof(MapView), new PropertyMetadata(null, OnReinitializingChanged));

    /// <summary>Mapbox access token. Changing it after load recreates the map.</summary>
    public string? AccessToken { get => (string?)GetValue(AccessTokenProperty); set => SetValue(AccessTokenProperty, value); }

    /// <summary>Identifies <see cref="MapStyle"/>.</summary>
    public static readonly DependencyProperty MapStyleProperty = DependencyProperty.Register(
        nameof(MapStyle), typeof(string), typeof(MapView), new PropertyMetadata(null, OnMapStyleChanged));

    /// <summary>Style URL (see <see cref="MapStyles"/>). Null: engine default.</summary>
    public string? MapStyle { get => (string?)GetValue(MapStyleProperty); set => SetValue(MapStyleProperty, value); }

    /// <summary>Identifies <see cref="Center"/>.</summary>
    public static readonly DependencyProperty CenterProperty = DependencyProperty.Register(
        nameof(Center), typeof(object), typeof(MapView), new PropertyMetadata(null, OnCenterChanged));

    /// <summary>Map center (updated when the user moves the map). In XAML use <see cref="Longitude"/> / <see cref="Latitude"/>.</summary>
    public LngLat Center
    {
        get => GetValue(CenterProperty) is LngLat c ? c : new LngLat(Longitude, Latitude);
        set => SetValue(CenterProperty, value);
    }

    /// <summary>Identifies <see cref="Longitude"/>.</summary>
    public static readonly DependencyProperty LongitudeProperty = DependencyProperty.Register(
        nameof(Longitude), typeof(double), typeof(MapView), new PropertyMetadata(0.0, OnLongitudeLatitudeChanged));

    /// <summary>Longitude of the center (XAML friendly; bind with <c>Mode=TwoWay</c> to follow the map).</summary>
    public double Longitude { get => (double)GetValue(LongitudeProperty); set => SetValue(LongitudeProperty, value); }

    /// <summary>Identifies <see cref="Latitude"/>.</summary>
    public static readonly DependencyProperty LatitudeProperty = DependencyProperty.Register(
        nameof(Latitude), typeof(double), typeof(MapView), new PropertyMetadata(0.0, OnLongitudeLatitudeChanged));

    /// <summary>Latitude of the center (XAML friendly; bind with <c>Mode=TwoWay</c> to follow the map).</summary>
    public double Latitude { get => (double)GetValue(LatitudeProperty); set => SetValue(LatitudeProperty, value); }

    /// <summary>Identifies <see cref="Zoom"/>.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(MapView), new PropertyMetadata(1.0, OnCameraChanged));

    /// <summary>Zoom level (bind with <c>Mode=TwoWay</c> to follow the map).</summary>
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>Identifies <see cref="Bearing"/>.</summary>
    public static readonly DependencyProperty BearingProperty = DependencyProperty.Register(
        nameof(Bearing), typeof(double), typeof(MapView), new PropertyMetadata(0.0, OnCameraChanged));

    /// <summary>Bearing in degrees.</summary>
    public double Bearing { get => (double)GetValue(BearingProperty); set => SetValue(BearingProperty, value); }

    /// <summary>Identifies <see cref="Pitch"/>.</summary>
    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(MapView), new PropertyMetadata(0.0, OnCameraChanged));

    /// <summary>Pitch in degrees.</summary>
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    /// <summary>Identifies <see cref="IsMapLoaded"/>.</summary>
    public static readonly DependencyProperty IsMapLoadedProperty = DependencyProperty.Register(
        nameof(IsMapLoaded), typeof(bool), typeof(MapView), new PropertyMetadata(false));

    /// <summary>True once the map is loaded (read-only).</summary>
    public bool IsMapLoaded { get => (bool)GetValue(IsMapLoadedProperty); private set => SetValue(IsMapLoadedProperty, value); }

    /// <summary>Identifies <see cref="ClickCommand"/>.</summary>
    public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.Register(
        nameof(ClickCommand), typeof(ICommand), typeof(MapView), new PropertyMetadata(null));

    /// <summary>Command executed on map clicks, with the <see cref="MapMouseEventArgs"/> as parameter.</summary>
    public ICommand? ClickCommand { get => (ICommand?)GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>Identifies <see cref="LoadedCommand"/>.</summary>
    public static readonly DependencyProperty LoadedCommandProperty = DependencyProperty.Register(
        nameof(LoadedCommand), typeof(ICommand), typeof(MapView), new PropertyMetadata(null));

    /// <summary>Command executed when the map is loaded, with the <see cref="CartoviaMap"/> as parameter.</summary>
    public ICommand? LoadedCommand { get => (ICommand?)GetValue(LoadedCommandProperty); set => SetValue(LoadedCommandProperty, value); }

    // ------------------------------------------------------------------ initialization

    private async void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_initStarted || _disposed) return;
        _initStarted = true;
        ApplyPropertiesToOptions();
        _webView = new WebView2();
        _root.Children.Add(_webView);
        await _controller.InitializeAsync(_webView);
    }

    private void ApplyPropertiesToOptions()
    {
        var o = Map.Options;
        o.Engine = Engine;
        if (AccessToken is not null) o.AccessToken = AccessToken;
        if (MapStyle is not null) o.Style = MapStyle;
        if (IsSet(CenterProperty) || IsSet(LongitudeProperty) || IsSet(LatitudeProperty)) o.Center = Center;
        if (IsSet(ZoomProperty)) o.Zoom = Zoom;
        if (IsSet(BearingProperty)) o.Bearing = Bearing;
        if (IsSet(PitchProperty)) o.Pitch = Pitch;
    }

    private bool IsSet(DependencyProperty dp) => ReadLocalValue(dp) != DependencyProperty.UnsetValue;

    private void OnInitializationFailed(object? sender, WinUIHostErrorEventArgs e)
    {
        _root.Children.Clear();
        _root.Children.Add(new TextBlock
        {
            Text = e.Message,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        });
        InitializationFailed?.Invoke(this, e);
    }

    // ------------------------------------------------------------------ map -> control

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

    private void SyncCamera(CameraState camera)
    {
        _syncing = true;
        try
        {
            SetValue(CenterProperty, camera.Center);
            SetValue(LongitudeProperty, camera.Center.Longitude);
            SetValue(LatitudeProperty, camera.Center.Latitude);
            SetValue(ZoomProperty, camera.Zoom);
            SetValue(BearingProperty, camera.Bearing);
            SetValue(PitchProperty, camera.Pitch);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ------------------------------------------------------------------ control -> map

    private static void OnCenterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (view._syncing || e.NewValue is not LngLat c) return;
        view._syncing = true;
        try
        {
            view.SetValue(LongitudeProperty, c.Longitude);
            view.SetValue(LatitudeProperty, c.Latitude);
        }
        finally
        {
            view._syncing = false;
        }
        view.MoveCamera(new CameraOptions { Center = c });
    }

    private static void OnLongitudeLatitudeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (view._syncing) return;
        var c = new LngLat(view.Longitude, view.Latitude);
        view._syncing = true;
        try { view.SetValue(CenterProperty, c); }
        finally { view._syncing = false; }
        view.MoveCamera(new CameraOptions { Center = c });
    }

    private static void OnCameraChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (view._syncing) return;
        var camera = new CameraOptions();
        if (e.Property == ZoomProperty) camera.Zoom = (double)e.NewValue;
        else if (e.Property == BearingProperty) camera.Bearing = (double)e.NewValue;
        else if (e.Property == PitchProperty) camera.Pitch = (double)e.NewValue;
        view.MoveCamera(camera);
    }

    private void MoveCamera(CameraOptions camera)
    {
        if (Map.IsLoaded) Map.FireAndForget(Map.JumpToAsync(camera), "Camera update");
    }

    private static void OnMapStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (e.NewValue is not string style) return;
        view.Map.Options.Style = style;
        if (view.Map.IsLoaded) view.Map.FireAndForget(view.Map.SetStyleAsync(style), "Style change");
    }

    private static void OnReinitializingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapView)d;
        if (!view._controller.IsInitialized) return;
        view.ApplyPropertiesToOptions();
        view.Map.FireAndForget(view.Map.ReinitializeAsync(), "Map re-initialization");
    }

    /// <summary>Releases the WebView2 and the map.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        _webView?.Close();
    }
}

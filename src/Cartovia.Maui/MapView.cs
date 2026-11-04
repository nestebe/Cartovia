using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Cartovia.Events;
using Microsoft.Maui.Controls;
#if WINDOWS
using Cartovia.WinUI;
#endif

namespace Cartovia.Maui;

/// <summary>Arguments of <see cref="MapView.InitializationFailed"/>.</summary>
public sealed class MapInitializationFailedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public MapInitializationFailedEventArgs(string message, bool isRuntimeMissing = false)
    {
        Message = message;
        IsRuntimeMissing = isRuntimeMissing;
    }

    /// <summary>Description of the problem.</summary>
    public string Message { get; }
    /// <summary>True when the WebView2 runtime is not installed.</summary>
    public bool IsRuntimeMissing { get; }
}

/// <summary>
/// .NET MAUI map control hosting MapLibre GL JS or Mapbox GL JS. On Windows the map runs in the WinUI WebView2;
/// other platforms display a placeholder and the map calls fail with <see cref="MapInitializationException"/>.
/// Register the handler with <c>builder.UseCartovia()</c>.
/// </summary>
/// <example><code>
/// &lt;carto:MapView MapStyle="https://tiles.openfreemap.org/styles/liberty" Center="2.35, 48.85" Zoom="12" /&gt;
/// </code></example>
public sealed class MapView : ContentView, IDisposable
{
#if WINDOWS
    private readonly WinUIMapController _controller;
#endif
    private bool _syncing;
    private bool _disposed;

    /// <summary>Creates the control.</summary>
    public MapView()
    {
#if WINDOWS
        _controller = new WinUIMapController(new MapOptions(), new WinUIHostOptions());
        _controller.InitializationFailed += (_, e) =>
        {
            Content = new Label { Text = e.Message, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            InitializationFailed?.Invoke(this, new MapInitializationFailedEventArgs(e.Message, e.IsRuntimeMissing));
        };
        Map = _controller.Map;
        Content = new CartoviaWebView(this);
#else
        var host = new UnsupportedPlatformMapHost();
        Map = new CartoviaMap(host, new MapOptions());
        host.Start();
        Content = new Label
        {
            Text = "The Cartovia map requires Windows.",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
#endif
        Map.Loaded += OnMapLoaded;
        Map.MoveEnded += OnMoveEnded;
        Map.Click += OnMapClick;
    }

    // ------------------------------------------------------------------ API

    /// <summary>The map API: camera, sources, layers, markers, plugins, events…</summary>
    public CartoviaMap Map { get; }

    /// <summary>Map options used at initialization (bindable properties override the matching members).</summary>
    public MapOptions Options => Map.Options;

#if WINDOWS
    /// <summary>WebView2 options (Windows). Must be set before the control is displayed.</summary>
    public WinUIHostOptions HostOptions => _controller.Host.Options;

    internal WinUIMapController Controller => _controller;
#endif

    /// <summary>Raised when the map is loaded.</summary>
    public event EventHandler<MapCameraEventArgs>? MapLoaded;

    /// <summary>Raised at the end of every camera movement.</summary>
    public event EventHandler<MapCameraEventArgs>? CameraChanged;

#pragma warning disable CS0067 // only raised on Windows
    /// <summary>Raised when the map cannot be hosted (WebView2 runtime missing…).</summary>
    public event EventHandler<MapInitializationFailedEventArgs>? InitializationFailed;
#pragma warning restore CS0067

    /// <summary>Captures the visible map as PNG bytes (Windows).</summary>
    public Task<byte[]> CaptureAsync()
#if WINDOWS
        => _controller.CaptureAsync();
#else
        => throw new PlatformNotSupportedException("The Cartovia map requires Windows.");
#endif

    /// <summary>Saves a PNG (or JPEG) screenshot of the map (Windows).</summary>
    public async Task SaveScreenshotAsync(string path)
    {
#if WINDOWS
        using var fs = File.Create(path);
        await _controller.CaptureAsync(fs, jpeg: path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
#else
        await Task.FromException(new PlatformNotSupportedException("The Cartovia map requires Windows."));
#endif
    }

    /// <summary>Prints the map to a PDF file (Windows).</summary>
    public Task<bool> PrintToPdfAsync(string path, bool landscape = true)
#if WINDOWS
        => _controller.PrintToPdfAsync(path, landscape);
#else
        => throw new PlatformNotSupportedException("The Cartovia map requires Windows.");
#endif

    /// <summary>Opens the developer tools (Windows, requires <c>HostOptions.EnableDevTools</c>).</summary>
    public void OpenDevTools()
    {
#if WINDOWS
        _controller.OpenDevTools();
#endif
    }

    /// <summary>Applies the current options and bindable properties by recreating the map.</summary>
    public Task ReinitializeAsync()
    {
        ApplyPropertiesToOptions();
        return Map.ReinitializeAsync();
    }

    // ------------------------------------------------------------------ bindable properties

    /// <summary>Identifies <see cref="Engine"/>.</summary>
    public static readonly BindableProperty EngineProperty = BindableProperty.Create(
        nameof(Engine), typeof(MapEngine), typeof(MapView), MapEngine.MapLibre, propertyChanged: OnReinitializingChanged);

    /// <summary>Rendering engine. Changing it after load recreates the map.</summary>
    public MapEngine Engine { get => (MapEngine)GetValue(EngineProperty); set => SetValue(EngineProperty, value); }

    /// <summary>Identifies <see cref="AccessToken"/>.</summary>
    public static readonly BindableProperty AccessTokenProperty = BindableProperty.Create(
        nameof(AccessToken), typeof(string), typeof(MapView), null, propertyChanged: OnReinitializingChanged);

    /// <summary>Mapbox access token. Changing it after load recreates the map.</summary>
    public string? AccessToken { get => (string?)GetValue(AccessTokenProperty); set => SetValue(AccessTokenProperty, value); }

    /// <summary>Identifies <see cref="MapStyle"/>.</summary>
    public static readonly BindableProperty MapStyleProperty = BindableProperty.Create(
        nameof(MapStyle), typeof(string), typeof(MapView), null, propertyChanged: OnMapStyleChanged);

    /// <summary>Style URL (see <see cref="MapStyles"/>). Null: engine default.</summary>
    public string? MapStyle { get => (string?)GetValue(MapStyleProperty); set => SetValue(MapStyleProperty, value); }

    /// <summary>Identifies <see cref="Center"/>.</summary>
    public static readonly BindableProperty CenterProperty = BindableProperty.Create(
        nameof(Center), typeof(LngLat), typeof(MapView), default(LngLat), BindingMode.TwoWay, propertyChanged: (b, o, n) => ((MapView)b).MoveCamera(new CameraOptions { Center = (LngLat)n }));

    /// <summary>Map center (two-way). In XAML: <c>Center="2.35, 48.85"</c> (longitude, latitude).</summary>
    public LngLat Center { get => (LngLat)GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

    /// <summary>Identifies <see cref="Zoom"/>.</summary>
    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double), typeof(MapView), 1.0, BindingMode.TwoWay, propertyChanged: (b, o, n) => ((MapView)b).MoveCamera(new CameraOptions { Zoom = (double)n }));

    /// <summary>Zoom level (two-way).</summary>
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>Identifies <see cref="Bearing"/>.</summary>
    public static readonly BindableProperty BearingProperty = BindableProperty.Create(
        nameof(Bearing), typeof(double), typeof(MapView), 0.0, BindingMode.TwoWay, propertyChanged: (b, o, n) => ((MapView)b).MoveCamera(new CameraOptions { Bearing = (double)n }));

    /// <summary>Bearing in degrees (two-way).</summary>
    public double Bearing { get => (double)GetValue(BearingProperty); set => SetValue(BearingProperty, value); }

    /// <summary>Identifies <see cref="Pitch"/>.</summary>
    public static readonly BindableProperty PitchProperty = BindableProperty.Create(
        nameof(Pitch), typeof(double), typeof(MapView), 0.0, BindingMode.TwoWay, propertyChanged: (b, o, n) => ((MapView)b).MoveCamera(new CameraOptions { Pitch = (double)n }));

    /// <summary>Pitch in degrees (two-way).</summary>
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    private static readonly BindablePropertyKey IsMapLoadedPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsMapLoaded), typeof(bool), typeof(MapView), false);

    /// <summary>Identifies <see cref="IsMapLoaded"/>.</summary>
    public static readonly BindableProperty IsMapLoadedProperty = IsMapLoadedPropertyKey.BindableProperty;

    /// <summary>True once the map is loaded.</summary>
    public bool IsMapLoaded => (bool)GetValue(IsMapLoadedProperty);

    /// <summary>Identifies <see cref="ClickCommand"/>.</summary>
    public static readonly BindableProperty ClickCommandProperty = BindableProperty.Create(
        nameof(ClickCommand), typeof(ICommand), typeof(MapView));

    /// <summary>Command executed on map clicks, with the <see cref="MapMouseEventArgs"/> as parameter.</summary>
    public ICommand? ClickCommand { get => (ICommand?)GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>Identifies <see cref="LoadedCommand"/>.</summary>
    public static readonly BindableProperty LoadedCommandProperty = BindableProperty.Create(
        nameof(LoadedCommand), typeof(ICommand), typeof(MapView));

    /// <summary>Command executed when the map is loaded, with the <see cref="CartoviaMap"/> as parameter.</summary>
    public ICommand? LoadedCommand { get => (ICommand?)GetValue(LoadedCommandProperty); set => SetValue(LoadedCommandProperty, value); }

    // ------------------------------------------------------------------ options

    internal void ApplyPropertiesToOptions()
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
        _syncing = true;
        try
        {
            Center = camera.Center;
            Zoom = camera.Zoom;
            Bearing = camera.Bearing;
            Pitch = camera.Pitch;
        }
        finally
        {
            _syncing = false;
        }
    }

    // ------------------------------------------------------------------ control -> map

    private void MoveCamera(CameraOptions camera)
    {
        if (_syncing || !Map.IsLoaded) return;
        Map.FireAndForget(Map.JumpToAsync(camera), "Camera update");
    }

    private static void OnMapStyleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (MapView)bindable;
        if (newValue is not string style) return;
        view.Map.Options.Style = style;
        if (view.Map.IsLoaded) view.Map.FireAndForget(view.Map.SetStyleAsync(style), "Style change");
    }

    private static void OnReinitializingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (MapView)bindable;
#if WINDOWS
        if (!view._controller.IsInitialized) return;
        view.ApplyPropertiesToOptions();
        view.Map.FireAndForget(view.Map.ReinitializeAsync(), "Map re-initialization");
#endif
    }

    /// <summary>Releases the WebView2 and the map.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if WINDOWS
        _controller.Dispose();
#else
        Map.Dispose();
#endif
    }
}

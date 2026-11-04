# WPF control

`Cartovia.Wpf.MapView` is a `UserControl` hosting the map. The full API is on `MapView.Map` (a `CartoviaMap`).

```xml
xmlns:carto="clr-namespace:Cartovia.Wpf;assembly=Cartovia.Wpf"

<carto:MapView x:Name="MapView"
               Engine="MapLibre"
               MapStyle="https://tiles.openfreemap.org/styles/liberty"
               Center="{Binding Center}" Zoom="{Binding Zoom}"
               Bearing="{Binding Bearing}" Pitch="{Binding Pitch}"
               ClickCommand="{Binding MapClickCommand}"
               LoadedCommand="{Binding MapLoadedCommand}" />
```

## Bindable properties

| Property | Type | Notes |
|---|---|---|
| `Engine` | `MapEngine` | `MapLibre` (default) or `Mapbox`. Changing it after load recreates the map. |
| `AccessToken` | `string` | Mapbox token. Changing it after load recreates the map. |
| `MapStyle` | `string` | Style URL. Changing it calls `SetStyleAsync` (your layers are restored). |
| `Center` | `LngLat` | **Two-way**: updated at the end of every camera move. XAML: `Center="2.35, 48.85"`. |
| `Zoom`, `Bearing`, `Pitch` | `double` | **Two-way**. |
| `IsMapLoaded` | `bool` | Read-only. |
| `ClickCommand` | `ICommand` | Executed on map clicks with the `MapMouseEventArgs`. |
| `LoadedCommand` | `ICommand` | Executed once the map is loaded with the `CartoviaMap`. |

## MVVM example

```csharp
public sealed class MapViewModel : INotifyPropertyChanged
{
    private LngLat _center = new(2.35, 48.85);
    private double _zoom = 11;

    public LngLat Center { get => _center; set { _center = value; OnPropertyChanged(); } }
    public double Zoom { get => _zoom; set { _zoom = value; OnPropertyChanged(); } }

    public ICommand MapClickCommand => new RelayCommand<MapMouseEventArgs>(e => Status = $"Clicked {e.LngLat}");
    public ICommand MapLoadedCommand => new RelayCommand<CartoviaMap>(async map =>
    {
        await map.AddGeoJsonAsync("stores", await LoadStoresAsync(), "#2563eb");
    });

    // Moving the map updates Center / Zoom; setting them from the view model moves the map.
    public void GoToLyon() { Center = new LngLat(4.8357, 45.7640); Zoom = 12; }

    // INotifyPropertyChanged / Status / LoadStoresAsync omitted
}
```

`RelayCommand<T>` is any command implementation (CommunityToolkit.Mvvm, Prism…).

## Options before loading

`MapView.Options` (`MapOptions`) and `MapView.HostOptions` (`WebView2HostOptions`) are applied when the control is
loaded. Set them in the window constructor, after `InitializeComponent()`:

```csharp
MapView.Options.InitialControls.Add(new NavigationControl { VisualizePitch = true });
MapView.Options.InitialControls.Add(new ScaleControl());
MapView.Options.MaxBounds = new LngLatBounds(-5.5, 41, 10, 51.5);
MapView.Options.Language = "fr";
MapView.HostOptions.AllowGeolocation = true;      // GeolocateControl
MapView.HostOptions.EnableDevTools = true;        // F12 / OpenDevTools()
```

## Events

| Event | Arguments |
|---|---|
| `MapLoaded` | `MapCameraEventArgs` |
| `CameraChanged` | `MapCameraEventArgs` (end of every camera move) |
| `InitializationFailed` | `MapHostErrorEventArgs` (`IsRuntimeMissing` when WebView2 is not installed) |

Every other event is on `MapView.Map` (see [Events](events.md)).

## Drawing WPF content over the map

The default WebView2 is an HWND: WPF elements cannot be drawn on top of it ("airspace"). Set
`UseCompositionControl` before the control is loaded to use `WebView2CompositionControl` instead:

```xml
<Grid>
    <carto:MapView x:Name="MapView" UseCompositionControl="True" />
    <Border HorizontalAlignment="Left" VerticalAlignment="Top" Margin="16" Padding="12"
            Background="#CC111827" CornerRadius="8">
        <TextBlock Foreground="White" Text="Overlay drawn by WPF" />
    </Border>
</Grid>
```

Composition mode is a bit slower; keep the default when you don't need overlays.
Alternatives without composition: [HTML controls](annotations-and-controls.md#custom-html-controls), markers and popups.

## Export

```csharp
await MapView.SaveScreenshotAsync(@"C:\temp\map.png");     // .jpg also supported
byte[] png = await MapView.CaptureAsync();
bool ok = await MapView.PrintToPdfAsync(@"C:\temp\map.pdf", landscape: true);
```

## Lifetime

The control initializes WebView2 on its first `Loaded` event. Call `MapView.Dispose()` when you remove it
permanently (closing the window disposes the WebView2 automatically).

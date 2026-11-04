#if WINDOWS
using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using WinWebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace Cartovia.Maui;

/// <summary>Internal view hosting the WinUI WebView2 of a <see cref="MapView"/>.</summary>
internal sealed class CartoviaWebView : View
{
    public CartoviaWebView(MapView owner) => Owner = owner;

    public MapView Owner { get; }
}

/// <summary>Windows handler of <see cref="CartoviaWebView"/>: creates the WinUI WebView2 and attaches the map host.</summary>
internal sealed class CartoviaWebViewHandler : ViewHandler<CartoviaWebView, WinWebView2>
{
    public static readonly PropertyMapper<CartoviaWebView, CartoviaWebViewHandler> Mapper = new(ViewMapper);

    private bool _initStarted;

    public CartoviaWebViewHandler() : base(Mapper) { }

    protected override WinWebView2 CreatePlatformView() => new();

    protected override void ConnectHandler(WinWebView2 platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Loaded += OnLoaded;
    }

    protected override void DisconnectHandler(WinWebView2 platformView)
    {
        platformView.Loaded -= OnLoaded;
        base.DisconnectHandler(platformView);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initStarted) return;
        _initStarted = true;
        var owner = VirtualView.Owner;
        owner.ApplyPropertiesToOptions();
        await owner.Controller.InitializeAsync(PlatformView);
    }
}
#endif

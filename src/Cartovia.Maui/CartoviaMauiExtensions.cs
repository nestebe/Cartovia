using Microsoft.Maui.Hosting;

namespace Cartovia.Maui;

/// <summary>Registration of the Cartovia handlers.</summary>
public static class CartoviaMauiExtensions
{
    /// <summary>
    /// Registers the Cartovia map handler. Call it in <c>MauiProgram.CreateMauiApp</c>:
    /// <c>builder.UseMauiApp&lt;App&gt;().UseCartovia();</c>
    /// </summary>
    public static MauiAppBuilder UseCartovia(this MauiAppBuilder builder)
    {
#if WINDOWS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<CartoviaWebView, CartoviaWebViewHandler>());
#endif
        return builder;
    }
}

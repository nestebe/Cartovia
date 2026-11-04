using System;
using Avalonia;

namespace Cartovia.Samples.Avalonia;

internal static class Program
{
    [STAThread] // WebView2 requires an STA UI thread
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

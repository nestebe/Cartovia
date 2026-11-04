using Cartovia.Maui;

namespace Cartovia.Samples.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
        => MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseCartovia()
            .Build();
}

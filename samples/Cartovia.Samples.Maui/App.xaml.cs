namespace Cartovia.Samples.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage())
        {
            Title = "Cartovia — MAUI showcase",
            Width = 1480,
            Height = 940,
            TitleBar = new TitleBar
            {
                Title = "Cartovia — MAUI showcase",
                BackgroundColor = Color.FromArgb("#0A0F1C"),
                ForegroundColor = Colors.White,
                HeightRequest = 40,
            },
        };
}

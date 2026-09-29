using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, AppPreferences preferences)
    {
        _services = services;
        InitializeComponent();
        preferences.ApplyTheme(this);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<AppShell>());
        _services.GetRequiredService<AppLifecycle>().Attach(window);
        return window;
    }
}

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

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<AppShell>());
}

using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using TransitGames.App.Pages;
using TransitGames.App.Services;
using TransitGames.App.ViewModels;
using TransitGames.Core;
using TransitGames.Core.Location;
using TransitGames.Geo;
using TransitGames.Tracking;
using TransitGames.Sync;
using TransitGames.Games.HideAndSeek.Maui;
using TransitGames.Games.Tag.Maui;
using TransitGames.UI;
using TransitGames.UI.Games;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Language must be set before any resource string is read.
        new AppPreferences(Preferences.Default).ApplyLanguage();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp() // Mapsui renders with SkiaSharp
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var services = builder.Services;
        services.AddTransitGamesCore(Path.Combine(FileSystem.AppDataDirectory, "transitgames.db3"));
        services.AddTransitGamesGeo();
        services.AddTransitGamesTracking();
        services.AddTransitGamesSync();
        services.AddTransitGamesUi();
#if ANDROID
        services.AddSingleton<ILocationTracker, TransitGames.App.Tracking.AndroidLocationTracker>();
#elif IOS
        services.AddSingleton<ILocationTracker, TransitGames.App.Tracking.IosLocationTracker>();
#endif

        // Games: one line per game.
        services.AddGame<HideAndSeekUiModule>();
        services.AddGame<TagUiModule>();

        services.AddSingleton<AppShell>();
        services.AddTransient<StartPage>().AddTransient<StartViewModel>();
        services.AddSingleton<NewGameWizard>();
        services.AddTransient<NewGamePage>().AddTransient<NewGameViewModel>();
        services.AddTransient<AreaSetupPage>().AddTransient<AreaSetupViewModel>();
        services.AddTransient<StationsSetupPage>().AddTransient<StationsSetupViewModel>();
        services.AddTransient<GameSetupPage>().AddTransient<GameSetupViewModel>();
        services.AddTransient<HistoryPage>().AddTransient<HistoryViewModel>();
        services.AddTransient<TracksPage>().AddTransient<TracksViewModel>();
        services.AddTransient<TrackPreviewPage>().AddTransient<TrackPreviewViewModel>();
        services.AddTransient<JoinGamePage>().AddTransient<JoinGameViewModel>();
        services.AddTransient<SettingsPage>().AddTransient<SettingsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Created eagerly: they must observe the session manager before the first game is resumed.
        app.Services.GetRequiredService<TrackingCoordinator>();
        app.Services.GetRequiredService<OnlineSessionCoordinator>();
        app.Services.GetRequiredService<TransitGames.Core.Sync.GameMessageDispatcher>();
        return app;
    }
}

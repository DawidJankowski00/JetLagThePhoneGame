using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using TransitGames.App.Pages;
using TransitGames.App.ViewModels;
using TransitGames.Core;
using TransitGames.Games.HideAndSeek.Maui;
using TransitGames.Games.Tag.Maui;
using TransitGames.UI;
using TransitGames.UI.Games;
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
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var services = builder.Services;
        services.AddTransitGamesCore(Path.Combine(FileSystem.AppDataDirectory, "transitgames.db3"));
        services.AddTransitGamesUi();

        // Games: one line per game.
        services.AddGame<HideAndSeekUiModule>();
        services.AddGame<TagUiModule>();

        services.AddSingleton<AppShell>();
        services.AddTransient<StartPage>().AddTransient<StartViewModel>();
        services.AddTransient<NewGamePage>().AddTransient<NewGameViewModel>();
        services.AddTransient<HistoryPage>().AddTransient<HistoryViewModel>();
        services.AddTransient<TracksPage>();
        services.AddTransient<SettingsPage>().AddTransient<SettingsViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitGames.UI.Games;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;
using TransitGames.UI.ViewModels;

namespace TransitGames.UI;

public static class UiServiceCollectionExtensions
{
    /// <summary>Shared UI infrastructure used by the app and by all game UI modules.</summary>
    public static IServiceCollection AddTransitGamesUi(this IServiceCollection services)
    {
        services.TryAddSingleton<IDialogService, ShellDialogService>();
        services.TryAddSingleton<INavigationService, ShellNavigationService>();
        services.TryAddSingleton<GameUiRegistry>();
        services.TryAddSingleton(Preferences.Default);
        services.TryAddSingleton<AppPreferences>();
        services.AddTransient<ActiveSessionViewModel>();
        return services;
    }
}

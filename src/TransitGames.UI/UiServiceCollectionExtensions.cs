using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitGames.UI.Games;
using TransitGames.UI.Maps;
using TransitGames.UI.Notifications;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;
using TransitGames.UI.ViewModels;

namespace TransitGames.UI;

public static class UiServiceCollectionExtensions
{
    /// <summary>Shared UI infrastructure used by the app and by all game UI modules (needs Core, Tracking and Sync).</summary>
    public static IServiceCollection AddTransitGamesUi(this IServiceCollection services)
    {
        services.TryAddSingleton<IDialogService, ShellDialogService>();
        services.TryAddSingleton<INavigationService, ShellNavigationService>();
        services.TryAddSingleton<GameUiRegistry>();
        services.TryAddSingleton(Preferences.Default);
        services.TryAddSingleton<AppPreferences>();
        services.TryAddSingleton<TrackingPreferences>();
        services.TryAddSingleton(DeviceInfo.Current);
        services.TryAddSingleton(Share.Default);
        services.TryAddSingleton<TrackingCoordinator>();
        services.TryAddSingleton<OnlinePreferences>();
        services.TryAddSingleton<OnlineGameService>();
        services.TryAddSingleton<OnlineSessionCoordinator>();
        services.TryAddSingleton<AppLifecycle>();
        services.TryAddSingleton<INotificationScheduler, NullNotificationScheduler>();
        services.TryAddSingleton<GameNotificationCoordinator>();
        services.TryAddSingleton(Geolocation.Default);
        services.TryAddSingleton<IDeviceLocationService, DeviceLocationService>();
        services.TryAddSingleton(FilePicker.Default);
        services.TryAddSingleton<ITextFilePicker, TextFilePicker>();
        services.TryAddSingleton<IMapLayerProvider>(
            _ => new MapLayerProvider(Path.Combine(FileSystem.CacheDirectory, "tiles", "osm")));
        services.AddTransient<ActiveSessionViewModel>();
        return services;
    }
}

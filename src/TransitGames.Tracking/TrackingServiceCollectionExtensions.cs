using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitGames.Tracking.Export;
using TransitGames.Tracking.Storage;

namespace TransitGames.Tracking;

public static class TrackingServiceCollectionExtensions
{
    /// <summary>Track storage, recorder and export. The platform <c>ILocationTracker</c> is registered by the app.</summary>
    public static IServiceCollection AddTransitGamesTracking(this IServiceCollection services)
    {
        services.TryAddSingleton<ITrackStore, SqliteTrackStore>();
        services.TryAddSingleton<TrackRecorder>();
        services.TryAddTransient<TrackExporter>();
        return services;
    }
}

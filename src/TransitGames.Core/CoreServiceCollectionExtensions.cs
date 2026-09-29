using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Core.Sync;
using TransitGames.Core.Timing;

namespace TransitGames.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers registry, session management, persistence and sync. <see cref="IGameSyncService"/> is the
    /// <see cref="GameSyncRouter"/>, offline (<see cref="LocalGameSyncService"/>) until an online game switches it.
    /// </summary>
    public static IServiceCollection AddTransitGamesCore(this IServiceCollection services, string databasePath)
    {
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddSingleton(new DatabaseOptions(databasePath));
        services.TryAddSingleton<AppDatabase>();
        services.TryAddSingleton<IGameSessionStore, SqliteGameSessionStore>();
        services.TryAddSingleton<IGameEventLog, SqliteGameEventLog>();
        services.TryAddSingleton<IStationRepository, SqliteStationRepository>();
        services.TryAddSingleton<IGameAreaRepository, SqliteGameAreaRepository>();
        services.TryAddSingleton<LocalGameSyncService>();
        services.TryAddSingleton<GameSyncRouter>();
        services.TryAddSingleton<IGameSyncService>(sp => sp.GetRequiredService<GameSyncRouter>());
        services.TryAddSingleton<GameRegistry>();
        services.TryAddSingleton<GameSessionManager>();
        services.TryAddSingleton<GameMessageDispatcher>();
        return services;
    }
}

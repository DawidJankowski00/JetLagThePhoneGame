using Microsoft.Extensions.DependencyInjection;

namespace TransitGames.UI.Games;

public sealed class GameUiRegistry
{
    private readonly Dictionary<string, IGameUiModule> _modules;

    public GameUiRegistry(IEnumerable<IGameUiModule> modules) =>
        _modules = modules.ToDictionary(m => m.GameId, StringComparer.Ordinal);

    public IReadOnlyCollection<IGameUiModule> Modules => _modules.Values;

    public IGameUiModule Get(string gameId) =>
        _modules.TryGetValue(gameId, out var module)
            ? module
            : throw new KeyNotFoundException($"No UI module registered for game '{gameId}'.");

    /// <summary>Registers all game routes with Shell. Call once when the Shell is created.</summary>
    public void RegisterRoutes()
    {
        foreach (var route in _modules.Values.SelectMany(m => m.Routes))
        {
            Routing.RegisterRoute(route.Route, route.PageType);
        }
    }
}

public static class GameUiServiceCollectionExtensions
{
    /// <summary>The single line needed to plug a game into the app.</summary>
    public static IServiceCollection AddGame<TUiModule>(this IServiceCollection services)
        where TUiModule : class, IGameUiModule, new()
    {
        var module = new TUiModule();
        services.AddSingleton<IGameUiModule>(module);
        module.RegisterServices(services);
        return services;
    }
}

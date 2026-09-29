using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TransitGames.Core.Games;

public static class GameServiceCollectionExtensions
{
    /// <summary>Registers a game module and lets it register its own services.</summary>
    public static IServiceCollection AddGameModule<TModule>(this IServiceCollection services)
        where TModule : class, IGameModule, new()
    {
        var module = new TModule();
        services.AddSingleton<IGameModule>(module);
        services.TryAddSingleton(module);
        module.RegisterServices(services);
        return services;
    }
}

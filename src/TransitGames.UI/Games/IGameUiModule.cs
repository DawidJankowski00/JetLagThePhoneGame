using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Games;

namespace TransitGames.UI.Games;

/// <summary>A Shell route contributed by a game (page type is resolved from DI).</summary>
public sealed record GameRoute(string Route, Type PageType);

/// <summary>
/// UI side of a game: pages, view models and routes. Pairs with the game's <see cref="IGameModule"/>,
/// which holds the MAUI-free logic.
/// </summary>
public interface IGameUiModule
{
    string GameId { get; }

    IReadOnlyList<GameRoute> Routes { get; }

    /// <summary>Route of the main in-game screen for the given role.</summary>
    string GetGameRoute(string roleId);

    /// <summary>
    /// Editor of the game's own settings, shown in the last step of the new-game wizard (it edits the given
    /// settings object in place). Null when the game has nothing to configure.
    /// </summary>
    View? CreateSettingsView(GameSettings settings);

    /// <summary>Registers the logic module plus pages and view models.</summary>
    void RegisterServices(IServiceCollection services);
}

/// <summary>Base class binding a UI module to its logic module type.</summary>
public abstract class GameUiModule<TGameModule> : IGameUiModule
    where TGameModule : class, IGameModule, new()
{
    public abstract string GameId { get; }

    public abstract IReadOnlyList<GameRoute> Routes { get; }

    public abstract string GetGameRoute(string roleId);

    public virtual View? CreateSettingsView(GameSettings settings) => null;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddGameModule<TGameModule>();
        foreach (var route in Routes)
        {
            services.AddTransient(route.PageType);
        }

        RegisterUiServices(services);
    }

    /// <summary>Registers view models and other UI services of the game.</summary>
    protected virtual void RegisterUiServices(IServiceCollection services)
    {
    }
}

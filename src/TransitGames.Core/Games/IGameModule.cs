using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Games;

/// <summary>
/// A pluggable game. Game logic lives in a plain .NET project implementing this interface;
/// its UI lives in a separate MAUI project (see TransitGames.UI.Games.IGameUiModule).
/// </summary>
public interface IGameModule
{
    /// <summary>Stable identifier, persisted in session snapshots. Never change it once released.</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>Glyph/emoji or image name shown on the start screen.</summary>
    string Icon { get; }

    int MinPlayers { get; }

    IReadOnlyList<GameRole> Roles { get; }

    GameSettings CreateDefaultSettings();

    IGameSession CreateSession(GameSettings settings, string localRoleId);

    IGameSession RestoreSession(GameSessionSnapshot snapshot);

    /// <summary>Registers game-specific services (question engines, card decks...).</summary>
    void RegisterServices(IServiceCollection services);
}

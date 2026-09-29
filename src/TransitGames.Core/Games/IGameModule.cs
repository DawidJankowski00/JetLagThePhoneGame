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

    /// <summary>Which role may see whose location. Anything not listed stays private.</summary>
    LocationSharingPolicy LocationSharing { get; }

    GameSettings CreateDefaultSettings();

    /// <summary>
    /// How often a role whose location is shared (see <see cref="LocationSharing"/>) sends it in an online game,
    /// e.g. live for Hide and Seek, every N minutes for Tag.
    /// </summary>
    TimeSpan GetLocationShareInterval(GameSettings settings);

    string SerializeSettings(GameSettings settings);

    GameSettings DeserializeSettings(string json);

    /// <param name="sessionId">Id of the new session; online games pass the id shared by all devices.</param>
    IGameSession CreateSession(GameSettings settings, string localRoleId, Guid? sessionId = null);

    IGameSession RestoreSession(GameSessionSnapshot snapshot);

    /// <summary>Registers game-specific services (question engines, card decks...).</summary>
    void RegisterServices(IServiceCollection services);
}

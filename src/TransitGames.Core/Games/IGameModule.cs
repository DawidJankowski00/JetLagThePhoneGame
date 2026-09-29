using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Events;
using TransitGames.Core.Notifications;
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

    /// <summary>
    /// Reminders for moments ahead in the session's current state (end of the hiding time, end of a head start...).
    /// Planned again after every change of the session: reminders missing from the new plan are cancelled.
    /// </summary>
    /// <param name="isOnline">Whether the session is an online game (e.g. pings are then sent automatically).</param>
    IReadOnlyList<GameNotification> PlanNotifications(IGameSession session, bool isOnline, DateTimeOffset now);

    /// <summary>
    /// Notification for a timeline event that happened while the app was in the background (in practice a message
    /// from the other team, or something the game did on its own); null to stay silent.
    /// </summary>
    GameNotification? NotificationFor(GameEvent gameEvent, string localRoleId);

    /// <summary>Notification for another player's position that arrived online (e.g. a Tag ping); null to stay silent.</summary>
    GameNotification? NotificationForLocation(string senderRoleId, string localRoleId);

    /// <summary>Result of the session (or its current standing) for the game history.</summary>
    GameResult DescribeResult(IGameSession session, DateTimeOffset now);

    /// <summary>Registers game-specific services (question engines, card decks...).</summary>
    void RegisterServices(IServiceCollection services);
}

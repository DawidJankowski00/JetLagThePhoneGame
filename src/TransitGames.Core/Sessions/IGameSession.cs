using TransitGames.Core.Events;
using TransitGames.Core.Games;

namespace TransitGames.Core.Sessions;

public interface IGameSession
{
    Guid Id { get; }

    string GameId { get; }

    GameSettings Settings { get; }

    /// <summary>The role played by the team holding this device.</summary>
    string LocalRoleId { get; }

    SessionStatus Status { get; }

    DateTimeOffset CreatedAt { get; }

    /// <summary>Raised after any state mutation; the session manager persists the snapshot in response.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when the session produces a timeline entry (question asked, player caught...).</summary>
    event EventHandler<GameEvent>? EventRaised;

    void Start(DateTimeOffset now);

    void Finish(DateTimeOffset now);

    GameSessionSnapshot CreateSnapshot(DateTimeOffset now);
}

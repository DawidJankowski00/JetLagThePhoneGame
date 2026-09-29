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

    /// <summary>How many times the roles changed in this game (see <see cref="Sync.RoleChange"/>).</summary>
    int RolesVersion { get; }

    SessionStatus Status { get; }

    DateTimeOffset CreatedAt { get; }

    /// <summary>Raised after any state mutation; the session manager persists the snapshot in response.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when the session produces a timeline entry (question asked, player caught...).</summary>
    event EventHandler<GameEvent>? EventRaised;

    void Start(DateTimeOffset now);

    /// <summary>For a device joining a game whose roles already changed: the version its chosen role belongs to.</summary>
    void InitializeRolesVersion(int version);

    /// <summary>Applies <paramref name="change"/> to the local role if it was made for the current version.</summary>
    /// <returns>True when the roles changed.</returns>
    bool ApplyRoleChange(Sync.RoleChange change, DateTimeOffset now);

    void Finish(DateTimeOffset now);

    GameSessionSnapshot CreateSnapshot(DateTimeOffset now);
}

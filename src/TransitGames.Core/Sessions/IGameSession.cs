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

    /// <summary>Online: this device's id in the game (see <see cref="Sync.SyncMessage.SenderDeviceId"/>); null offline.</summary>
    Guid? LocalDeviceId { get; }

    /// <summary>This device created the game (offline games: always).</summary>
    bool IsHost { get; }

    SessionStatus Status { get; }

    DateTimeOffset CreatedAt { get; }

    /// <summary>When the game was finished; null while it is not.</summary>
    DateTimeOffset? FinishedAt { get; }

    /// <summary>Raised after any state mutation; the session manager persists the snapshot in response.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when the session produces a timeline entry (question asked, player caught...).</summary>
    event EventHandler<GameEvent>? EventRaised;

    void Start(DateTimeOffset now);

    /// <summary>For a device joining a game whose roles already changed: the version its chosen role belongs to.</summary>
    void InitializeRolesVersion(int version);

    /// <summary>Online game: which device this is and whether it created the game. Only before the game starts.</summary>
    void InitializeDevice(Guid deviceId, bool isHost);

    /// <summary>Applies <paramref name="change"/> to the local role if it was made for the current version.</summary>
    /// <returns>True when the roles changed.</returns>
    bool ApplyRoleChange(Sync.RoleChange change, DateTimeOffset now);

    void Finish(DateTimeOffset now);

    GameSessionSnapshot CreateSnapshot(DateTimeOffset now);
}

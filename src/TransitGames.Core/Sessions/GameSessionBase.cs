using System.Text.Json;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Resources;
using TransitGames.Core.Sync;

namespace TransitGames.Core.Sessions;

/// <summary>
/// Base class for sessions whose whole mutable state is a single JSON-serializable object.
/// Mutations must go through <see cref="Mutate"/> so the state is persisted.
/// </summary>
public abstract class GameSessionBase<TSettings, TState> : IGameSession
    where TSettings : GameSettings
    where TState : class, new()
{
    protected GameSessionBase(Guid id, string gameId, TSettings settings, string localRoleId)
    {
        Id = id;
        GameId = gameId;
        Settings = settings;
        LocalRoleId = localRoleId;
        Status = SessionStatus.Setup;
        State = new TState();
    }

    protected GameSessionBase(GameSessionSnapshot snapshot, TSettings settings)
    {
        Id = snapshot.SessionId;
        GameId = snapshot.GameId;
        Settings = settings;
        LocalRoleId = snapshot.LocalRoleId;
        RolesVersion = snapshot.RolesVersion;
        LocalDeviceId = snapshot.LocalDeviceId;
        IsHost = snapshot.IsHost;
        Status = snapshot.Status;
        CreatedAt = snapshot.CreatedAt;
        // Games finished before the time was stored: their last save is when they ended.
        FinishedAt = snapshot.FinishedAt ?? (snapshot.Status == SessionStatus.Finished ? snapshot.UpdatedAt : null);
        State = JsonSerializer.Deserialize<TState>(snapshot.StateJson, GameJson.Options) ?? new TState();
    }

    public Guid Id { get; }
    public string GameId { get; }
    public TSettings Settings { get; }
    GameSettings IGameSession.Settings => Settings;
    public string LocalRoleId { get; private set; }
    public int RolesVersion { get; private set; }
    public Guid? LocalDeviceId { get; private set; }
    public bool IsHost { get; private set; } = true;
    public SessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    protected TState State { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<GameEvent>? EventRaised;

    public void Start(DateTimeOffset now)
    {
        if (Status != SessionStatus.Setup)
        {
            return;
        }

        CreatedAt = CreatedAt == default ? Settings.StartedAt ?? now : CreatedAt;
        Status = SessionStatus.Running;
        OnStarted(now);
        Raise(GameEvent.Create(Id, now, GameEventTypes.SessionStarted, CoreStrings.SessionStarted));
        NotifyChanged();
    }

    public void InitializeRolesVersion(int version)
    {
        if (Status != SessionStatus.Setup)
        {
            throw new InvalidOperationException("The roles version can only be set before the game starts.");
        }

        RolesVersion = version;
    }

    public void InitializeDevice(Guid deviceId, bool isHost)
    {
        if (Status != SessionStatus.Setup)
        {
            throw new InvalidOperationException("The device can only be set before the game starts.");
        }

        LocalDeviceId = deviceId;
        IsHost = isHost;
    }

    public bool ApplyRoleChange(RoleChange change, DateTimeOffset now)
    {
        if (change.FromVersion != RolesVersion)
        {
            return false;
        }

        var previous = LocalRoleId;
        LocalRoleId = change.Apply(previous, LocalDeviceId);
        RolesVersion++;
        OnRolesChanged(previous, now);
        NotifyChanged();
        return true;
    }

    public void Finish(DateTimeOffset now)
    {
        if (Status == SessionStatus.Finished)
        {
            return;
        }

        Status = SessionStatus.Finished;
        FinishedAt = now;
        OnFinished(now);
        Raise(GameEvent.Create(Id, now, GameEventTypes.SessionFinished, CoreStrings.SessionFinished));
        NotifyChanged();
    }

    public GameSessionSnapshot CreateSnapshot(DateTimeOffset now) => new()
    {
        SessionId = Id,
        GameId = GameId,
        LocalRoleId = LocalRoleId,
        RolesVersion = RolesVersion,
        LocalDeviceId = LocalDeviceId,
        IsHost = IsHost,
        Status = Status,
        CreatedAt = CreatedAt == default ? now : CreatedAt,
        UpdatedAt = now,
        FinishedAt = FinishedAt,
        SettingsJson = JsonSerializer.Serialize(Settings, Settings.GetType(), GameJson.Options),
        StateJson = JsonSerializer.Serialize(State, GameJson.Options),
    };

    protected virtual void OnStarted(DateTimeOffset now)
    {
    }

    protected virtual void OnFinished(DateTimeOffset now)
    {
    }

    /// <summary>Called after <see cref="ApplyRoleChange"/> changed the local role (from <paramref name="previousRoleId"/>).</summary>
    protected virtual void OnRolesChanged(string previousRoleId, DateTimeOffset now)
    {
    }

    /// <summary>Applies a change to the state and notifies listeners so it gets persisted.</summary>
    protected void Mutate(Action<TState> change)
    {
        change(State);
        NotifyChanged();
    }

    protected void NotifyChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    protected void Raise(GameEvent gameEvent) => EventRaised?.Invoke(this, gameEvent);
}

using System.Text.Json;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Resources;

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
        Status = snapshot.Status;
        CreatedAt = snapshot.CreatedAt;
        State = JsonSerializer.Deserialize<TState>(snapshot.StateJson, GameJson.Options) ?? new TState();
    }

    public Guid Id { get; }
    public string GameId { get; }
    public TSettings Settings { get; }
    GameSettings IGameSession.Settings => Settings;
    public string LocalRoleId { get; protected set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected TState State { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<GameEvent>? EventRaised;

    public void Start(DateTimeOffset now)
    {
        if (Status != SessionStatus.Setup)
        {
            return;
        }

        CreatedAt = CreatedAt == default ? now : CreatedAt;
        Status = SessionStatus.Running;
        OnStarted(now);
        Raise(GameEvent.Create(Id, now, GameEventTypes.SessionStarted, CoreStrings.SessionStarted));
        NotifyChanged();
    }

    public void Finish(DateTimeOffset now)
    {
        if (Status == SessionStatus.Finished)
        {
            return;
        }

        Status = SessionStatus.Finished;
        OnFinished(now);
        Raise(GameEvent.Create(Id, now, GameEventTypes.SessionFinished, CoreStrings.SessionFinished));
        NotifyChanged();
    }

    public GameSessionSnapshot CreateSnapshot(DateTimeOffset now) => new()
    {
        SessionId = Id,
        GameId = GameId,
        LocalRoleId = LocalRoleId,
        Status = Status,
        CreatedAt = CreatedAt == default ? now : CreatedAt,
        UpdatedAt = now,
        SettingsJson = JsonSerializer.Serialize(Settings, Settings.GetType(), GameJson.Options),
        StateJson = JsonSerializer.Serialize(State, GameJson.Options),
    };

    protected virtual void OnStarted(DateTimeOffset now)
    {
    }

    protected virtual void OnFinished(DateTimeOffset now)
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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Timing;

namespace TransitGames.Core.Sessions;

/// <summary>
/// Owns the active session: creates, resumes and finishes it, persists every change
/// and forwards session events to the event log.
/// </summary>
public sealed class GameSessionManager
{
    private readonly GameRegistry _registry;
    private readonly IGameSessionStore _store;
    private readonly IGameEventLog _eventLog;
    private readonly IClock _clock;
    private readonly ILogger<GameSessionManager> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public GameSessionManager(
        GameRegistry registry,
        IGameSessionStore store,
        IGameEventLog eventLog,
        IClock clock,
        ILogger<GameSessionManager>? logger = null)
    {
        _registry = registry;
        _store = store;
        _eventLog = eventLog;
        _clock = clock;
        _logger = logger ?? NullLogger<GameSessionManager>.Instance;
    }

    public IGameSession? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    /// <summary>Raised after a session was finished and saved (current or not, e.g. ended by another device).</summary>
    public event EventHandler<IGameSession>? SessionFinished;

    /// <param name="sessionId">Id shared by all devices of an online game; a new one when null.</param>
    public async Task<IGameSession> StartNewAsync(string gameId, GameSettings settings, string localRoleId, Guid? sessionId = null)
    {
        var module = _registry.Get(gameId);
        var session = module.CreateSession(settings, localRoleId, sessionId);
        Attach(session);
        session.Start(_clock.UtcNow);
        await SaveAsync().ConfigureAwait(false);
        return session;
    }

    /// <summary>Resumes the last unfinished session (e.g. after the OS killed the app).</summary>
    public async Task<IGameSession?> TryResumeAsync()
    {
        if (Current is not null)
        {
            return Current;
        }

        var snapshot = await _store.GetLatestUnfinishedAsync().ConfigureAwait(false);
        if (snapshot is null || !_registry.TryGet(snapshot.GameId, out var module) || module is null)
        {
            return null;
        }

        var session = module.RestoreSession(snapshot);
        Attach(session);
        return session;
    }

    public async Task FinishAsync()
    {
        if (Current is not { } session)
        {
            return;
        }

        session.Finish(_clock.UtcNow);
        await SaveAsync().ConfigureAwait(false);
        Detach();
        SessionFinished?.Invoke(this, session);
    }

    /// <summary>
    /// Applies a change to the session with the given id — the current one, or a saved one that is not on screen
    /// (e.g. a message from the other team arrives while the player is on another tab). Returns false when the
    /// session is unknown. Saved sessions are restored, changed, saved; their events go to the event log.
    /// </summary>
    public async Task<bool> MutateAsync(Guid sessionId, Action<IGameSession> change)
    {
        if (Current is { } current && current.Id == sessionId)
        {
            change(current); // persisted through StateChanged
            return true;
        }

        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await _store.LoadAsync(sessionId).ConfigureAwait(false);
            if (snapshot is null || !_registry.TryGet(snapshot.GameId, out var module) || module is null)
            {
                return false;
            }

            var session = module.RestoreSession(snapshot);
            session.EventRaised += OnEventRaised;
            try
            {
                change(session);
            }
            finally
            {
                session.EventRaised -= OnEventRaised;
            }

            await _store.SaveAsync(session.CreateSnapshot(_clock.UtcNow)).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Finishes the session with the given id: the current one, or one that was left (e.g. the other device ended
    /// an online game while this device was on another screen). Unknown or already finished sessions are ignored.
    /// </summary>
    public async Task FinishAsync(Guid sessionId)
    {
        if (Current?.Id == sessionId)
        {
            await FinishAsync().ConfigureAwait(false);
            return;
        }

        var snapshot = await _store.LoadAsync(sessionId).ConfigureAwait(false);
        if (snapshot is null || snapshot.Status == SessionStatus.Finished || !_registry.TryGet(snapshot.GameId, out var module) || module is null)
        {
            return;
        }

        var session = module.RestoreSession(snapshot);
        session.EventRaised += OnEventRaised;
        session.Finish(_clock.UtcNow);
        session.EventRaised -= OnEventRaised;
        await _store.SaveAsync(session.CreateSnapshot(_clock.UtcNow)).ConfigureAwait(false);
        SessionFinished?.Invoke(this, session);
    }

    /// <summary>Leaves the current session without finishing it (it stays resumable).</summary>
    public void Close() => Detach();

    public async Task SaveAsync()
    {
        if (Current is not { } session)
        {
            return;
        }

        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(session.CreateSnapshot(_clock.UtcNow)).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void Attach(IGameSession session)
    {
        Detach();
        Current = session;
        session.StateChanged += OnStateChanged;
        session.EventRaised += OnEventRaised;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Detach()
    {
        if (Current is { } previous)
        {
            previous.StateChanged -= OnStateChanged;
            previous.EventRaised -= OnEventRaised;
            Current = null;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        try
        {
            await SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist session state");
        }
    }

    private async void OnEventRaised(object? sender, GameEvent gameEvent)
    {
        try
        {
            await _eventLog.AppendAsync(gameEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append game event {Type}", gameEvent.Type);
        }
    }
}

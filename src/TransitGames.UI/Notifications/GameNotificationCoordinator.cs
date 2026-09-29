using Microsoft.Extensions.Logging;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Notifications;
using TransitGames.Core.Sessions;
using TransitGames.Core.Timing;
using TransitGames.Sync;
using TransitGames.UI.Resources;
using TransitGames.UI.Services;
using TransitGames.UI.Settings;

namespace TransitGames.UI.Notifications;

/// <summary>
/// Turns games into system notifications:
/// <list type="bullet">
/// <item>reminders planned by the game (<see cref="IGameModule.PlanNotifications"/>) for the session in play,
/// planned again after every change of it — they fire even when the app is closed;</item>
/// <item>news while the app is in the background: timeline events (<see cref="IGameModule.NotificationFor"/>, in
/// practice messages from the other team), positions arriving online (e.g. Tag pings) and the game being finished
/// on another phone.</item>
/// </list>
/// Nothing is shown for things that happen while the app is on screen — the game shows them itself.
/// </summary>
public sealed class GameNotificationCoordinator
{
    private const string ScheduleKey = "notifications.schedule";

    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;
    private readonly IGameSessionStore _store;
    private readonly OnlineSessionStore _links;
    private readonly OnlineSessionCoordinator _online;
    private readonly INotificationScheduler _scheduler;
    private readonly AppPreferences _appPreferences;
    private readonly IPreferences _preferences;
    private readonly AppLifecycle _lifecycle;
    private readonly IClock _clock;
    private readonly ILogger<GameNotificationCoordinator> _logger;
    private readonly object _gate = new();
    private readonly NotificationSchedule _schedule;

    private IGameSession? _current;
    private bool? _currentIsOnline;
    private bool _permissionAsked;

    public GameNotificationCoordinator(
        GameSessionManager sessions,
        GameRegistry registry,
        IGameSessionStore store,
        IGameEventLog eventLog,
        OnlineSessionStore links,
        OnlineSessionCoordinator online,
        INotificationScheduler scheduler,
        AppPreferences appPreferences,
        IPreferences preferences,
        AppLifecycle lifecycle,
        IClock clock,
        ILogger<GameNotificationCoordinator> logger)
    {
        _sessions = sessions;
        _registry = registry;
        _store = store;
        _links = links;
        _online = online;
        _scheduler = scheduler;
        _appPreferences = appPreferences;
        _preferences = preferences;
        _lifecycle = lifecycle;
        _clock = clock;
        _logger = logger;
        _schedule = NotificationSchedule.Parse(preferences.Get(ScheduleKey, string.Empty));

        // The system drops scheduled reminders when the app is force-stopped, updated or the phone restarts;
        // scheduling them again with the same ids is harmless when they are still there.
        foreach (var (id, entry) in _schedule.Entries.Where(e => e.Value.At > clock.UtcNow))
        {
            _scheduler.Schedule(id, entry.Title, entry.Body, entry.At);
        }

        _sessions.CurrentChanged += OnCurrentChanged;
        _sessions.SessionFinished += OnSessionFinished;
        eventLog.EventAppended += OnEventAppended;
        _online.RemoteLocationReceived += OnRemoteLocation;
        OnCurrentChanged(null, EventArgs.Empty);
    }

    /// <summary>Called when a game screen is shown: asks for the permission once per app run.</summary>
    public async void OnGameScreenShown()
    {
        try
        {
            if (_appPreferences.NotificationsEnabled && !_permissionAsked)
            {
                _permissionAsked = true;
                await _scheduler.RequestPermissionAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Asking for the notification permission failed");
        }
    }

    /// <summary>After the notifications preference changed.</summary>
    public void OnPreferenceChanged()
    {
        if (_appPreferences.NotificationsEnabled)
        {
            Replan();
            return;
        }

        lock (_gate)
        {
            foreach (var id in _schedule.ClearAll())
            {
                _scheduler.Cancel(id);
            }

            Save();
        }
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_current is not null)
            {
                _current.StateChanged -= OnStateChanged;
            }

            _current = _sessions.Current;
            _currentIsOnline = null;
            if (_current is not null)
            {
                _current.StateChanged += OnStateChanged;
            }
        }

        Replan();
    }

    private void OnStateChanged(object? sender, EventArgs e) => Replan();

    private async void Replan()
    {
        try
        {
            var session = _current;
            if (session is null || !_registry.TryGet(session.GameId, out var module) || module is null)
            {
                return;
            }

            var isOnline = _currentIsOnline ??= await _links.GetLinkAsync(session.Id) is not null;
            var plan = _appPreferences.NotificationsEnabled && session.Status == SessionStatus.Running
                ? module.PlanNotifications(session, isOnline, _clock.UtcNow)
                : [];
            lock (_gate)
            {
                if (!ReferenceEquals(session, _current))
                {
                    return; // another game came on screen meanwhile; it plans its own reminders
                }

                var changes = _schedule.Replace(session.Id, plan, _clock.UtcNow);
                foreach (var id in changes.ToCancel)
                {
                    _scheduler.Cancel(id);
                }

                foreach (var (id, entry) in changes.ToSchedule)
                {
                    _scheduler.Schedule(id, entry.Title, entry.Body, entry.At);
                }

                if (changes.ToCancel.Count > 0 || changes.ToSchedule.Count > 0)
                {
                    Save();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Planning notifications failed");
        }
    }

    private void OnSessionFinished(object? sender, IGameSession session)
    {
        lock (_gate)
        {
            foreach (var id in _schedule.Clear(session.Id))
            {
                _scheduler.Cancel(id);
            }

            Save();
        }
    }

    private async void OnEventAppended(object? sender, GameEvent gameEvent)
    {
        try
        {
            if (_lifecycle.IsInForeground || !_appPreferences.NotificationsEnabled)
            {
                return;
            }

            var (module, roleId) = await FindAsync(gameEvent.SessionId);
            if (module is null || roleId is null)
            {
                return;
            }

            var notification = gameEvent.Type == GameEventTypes.SessionFinished
                ? new GameNotification("finished", UiStrings.NotifyGameFinishedTitle, string.Format(UiStrings.NotifyGameFinishedFormat, module.DisplayName))
                : module.NotificationFor(gameEvent, roleId);
            Show(gameEvent.SessionId, notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notifying the event {Type} failed", gameEvent.Type);
        }
    }

    private void OnRemoteLocation(object? sender, RemoteLocationNews news)
    {
        if (_lifecycle.IsInForeground || !_appPreferences.NotificationsEnabled
            || _current is not { } session || session.Id != news.SessionId
            || !_registry.TryGet(session.GameId, out var module) || module is null)
        {
            return;
        }

        Show(session.Id, module.NotificationForLocation(news.SenderRoleId, news.LocalRoleId));
    }

    private void Show(Guid sessionId, GameNotification? notification)
    {
        if (notification is not null)
        {
            _scheduler.Show(NotificationSchedule.Id(sessionId, notification.Key), notification.Title, notification.Body);
        }
    }

    /// <summary>The game and local role of a session — the one in play, or a saved one (e.g. after "leave").</summary>
    private async Task<(IGameModule? Module, string? RoleId)> FindAsync(Guid sessionId)
    {
        string gameId;
        string roleId;
        if (_current is { } current && current.Id == sessionId)
        {
            (gameId, roleId) = (current.GameId, current.LocalRoleId);
        }
        else if (await _store.LoadAsync(sessionId) is { } snapshot)
        {
            (gameId, roleId) = (snapshot.GameId, snapshot.LocalRoleId);
        }
        else
        {
            return (null, null);
        }

        return _registry.TryGet(gameId, out var module) ? (module, roleId) : (null, null);
    }

    private void Save() => _preferences.Set(ScheduleKey, _schedule.Serialize());
}

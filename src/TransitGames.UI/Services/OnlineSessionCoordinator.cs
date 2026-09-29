using System.Text.Json;
using Microsoft.Extensions.Logging;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Sync;
using TransitGames.UI.Resources;

namespace TransitGames.UI.Services;

/// <summary>Last known position of another device, as far as the local role may see it.</summary>
public sealed record RemotePlayer(Guid DeviceId, string Name, string RoleId, GeoPoint Point, DateTimeOffset Timestamp, bool IsTeammate);

/// <summary>What the local role reveals about its position: to whom, how often, and when it last did.</summary>
public sealed record LocationShareInfo(bool IsShared, IReadOnlyList<string> ViewerRoleNames, TimeSpan Interval, DateTimeOffset? LastSharedAt)
{
    public static LocationShareInfo Private { get; } = new(false, [], TimeSpan.Zero, null);
}

/// <summary>
/// Connects the game on screen to its transport: <see cref="SignalRGameSyncService"/> for online games,
/// <see cref="LocalGameSyncService"/> otherwise (games only see the <see cref="GameSyncRouter"/>). For online games it
/// <list type="bullet">
/// <item>shares this device's position — only if the game's <see cref="LocationSharingPolicy"/> lets anybody see
/// the local role, at the game's interval (the server enforces the same policy again);</item>
/// <item>collects the positions of other devices the local role may see;</item>
/// <item>ends the game on every device when one of them finishes it;</item>
/// <item>follows role changes (e.g. runners and chasers swapping): sharing and visible positions switch to the
/// new role at once, and positions seen under the old role are forgotten.</item>
/// </list>
/// Like track recording, the connection stays up after leaving the game screen: the game goes on outside.
/// </summary>
public sealed class OnlineSessionCoordinator
{
    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;
    private readonly GameSyncRouter _router;
    private readonly LocalGameSyncService _local;
    private readonly SignalRGameSyncService _online;
    private readonly OnlineSessionStore _store;
    private readonly ILocationTracker _location;
    private readonly TrackingCoordinator _tracking;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<OnlineSessionCoordinator> _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, RemotePlayer> _players = [];

    private IGameSession? _session;
    private IGameModule? _module;
    private OnlineLink? _link;
    private LocationShareInfo _sharing = LocationShareInfo.Private;

    public OnlineSessionCoordinator(
        GameSessionManager sessions,
        GameRegistry registry,
        GameSyncRouter router,
        LocalGameSyncService local,
        SignalRGameSyncService online,
        OnlineSessionStore store,
        ILocationTracker location,
        TrackingCoordinator tracking,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<OnlineSessionCoordinator> logger)
    {
        _sessions = sessions;
        _registry = registry;
        _router = router;
        _local = local;
        _online = online;
        _store = store;
        _location = location;
        _tracking = tracking;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;

        _sessions.SessionFinished += OnSessionFinished;
        _router.StateChanged += (_, _) => RaiseChanged();
        _router.PeersChanged += (_, _) => RaiseChanged();
        _router.MessageReceived += OnMessageReceived;
        _location.LocationChanged += OnLocationChanged;
    }

    /// <summary>Raised (on any thread) when connection, peers, positions or sharing info change.</summary>
    public event EventHandler? Changed;

    /// <summary>Online link of the active game; null for offline games.</summary>
    public OnlineLink? Link => _link;

    public Guid? SessionId => _session?.Id;

    public SyncConnectionState State => _router.State;

    public IReadOnlyList<SyncPeer> Peers => _router.Peers;

    /// <summary>True when the server no longer knows this game (e.g. deleted after the retention period).</summary>
    public bool IsRejected => _link is not null && _online.IsRejected;

    public LocationShareInfo Sharing => _sharing;

    public IReadOnlyList<RemotePlayer> Players
    {
        get
        {
            lock (_gate)
            {
                return [.. _players.Values.OrderBy(p => p.Name)];
            }
        }
    }

    /// <summary>The local role now (online it follows role changes even when the game is not on screen).</summary>
    private string? CurrentRole => _link?.RoleId ?? _session?.LocalRoleId;

    public string RoleName(string roleId) =>
        _module?.Roles.FirstOrDefault(r => r.Id == roleId)?.DisplayName ?? roleId;

    /// <summary>Called when a game screen is shown; switches the transport to that game.</summary>
    public async void OnGameScreenShown(IGameSession session)
    {
        try
        {
            await ActivateAsync(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connecting the game failed");
        }
    }

    public async Task ActivateAsync(IGameSession session)
    {
        await _switchLock.WaitAsync();
        try
        {
            if (_session?.Id == session.Id)
            {
                return;
            }

            await DeactivateCoreAsync();
            _session = session;
            _module = _registry.TryGet(session.GameId, out var module) ? module : null;
            _link = session.Status == SessionStatus.Finished ? null : await _store.GetLinkAsync(session.Id);

            if (_link is null || _module is null)
            {
                _router.Use(_local);
                await _local.ConnectAsync(session.Id, session.LocalRoleId);
                RaiseChanged();
                return;
            }

            _sharing = SharingFor(_module, session.Settings, _link.RoleId);
            _router.Use(_online);
            await _online.ConnectAsync(session.Id, session.LocalRoleId);
            RaiseChanged();
        }
        finally
        {
            _switchLock.Release();
        }

        // Sharing needs a continuous position, which comes from the recording service.
        if (_sharing.IsShared)
        {
            await _tracking.EnsureRecordingAsync(session);
        }
    }

    private async Task DeactivateCoreAsync()
    {
        if (_link is not null)
        {
            await _online.DisconnectAsync();
        }

        _router.Use(_local);
        _session = null;
        _module = null;
        _link = null;
        _sharing = LocationShareInfo.Private;
        lock (_gate)
        {
            _players.Clear();
        }
    }

    private LocationShareInfo SharingFor(IGameModule module, GameSettings settings, string roleId)
    {
        var policy = module.LocationSharing;
        return policy.IsShared(roleId)
            ? new LocationShareInfo(
                true,
                policy.ViewersOf(roleId).Select(RoleName).ToList(),
                module.GetLocationShareInterval(settings),
                null)
            : LocationShareInfo.Private;
    }

    private async void OnLocationChanged(object? sender, LocationSample sample)
    {
        try
        {
            if (_link is null || _session is null || !_sharing.IsShared)
            {
                return;
            }

            // 10% tolerance so a fix arriving slightly early does not skip a whole interval.
            if (_sharing.LastSharedAt is { } last && sample.Timestamp - last < _sharing.Interval * 0.9)
            {
                return;
            }

            _sharing = _sharing with { LastSharedAt = sample.Timestamp };
            await _router.PublishAsync(new SyncMessage
            {
                Id = Guid.NewGuid(),
                SessionId = _session.Id,
                Type = SyncMessageTypes.Location,
                SenderRoleId = CurrentRole ?? _session.LocalRoleId,
                Timestamp = sample.Timestamp,
                Audience = SyncAudience.OwnTeam, // the server adds the viewers allowed by the policy
                PayloadJson = JsonSerializer.Serialize(new LocationPayload(sample.Point, sample.Timestamp, sample.AccuracyMeters), GameJson.Options),
            });
            RaiseChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sharing the location failed");
        }
    }

    private void OnMessageReceived(object? sender, SyncMessage message)
    {
        var session = _session;
        var link = _link;
        if (session is null || link is null || message.SessionId != session.Id)
        {
            return;
        }

        if (message.RoleChange is not null)
        {
            OnRolesChanged(session);
        }

        if (message.SenderDeviceId == link.DeviceId)
        {
            return;
        }

        switch (message.Type)
        {
            case SyncMessageTypes.Location:
                OnRemoteLocation(session, message);
                break;
            case SyncMessageTypes.SessionFinished:
                OnRemoteFinish(session.Id, message.SenderName);
                break;
        }
    }

    /// <summary>The transport has already moved the link to the new role; sharing and positions follow it.</summary>
    private async void OnRolesChanged(IGameSession session)
    {
        try
        {
            if (_online.Link is not { } link || link.SessionId != session.Id || _module is not { } module
                || link.RoleId == _link?.RoleId && link.RolesVersion == _link.RolesVersion)
            {
                return;
            }

            _link = link;
            _sharing = SharingFor(module, session.Settings, link.RoleId);
            lock (_gate)
            {
                _players.Clear();
            }

            RaiseChanged();
            if (_sharing.IsShared)
            {
                await _tracking.EnsureRecordingAsync(session);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Following the role change failed");
        }
    }

    private void OnRemoteLocation(IGameSession session, SyncMessage message)
    {
        // The server already filters; checking again keeps the rule local and visible.
        if (_module is not { } module || message.SenderDeviceId is not { } deviceId || CurrentRole is not { } role
            || !module.LocationSharing.CanSee(role, message.SenderRoleId))
        {
            return;
        }

        LocationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LocationPayload>(message.PayloadJson, GameJson.Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid location payload");
            return;
        }

        if (payload is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_players.TryGetValue(deviceId, out var known) && known.Timestamp >= payload.Timestamp)
            {
                return;
            }

            _players[deviceId] = new RemotePlayer(
                deviceId,
                message.SenderName ?? RoleName(message.SenderRoleId),
                message.SenderRoleId,
                payload.Point,
                payload.Timestamp,
                message.SenderRoleId == role);
        }

        RaiseChanged();
    }

    private void OnRemoteFinish(Guid sessionId, string? finishedBy) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var wasOnScreen = _sessions.Current?.Id == sessionId;
                await _sessions.FinishAsync(sessionId);
                await _dialogs.AlertAsync(UiStrings.OnlineGameFinishedTitle, string.Format(UiStrings.OnlineGameFinishedBy, finishedBy ?? "?"));
                if (wasOnScreen)
                {
                    await _navigation.GoHomeAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finishing the game after a remote finish failed");
            }
        });

    private async void OnSessionFinished(object? sender, IGameSession session)
    {
        try
        {
            await _switchLock.WaitAsync();
            try
            {
                if (_session?.Id != session.Id)
                {
                    return;
                }

                if (_link is not null)
                {
                    // Tell the others (queued if offline; harmless if they finished it themselves).
                    await _router.PublishAsync(new SyncMessage
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        Type = SyncMessageTypes.SessionFinished,
                        SenderRoleId = session.LocalRoleId,
                        Timestamp = DateTimeOffset.UtcNow,
                        Audience = SyncAudience.Everyone,
                        PayloadJson = "{}",
                    });
                }

                await DeactivateCoreAsync();
            }
            finally
            {
                _switchLock.Release();
            }

            RaiseChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Closing the online game failed");
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

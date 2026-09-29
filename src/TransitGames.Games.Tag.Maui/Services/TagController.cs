using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Core.Timing;
using TransitGames.Games.Tag.Sync;
using TransitGames.Games.Tag.Targets;
using TransitGames.Geo.Areas;

namespace TransitGames.Games.Tag.Maui.Services;

/// <summary>
/// Shared by the Tag screens: the current session, its area and every action, published through
/// <see cref="IGameSyncService"/> (the state changes when the message comes back — at once for own messages).
/// Also the runners' geofence: with <see cref="TagSettings.AutoCapture"/>, every GPS fix captures targets in range
/// (targets with a challenge are only announced: the runners confirm the challenge themselves) and ends the round at
/// the final target.
/// </summary>
public sealed class TagController
{
    private readonly GameSessionManager _sessions;
    private readonly IGameSyncService _sync;
    private readonly ILocationTracker _location;
    private readonly IClock _clock;
    private readonly ILogger<TagController> _logger;
    private readonly HashSet<string> _announced = [];
    private readonly HashSet<string> _pending = [];
    private (Guid SessionId, GameArea Area)? _area;

    public TagController(
        GameSessionManager sessions,
        IGameSyncService sync,
        ILocationTracker location,
        IClock clock,
        ILogger<TagController> logger)
    {
        _sessions = sessions;
        _sync = sync;
        _location = location;
        _clock = clock;
        _logger = logger;
        _location.LocationChanged += OnLocationChanged;
    }

    /// <summary>Raised (on any thread) when the runners reach a target with a challenge, once per target and round.</summary>
    public event EventHandler<TagTarget>? ChallengeInReach;

    public TagSession? Session => _sessions.Current as TagSession;

    public DateTimeOffset Now => _clock.UtcNow;

    /// <summary>Online game: pings and captures travel by themselves; offline the teams tell each other.</summary>
    public bool IsOnline => _sync.IsOnline;

    public GeoPoint? LastLocation => _location.LastLocation?.Point;

    public GameArea? Area
    {
        get
        {
            if (Session is not { Settings.Area: { } definition } session)
            {
                return null;
            }

            if (_area?.SessionId != session.Id)
            {
                _area = (session.Id, GameArea.FromDefinition(definition));
            }

            return _area.Value.Area;
        }
    }

    /// <summary>This device's position: the running tracker's last fix, a fresh fix, or null.</summary>
    public async Task<GeoPoint?> CurrentLocationAsync()
    {
        if (_location.LastLocation is { } last && Now - last.Timestamp < TimeSpan.FromMinutes(2))
        {
            return last.Point;
        }

        try
        {
            return (await _location.GetCurrentLocationAsync())?.Point ?? _location.LastLocation?.Point;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            _logger.LogInformation(ex, "No location available");
            return _location.LastLocation?.Point;
        }
    }

    public Task CaptureAsync(string targetId) =>
        Session is { } session && session.CanCapture(targetId, Now)
            ? Publish(s => TagMessages.CaptureTarget(s, new CapturePayload(s.CurrentRound.Index, targetId, Now)))
            : Task.CompletedTask;

    /// <summary>Ends the round (caught, time up, final reached). Only the runners attach their position.</summary>
    public async Task EndRoundAsync(RoundEndReason reason)
    {
        if (Session is not { } session)
        {
            return;
        }

        var point = session.IsRunner && reason != RoundEndReason.TimeUp ? await CurrentLocationAsync() : null;
        await Publish(s => TagMessages.EndRound(s, s.CreateRoundEnd(reason, Now, point)));
    }

    public Task SpendAsync(int amount, string note) =>
        amount > 0
            ? Publish(s => TagMessages.SpendCoins(s, new SpendPayload(s.CurrentRound.Index, Guid.NewGuid(), amount, note, Now)))
            : Task.CompletedTask;

    private async void OnLocationChanged(object? sender, LocationSample sample)
    {
        try
        {
            await CheckGeofenceAsync(sample.Point);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checking targets in range failed");
        }
    }

    private async Task CheckGeofenceAsync(GeoPoint point)
    {
        if (Session is not { IsRunner: true, Settings.AutoCapture: true } session
            || session.Phase(Now) is not (TagPhase.HeadStart or TagPhase.Chase))
        {
            return;
        }

        var round = $"{session.Id}/{session.CurrentRound.Index}";
        if (session.IsAtFinal(point))
        {
            if (_pending.Add(round + "/final"))
            {
                _logger.LogInformation("Final target reached");
                await EndRoundAsync(RoundEndReason.ReachedFinal);
            }

            return;
        }

        foreach (var target in session.TargetsInReach(point))
        {
            var key = $"{round}/{target.Id}";
            if (target.Challenge is not null)
            {
                if (_announced.Add(key))
                {
                    ChallengeInReach?.Invoke(this, target);
                }
            }
            else if (_pending.Add(key))
            {
                _logger.LogInformation("Target {Target} captured automatically", target.Id);
                await CaptureAsync(target.Id);
            }
        }
    }

    private Task Publish(Func<TagSession, SyncMessage> build) =>
        Session is { } session ? _sync.PublishAsync(build(session)) : Task.CompletedTask;
}

using Microsoft.Extensions.Logging;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Core.Timing;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Sync;
using TransitGames.Geo.Areas;
using TransitGames.Tracking;
using TransitGames.Tracking.Storage;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.Services;

/// <summary>Why the host could not start the game.</summary>
public enum StartProblem
{
    None,
    TooFewPlayers,
    MissingDestinations,

    /// <summary>The game's stations do not allow drawing fair destinations (distance and spacing rules).</summary>
    DrawFailed,
}

/// <summary>The runner's position as the chasers last saw it (a ping).</summary>
public sealed record RunnerSighting(GeoPoint Point, DateTimeOffset At);

/// <summary>
/// Shared by the Tag screens: the current session, its area and every action, published through
/// <see cref="IGameSyncService"/> (the state changes when the message comes back — at once for own messages).
/// Also the runner's geofence: reaching the own destination is announced once per turn (the runner confirms it).
/// </summary>
public sealed class TagController
{
    private readonly GameSessionManager _sessions;
    private readonly IGameSyncService _sync;
    private readonly ILocationTracker _location;
    private readonly ITrackStore _tracks;
    private readonly OnlineSessionCoordinator _online;
    private readonly IClock _clock;
    private readonly ILogger<TagController> _logger;
    private readonly HashSet<string> _announced = [];
    private (Guid SessionId, GameArea Area)? _area;
    private string? _settling;

    public TagController(
        GameSessionManager sessions,
        IGameSyncService sync,
        ILocationTracker location,
        ITrackStore tracks,
        OnlineSessionCoordinator online,
        IClock clock,
        ILogger<TagController> logger)
    {
        _sessions = sessions;
        _sync = sync;
        _location = location;
        _tracks = tracks;
        _online = online;
        _clock = clock;
        _logger = logger;
        _location.LocationChanged += OnLocationChanged;
    }

    /// <summary>Raised (on any thread) when the runner enters the radius of their destination, once per turn.</summary>
    public event EventHandler? DestinationInReach;

    public TagSession? Session => _sessions.Current as TagSession;

    public DateTimeOffset Now => _clock.UtcNow;

    public GeoPoint? LastLocation => _location.LastLocation?.Point;

    /// <summary>Players connected to the online game (the roster before the start).</summary>
    public IReadOnlyList<SyncPeer> Peers => _sync.Peers;

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

    /// <summary>The newest ping of the runner this device received (chasers only; the runner's own device has GPS).</summary>
    public RunnerSighting? LastRunnerSighting =>
        _online.Players.Where(p => p.RoleId == TagRoles.Runner).MaxBy(p => p.Timestamp) is { } ping
            ? new RunnerSighting(ping.Point, ping.Timestamp)
            : null;

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

    // ---- Setup -----------------------------------------------------------------------------------------------

    public Task SetDestinationAsync(Guid playerId, GeoPoint point, string name) =>
        Session is { } session && session.CanSetDestination(playerId)
            ? Publish(s => TagMessages.SetDestination(s, new DestinationPayload(playerId, point, name), Now))
            : Task.CompletedTask;

    /// <summary>The host starts the game with everybody who joined; the first runner is drawn here.</summary>
    public async Task<StartProblem> StartGameAsync(GeoPoint startPoint)
    {
        if (Session is not { IsHost: true, IsStarted: false } session)
        {
            return StartProblem.None;
        }

        var roster = Peers.Select(p => (p.DeviceId, p.Name)).ToList();
        if (roster.Count < 2)
        {
            return StartProblem.TooFewPlayers;
        }

        var start = session.CreateStart(Now, startPoint, roster, Random.Shared);
        if (start is null)
        {
            return session.Settings.DestinationMode == Destinations.DestinationMode.RandomStations
                ? StartProblem.DrawFailed
                : StartProblem.MissingDestinations;
        }

        await Publish(s => TagMessages.StartGame(s, start));
        return StartProblem.None;
    }

    // ---- Runner ----------------------------------------------------------------------------------------------

    public Task StartRideAsync(TransportMode mode, int? manualFare = null) =>
        Session is { } session && session.QuoteRide(mode, Now, manualFare).IsAllowed && session.OpenRide(session.LocalPlayerId) is null
            ? Publish(s => TagMessages.StartRide(s, s.CreateRideStart(mode, Now, manualFare)))
            : Task.CompletedTask;

    /// <summary>Ends the open ride; per-km fares are settled from the GPS track recorded during it.</summary>
    /// <returns>The measured distance, or null when there was no track (the minimum fare is charged).</returns>
    public async Task<double?> EndRideAsync()
    {
        if (Session is not { } session || session.OpenRide(session.LocalPlayerId) is not { } ride)
        {
            return null;
        }

        var now = Now;
        double? distance = null;
        try
        {
            if (await _tracks.FindOpenAsync(session.Id) is { } track)
            {
                var measured = GeoMath.DistanceBetween(await _tracks.GetPointsAsync(track.Id), ride.StartedAt, now);
                distance = measured > 0 ? measured : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the track of the ride failed");
        }

        await Publish(s => TagMessages.EndRide(s, s.CreateRideEnd(ride, distance ?? 0, now)));
        return distance;
    }

    public Task DrawCardAsync() =>
        Session is { } session && session.DrawBlockReason(Now) == DrawBlock.None
            ? Publish(s => TagMessages.DrawCard(s, s.CreateDraw(Now, Random.Shared)))
            : Task.CompletedTask;

    public Task ResolveChallengeAsync(DrawnCard drawn, bool completed) =>
        Session is { } session && (!completed || session.CanCompleteChallenge(drawn, Now))
            ? Publish(s => TagMessages.ResolveCard(s, new CardResolvePayload(drawn.Id, completed, Now)))
            : Task.CompletedTask;

    /// <summary>
    /// Confirms a catch: the runner with their GPS position, the host (forced) with the last ping, or the start of the
    /// turn when there is none. The next turn starts there.
    /// </summary>
    public async Task ConfirmCatchAsync(Guid? catcherId)
    {
        if (Session is not { } session || !session.CanConfirmCatch(Now))
        {
            return;
        }

        var point = session.IsLocalRunner ? await CurrentLocationAsync() : LastRunnerSighting?.Point;
        point ??= session.CurrentTurn!.StartPoint;
        var runner = session.CurrentTurn!.RunnerId;
        await Publish(s => TagMessages.ConfirmCatch(s, s.CreateCatch(Now, catcherId, point.Value, Random.Shared), runner));
    }

    public async Task ReportArrivalAsync()
    {
        if (Session is { } session && await CurrentLocationAsync() is { } point && session.CanReportArrival(point, Now))
        {
            await Publish(s => TagMessages.ReportArrival(s, s.CreateArrival(Now)));
        }
    }

    /// <summary>
    /// At the time limit the runner's phone settles the game with its GPS position by itself (once); the host can do
    /// it with the last ping or a point on the map when that phone does not answer.
    /// </summary>
    public async Task SettleTimeUpAsync(GeoPoint? point = null, EndPointSource source = EndPointSource.Gps)
    {
        if (Session is not { } session || !session.CanSettleTimeUp(Now))
        {
            return;
        }

        if (source == EndPointSource.Gps)
        {
            var key = session.Id + "/end";
            if (!session.IsLocalRunner || _settling == key)
            {
                return;
            }

            _settling = key;
            point = await CurrentLocationAsync();
        }

        if (point is { } p)
        {
            await Publish(s => TagMessages.EndGame(s, new EndPayload(Now, p, source)));
        }
        else
        {
            _settling = null; // no GPS fix yet: try again on the next tick
        }
    }

    // ---- Chasers and host ------------------------------------------------------------------------------------

    public Task RequestCatchAsync() =>
        Session is { } session && session.CanRequestCatch(Now)
            ? Publish(s => TagMessages.RequestCatch(s, new CatchRequestPayload(s.CurrentTurn!.Index, Guid.NewGuid(), s.LocalPlayerId, Now)))
            : Task.CompletedTask;

    public Task AdjustCoinsAsync(Guid playerId, int amount, string reason) =>
        Session is { IsHost: true, IsStarted: true } && amount != 0
            ? Publish(s => TagMessages.AdjustCoins(s, new AdjustPayload(Guid.NewGuid(), playerId, amount, reason, Now)))
            : Task.CompletedTask;

    private void OnLocationChanged(object? sender, LocationSample sample)
    {
        try
        {
            if (Session is not { } session)
            {
                return;
            }

            session.UpdateOwnPosition(sample.Point);
            if (session.CanReportArrival(sample.Point, Now)
                && _announced.Add($"{session.Id}/{session.CurrentTurn!.Index}/arrival"))
            {
                DestinationInReach?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checking the destination failed");
        }
    }

    private Task Publish(Func<TagSession, SyncMessage> build) =>
        Session is { } session ? _sync.PublishAsync(build(session)) : Task.CompletedTask;
}

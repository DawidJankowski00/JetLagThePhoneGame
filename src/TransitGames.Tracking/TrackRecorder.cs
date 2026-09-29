using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransitGames.Core.Location;
using TransitGames.Core.Timing;
using TransitGames.Tracking.Storage;

namespace TransitGames.Tracking;

/// <summary>
/// Records the fixes of <see cref="ILocationTracker"/> into the active track. A game session has one track per
/// device: starting it again (app restarted, game resumed) continues the open track in a new segment.
/// </summary>
public sealed class TrackRecorder
{
    private readonly ILocationTracker _tracker;
    private readonly ITrackStore _store;
    private readonly IClock _clock;
    private readonly ILogger<TrackRecorder> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _appendLock = new(1, 1);
    private TrackPointFilter? _filter;
    private int _segment;

    public TrackRecorder(ILocationTracker tracker, ITrackStore store, IClock clock, ILogger<TrackRecorder>? logger = null)
    {
        _tracker = tracker;
        _store = store;
        _clock = clock;
        _logger = logger ?? NullLogger<TrackRecorder>.Instance;
    }

    /// <summary>The track being recorded (with up-to-date statistics), or null when not recording.</summary>
    public TrackInfo? ActiveTrack { get; private set; }

    public bool IsRecording => ActiveTrack is not null;

    /// <summary>Recording started or stopped, or the active track's statistics changed.</summary>
    public event EventHandler? StateChanged;

    public event EventHandler<TrackPoint>? PointRecorded;

    /// <summary>
    /// Starts recording. For a game session the open track of that session is continued; otherwise a new track is
    /// created. Throws whatever the platform tracker throws (e.g. missing location permission).
    /// </summary>
    public async Task<TrackInfo> StartAsync(TrackStartRequest request, TrackingOptions options, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ActiveTrack is { } active)
            {
                if (request.SessionId is not null && active.SessionId == request.SessionId)
                {
                    return active;
                }

                await StopCoreAsync(close: active.SessionId is null).ConfigureAwait(false);
            }

            var existing = request.SessionId is { } sessionId
                ? await _store.FindOpenAsync(sessionId, cancellationToken).ConfigureAwait(false)
                : null;
            var track = existing ?? await _store.CreateAsync(
                new TrackInfo
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name,
                    DeviceName = request.DeviceName,
                    SessionId = request.SessionId,
                    GameId = request.GameId,
                    RoleId = request.RoleId,
                    StartedAt = _clock.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);

            _segment = existing is null ? 0 : await _store.GetLastSegmentAsync(track.Id, cancellationToken).ConfigureAwait(false) + 1;
            _filter = new TrackPointFilter(options);
            ActiveTrack = track;
            _tracker.LocationChanged += OnLocationChanged;
            try
            {
                await _tracker.StartAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _tracker.LocationChanged -= OnLocationChanged;
                ActiveTrack = null;
                if (existing is null)
                {
                    await _store.DeleteAsync(track.Id, CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }

            _logger.LogInformation("Recording track {TrackId} from segment {Segment}", track.Id, _segment);
        }
        finally
        {
            _stateLock.Release();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return ActiveTrack!;
    }

    /// <summary>Stops recording but keeps the track open, so the same game can continue it later.</summary>
    public Task PauseAsync() => StopAsync(close: false);

    /// <summary>Stops recording and closes the track.</summary>
    public Task StopAsync() => StopAsync(close: true);

    private async Task StopAsync(bool close)
    {
        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ActiveTrack is null)
            {
                return;
            }

            await StopCoreAsync(close).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopCoreAsync(bool close)
    {
        var track = ActiveTrack!;
        _tracker.LocationChanged -= OnLocationChanged;
        ActiveTrack = null;
        _filter = null;
        try
        {
            await _tracker.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            // Wait for a point that is being written, so closing happens after it.
            await _appendLock.WaitAsync().ConfigureAwait(false);
            _appendLock.Release();
            if (close)
            {
                await _store.CloseAsync(track.Id, _clock.UtcNow).ConfigureAwait(false);
            }
        }
    }

    private async void OnLocationChanged(object? sender, LocationSample sample)
    {
        try
        {
            await RecordAsync(sample).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store a track point");
        }
    }

    private async Task RecordAsync(LocationSample sample)
    {
        await _appendLock.WaitAsync().ConfigureAwait(false);
        TrackPoint point;
        try
        {
            if (ActiveTrack is not { } track || _filter is not { } filter)
            {
                return;
            }

            var decision = filter.Evaluate(sample);
            if (decision == TrackPointDecision.Reject)
            {
                return;
            }

            if (decision == TrackPointDecision.AcceptNewSegment)
            {
                _segment++;
            }

            point = TrackPoint.From(sample, _segment);
            await _store.AppendAsync(track.Id, point).ConfigureAwait(false);
            var updated = await _store.GetAsync(track.Id).ConfigureAwait(false);
            if (updated is not null && ActiveTrack?.Id == track.Id)
            {
                ActiveTrack = updated;
            }
        }
        finally
        {
            _appendLock.Release();
        }

        PointRecorded?.Invoke(this, point);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

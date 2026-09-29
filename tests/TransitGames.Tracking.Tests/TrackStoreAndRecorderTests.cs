using TransitGames.Core.Location;
using TransitGames.Core.Persistence;
using TransitGames.Tracking.Storage;
using static TransitGames.Tracking.Tests.TestData;

namespace TransitGames.Tracking.Tests;

public sealed class TrackStoreAndRecorderTests : IAsyncLifetime
{
    private static readonly TrackingOptions Options = TrackingOptions.Default with { Interval = TimeSpan.FromSeconds(5), MinDistanceMeters = 10 };

    private readonly TempDatabase _temp = new();
    private readonly FakeClock _clock = new();
    private readonly FakeLocationTracker _tracker = new();
    private AppDatabase _database = null!;
    private SqliteTrackStore _store = null!;
    private TrackRecorder _recorder = null!;

    public Task InitializeAsync()
    {
        _database = new AppDatabase(new DatabaseOptions(_temp.Path));
        _store = new SqliteTrackStore(_database);
        _recorder = new TrackRecorder(_tracker, _store, _clock);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task Store_AccumulatesDistanceWithinSegmentsOnly()
    {
        var track = await _store.CreateAsync(Track());

        await _store.AppendAsync(track.Id, Point(0, 0, 0));
        await _store.AppendAsync(track.Id, Point(0, 100, 10));
        await _store.AppendAsync(track.Id, Point(1, 5000, 400)); // after a gap: not counted
        await _store.AppendAsync(track.Id, Point(1, 5050, 410));

        var stored = (await _store.GetAsync(track.Id))!;
        Assert.Equal(4, stored.PointCount);
        Assert.Equal(150, stored.DistanceMeters, 0);
        Assert.Equal(T0.AddSeconds(410), stored.LastPointAt);
        Assert.Equal(1, await _store.GetLastSegmentAsync(track.Id));
        Assert.Equal([0, 0, 1, 1], (await _store.GetPointsAsync(track.Id)).Select(p => p.Segment));
    }

    [Fact]
    public async Task Store_CloseListAndDelete()
    {
        var session = Guid.NewGuid();
        var older = await _store.CreateAsync(Track(session));
        var newer = await _store.CreateAsync(Track() with { StartedAt = T0.AddHours(1) });
        await _store.AppendAsync(older.Id, Point(0, 0, 0));

        Assert.Equal(older.Id, (await _store.FindOpenAsync(session))!.Id);
        await _store.CloseAsync(older.Id, T0.AddMinutes(30));
        Assert.Null(await _store.FindOpenAsync(session));

        var list = await _store.ListAsync();
        Assert.Equal([newer.Id, older.Id], list.Select(t => t.Id));
        Assert.Equal(TimeSpan.FromMinutes(30), list[1].Duration);
        Assert.False(list[1].IsOpen);

        await _store.DeleteAsync(older.Id);
        Assert.Empty(await _store.GetPointsAsync(older.Id));
        Assert.Single(await _store.ListAsync());
        Assert.Equal(-1, await _store.GetLastSegmentAsync(newer.Id));
    }

    [Fact]
    public async Task Recorder_StoresFilteredFixesImmediately()
    {
        var track = await _recorder.StartAsync(new TrackStartRequest("Manual"), Options);
        Assert.True(_tracker.IsTracking);
        Assert.Same(Options, _tracker.Options);

        _tracker.Emit(Sample(0, 0));
        _tracker.Emit(Sample(2, 1));      // too soon
        _tracker.Emit(Sample(50, 6));
        _tracker.Emit(Sample(60, 12, accuracy: 500)); // inaccurate
        await WaitForPointsAsync(track.Id, 2);

        Assert.Equal(2, _recorder.ActiveTrack!.PointCount);
        Assert.Equal(50, _recorder.ActiveTrack.DistanceMeters, 0);

        _clock.UtcNow = T0.AddMinutes(1);
        await _recorder.StopAsync();

        Assert.False(_tracker.IsTracking);
        Assert.False(_recorder.IsRecording);
        var stored = (await _store.GetAsync(track.Id))!;
        Assert.Equal(T0.AddMinutes(1), stored.EndedAt);
        Assert.Equal(2, stored.PointCount);
    }

    [Fact]
    public async Task Recorder_ContinuesOpenSessionTrackInNewSegment()
    {
        var session = Guid.NewGuid();
        var request = new TrackStartRequest("Tag · Goniący", "Pixel 7", session, "tag", "chaser");

        var first = await _recorder.StartAsync(request, Options);
        _tracker.Emit(Sample(0, 0));
        _tracker.Emit(Sample(100, 10));
        await WaitForPointsAsync(first.Id, 2);
        await _recorder.PauseAsync(); // game left, not finished

        var second = await _recorder.StartAsync(request, Options);
        _tracker.Emit(Sample(100, 600));
        await WaitForPointsAsync(first.Id, 3);
        await _recorder.StopAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _store.ListAsync());
        Assert.Equal([0, 0, 1], (await _store.GetPointsAsync(first.Id)).Select(p => p.Segment));
        Assert.Equal(100, (await _store.GetAsync(first.Id))!.DistanceMeters, 0);
    }

    [Fact]
    public async Task Recorder_SignalGapStartsNewSegment()
    {
        var track = await _recorder.StartAsync(new TrackStartRequest("Manual"), Options);
        _tracker.Emit(Sample(0, 0));
        _tracker.Emit(Sample(3000, 400));
        await WaitForPointsAsync(track.Id, 2);
        await _recorder.StopAsync();

        Assert.Equal([0, 1], (await _store.GetPointsAsync(track.Id)).Select(p => p.Segment));
    }

    [Fact]
    public async Task Recorder_WhenTrackerFails_RemovesNewTrackAndRethrows()
    {
        _tracker.StartFailure = new UnauthorizedAccessException("no permission");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _recorder.StartAsync(new TrackStartRequest("Manual"), Options));

        Assert.False(_recorder.IsRecording);
        Assert.Empty(await _store.ListAsync());
    }

    private async Task WaitForPointsAsync(Guid trackId, int count)
    {
        // Fixes are stored asynchronously (the platform raises events on its own thread).
        for (var i = 0; i < 200; i++)
        {
            var stored = (await _store.GetAsync(trackId))?.PointCount ?? 0;
            var active = _recorder.ActiveTrack?.PointCount ?? count;
            if (stored >= count && active >= count)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Expected {count} stored points.");
    }
}

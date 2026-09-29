using TransitGames.Core.Events;
using TransitGames.Core.Geography;
using TransitGames.Core.Location;
using TransitGames.Core.Timing;

namespace TransitGames.Tracking.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // Warsaw, Plac Defilad.
    public static readonly GeoPoint Origin = new(52.2319, 21.0067);

    /// <summary>A point roughly <paramref name="metersNorth"/> north of <see cref="Origin"/>.</summary>
    public static GeoPoint North(double metersNorth) => new(Origin.Latitude + metersNorth / 111_195.0, Origin.Longitude);

    public static LocationSample Sample(double metersNorth, double seconds, double? accuracy = 5, double? altitude = null) =>
        new(North(metersNorth), T0.AddSeconds(seconds), accuracy, altitude);

    public static TrackPoint Point(int segment, double metersNorth, double seconds, double? altitude = null) =>
        new(segment, North(metersNorth), T0.AddSeconds(seconds), 5, altitude);

    public static TrackInfo Track(Guid? sessionId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Tag · Goniący",
        DeviceName = "Pixel 7",
        SessionId = sessionId,
        GameId = "tag",
        RoleId = "chaser",
        StartedAt = T0,
    };
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = TestData.T0;
}

internal sealed class FakeLocationTracker : ILocationTracker
{
    public bool IsTracking { get; private set; }

    public LocationSample? LastLocation { get; private set; }

    public TrackingOptions? Options { get; private set; }

    public Exception? StartFailure { get; set; }

    public event EventHandler<LocationSample>? LocationChanged;

    public Task<LocationSample?> GetCurrentLocationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LastLocation);

    public Task StartAsync(TrackingOptions options, CancellationToken cancellationToken = default)
    {
        if (StartFailure is not null)
        {
            throw StartFailure;
        }

        Options = options;
        IsTracking = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsTracking = false;
        return Task.CompletedTask;
    }

    public void Emit(LocationSample sample)
    {
        LastLocation = sample;
        LocationChanged?.Invoke(this, sample);
    }
}

internal sealed class FakeEventLog : IGameEventLog
{
    public List<GameEvent> Events { get; } = [];

    public event EventHandler<GameEvent>? EventAppended;

    public Task AppendAsync(GameEvent gameEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(gameEvent);
        EventAppended?.Invoke(this, gameEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameEvent>> GetEventsAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameEvent>>(Events.Where(e => e.SessionId == sessionId).ToList());
}

internal sealed class TempDatabase : IDisposable
{
    public TempDatabase() =>
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"transitgames-test-{Guid.NewGuid():N}.db3");

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; temp files are cleaned up by the OS.
        }
    }
}

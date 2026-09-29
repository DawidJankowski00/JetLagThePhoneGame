namespace TransitGames.Tracking.Storage;

/// <summary>Tracks and their points. Every point is written as soon as it is accepted (crash-safe recording).</summary>
public interface ITrackStore
{
    Task<TrackInfo> CreateAsync(TrackInfo track, CancellationToken cancellationToken = default);

    Task<TrackInfo?> GetAsync(Guid trackId, CancellationToken cancellationToken = default);

    /// <summary>All tracks, newest first.</summary>
    Task<IReadOnlyList<TrackInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The newest still-open track of a game session, if any.</summary>
    Task<TrackInfo?> FindOpenAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Appends a point and updates the track's point count, distance and last point time.</summary>
    Task AppendAsync(Guid trackId, TrackPoint point, CancellationToken cancellationToken = default);

    /// <summary>Points in recording order.</summary>
    Task<IReadOnlyList<TrackPoint>> GetPointsAsync(Guid trackId, CancellationToken cancellationToken = default);

    /// <summary>Highest segment number used so far, or -1 when the track has no points.</summary>
    Task<int> GetLastSegmentAsync(Guid trackId, CancellationToken cancellationToken = default);

    Task CloseAsync(Guid trackId, DateTimeOffset endedAt, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid trackId, CancellationToken cancellationToken = default);
}

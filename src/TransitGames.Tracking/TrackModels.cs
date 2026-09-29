using TransitGames.Core.Geography;
using TransitGames.Core.Location;

namespace TransitGames.Tracking;

/// <summary>A recorded track: one device (team) during one game session, or a manual recording.</summary>
public sealed record TrackInfo
{
    public required Guid Id { get; init; }

    /// <summary>Human-readable name, e.g. "Tag · Chaser".</summary>
    public required string Name { get; init; }

    /// <summary>Device that recorded the track (GPX: one track per player/device).</summary>
    public string? DeviceName { get; init; }

    /// <summary>Game session the track belongs to; null for a manual recording.</summary>
    public Guid? SessionId { get; init; }

    public string? GameId { get; init; }

    public string? RoleId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Null while the track can still be continued (recording, or the game was left without finishing).</summary>
    public DateTimeOffset? EndedAt { get; init; }

    public int PointCount { get; init; }

    /// <summary>Sum of distances within segments (gaps between segments are not counted).</summary>
    public double DistanceMeters { get; init; }

    public DateTimeOffset? LastPointAt { get; init; }

    public bool IsOpen => EndedAt is null;

    public TimeSpan Duration => (EndedAt ?? LastPointAt ?? StartedAt) - StartedAt;
}

/// <summary>A stored GPS fix. Consecutive points with the same <see cref="Segment"/> form one GPX trkseg.</summary>
public sealed record TrackPoint(
    int Segment,
    GeoPoint Point,
    DateTimeOffset Timestamp,
    double? AccuracyMeters = null,
    double? AltitudeMeters = null,
    double? SpeedMetersPerSecond = null)
{
    public static TrackPoint From(LocationSample sample, int segment) => new(
        segment,
        sample.Point,
        sample.Timestamp,
        sample.AccuracyMeters,
        sample.AltitudeMeters,
        sample.SpeedMetersPerSecond);
}

/// <summary>What to record: a game session's track (continued if it already exists) or a manual one.</summary>
public sealed record TrackStartRequest(
    string Name,
    string? DeviceName = null,
    Guid? SessionId = null,
    string? GameId = null,
    string? RoleId = null);

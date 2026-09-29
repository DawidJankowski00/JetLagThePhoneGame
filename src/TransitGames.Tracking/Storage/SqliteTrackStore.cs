using SQLite;
using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;

namespace TransitGames.Tracking.Storage;

public sealed class SqliteTrackStore : ITrackStore
{
    private readonly AppDatabase _database;

    public SqliteTrackStore(AppDatabase database) => _database = database;

    public async Task<TrackInfo> CreateAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        var db = await TracksAsync().ConfigureAwait(false);
        await db.InsertAsync(TrackRow.From(track)).ConfigureAwait(false);
        return track;
    }

    public async Task<TrackInfo?> GetAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var db = await TracksAsync().ConfigureAwait(false);
        var id = trackId.ToString();
        var row = await db.Table<TrackRow>().Where(r => r.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        return row?.ToInfo();
    }

    public async Task<IReadOnlyList<TrackInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var db = await TracksAsync().ConfigureAwait(false);
        var rows = await db.Table<TrackRow>().OrderByDescending(r => r.StartedAtTicks).ToListAsync().ConfigureAwait(false);
        return rows.Select(r => r.ToInfo()).ToList();
    }

    public async Task<TrackInfo?> FindOpenAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await TracksAsync().ConfigureAwait(false);
        var id = sessionId.ToString();
        var row = await db.Table<TrackRow>()
            .Where(r => r.SessionId == id && r.EndedAtTicks == null)
            .OrderByDescending(r => r.StartedAtTicks)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return row?.ToInfo();
    }

    public async Task AppendAsync(Guid trackId, TrackPoint point, CancellationToken cancellationToken = default)
    {
        var db = await PointsAsync().ConfigureAwait(false);
        var id = trackId.ToString();
        var row = PointRow.From(id, point);
        await db.RunInTransactionAsync(connection =>
        {
            var previous = connection.Query<PointRow>(
                "SELECT * FROM track_points WHERE TrackId = ? ORDER BY Id DESC LIMIT 1", id).FirstOrDefault();
            var increment = previous is not null && previous.Segment == point.Segment
                ? GeoMath.DistanceMeters(previous.ToPoint().Point, point.Point)
                : 0;
            connection.Insert(row);
            connection.Execute(
                "UPDATE tracks SET PointCount = PointCount + 1, DistanceMeters = DistanceMeters + ?, LastPointAtTicks = ? WHERE Id = ?",
                increment,
                row.TimestampTicks,
                id);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrackPoint>> GetPointsAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var db = await PointsAsync().ConfigureAwait(false);
        var id = trackId.ToString();
        var rows = await db.Table<PointRow>().Where(r => r.TrackId == id).OrderBy(r => r.Id).ToListAsync().ConfigureAwait(false);
        return rows.Select(r => r.ToPoint()).ToList();
    }

    public async Task<int> GetLastSegmentAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var db = await PointsAsync().ConfigureAwait(false);
        return await db.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(Segment), -1) FROM track_points WHERE TrackId = ?", trackId.ToString()).ConfigureAwait(false);
    }

    public async Task CloseAsync(Guid trackId, DateTimeOffset endedAt, CancellationToken cancellationToken = default)
    {
        var db = await TracksAsync().ConfigureAwait(false);
        await db.ExecuteAsync(
            "UPDATE tracks SET EndedAtTicks = ? WHERE Id = ?", endedAt.UtcTicks, trackId.ToString()).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var db = await PointsAsync().ConfigureAwait(false);
        var id = trackId.ToString();
        await db.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM track_points WHERE TrackId = ?", id);
            connection.Execute("DELETE FROM tracks WHERE Id = ?", id);
        }).ConfigureAwait(false);
    }

    private Task<SQLiteAsyncConnection> TracksAsync() => _database.GetAsync<TrackRow>();

    /// <summary>Point operations also touch the tracks table, so make sure both exist.</summary>
    private async Task<SQLiteAsyncConnection> PointsAsync()
    {
        await _database.GetAsync<TrackRow>().ConfigureAwait(false);
        return await _database.GetAsync<PointRow>().ConfigureAwait(false);
    }

    [Table("tracks")]
    internal sealed class TrackRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";
        public string? DeviceName { get; set; }

        [Indexed]
        public string? SessionId { get; set; }

        public string? GameId { get; set; }
        public string? RoleId { get; set; }
        public long StartedAtTicks { get; set; }
        public long? EndedAtTicks { get; set; }
        public int PointCount { get; set; }
        public double DistanceMeters { get; set; }
        public long? LastPointAtTicks { get; set; }

        public static TrackRow From(TrackInfo t) => new()
        {
            Id = t.Id.ToString(),
            Name = t.Name,
            DeviceName = t.DeviceName,
            SessionId = t.SessionId?.ToString(),
            GameId = t.GameId,
            RoleId = t.RoleId,
            StartedAtTicks = t.StartedAt.UtcTicks,
            EndedAtTicks = t.EndedAt?.UtcTicks,
            PointCount = t.PointCount,
            DistanceMeters = t.DistanceMeters,
            LastPointAtTicks = t.LastPointAt?.UtcTicks,
        };

        public TrackInfo ToInfo() => new()
        {
            Id = Guid.Parse(Id),
            Name = Name,
            DeviceName = DeviceName,
            SessionId = SessionId is null ? null : Guid.Parse(SessionId),
            GameId = GameId,
            RoleId = RoleId,
            StartedAt = Utc(StartedAtTicks),
            EndedAt = EndedAtTicks is { } ended ? Utc(ended) : null,
            PointCount = PointCount,
            DistanceMeters = DistanceMeters,
            LastPointAt = LastPointAtTicks is { } last ? Utc(last) : null,
        };
    }

    [Table("track_points")]
    internal sealed class PointRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }

        [Indexed]
        public string TrackId { get; set; } = "";

        public int Segment { get; set; }
        public long TimestampTicks { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Accuracy { get; set; }
        public double? Altitude { get; set; }
        public double? Speed { get; set; }

        public static PointRow From(string trackId, TrackPoint p) => new()
        {
            TrackId = trackId,
            Segment = p.Segment,
            TimestampTicks = p.Timestamp.UtcTicks,
            Latitude = p.Point.Latitude,
            Longitude = p.Point.Longitude,
            Accuracy = p.AccuracyMeters,
            Altitude = p.AltitudeMeters,
            Speed = p.SpeedMetersPerSecond,
        };

        public TrackPoint ToPoint() =>
            new(Segment, new GeoPoint(Latitude, Longitude), Utc(TimestampTicks), Accuracy, Altitude, Speed);
    }

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
}

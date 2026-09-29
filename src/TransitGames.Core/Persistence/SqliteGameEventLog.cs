using System.Text.Json;
using SQLite;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;

namespace TransitGames.Core.Persistence;

public sealed class SqliteGameEventLog : IGameEventLog
{
    private readonly AppDatabase _database;

    public SqliteGameEventLog(AppDatabase database) => _database = database;

    public event EventHandler<GameEvent>? EventAppended;

    public async Task AppendAsync(GameEvent gameEvent, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<EventRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(EventRow.From(gameEvent)).ConfigureAwait(false);
        EventAppended?.Invoke(this, gameEvent);
    }

    public async Task<IReadOnlyList<GameEvent>> GetEventsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<EventRow>().ConfigureAwait(false);
        var id = sessionId.ToString();
        // Events at the same moment (a round ends and the next one starts) keep the order they were written in.
        var rows = await db.QueryAsync<EventRow>(
                "SELECT * FROM game_events WHERE SessionId = ? ORDER BY TimestampTicks, rowid", id)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<EventRow>().ConfigureAwait(false);
        var id = sessionId.ToString();
        await db.Table<EventRow>().DeleteAsync(r => r.SessionId == id).ConfigureAwait(false);
    }

    [Table("game_events")]
    internal sealed class EventRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        [Indexed]
        public string SessionId { get; set; } = "";

        public long TimestampTicks { get; set; }
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Details { get; set; }
        public string? ActorRoleId { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? DataJson { get; set; }

        public static EventRow From(GameEvent e) => new()
        {
            Id = e.Id.ToString(),
            SessionId = e.SessionId.ToString(),
            TimestampTicks = e.Timestamp.UtcTicks,
            Type = e.Type,
            Title = e.Title,
            Details = e.Details,
            ActorRoleId = e.ActorRoleId,
            Latitude = e.Location?.Latitude,
            Longitude = e.Location?.Longitude,
            DataJson = e.Data is null ? null : JsonSerializer.Serialize(e.Data, GameJson.Options),
        };

        public GameEvent ToEvent() => new()
        {
            Id = Guid.Parse(Id),
            SessionId = Guid.Parse(SessionId),
            Timestamp = new DateTimeOffset(TimestampTicks, TimeSpan.Zero),
            Type = Type,
            Title = Title,
            Details = Details,
            ActorRoleId = ActorRoleId,
            Location = Latitude is { } lat && Longitude is { } lon ? new GeoPoint(lat, lon) : null,
            Data = DataJson is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson, GameJson.Options),
        };
    }
}

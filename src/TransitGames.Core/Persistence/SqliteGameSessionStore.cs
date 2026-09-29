using SQLite;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Persistence;

public sealed class SqliteGameSessionStore : IGameSessionStore
{
    private readonly AppDatabase _database;

    public SqliteGameSessionStore(AppDatabase database) => _database = database;

    public async Task SaveAsync(GameSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(SessionRow.From(snapshot)).ConfigureAwait(false);
    }

    public async Task<GameSessionSnapshot?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        var id = sessionId.ToString();
        var row = await db.Table<SessionRow>().Where(r => r.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        return row?.ToSnapshot();
    }

    public async Task<GameSessionSnapshot?> GetLatestUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        var finished = (int)SessionStatus.Finished;
        var row = await db.Table<SessionRow>()
            .Where(r => r.Status != finished)
            .OrderByDescending(r => r.UpdatedAtTicks)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return row?.ToSnapshot();
    }

    public async Task<IReadOnlyList<GameSessionSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        var rows = await db.Table<SessionRow>().OrderByDescending(r => r.CreatedAtTicks).ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(r => r.ToSnapshot()).ToList();
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        await db.DeleteAsync<SessionRow>(sessionId.ToString()).ConfigureAwait(false);
    }

    [Table("sessions")]
    internal sealed class SessionRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string GameId { get; set; } = "";
        public string LocalRoleId { get; set; } = "";
        public int RolesVersion { get; set; }

        [Indexed]
        public int Status { get; set; }

        public long CreatedAtTicks { get; set; }

        [Indexed]
        public long UpdatedAtTicks { get; set; }

        public string SettingsJson { get; set; } = "";
        public string StateJson { get; set; } = "";

        public static SessionRow From(GameSessionSnapshot s) => new()
        {
            Id = s.SessionId.ToString(),
            GameId = s.GameId,
            LocalRoleId = s.LocalRoleId,
            RolesVersion = s.RolesVersion,
            Status = (int)s.Status,
            CreatedAtTicks = s.CreatedAt.UtcTicks,
            UpdatedAtTicks = s.UpdatedAt.UtcTicks,
            SettingsJson = s.SettingsJson,
            StateJson = s.StateJson,
        };

        public GameSessionSnapshot ToSnapshot() => new()
        {
            SessionId = Guid.Parse(Id),
            GameId = GameId,
            LocalRoleId = LocalRoleId,
            RolesVersion = RolesVersion,
            Status = (SessionStatus)Status,
            CreatedAt = new DateTimeOffset(CreatedAtTicks, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(UpdatedAtTicks, TimeSpan.Zero),
            SettingsJson = SettingsJson,
            StateJson = StateJson,
        };
    }
}

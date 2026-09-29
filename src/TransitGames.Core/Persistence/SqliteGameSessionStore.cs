using SQLite;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Persistence;

/// <summary>
/// Sessions in the <c>sessions</c> table. Schema changes: new columns are added by sqlite-net when the table is
/// opened (old rows get defaults); changes of a game's JSON are <see cref="IGameSessionMigration"/>s, run once before
/// the first read or write.
/// </summary>
public sealed class SqliteGameSessionStore : IGameSessionStore
{
    private readonly AppDatabase _database;
    private readonly IReadOnlyList<IGameSessionMigration> _migrations;
    private readonly Lazy<Task> _migrated;

    public SqliteGameSessionStore(AppDatabase database, IEnumerable<IGameSessionMigration>? migrations = null)
    {
        _database = database;
        _migrations = migrations?.ToList() ?? [];
        _migrated = new Lazy<Task>(MigrateAsync);
    }

    public async Task SaveAsync(GameSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var db = await GetAsync().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(SessionRow.From(snapshot)).ConfigureAwait(false);
    }

    public async Task<GameSessionSnapshot?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await GetAsync().ConfigureAwait(false);
        var id = sessionId.ToString();
        var row = await db.Table<SessionRow>().Where(r => r.Id == id).FirstOrDefaultAsync().ConfigureAwait(false);
        return row?.ToSnapshot();
    }

    public async Task<GameSessionSnapshot?> GetLatestUnfinishedAsync(CancellationToken cancellationToken = default)
    {
        var db = await GetAsync().ConfigureAwait(false);
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
        var db = await GetAsync().ConfigureAwait(false);
        var rows = await db.Table<SessionRow>().OrderByDescending(r => r.CreatedAtTicks).ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(r => r.ToSnapshot()).ToList();
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var db = await GetAsync().ConfigureAwait(false);
        await db.DeleteAsync<SessionRow>(sessionId.ToString()).ConfigureAwait(false);
    }

    private async Task<SQLiteAsyncConnection> GetAsync()
    {
        await _migrated.Value.ConfigureAwait(false);
        return await _database.GetAsync<SessionRow>().ConfigureAwait(false);
    }

    private async Task MigrateAsync()
    {
        if (_migrations.Count == 0)
        {
            return;
        }

        var db = await _database.GetAsync<SessionRow>().ConfigureAwait(false);
        await _database.GetAsync<SchemaMigrationRow>().ConfigureAwait(false);
        foreach (var migration in _migrations)
        {
            if (await db.FindAsync<SchemaMigrationRow>(migration.Id).ConfigureAwait(false) is not null)
            {
                continue;
            }

            var gameId = migration.GameId;
            var rows = await db.Table<SessionRow>().Where(r => r.GameId == gameId).ToListAsync().ConfigureAwait(false);
            var changed = rows
                .Select(r => migration.Migrate(r.ToSnapshot()))
                .OfType<GameSessionSnapshot>()
                .Select(SessionRow.From)
                .ToList();
            await db.RunInTransactionAsync(tx =>
            {
                foreach (var row in changed)
                {
                    tx.InsertOrReplace(row);
                }

                tx.Insert(new SchemaMigrationRow { Id = migration.Id, AppliedAtTicks = DateTimeOffset.UtcNow.UtcTicks, Sessions = changed.Count });
            }).ConfigureAwait(false);
        }
    }

    [Table("sessions")]
    internal sealed class SessionRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string GameId { get; set; } = "";
        public string LocalRoleId { get; set; } = "";
        public int RolesVersion { get; set; }

        /// <summary>Online device id of this device in the game (null offline); column added in schema 2.</summary>
        public string? LocalDeviceId { get; set; }

        /// <summary>The game was joined, not created, on this device; column added in schema 2 (old rows: host).</summary>
        public bool IsGuest { get; set; }

        [Indexed]
        public int Status { get; set; }

        public long CreatedAtTicks { get; set; }

        [Indexed]
        public long UpdatedAtTicks { get; set; }

        public long? FinishedAtTicks { get; set; }

        public string SettingsJson { get; set; } = "";
        public string StateJson { get; set; } = "";

        public static SessionRow From(GameSessionSnapshot s) => new()
        {
            Id = s.SessionId.ToString(),
            GameId = s.GameId,
            LocalRoleId = s.LocalRoleId,
            RolesVersion = s.RolesVersion,
            LocalDeviceId = s.LocalDeviceId?.ToString(),
            IsGuest = !s.IsHost,
            Status = (int)s.Status,
            CreatedAtTicks = s.CreatedAt.UtcTicks,
            UpdatedAtTicks = s.UpdatedAt.UtcTicks,
            FinishedAtTicks = s.FinishedAt?.UtcTicks,
            SettingsJson = s.SettingsJson,
            StateJson = s.StateJson,
        };

        public GameSessionSnapshot ToSnapshot() => new()
        {
            SessionId = Guid.Parse(Id),
            GameId = GameId,
            LocalRoleId = LocalRoleId,
            RolesVersion = RolesVersion,
            LocalDeviceId = Guid.TryParse(LocalDeviceId, out var deviceId) ? deviceId : null,
            IsHost = !IsGuest,
            Status = (SessionStatus)Status,
            CreatedAt = new DateTimeOffset(CreatedAtTicks, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(UpdatedAtTicks, TimeSpan.Zero),
            FinishedAt = FinishedAtTicks is { } finished ? new DateTimeOffset(finished, TimeSpan.Zero) : null,
            SettingsJson = SettingsJson,
            StateJson = StateJson,
        };
    }
}

using SQLite;
using TransitGames.Core.Geography;

namespace TransitGames.Core.Persistence;

public sealed class SqliteGameAreaRepository : IGameAreaRepository
{
    private readonly AppDatabase _database;

    public SqliteGameAreaRepository(AppDatabase database) => _database = database;

    public async Task SaveAsync(
        string key,
        GameAreaDefinition definition,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<AreaRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(new AreaRow
        {
            Key = key,
            Name = definition.Name,
            GeoJson = definition.GeoJson,
            Source = (int)definition.Source,
            LastUsedUtcTicks = usedAt.UtcTicks,
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SavedGameArea>> ListAsync(CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<AreaRow>().ConfigureAwait(false);
        var rows = await db.Table<AreaRow>().OrderByDescending(r => r.LastUsedUtcTicks).ToListAsync().ConfigureAwait(false);
        return rows.Select(r => new SavedGameArea(
                r.Key,
                new GameAreaDefinition(r.Name, r.GeoJson, (GameAreaSource)r.Source),
                new DateTimeOffset(r.LastUsedUtcTicks, TimeSpan.Zero)))
            .ToList();
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<AreaRow>().ConfigureAwait(false);
        await db.DeleteAsync<AreaRow>(key).ConfigureAwait(false);
    }

    [Table("areas")]
    internal sealed class AreaRow
    {
        [PrimaryKey]
        public string Key { get; set; } = "";

        public string Name { get; set; } = "";
        public string GeoJson { get; set; } = "";
        public int Source { get; set; }

        [Indexed]
        public long LastUsedUtcTicks { get; set; }
    }
}

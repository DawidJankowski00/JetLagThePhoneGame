using SQLite;
using TransitGames.Core.Geography;
using TransitGames.Core.Stations;

namespace TransitGames.Core.Persistence;

public sealed class SqliteStationRepository : IStationRepository
{
    private readonly AppDatabase _database;

    public SqliteStationRepository(AppDatabase database) => _database = database;

    public async Task<IReadOnlyList<Station>> GetStationsAsync(string areaKey, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<StationRow>().ConfigureAwait(false);
        var rows = await db.Table<StationRow>().Where(r => r.AreaKey == areaKey).OrderBy(r => r.Name).ToListAsync()
            .ConfigureAwait(false);
        return rows.Select(r => r.ToStation()).ToList();
    }

    public async Task ReplaceDownloadedAsync(
        string areaKey,
        IReadOnlyList<Station> stations,
        CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<StationRow>().ConfigureAwait(false);
        await db.RunInTransactionAsync(conn =>
        {
            var existing = conn.Table<StationRow>().Where(r => r.AreaKey == areaKey).ToList();
            var disabled = existing.Where(r => !r.IsEnabled).Select(r => r.StationId).ToHashSet();

            foreach (var row in existing.Where(r => !r.IsCustom))
            {
                conn.Delete(row);
            }

            foreach (var station in stations)
            {
                var row = StationRow.From(areaKey, station);
                row.IsEnabled = station.IsEnabled && !disabled.Contains(station.Id);
                conn.InsertOrReplace(row);
            }
        }).ConfigureAwait(false);
    }

    public async Task UpsertAsync(string areaKey, Station station, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<StationRow>().ConfigureAwait(false);
        await db.InsertOrReplaceAsync(StationRow.From(areaKey, station)).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(
        string areaKey,
        string stationId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<StationRow>().ConfigureAwait(false);
        var key = StationRow.MakeKey(areaKey, stationId);
        await db.ExecuteAsync("UPDATE stations SET IsEnabled = ? WHERE Key = ?", isEnabled, key).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string areaKey, string stationId, CancellationToken cancellationToken = default)
    {
        var db = await _database.GetAsync<StationRow>().ConfigureAwait(false);
        await db.DeleteAsync<StationRow>(StationRow.MakeKey(areaKey, stationId)).ConfigureAwait(false);
    }

    [Table("stations")]
    internal sealed class StationRow
    {
        [PrimaryKey]
        public string Key { get; set; } = "";

        [Indexed]
        public string AreaKey { get; set; } = "";

        public string StationId { get; set; } = "";
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Modes { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsCustom { get; set; }

        public static string MakeKey(string areaKey, string stationId) => $"{areaKey}|{stationId}";

        public static StationRow From(string areaKey, Station s) => new()
        {
            Key = MakeKey(areaKey, s.Id),
            AreaKey = areaKey,
            StationId = s.Id,
            Name = s.Name,
            Latitude = s.Location.Latitude,
            Longitude = s.Location.Longitude,
            Modes = (int)s.Modes,
            IsEnabled = s.IsEnabled,
            IsCustom = s.IsCustom,
        };

        public Station ToStation() => new()
        {
            Id = StationId,
            Name = Name,
            Location = new GeoPoint(Latitude, Longitude),
            Modes = (TransitModes)Modes,
            IsEnabled = IsEnabled,
            IsCustom = IsCustom,
        };
    }
}

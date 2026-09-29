using TransitGames.Core.Persistence;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;
using TransitGames.Geo.Stations;

namespace TransitGames.Geo.Tests;

public sealed class StationCatalogTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"transitgames-geo-{Guid.NewGuid():N}.db3");
    private readonly AppDatabase _database;
    private readonly FakeOverpass _overpass = new();
    private readonly StationCatalog _catalog;
    private readonly GameArea _area = TestData.Area();

    public StationCatalogTests()
    {
        _database = new AppDatabase(new DatabaseOptions(_dbPath));
        _catalog = new StationCatalog(new SqliteStationRepository(_database), _overpass);
    }

    [Fact]
    public async Task Download_FiltersToAreaAndMergesDuplicates()
    {
        _overpass.Elements =
        [
            TestData.Node(1, TestData.Center, ("railway", "station"), ("station", "subway"), ("name", "Centrum")),
            TestData.Node(2, TestData.Offset(TestData.Center, 60, 0), ("railway", "tram_stop"), ("name", "Centrum")),
            TestData.Node(3, TestData.Offset(TestData.Center, 800, 800), ("railway", "halt"), ("name", "Powiśle")),
            TestData.Node(4, TestData.Offset(TestData.Center, 9000, 0), ("railway", "station"), ("name", "Far away")),
        ];

        var result = await _catalog.DownloadAsync(_area, TransitModes.Default);

        Assert.Equal(4, result.Found);
        Assert.Equal(1, result.OutsideArea);
        Assert.Equal(1, result.Merged);
        Assert.Equal(["Centrum", "Powiśle"], result.Stations.Select(s => s.Name));
        Assert.Equal(TransitModes.Subway | TransitModes.Tram, result.Stations[0].Modes);
        Assert.Contains("[bbox:", _overpass.LastQuery);
    }

    [Fact]
    public async Task Download_MergesNumberedPlatformsWithStation()
    {
        // Real Warsaw pattern: metro station + tram stop positions "Centrum 07..10" around it.
        _overpass.Elements =
        [
            TestData.Node(1, TestData.Center, ("railway", "station"), ("station", "subway"), ("name", "Centrum")),
            TestData.Node(2, TestData.Offset(TestData.Center, 80, 20), ("railway", "tram_stop"), ("name", "Centrum 07"), ("ref", "701307")),
            TestData.Node(3, TestData.Offset(TestData.Center, 90, -20), ("railway", "tram_stop"), ("name", "Centrum 08"), ("ref", "701308")),
        ];

        var result = await _catalog.DownloadAsync(_area, TransitModes.Default);

        var centrum = Assert.Single(result.Stations);
        Assert.Equal("Centrum", centrum.Name);
        Assert.Equal(TransitModes.Subway | TransitModes.Tram, centrum.Modes);
    }

    [Fact]
    public async Task Redownload_KeepsDisabledFlagsAndCustomStations()
    {
        _overpass.Elements = [TestData.Node(1, TestData.Center, ("railway", "station"), ("name", "Centrum"))];
        var first = await _catalog.DownloadAsync(_area, TransitModes.Default);
        await _catalog.SetEnabledAsync(_area, first.Stations[0], false);
        var custom = await _catalog.AddCustomAsync(_area, "Pod pomnikiem", TestData.Offset(TestData.Center, 300, 300));

        var second = await _catalog.DownloadAsync(_area, TransitModes.Default);

        Assert.False(second.Stations.Single(s => s.Name == "Centrum").IsEnabled);
        Assert.Contains(second.Stations, s => s.Id == custom.Id && s.IsCustom);
    }

    [Fact]
    public async Task CachedStations_AreAvailableWithoutNetwork()
    {
        _overpass.Elements = [TestData.Node(1, TestData.Center, ("railway", "station"), ("name", "Centrum"))];
        await _catalog.DownloadAsync(_area, TransitModes.Default);
        _overpass.Elements = null; // network gone

        var cached = await _catalog.GetCachedAsync(GameArea.FromDefinition(_area.ToDefinition()));

        Assert.Single(cached);
    }

    [Fact]
    public async Task AddCustom_OutsideArea_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _catalog.AddCustomAsync(_area, "Nope", TestData.Offset(TestData.Center, 50_000, 0)));
    }

    [Fact]
    public async Task ImportGeoJson_AddsNamedPointsInsideArea()
    {
        var inside = TestData.Offset(TestData.Center, 100, 100);
        var geoJson = """{"type":"FeatureCollection","features":[""" + string.Join(",",
            PointFeature("Fontanna", inside.Longitude, inside.Latitude),
            PointFeature(null, inside.Longitude, inside.Latitude),
            PointFeature("Daleko", 25.0, 50.0)) + "]}";

        var result = await _catalog.ImportGeoJsonAsync(_area, geoJson);
        await _catalog.ImportGeoJsonAsync(_area, geoJson); // idempotent

        Assert.Equal(1, result.SkippedWithoutName);
        Assert.Equal(1, result.SkippedOutsideArea);
        var stored = Assert.Single(await _catalog.GetCachedAsync(_area));
        Assert.True(stored.IsCustom);
        Assert.Equal("Fontanna", stored.Name);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; temp files are cleaned up by the OS.
        }
    }

    private static string PointFeature(string? name, double lon, double lat)
    {
        var properties = name is null ? "{}" : "{\"name\":\"" + name + "\"}";
        return FormattableString.Invariant(
            $"{{\"type\":\"Feature\",\"properties\":{properties},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{lon},{lat}]}}}}");
    }

    private sealed class FakeOverpass : IOverpassClient
    {
        public List<OverpassElement>? Elements { get; set; } = [];

        public string LastQuery { get; private set; } = "";

        public Task<OverpassResponse> QueryAsync(string query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Elements is null
                ? throw new OverpassException("offline")
                : Task.FromResult(new OverpassResponse { Elements = Elements });
        }
    }
}

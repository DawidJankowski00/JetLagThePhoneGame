using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;
using TransitGames.Core.Stations;

namespace TransitGames.Core.Tests;

public sealed class StationRepositoryTests : IDisposable
{
    private const string Area = "area-1";
    private readonly TempDatabase _file = new();
    private readonly SqliteStationRepository _repository;

    public StationRepositoryTests() =>
        _repository = new SqliteStationRepository(new AppDatabase(new DatabaseOptions(_file.Path)));

    public void Dispose() => _file.Dispose();

    private static Station Osm(string id, string name) => new()
    {
        Id = $"osm:node/{id}",
        Name = name,
        Location = new GeoPoint(52.2, 21.0),
        Modes = TransitModes.Subway,
    };

    [Fact]
    public async Task ReplaceDownloaded_KeepsCustomStationsAndDisabledFlags()
    {
        await _repository.ReplaceDownloadedAsync(Area, [Osm("1", "Centrum"), Osm("2", "Politechnika")]);
        await _repository.UpsertAsync(Area, new Station
        {
            Id = "custom:x",
            Name = "Mój przystanek",
            Location = new GeoPoint(52.21, 21.01),
            IsCustom = true,
        });
        await _repository.SetEnabledAsync(Area, "osm:node/2", false);

        // A re-download drops station 1 and keeps station 2.
        await _repository.ReplaceDownloadedAsync(Area, [Osm("2", "Politechnika"), Osm("3", "Wilanowska")]);

        var stations = await _repository.GetStationsAsync(Area);
        Assert.Equal(["Mój przystanek", "Politechnika", "Wilanowska"], stations.Select(s => s.Name));
        Assert.False(stations.Single(s => s.Id == "osm:node/2").IsEnabled);
        Assert.True(stations.Single(s => s.Id == "osm:node/3").IsEnabled);
    }

    [Fact]
    public async Task Areas_AreIsolated()
    {
        await _repository.ReplaceDownloadedAsync(Area, [Osm("1", "A")]);
        await _repository.ReplaceDownloadedAsync("other", [Osm("1", "B")]);

        Assert.Equal("A", (await _repository.GetStationsAsync(Area)).Single().Name);
        await _repository.DeleteAsync(Area, "osm:node/1");
        Assert.Empty(await _repository.GetStationsAsync(Area));
        Assert.Single(await _repository.GetStationsAsync("other"));
    }
}

using TransitGames.Core.Stations;
using TransitGames.Geo.Projection;
using TransitGames.Geo.Stations;

namespace TransitGames.Geo.Tests;

public class StationDeduplicatorTests
{
    private readonly LocalProjection _projection = LocalProjection.CenteredOn(TestData.Center);

    [Fact]
    public void SameNameWithinRadius_IsMerged()
    {
        var a = TestData.Station("osm:node/2", "Centrum", TestData.Center, TransitModes.Subway);
        var b = TestData.Station("osm:node/1", "centrum ", TestData.Offset(TestData.Center, 100, 0), TransitModes.Tram);

        var result = StationDeduplicator.Deduplicate([a, b], _projection);

        var merged = Assert.Single(result);
        Assert.Equal("osm:node/1", merged.Id);
        Assert.Equal(TransitModes.Subway | TransitModes.Tram, merged.Modes);
        Assert.Equal(50, _projection.Distance(TestData.Center, merged.Location), 0);
    }

    [Fact]
    public void SameNameFarApart_StaysSeparate()
    {
        var a = TestData.Station("osm:node/1", "Dworzec", TestData.Center);
        var b = TestData.Station("osm:node/2", "Dworzec", TestData.Offset(TestData.Center, 0, 400));

        Assert.Equal(2, StationDeduplicator.Deduplicate([a, b], _projection).Count);
    }

    [Fact]
    public void DifferentNamesClose_StaySeparate()
    {
        var a = TestData.Station("osm:node/1", "Plac Wilsona", TestData.Center);
        var b = TestData.Station("osm:node/2", "Plac Wilsona 02", TestData.Offset(TestData.Center, 20, 0));

        Assert.Equal(2, StationDeduplicator.Deduplicate([a, b], _projection).Count);
    }

    [Fact]
    public void ChainOfCloseStops_IsOneStation()
    {
        var stops = Enumerable.Range(0, 3)
            .Select(i => TestData.Station($"osm:node/{i}", "Rondo", TestData.Offset(TestData.Center, i * 120, 0), TransitModes.Bus))
            .ToList();

        Assert.Single(StationDeduplicator.Deduplicate(stops, _projection));
    }

    [Fact]
    public void Output_IsSortedByName()
    {
        var result = StationDeduplicator.Deduplicate(
        [
            TestData.Station("osm:node/1", "Zacisze", TestData.Center),
            TestData.Station("osm:node/2", "Arkadia", TestData.Center),
        ], _projection);

        Assert.Equal(["Arkadia", "Zacisze"], result.Select(s => s.Name));
    }
}

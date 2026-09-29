using NetTopologySuite.Geometries;
using TransitGames.Core.Stations;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Tests;

public class OverpassParserTests
{
    [Theory]
    [InlineData(TransitModes.Rail, "railway=station")]
    [InlineData(TransitModes.Rail, "railway=halt")]
    [InlineData(TransitModes.Subway, "railway=station", "station=subway")]
    [InlineData(TransitModes.Rail | TransitModes.Subway, "public_transport=station", "train=yes", "subway=yes")]
    [InlineData(TransitModes.Tram, "railway=tram_stop")]
    [InlineData(TransitModes.Tram | TransitModes.Bus, "public_transport=station", "tram=yes", "bus=yes")]
    [InlineData(TransitModes.Bus, "highway=bus_stop")]
    [InlineData(TransitModes.Bus, "amenity=bus_station")]
    [InlineData(TransitModes.None, "railway=station", "station=miniature")]
    [InlineData(TransitModes.None, "public_transport=platform")]
    public void Classify_ReadsOsmTags(TransitModes expected, params string[] tags)
    {
        var element = TestData.Node(1, TestData.Center, tags.Select(t => (t.Split('=')[0], t.Split('=')[1])).ToArray());

        Assert.Equal(expected, OverpassStationParser.Classify(element));
    }

    [Fact]
    public void Parse_SkipsUnnamedAndUnrequested_AndUsesWayCentre()
    {
        var response = new OverpassResponse
        {
            Elements =
            [
                TestData.Node(1, TestData.Center, ("railway", "station"), ("name", " Centralna ")),
                TestData.Node(2, TestData.Center, ("railway", "station")),
                TestData.Node(3, TestData.Center, ("highway", "bus_stop"), ("name", "Bus only")),
                new OverpassElement
                {
                    Type = "way",
                    Id = 4,
                    Center = new OverpassPoint(52.24, 21.02),
                    Tags = new() { ["railway"] = "tram_stop", ["highway"] = "bus_stop", ["name"] = "Tram+Bus" },
                },
            ],
        };

        var stations = OverpassStationParser.Parse(response, TransitModes.Default);

        Assert.Equal(["osm:node/1", "osm:way/4"], stations.Select(s => s.Id));
        Assert.Equal("Centralna", stations[0].Name);
        Assert.Equal(TransitModes.Tram, stations[1].Modes); // bus masked out: not requested
        Assert.Equal(52.24, stations[1].Location.Latitude);
    }

    [Theory]
    [InlineData("Centrum 07", "701307", "Centrum")]
    [InlineData("Plac Bankowy 08", "08", "Plac Bankowy")]
    [InlineData("Linia 15", "3", "Linia 15")] // number is part of the name, not the platform ref
    [InlineData("Rondo ONZ 03", null, "Rondo ONZ 03")]
    public void StopName_DropsPlatformNumberMatchingRef(string name, string? reference, string expected)
    {
        var tags = new List<(string, string)> { ("railway", "tram_stop"), ("name", name) };
        if (reference is not null)
        {
            tags.Add(("ref", reference));
        }

        Assert.Equal(expected, OverpassStationParser.StationName(TestData.Node(1, TestData.Center, [.. tags])));
    }

    [Fact]
    public void StationName_IsKeptForRailwayStations()
    {
        var station = TestData.Node(1, TestData.Center, ("railway", "halt"), ("name", "Warszawa Ochota 2"), ("ref", "2"));

        Assert.Equal("Warszawa Ochota 2", OverpassStationParser.StationName(station));
    }

    [Fact]
    public void ParseList_SortsMostLocalFirst()
    {
        var response = new OverpassResponse
        {
            Elements =
            [
                Relation(10, ("name", "Mazowieckie"), ("admin_level", "4")),
                Relation(11, ("name", "Śródmieście"), ("admin_level", "9")),
                Relation(12, ("name", "Warszawa"), ("admin_level", "6")),
                Relation(13, ("admin_level", "8")),
            ],
        };

        var boundaries = OverpassBoundaryParser.ParseList(response);

        Assert.Equal(["Śródmieście", "Warszawa", "Mazowieckie"], boundaries.Select(b => b.Name));
        Assert.Equal(11, boundaries[0].RelationId);
    }

    [Fact]
    public void ParseGeometry_JoinsOuterWaysAndCutsInnerRings()
    {
        // Outer 10×10 square split into two ways; inner 2×2 square as a hole.
        var relation = new OverpassElement
        {
            Type = "relation",
            Id = 99,
            Members =
            [
                Way("outer", (0, 0), (10, 0), (10, 10)),
                Way("outer", (10, 10), (0, 10), (0, 0)),
                Way("inner", (4, 4), (6, 4), (6, 6), (4, 6), (4, 4)),
                new OverpassMember { Type = "node", Ref = 1, Role = "admin_centre" },
            ],
        };

        var geometry = OverpassBoundaryParser.ParseGeometry(new OverpassResponse { Elements = [relation] }, 99);

        var polygon = Assert.IsType<Polygon>(geometry);
        Assert.Single(polygon.Holes);
        Assert.Equal(96, polygon.Area, 9);
    }

    [Fact]
    public void ParseGeometry_WithoutClosedOuterRing_Throws()
    {
        var relation = new OverpassElement { Type = "relation", Id = 5, Members = [Way("outer", (0, 0), (10, 0), (10, 10))] };

        Assert.Throws<OverpassException>(() => OverpassBoundaryParser.ParseGeometry(new OverpassResponse { Elements = [relation] }, 5));
    }

    [Fact]
    public void StationsQuery_IncludesOnlyRequestedModes()
    {
        var query = OverpassQueries.Stations(new(52, 21), new(52.1, 21.1), TransitModes.Tram);

        Assert.Contains("tram_stop", query);
        Assert.DoesNotContain("bus_stop", query);
        Assert.DoesNotContain("halt", query);
        Assert.Contains("[bbox:52.000000,21.000000,52.100000,21.100000]", query);
    }

    private static OverpassElement Relation(long id, params (string Key, string Value)[] tags) =>
        new() { Type = "relation", Id = id, Tags = tags.ToDictionary(t => t.Key, t => t.Value) };

    private static OverpassMember Way(string role, params (double Lon, double Lat)[] points) => new()
    {
        Type = "way",
        Role = role,
        Geometry = points.Select(p => (OverpassPoint?)new OverpassPoint(p.Lat, p.Lon)).ToList(),
    };
}

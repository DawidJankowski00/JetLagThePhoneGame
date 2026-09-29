using TransitGames.Geo.Overpass;
using TransitGames.Geo.Places;

namespace TransitGames.Geo.Tests;

public class PlaceParsingTests
{
    [Fact]
    public void Points_come_from_nodes_and_centres_with_a_fallback_name()
    {
        var response = new OverpassResponse
        {
            Elements =
            [
                new() { Type = "node", Id = 1, Lat = 52.1, Lon = 21.1, Tags = new() { ["name"] = "Muzeum A" } },
                new() { Type = "way", Id = 2, Center = new(52.2, 21.2) },
                new() { Type = "relation", Id = 3 }, // no position: skipped
            ],
        };

        var places = PlaceCatalog.ParsePoints(response, "muzeum");

        Assert.Equal(["node/1", "way/2"], places.Select(p => p.Id).Order());
        Assert.Equal("muzeum (w2)", places.Single(p => p.Id == "way/2").Name);
    }
}

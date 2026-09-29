using NetTopologySuite.Geometries;
using TransitGames.Geo.Areas;

namespace TransitGames.Geo.Tests;

public class GeoJsonTests
{
    private const string Square1 = """{"type":"Polygon","coordinates":[[[21.0,52.0],[21.1,52.0],[21.1,52.1],[21.0,52.1],[21.0,52.0]]]}""";
    private const string Square2 = """{"type":"Polygon","coordinates":[[[21.5,52.0],[21.6,52.0],[21.6,52.1],[21.5,52.1],[21.5,52.0]]]}""";
    private const string Overlapping = """{"type":"Polygon","coordinates":[[[21.05,52.0],[21.15,52.0],[21.15,52.1],[21.05,52.1],[21.05,52.0]]]}""";

    [Fact]
    public void ReadsBareGeometry()
    {
        var geometry = GeoJson.ReadPolygonal(Square1);

        Assert.IsType<Polygon>(geometry);
        Assert.Equal(21.0, geometry.EnvelopeInternal.MinX, 9);
    }

    [Fact]
    public void ReadsFeature()
    {
        var geometry = GeoJson.ReadPolygonal($$"""{"type":"Feature","properties":{"name":"x"},"geometry":{{Square1}}}""");

        Assert.IsType<Polygon>(geometry);
    }

    [Fact]
    public void FeatureCollection_DisjointPolygons_BecomeMultiPolygon()
    {
        var geometry = GeoJson.ReadPolygonal(Collection(Square1, Square2));

        Assert.IsType<MultiPolygon>(geometry);
        Assert.Equal(2, geometry.NumGeometries);
    }

    [Fact]
    public void FeatureCollection_OverlappingPolygons_AreUnited()
    {
        var geometry = GeoJson.ReadPolygonal(Collection(Square1, Overlapping));

        Assert.IsType<Polygon>(geometry);
        Assert.Equal(0.015, geometry.Area, 9);
    }

    [Fact]
    public void PointsOnly_Throw()
    {
        var points = Collection("""{"type":"Point","coordinates":[21.0,52.0]}""");

        Assert.Throws<GeoJsonFormatException>(() => GeoJson.ReadPolygonal(points));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"type":"Polygon","coordinates":"oops"}""")]
    public void Malformed_Throws(string text)
    {
        Assert.Throws<GeoJsonFormatException>(() => GeoJson.ReadPolygonal(text));
    }

    [Fact]
    public void Write_ProducesReadableGeoJson()
    {
        var geometry = GeoJson.ReadPolygonal(Square1);

        var text = GeoJson.Write(geometry);

        Assert.Contains("\"Polygon\"", text);
        Assert.True(GeoJson.ReadPolygonal(text).EqualsExact(geometry));
    }

    private static string Collection(params string[] geometries) =>
        """{"type":"FeatureCollection","features":[""" +
        string.Join(",", geometries.Select(g => $$"""{"type":"Feature","properties":{},"geometry":{{g}}}""")) +
        "]}";
}

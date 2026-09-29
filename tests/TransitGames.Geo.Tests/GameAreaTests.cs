using NetTopologySuite.Geometries;
using TransitGames.Core.Geography;
using TransitGames.Geo.Areas;

namespace TransitGames.Geo.Tests;

public class GameAreaTests
{
    [Fact]
    public void Contains_UsesTheExactPolygon()
    {
        var area = TestData.Area();

        Assert.True(area.Contains(TestData.Center));
        Assert.True(area.Contains(TestData.Offset(TestData.Center, 1900, 2000)));
        Assert.False(area.Contains(TestData.Offset(TestData.Center, 2200, 0)));
    }

    [Fact]
    public void Area_IsComputedInSquareKilometres()
    {
        // 1 km × 1 km square built from metric offsets.
        var c = TestData.Center;
        var area = GameArea.FromVertices("km²",
        [
            TestData.Offset(c, -500, -500), TestData.Offset(c, 500, -500),
            TestData.Offset(c, 500, 500), TestData.Offset(c, -500, 500),
        ]);

        Assert.InRange(area.AreaSquareKilometers, 0.995, 1.005);
    }

    [Fact]
    public void Key_IgnoresStartVertexAndOrientation()
    {
        var vertices = TestData.Rectangle(TestData.Center, 0.02, 0.03);
        var rotated = vertices.Skip(2).Concat(vertices.Take(2)).ToList();
        var reversed = vertices.Reverse().ToList();

        var key = GameArea.FromVertices("a", vertices).Key;

        Assert.Equal(key, GameArea.FromVertices("b", rotated).Key);
        Assert.Equal(key, GameArea.FromVertices("c", reversed).Key);
        Assert.NotEqual(key, GameArea.FromVertices("d", TestData.Rectangle(TestData.Center, 0.02, 0.031)).Key);
    }

    [Fact]
    public void Definition_RoundTripKeepsGeometryAndKey()
    {
        var area = TestData.Area();

        var restored = GameArea.FromDefinition(area.ToDefinition());

        Assert.Equal(area.Key, restored.Key);
        Assert.Equal("Test", restored.Name);
        Assert.Equal(GameAreaSource.Drawn, restored.Source);
    }

    [Fact]
    public void FromVertices_FixesSelfIntersectingPolygon()
    {
        var c = TestData.Center;
        var bowTie = new List<GeoPoint>
        {
            TestData.Offset(c, -500, -500), TestData.Offset(c, 500, 500),
            TestData.Offset(c, 500, -500), TestData.Offset(c, -500, 500),
        };

        var area = GameArea.FromVertices("bow tie", bowTie);

        Assert.True(area.Geometry.IsValid);
        Assert.InRange(area.AreaSquareKilometers, 0.49, 0.51);
    }

    [Fact]
    public void FromVertices_RequiresThreePoints()
    {
        Assert.Throws<ArgumentException>(() => GameArea.FromVertices("x", [TestData.Center, TestData.Offset(TestData.Center, 10, 10)]));
    }

    [Fact]
    public void Simplify_DropsVerticesWithinTolerance()
    {
        // Circle with 400 vertices, radius 2 km.
        var c = TestData.Center;
        var vertices = Enumerable.Range(0, 400)
            .Select(i => TestData.Offset(c, 2000 * Math.Cos(i * Math.PI / 200), 2000 * Math.Sin(i * Math.PI / 200)))
            .ToList();
        var area = GameArea.FromVertices("circle", vertices);

        var simplified = area.Simplify(10);

        Assert.True(simplified.Geometry.NumPoints < area.Geometry.NumPoints / 4);
        Assert.InRange(simplified.AreaSquareKilometers / area.AreaSquareKilometers, 0.99, 1.0);
    }

    [Fact]
    public void Create_RejectsNonPolygons()
    {
        var point = new GeometryFactory().CreatePoint(new Coordinate(21, 52));

        Assert.Throws<ArgumentException>(() => GameArea.Create("p", point, GameAreaSource.Drawn));
    }
}

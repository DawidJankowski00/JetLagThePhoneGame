using TransitGames.Core.Geography;
using TransitGames.Geo.Projection;

namespace TransitGames.Geo.Tests;

public class LocalProjectionTests
{
    private const double A = 6_378_137.0;
    private const double E2 = 0.00669437999014;

    private readonly LocalProjection _projection = LocalProjection.CenteredOn(TestData.Center);

    [Fact]
    public void Origin_ProjectsToZero()
    {
        var c = _projection.Project(TestData.Center);

        Assert.Equal(0, c.X, 3);
        Assert.Equal(0, c.Y, 3);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.2)]
    public void NorthSouthDistance_MatchesMeridianArc(double deltaLatitude)
    {
        var north = TestData.Center with { Latitude = TestData.Center.Latitude + deltaLatitude };

        var expected = MeridianArc(TestData.Center.Latitude, north.Latitude);
        var actual = _projection.Distance(TestData.Center, north);

        Assert.InRange(actual, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public void EastWestDistance_MatchesParallelArc_20KmAway()
    {
        var phi = TestData.Center.Latitude * Math.PI / 180;
        var n = A / Math.Sqrt(1 - E2 * Math.Sin(phi) * Math.Sin(phi));
        const double deltaLon = 0.3;
        var east = TestData.Center with { Longitude = TestData.Center.Longitude + deltaLon };

        var expected = n * Math.Cos(phi) * deltaLon * Math.PI / 180; // ~20.5 km
        var actual = _projection.Distance(TestData.Center, east);

        Assert.InRange(actual, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public void ProjectUnproject_RoundTrips()
    {
        var p = new GeoPoint(52.31234, 20.87654);

        var back = _projection.Unproject(_projection.Project(p));

        Assert.Equal(p.Latitude, back.Latitude, 9);
        Assert.Equal(p.Longitude, back.Longitude, 9);
    }

    [Fact]
    public void GeometryProjection_RoundTrips()
    {
        var area = TestData.Area();

        var back = area.Projection.Unproject(area.LocalGeometry);

        Assert.True(back.EqualsExact(area.Geometry, 1e-9));
    }

    /// <summary>Meridian arc length by Simpson integration of the meridional radius of curvature.</summary>
    private static double MeridianArc(double lat1, double lat2)
    {
        const int steps = 1000;
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, h = (p2 - p1) / steps, sum = 0;
        for (var i = 0; i <= steps; i++)
        {
            var phi = p1 + i * h;
            var m = A * (1 - E2) / Math.Pow(1 - E2 * Math.Sin(phi) * Math.Sin(phi), 1.5);
            sum += m * (i == 0 || i == steps ? 1 : i % 2 == 1 ? 4 : 2);
        }

        return sum * h / 3;
    }
}

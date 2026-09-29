using TransitGames.Core.Geography;

namespace TransitGames.Tracking.Tests;

public class GeoMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Distance_between_two_moments_sums_the_steps_in_that_time_only()
    {
        // Points 0.01° of latitude apart (≈ 1112 m), one per minute; the second segment follows a gap.
        var points = Enumerable.Range(0, 6)
            .Select(i => new TrackPoint(i < 3 ? 0 : 1, new GeoPoint(52 + i * 0.01, 21), T0.AddMinutes(i)))
            .ToList();

        var ride = GeoMath.DistanceBetween(points, T0.AddMinutes(1), T0.AddMinutes(4));

        Assert.InRange(ride, 3 * 1111.0, 3 * 1113.0);
        Assert.Equal(0, GeoMath.DistanceBetween(points, T0.AddMinutes(10), T0.AddMinutes(20)));
    }
}

using TransitGames.Core.Geography;

namespace TransitGames.Tracking;

/// <summary>Distances and interpolation between consecutive GPS fixes (tracks are just point sequences).</summary>
public static class GeoMath
{
    public static double DistanceMeters(GeoPoint a, GeoPoint b) => a.DistanceTo(b);

    public static GeoPoint Interpolate(GeoPoint a, GeoPoint b, double fraction) => new(
        a.Latitude + (b.Latitude - a.Latitude) * fraction,
        a.Longitude + (b.Longitude - a.Longitude) * fraction);

    /// <summary>
    /// Length of the track between two moments (e.g. a paid ride): the sum of the steps between consecutive fixes
    /// in that time. A gap in the recording (a new segment) counts as a straight line, since the device did travel it.
    /// </summary>
    public static double DistanceBetween(IEnumerable<TrackPoint> points, DateTimeOffset from, DateTimeOffset to)
    {
        var inRange = points.Where(p => p.Timestamp >= from && p.Timestamp <= to).OrderBy(p => p.Timestamp).ToList();
        var total = 0.0;
        for (var i = 1; i < inRange.Count; i++)
        {
            total += inRange[i - 1].Point.DistanceTo(inRange[i].Point);
        }

        return total;
    }
}

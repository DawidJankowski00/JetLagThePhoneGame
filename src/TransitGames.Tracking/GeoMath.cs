using TransitGames.Core.Geography;

namespace TransitGames.Tracking;

/// <summary>
/// Great-circle distance between consecutive GPS fixes. Tracks are just point sequences, so the spherical
/// formula (error well below GPS accuracy) is enough; area geometry uses the metric projection in TransitGames.Geo.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);
        var dLat = lat2 - lat1;
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public static GeoPoint Interpolate(GeoPoint a, GeoPoint b, double fraction) => new(
        a.Latitude + (b.Latitude - a.Latitude) * fraction,
        a.Longitude + (b.Longitude - a.Longitude) * fraction);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}

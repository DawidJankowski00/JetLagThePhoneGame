using TransitGames.Core.Geography;

namespace TransitGames.Tracking;

/// <summary>Distances and interpolation between consecutive GPS fixes (tracks are just point sequences).</summary>
public static class GeoMath
{
    public static double DistanceMeters(GeoPoint a, GeoPoint b) => a.DistanceTo(b);

    public static GeoPoint Interpolate(GeoPoint a, GeoPoint b, double fraction) => new(
        a.Latitude + (b.Latitude - a.Latitude) * fraction,
        a.Longitude + (b.Longitude - a.Longitude) * fraction);
}

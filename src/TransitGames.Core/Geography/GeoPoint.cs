namespace TransitGames.Core.Geography;

/// <summary>
/// A WGS84 coordinate. Area geometry is computed in projected metric space (see TransitGames.Geo); point-to-point
/// distances (GPS fixes, reaching a target) use the spherical formula, whose error is well below GPS accuracy.
/// </summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Great-circle distance in meters.</summary>
    public double DistanceTo(GeoPoint other)
    {
        var lat1 = DegreesToRadians(Latitude);
        var lat2 = DegreesToRadians(other.Latitude);
        var dLat = lat2 - lat1;
        var dLon = DegreesToRadians(other.Longitude - Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:F6}, {Longitude:F6}");

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}

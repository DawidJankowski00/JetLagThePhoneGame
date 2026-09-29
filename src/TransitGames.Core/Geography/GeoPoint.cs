namespace TransitGames.Core.Geography;

/// <summary>A WGS84 coordinate. All distance computations happen in projected metric space (see TransitGames.Geo).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:F6}, {Longitude:F6}");
}

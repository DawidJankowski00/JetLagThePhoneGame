namespace TransitGames.Geo;

public static class GeoDefaults
{
    /// <summary>
    /// HTTP User-Agent sent to OSM services (tiles, Overpass). Their usage policies require an identifiable
    /// application with a contact point.
    /// </summary>
    public const string UserAgent = "TransitGames/1.0 (+https://github.com/DawidJankowski00/JetLagThePhoneGame)";

    /// <summary>Stations with the same name closer than this are merged into one.</summary>
    public const double StationMergeRadiusMeters = 150;

    /// <summary>Simplification tolerance for downloaded OSM boundaries.</summary>
    public const double BoundarySimplifyToleranceMeters = 10;
}

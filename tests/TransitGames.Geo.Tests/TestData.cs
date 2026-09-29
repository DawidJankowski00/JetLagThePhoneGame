using TransitGames.Core.Geography;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Overpass;

namespace TransitGames.Geo.Tests;

internal static class TestData
{
    public static readonly GeoPoint Center = new(52.23, 21.01);

    /// <summary>Rectangle ~4.4 km (N-S) × ~4.1 km (E-W) around <see cref="Center"/>.</summary>
    public static GameArea Area() => GameArea.FromVertices("Test", Rectangle(Center, 0.02, 0.03));

    public static IReadOnlyList<GeoPoint> Rectangle(GeoPoint c, double dLat, double dLon) =>
    [
        new(c.Latitude - dLat, c.Longitude - dLon),
        new(c.Latitude - dLat, c.Longitude + dLon),
        new(c.Latitude + dLat, c.Longitude + dLon),
        new(c.Latitude + dLat, c.Longitude - dLon),
    ];

    /// <summary>Point displaced from <paramref name="origin"/> by metres east/north (small-distance approximation).</summary>
    public static GeoPoint Offset(GeoPoint origin, double eastMeters, double northMeters) => new(
        origin.Latitude + northMeters / 111_267.0,
        origin.Longitude + eastMeters / (111_320.0 * Math.Cos(origin.Latitude * Math.PI / 180)));

    public static Station Station(string id, string name, GeoPoint location, TransitModes modes = TransitModes.Rail) =>
        new() { Id = id, Name = name, Location = location, Modes = modes };

    public static OverpassElement Node(long id, GeoPoint at, params (string Key, string Value)[] tags) => new()
    {
        Type = "node",
        Id = id,
        Lat = at.Latitude,
        Lon = at.Longitude,
        Tags = tags.ToDictionary(t => t.Key, t => t.Value),
    };
}

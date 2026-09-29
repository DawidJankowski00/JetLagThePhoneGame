using TransitGames.Core.Geography;

namespace TransitGames.Geo.Places;

/// <summary>
/// A point of interest used by questions (museum, park, station...). Area places (districts) also carry their
/// polygon as GeoJSON (WGS84); for them "nearest" means "containing".
/// </summary>
public sealed record Place(string Id, string Name, GeoPoint Location, string? AreaGeoJson = null);

public enum PlaceShape
{
    /// <summary>Compared by distance (Voronoi cells, buffers).</summary>
    Point,

    /// <summary>Compared by containment (administrative districts).</summary>
    Area,
}

/// <summary>A kind of place questions can refer to, with the OpenStreetMap filters used to download it.</summary>
public sealed record PlaceCategory(string Id, PlaceShape Shape, IReadOnlyList<string> OverpassSelectors)
{
    /// <summary>Uses the game's own stations instead of downloading anything.</summary>
    public bool IsGameStations => Id == PlaceCategories.StationsId;
}

public static class PlaceCategories
{
    public const string StationsId = "station";
    public const string DistrictId = "district";

    /// <summary>
    /// Built-in categories. Selectors are Overpass QL statements without the bounding box (added by the query
    /// builder). Ways and relations are reduced to their centre.
    /// </summary>
    public static IReadOnlyList<PlaceCategory> All { get; } =
    [
        new(StationsId, PlaceShape.Point, []),
        new("park", PlaceShape.Point, ["nwr[\"leisure\"=\"park\"][\"name\"]"]),
        new("museum", PlaceShape.Point, ["nwr[\"tourism\"=\"museum\"]"]),
        new("hospital", PlaceShape.Point, ["nwr[\"amenity\"=\"hospital\"]"]),
        new("library", PlaceShape.Point, ["nwr[\"amenity\"=\"library\"]"]),
        new("cinema", PlaceShape.Point, ["nwr[\"amenity\"=\"cinema\"]"]),
        new("theatre", PlaceShape.Point, ["nwr[\"amenity\"=\"theatre\"]"]),
        new("university", PlaceShape.Point, ["nwr[\"amenity\"=\"university\"]"]),
        new("zoo", PlaceShape.Point, ["nwr[\"tourism\"=\"zoo\"]"]),
        new("airport", PlaceShape.Point, ["nwr[\"aeroway\"=\"aerodrome\"][\"iata\"]", "nwr[\"aeroway\"=\"aerodrome\"][\"icao\"]"]),
        new(DistrictId, PlaceShape.Area, []),
    ];

    public static PlaceCategory Get(string id) =>
        All.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException($"Unknown place category '{id}'.", nameof(id));
}

namespace TransitGames.Core.Geography;

/// <summary>
/// The playable area of a game, stored as a GeoJSON geometry (Polygon or MultiPolygon, WGS84).
/// Core keeps it as text so it does not depend on a geometry library; TransitGames.Geo parses it.
/// </summary>
public sealed record GameAreaDefinition(string Name, string GeoJson, GameAreaSource Source);

public enum GameAreaSource
{
    Drawn,
    OsmBoundary,
    ImportedGeoJson,
}

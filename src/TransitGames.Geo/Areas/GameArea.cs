using System.Security.Cryptography;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.IO;
using NetTopologySuite.Precision;
using NetTopologySuite.Simplify;
using TransitGames.Core.Geography;
using TransitGames.Geo.Projection;

namespace TransitGames.Geo.Areas;

/// <summary>
/// A parsed game area: WGS84 geometry for display/storage plus its local metric twin for calculations.
/// </summary>
public sealed class GameArea
{
    private readonly IPreparedGeometry _preparedLocal;

    private GameArea(string name, Geometry wgs84, GameAreaSource source)
    {
        Name = name;
        Source = source;
        Geometry = wgs84;
        var centroid = wgs84.Centroid;
        Projection = LocalProjection.CenteredOn(new GeoPoint(centroid.Y, centroid.X));
        LocalGeometry = Projection.Project(wgs84);
        _preparedLocal = PreparedGeometryFactory.Prepare(LocalGeometry);
        Key = ComputeKey(wgs84);
    }

    public string Name { get; }

    public GameAreaSource Source { get; }

    /// <summary>Polygon or MultiPolygon in WGS84 (X = lon, Y = lat).</summary>
    public Geometry Geometry { get; }

    /// <summary><see cref="Geometry"/> in metres of <see cref="Projection"/>.</summary>
    public Geometry LocalGeometry { get; }

    public LocalProjection Projection { get; }

    /// <summary>
    /// Stable identifier derived from the geometry (rounded to ~0.1 m), used to cache stations per area.
    /// The same area imported twice gets the same key.
    /// </summary>
    public string Key { get; }

    public double AreaSquareKilometers => LocalGeometry.Area / 1_000_000;

    public GeoPoint Center => Projection.Origin;

    /// <summary>South-west and north-east corners in WGS84.</summary>
    public (GeoPoint SouthWest, GeoPoint NorthEast) Bounds
    {
        get
        {
            var e = Geometry.EnvelopeInternal;
            return (new GeoPoint(e.MinY, e.MinX), new GeoPoint(e.MaxY, e.MaxX));
        }
    }

    public static GameArea FromDefinition(GameAreaDefinition definition) =>
        new(definition.Name, GeoJson.ReadPolygonal(definition.GeoJson), definition.Source);

    /// <param name="wgs84">Polygon or MultiPolygon in lon/lat.</param>
    public static GameArea Create(string name, Geometry wgs84, GameAreaSource source)
    {
        if (wgs84 is not (Polygon or MultiPolygon) || wgs84.IsEmpty)
        {
            throw new ArgumentException("A game area must be a non-empty Polygon or MultiPolygon.", nameof(wgs84));
        }

        return new GameArea(name, wgs84.IsValid ? wgs84 : GeometryFixer.Fix(wgs84), source);
    }

    /// <summary>Builds an area from a polygon drawn on the map (vertices in drawing order, not closed).</summary>
    public static GameArea FromVertices(string name, IReadOnlyList<GeoPoint> vertices)
    {
        if (vertices.Count < 3)
        {
            throw new ArgumentException("A polygon needs at least three vertices.", nameof(vertices));
        }

        var ring = vertices.Append(vertices[0]).Select(v => new Coordinate(v.Longitude, v.Latitude)).ToArray();
        var polygon = GeometryFactories.Wgs84.CreatePolygon(ring);
        var fixedGeometry = GeometryFixer.Fix(polygon);
        if (fixedGeometry is not (Polygon or MultiPolygon) || fixedGeometry.IsEmpty || fixedGeometry.Area <= 0)
        {
            throw new ArgumentException("The drawn polygon has no area.", nameof(vertices));
        }

        return new GameArea(name, fixedGeometry, GameAreaSource.Drawn);
    }

    public bool Contains(GeoPoint point) => _preparedLocal.Covers(Projection.ProjectPoint(point));

    /// <summary>
    /// Returns a copy simplified with the given tolerance in metres (topology preserved). Used for OSM boundaries
    /// with thousands of vertices, which would bloat every session snapshot.
    /// </summary>
    public GameArea Simplify(double toleranceMeters)
    {
        var simplified = TopologyPreservingSimplifier.Simplify(LocalGeometry, toleranceMeters);
        return Create(Name, Projection.Unproject(simplified), Source);
    }

    public GameAreaDefinition ToDefinition() => new(Name, GeoJson.Write(Geometry), Source);

    private static string ComputeKey(Geometry wgs84)
    {
        // 1e6 per degree ≈ 0.1 m; normalization makes the key independent of ring start/orientation.
        var reduced = GeometryPrecisionReducer.Reduce(wgs84, new PrecisionModel(1_000_000));
        reduced.Normalize();
        var hash = SHA256.HashData(new WKBWriter().Write(reduced));
        return "area-" + Convert.ToHexStringLower(hash, 0, 8);
    }
}

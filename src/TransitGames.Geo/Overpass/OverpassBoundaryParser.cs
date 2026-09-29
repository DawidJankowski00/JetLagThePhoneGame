using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Polygonize;
using TransitGames.Geo.Projection;

namespace TransitGames.Geo.Overpass;

/// <summary>An OSM administrative boundary relation (city, district, municipality...).</summary>
public sealed record AdministrativeBoundary(long RelationId, string Name, int AdminLevel);

public static class OverpassBoundaryParser
{
    /// <summary>Boundaries from a tags-only answer, most local (highest admin_level) first.</summary>
    public static IReadOnlyList<AdministrativeBoundary> ParseList(OverpassResponse response) =>
        response.Elements
            .Where(e => e.Type == "relation")
            .Select(e => (Element: e, Level: int.TryParse(e.Tag("admin_level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0))
            .Where(x => x.Level >= 2 && !string.IsNullOrWhiteSpace(x.Element.Tag("name")))
            .Select(x => new AdministrativeBoundary(x.Element.Id, x.Element.Tag("name")!, x.Level))
            .DistinctBy(b => b.RelationId)
            .OrderByDescending(b => b.AdminLevel)
            .ThenBy(b => b.Name, StringComparer.CurrentCulture)
            .ToList();

    /// <summary>
    /// Assembles a multipolygon relation (answer of <c>out geom</c>) into a WGS84 Polygon/MultiPolygon:
    /// union of rings built from "outer" ways minus rings built from "inner" ways.
    /// </summary>
    /// <exception cref="OverpassException">The relation has no closed outer ring.</exception>
    public static Geometry ParseGeometry(OverpassResponse response, long relationId)
    {
        var relation = response.Elements.FirstOrDefault(e => e.Type == "relation" && e.Id == relationId)
            ?? throw new OverpassException($"Relation {relationId} was not returned.");
        var ways = (relation.Members ?? []).Where(m => m.Type == "way" && m.Geometry is { Count: >= 2 }).ToList();

        var outer = Polygonize(ways.Where(m => m.Role is "outer" or ""));
        if (outer.IsEmpty)
        {
            throw new OverpassException($"Relation {relationId} has no closed outer boundary.");
        }

        var inner = Polygonize(ways.Where(m => m.Role is "inner"));
        var result = inner.IsEmpty ? outer : outer.Difference(inner);
        return GeometryFixer.Fix(result);
    }

    private static Geometry Polygonize(IEnumerable<OverpassMember> ways)
    {
        var polygonizer = new Polygonizer(extractOnlyPolygonal: true);
        foreach (var way in ways)
        {
            // A null point means the node is missing from the answer; skip the gap rather than inventing geometry.
            var coordinates = way.Geometry!.Where(p => p is not null).Select(p => new Coordinate(p!.Lon, p.Lat)).ToArray();
            if (coordinates.Length >= 2)
            {
                polygonizer.Add(GeometryFactories.Wgs84.CreateLineString(coordinates));
            }
        }

        var polygons = polygonizer.GetPolygons();
        return polygons.Count == 0
            ? GeometryFactories.Wgs84.CreatePolygon()
            : GeometryFactories.Wgs84.BuildGeometry(polygons).Union();
    }
}

using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Triangulate;
using TransitGames.Geo.Projection;

namespace TransitGames.Games.HideAndSeek.Spatial;

/// <summary>Robust geometry helpers in local metric coordinates (one unit = one metre).</summary>
public static class GeoOps
{
    /// <summary>Segments per quarter circle: 32 keeps a 1 km circle within 1.2 m of the true circle.</summary>
    public const int CircleQuadrantSegments = 32;

    /// <summary>Regions smaller than this (m²) count as empty (numerical slivers along shared edges).</summary>
    public const double EmptyAreaThreshold = 1.0;

    private static readonly GeometryFactory Factory = GeometryFactories.Local;

    public static Geometry Empty => Factory.CreatePolygon();

    public static Geometry Circle(Coordinate center, double radius) =>
        Factory.CreatePoint(center).Buffer(radius, CircleQuadrantSegments);

    public static Geometry Intersection(Geometry a, Geometry b) =>
        OverlayNGRobust.Overlay(a, b, SpatialFunction.Intersection);

    public static Geometry Difference(Geometry a, Geometry b) =>
        OverlayNGRobust.Overlay(a, b, SpatialFunction.Difference);

    public static Geometry Union(IEnumerable<Geometry> geometries)
    {
        var list = geometries.Where(g => !g.IsEmpty).ToList();
        return list.Count == 0 ? Empty : UnaryUnionOp.Union(list);
    }

    public static bool IsEffectivelyEmpty(Geometry geometry) =>
        geometry.IsEmpty || geometry.Area < EmptyAreaThreshold;

    /// <summary>
    /// The half-plane of points strictly closer to <paramref name="closerTo"/> than to <paramref name="fartherFrom"/>
    /// (bounded by the perpendicular bisector), as a polygon large enough to cover any game area.
    /// </summary>
    public static Geometry HalfPlaneCloserTo(Coordinate closerTo, Coordinate fartherFrom)
    {
        const double Size = 1_000_000; // 1000 km: far beyond any game area around the local origin
        var dx = closerTo.X - fartherFrom.X;
        var dy = closerTo.Y - fartherFrom.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9)
        {
            throw new ArgumentException("The two points of a thermometer must differ.");
        }

        var (ux, uy) = (dx / length, dy / length); // towards closerTo
        var (px, py) = (-uy, ux); // along the bisector
        var mx = (closerTo.X + fartherFrom.X) / 2;
        var my = (closerTo.Y + fartherFrom.Y) / 2;
        return Factory.CreatePolygon(
        [
            new Coordinate(mx + px * Size, my + py * Size),
            new Coordinate(mx + px * Size + ux * Size, my + py * Size + uy * Size),
            new Coordinate(mx - px * Size + ux * Size, my - py * Size + uy * Size),
            new Coordinate(mx - px * Size, my - py * Size),
            new Coordinate(mx + px * Size, my + py * Size),
        ]);
    }

    /// <summary>
    /// Voronoi cells of the sites, clipped to an envelope that covers <paramref name="cover"/>. Sites at the same
    /// position share one cell (the first id wins, later ones map to it as well).
    /// </summary>
    public static IReadOnlyDictionary<string, Geometry> VoronoiCells(
        IReadOnlyList<(string Id, Coordinate Site)> sites,
        Envelope cover)
    {
        if (sites.Count == 0)
        {
            return new Dictionary<string, Geometry>();
        }

        var clip = new Envelope(cover);
        foreach (var (_, site) in sites)
        {
            clip.ExpandToInclude(site);
        }

        clip.ExpandBy(Math.Max(clip.Width, clip.Height) + 10_000);

        // Group identical positions (e.g. two museums in one building) under the first id.
        var unique = new List<(string Id, Coordinate Site)>();
        var aliases = new Dictionary<string, string>();
        foreach (var (id, site) in sites)
        {
            var same = unique.FirstOrDefault(u => u.Site.Distance(site) < 0.01);
            if (same.Id is null)
            {
                unique.Add((id, site));
            }
            else
            {
                aliases[id] = same.Id;
            }
        }

        var cells = new Dictionary<string, Geometry>();
        if (unique.Count == 1)
        {
            cells[unique[0].Id] = Factory.ToGeometry(clip);
        }
        else
        {
            // The builder's own clipping uses the legacy overlay, which fails on nearly degenerate input
            // (e.g. symmetric sites). Take the raw cells and clip them with the robust overlay instead.
            var builder = new VoronoiDiagramBuilder { ClipEnvelope = clip };
            builder.SetSites(unique.Select(u => u.Site).ToList());
            var clipPolygon = Factory.ToGeometry(clip);
            foreach (var raw in builder.GetSubdivision().GetVoronoiCellPolygons(Factory))
            {
                var site = (Coordinate)raw.UserData;
                var owner = unique.MinBy(u => u.Site.Distance(site));
                cells[owner.Id] = Intersection(raw, clipPolygon);
            }
        }

        foreach (var (alias, owner) in aliases)
        {
            if (cells.TryGetValue(owner, out var cell))
            {
                cells[alias] = cell;
            }
        }

        return cells;
    }
}

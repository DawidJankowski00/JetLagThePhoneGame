using NetTopologySuite.Geometries;
using TransitGames.Core.Stations;
using TransitGames.Geo.Projection;

namespace TransitGames.Geo.Stations;

/// <summary>
/// Merges OSM elements describing one physical station (station node, platforms, stop positions of both directions)
/// into one <see cref="Station"/>.
/// </summary>
public static class StationDeduplicator
{
    /// <summary>
    /// Stations with the same name (case- and whitespace-insensitive) are merged when they are linked by a chain of
    /// stations closer than <paramref name="radiusMeters"/> to each other (single linkage). The merged station keeps
    /// the ordinally smallest id (stable across downloads), the union of modes and the mean position.
    /// </summary>
    public static IReadOnlyList<Station> Deduplicate(
        IEnumerable<Station> stations,
        LocalProjection projection,
        double radiusMeters = GeoDefaults.StationMergeRadiusMeters)
    {
        var result = new List<Station>();
        foreach (var group in stations.GroupBy(s => NormalizeName(s.Name)))
        {
            var members = group.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            var points = members.Select(s => projection.Project(s.Location)).ToList();
            var clusters = new UnionFind(members.Count);
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (points[i].Distance(points[j]) <= radiusMeters)
                    {
                        clusters.Union(i, j);
                    }
                }
            }

            foreach (var cluster in Enumerable.Range(0, members.Count).GroupBy(clusters.Find))
            {
                var indices = cluster.ToList();
                var first = members[indices[0]];
                var mean = new Coordinate(indices.Average(i => points[i].X), indices.Average(i => points[i].Y));
                result.Add(first with
                {
                    Location = indices.Count == 1 ? first.Location : projection.Unproject(mean),
                    Modes = indices.Aggregate(TransitModes.None, (m, i) => m | members[i].Modes),
                });
            }
        }

        return result.OrderBy(s => s.Name, StringComparer.CurrentCulture).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    public static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed class UnionFind
    {
        private readonly int[] _parent;

        public UnionFind(int count) => _parent = Enumerable.Range(0, count).ToArray();

        public int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }

            return i;
        }

        public void Union(int a, int b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra != rb)
            {
                _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
        }
    }
}

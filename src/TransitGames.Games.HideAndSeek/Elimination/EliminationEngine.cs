using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using TransitGames.Core.Stations;
using TransitGames.Geo.Areas;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Spatial;

namespace TransitGames.Games.HideAndSeek.Elimination;

/// <summary>A station and whether the hiders can still be in its zone.</summary>
public sealed record StationStatus(Station Station, bool IsPossible);

/// <summary>Result of applying all answered questions to the game area.</summary>
public sealed class EliminationResult
{
    public required Geometry PossibleLocal { get; init; }

    /// <summary>Where the hiders can be (WGS84): the area left by the answers, within zones of possible stations.</summary>
    public required Geometry PossibleWgs84 { get; init; }

    /// <summary>The rest of the game area (WGS84), drawn darkened.</summary>
    public required Geometry EliminatedWgs84 { get; init; }

    public required IReadOnlyList<StationStatus> Stations { get; init; }

    public int RemainingStations => Stations.Count(s => s.IsPossible);

    public int TotalStations => Stations.Count;
}

/// <summary>
/// Computes the possible hiding area. It is a pure function of the question list (not a mutation), so editing or
/// removing an answer simply recomputes everything from the game area.
/// </summary>
public static class EliminationEngine
{
    public static EliminationResult Compute(
        GameArea area,
        IReadOnlyList<Station> stations,
        double hidingZoneRadiusMeters,
        IEnumerable<AskedQuestion> questions)
    {
        var projection = area.Projection;
        var possible = area.LocalGeometry;
        foreach (var question in questions.Where(q => q.Answer is not null && !q.Vetoed).OrderBy(q => q.AskedAt))
        {
            possible = QuestionFactory.Create(question.Spec, projection).Apply(possible, question.Answer!);
            if (possible.IsEmpty)
            {
                break;
            }
        }

        var prepared = PreparedGeometryFactory.Prepare(possible);
        var statuses = new List<StationStatus>(stations.Count);
        var zones = new List<Geometry>();
        foreach (var station in stations)
        {
            var zone = GeoOps.Circle(projection.Project(station.Location), hidingZoneRadiusMeters);
            var isPossible = !possible.IsEmpty && prepared.Intersects(zone)
                && !GeoOps.IsEffectivelyEmpty(GeoOps.Intersection(possible, zone));
            statuses.Add(new StationStatus(station, isPossible));
            if (isPossible)
            {
                zones.Add(zone);
            }
        }

        var candidate = GeoOps.Intersection(possible, GeoOps.Union(zones));
        var eliminated = GeoOps.Difference(area.LocalGeometry, candidate);
        return new EliminationResult
        {
            PossibleLocal = candidate,
            PossibleWgs84 = projection.Unproject(candidate),
            EliminatedWgs84 = projection.Unproject(eliminated),
            Stations = statuses,
        };
    }
}

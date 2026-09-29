using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using TransitGames.Core.Geography;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Projection;
using TransitGames.Games.HideAndSeek.Places;
using TransitGames.Games.HideAndSeek.Spatial;

namespace TransitGames.Games.HideAndSeek.Questions;

/// <summary>
/// A question with its geometry. All geometry is in the local metric coordinates of the game area's projection.
/// </summary>
public interface IQuestion
{
    QuestionSpec Spec { get; }

    /// <summary>Narrows the area where the hiders can be, given their answer.</summary>
    Geometry Apply(Geometry possibleArea, QuestionAnswer answer);

    /// <summary>The correct answer for hiders standing at <paramref name="hider"/> (a hint; hiders confirm it).</summary>
    QuestionAnswer SuggestAnswer(GeoPoint hider);
}

/// <summary>Builds <see cref="IQuestion"/>s from their specs for one game area.</summary>
public static class QuestionFactory
{
    public static IQuestion Create(QuestionSpec spec, LocalProjection projection) => spec switch
    {
        RadarSpec radar => new RadarQuestion(radar, projection),
        ThermometerSpec thermometer => new ThermometerQuestion(thermometer, projection),
        MatchingSpec matching when PlaceCategories.Get(matching.CategoryId).Shape == PlaceShape.Area =>
            new AreaMatchingQuestion(matching, projection),
        MatchingSpec matching => new MatchingQuestion(matching, projection),
        MeasuringSpec measuring => new MeasuringQuestion(measuring, projection),
        TentaclesSpec tentacles => new TentaclesQuestion(tentacles, projection),
        PhotoSpec photo => new PhotoQuestion(photo),
        _ => throw new ArgumentException($"Unsupported question {spec.GetType().Name}.", nameof(spec)),
    };

    /// <summary>Nearest place to a point (ties: first in the list).</summary>
    internal static (Place Place, double Distance)? Nearest(IEnumerable<Place> places, Coordinate point, LocalProjection projection)
    {
        (Place, double)? best = null;
        foreach (var place in places)
        {
            var distance = projection.Project(place.Location).Distance(point);
            if (best is null || distance < best.Value.Item2)
            {
                best = (place, distance);
            }
        }

        return best;
    }
}

internal sealed class RadarQuestion(RadarSpec spec, LocalProjection projection) : IQuestion
{
    private readonly Coordinate _seeker = projection.Project(spec.SeekerLocation);

    public QuestionSpec Spec => spec;

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        var circle = GeoOps.Circle(_seeker, spec.RadiusMeters);
        return answer.IsYes == true ? GeoOps.Intersection(possibleArea, circle) : GeoOps.Difference(possibleArea, circle);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider) =>
        projection.Project(hider).Distance(_seeker) <= spec.RadiusMeters ? QuestionAnswer.Yes : QuestionAnswer.No;
}

internal sealed class ThermometerQuestion(ThermometerSpec spec, LocalProjection projection) : IQuestion
{
    private readonly Coordinate _start = projection.Project(spec.Start);
    private readonly Coordinate _end = projection.Project(spec.End);

    public QuestionSpec Spec => spec;

    /// <summary>Hotter = the hiders are closer to where the seekers are now (the end) than to the start.</summary>
    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        var hotter = GeoOps.HalfPlaneCloserTo(_end, _start);
        return answer.IsYes == true ? GeoOps.Intersection(possibleArea, hotter) : GeoOps.Difference(possibleArea, hotter);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider)
    {
        var h = projection.Project(hider);
        return h.Distance(_end) < h.Distance(_start) ? QuestionAnswer.Yes : QuestionAnswer.No;
    }
}

/// <summary>Point places: the answer is whether the hiders' nearest place is the seekers' nearest place (its Voronoi cell).</summary>
internal sealed class MatchingQuestion(MatchingSpec spec, LocalProjection projection) : IQuestion
{
    private readonly Coordinate _seeker = projection.Project(spec.SeekerLocation);

    public QuestionSpec Spec => spec;

    public Place? SeekersPlace => QuestionFactory.Nearest(spec.Places, _seeker, projection)?.Place;

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        if (SeekersPlace is not { } place)
        {
            return possibleArea;
        }

        var cells = GeoOps.VoronoiCells(spec.Places.Select(p => (p.Id, projection.Project(p.Location))).ToList(), possibleArea.EnvelopeInternal);
        var cell = cells[place.Id];
        return answer.IsYes == true ? GeoOps.Intersection(possibleArea, cell) : GeoOps.Difference(possibleArea, cell);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider)
    {
        var hiders = QuestionFactory.Nearest(spec.Places, projection.Project(hider), projection)?.Place;
        return hiders is not null && hiders.Id == SeekersPlace?.Id ? QuestionAnswer.Yes : QuestionAnswer.No;
    }
}

/// <summary>Area places (districts): "Are you in the same district as me?"</summary>
internal sealed class AreaMatchingQuestion : IQuestion
{
    private readonly MatchingSpec _spec;
    private readonly LocalProjection _projection;
    private readonly List<(Place Place, Geometry Local, IPreparedGeometry Prepared)> _areas;

    public AreaMatchingQuestion(MatchingSpec spec, LocalProjection projection)
    {
        _spec = spec;
        _projection = projection;
        _areas = spec.Places
            .Where(p => p.AreaGeoJson is not null)
            .Select(p =>
            {
                var local = projection.Project(GeoJson.ReadPolygonal(p.AreaGeoJson!));
                return (p, local, PreparedGeometryFactory.Prepare(local));
            })
            .ToList();
    }

    public QuestionSpec Spec => _spec;

    public Place? SeekersPlace => Containing(_spec.SeekerLocation)?.Place;

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        var seekers = Containing(_spec.SeekerLocation);
        if (seekers is null)
        {
            return possibleArea;
        }

        var area = seekers.Value.Local;
        return answer.IsYes == true ? GeoOps.Intersection(possibleArea, area) : GeoOps.Difference(possibleArea, area);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider)
    {
        var seekers = Containing(_spec.SeekerLocation);
        var hiders = Containing(hider);
        return seekers is not null && hiders is not null && seekers.Value.Place.Id == hiders.Value.Place.Id
            ? QuestionAnswer.Yes
            : QuestionAnswer.No;
    }

    private (Place Place, Geometry Local, IPreparedGeometry Prepared)? Containing(GeoPoint point)
    {
        var p = _projection.ProjectPoint(point);
        foreach (var area in _areas)
        {
            if (area.Prepared.Covers(p))
            {
                return area;
            }
        }

        return null;
    }
}

/// <summary>
/// "Are you closer to your nearest X than I am to mine?" Closer = within the seekers' distance of any X, i.e.
/// inside the union of circles of that radius around all X.
/// </summary>
internal sealed class MeasuringQuestion(MeasuringSpec spec, LocalProjection projection) : IQuestion
{
    private readonly Coordinate _seeker = projection.Project(spec.SeekerLocation);

    public QuestionSpec Spec => spec;

    public double? SeekersDistance => QuestionFactory.Nearest(spec.Places, _seeker, projection)?.Distance;

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        if (SeekersDistance is not { } distance)
        {
            return possibleArea;
        }

        // Only places that can reach the area matter.
        var reach = new Envelope(possibleArea.EnvelopeInternal);
        reach.ExpandBy(distance);
        var circles = spec.Places
            .Select(p => projection.Project(p.Location))
            .Where(reach.Contains)
            .Select(c => GeoOps.Circle(c, distance));
        var closer = GeoOps.Union(circles);
        return answer.IsYes == true ? GeoOps.Intersection(possibleArea, closer) : GeoOps.Difference(possibleArea, closer);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider)
    {
        var hiders = QuestionFactory.Nearest(spec.Places, projection.Project(hider), projection)?.Distance;
        return hiders is not null && SeekersDistance is { } seekers && hiders.Value < seekers ? QuestionAnswer.Yes : QuestionAnswer.No;
    }
}

/// <summary>
/// "Of all X within R of me, which are you closest to?" The answer is a place (the hiders are in the circle and in
/// that place's Voronoi cell among the candidates) or null (the hiders are outside the circle).
/// </summary>
internal sealed class TentaclesQuestion(TentaclesSpec spec, LocalProjection projection) : IQuestion
{
    private readonly Coordinate _seeker = projection.Project(spec.SeekerLocation);

    public QuestionSpec Spec => spec;

    /// <summary>Places within the radius of the seekers — the possible answers.</summary>
    public IReadOnlyList<Place> Candidates =>
        spec.Places.Where(p => projection.Project(p.Location).Distance(_seeker) <= spec.RadiusMeters).ToList();

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer)
    {
        var circle = GeoOps.Circle(_seeker, spec.RadiusMeters);
        if (answer.PlaceId is null)
        {
            return GeoOps.Difference(possibleArea, circle);
        }

        var candidates = Candidates;
        if (candidates.All(c => c.Id != answer.PlaceId))
        {
            return GeoOps.Intersection(possibleArea, circle); // unknown place: only "within the radius" is certain
        }

        var cells = GeoOps.VoronoiCells(candidates.Select(p => (p.Id, projection.Project(p.Location))).ToList(), circle.EnvelopeInternal);
        return GeoOps.Intersection(GeoOps.Intersection(possibleArea, circle), cells[answer.PlaceId]);
    }

    public QuestionAnswer SuggestAnswer(GeoPoint hider)
    {
        var h = projection.Project(hider);
        if (h.Distance(_seeker) > spec.RadiusMeters)
        {
            return QuestionAnswer.Place(null);
        }

        return QuestionAnswer.Place(QuestionFactory.Nearest(Candidates, h, projection)?.Place.Id);
    }
}

internal sealed class PhotoQuestion(PhotoSpec spec) : IQuestion
{
    public QuestionSpec Spec => spec;

    public Geometry Apply(Geometry possibleArea, QuestionAnswer answer) => possibleArea;

    public QuestionAnswer SuggestAnswer(GeoPoint hider) => new(Text: string.Empty);
}

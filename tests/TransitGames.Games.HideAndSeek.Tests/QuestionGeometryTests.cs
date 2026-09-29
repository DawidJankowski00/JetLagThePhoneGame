using System.Text.Json;
using NetTopologySuite.Geometries;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Projection;
using TransitGames.Geo.Places;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Spatial;

namespace TransitGames.Games.HideAndSeek.Tests;

/// <summary>A synthetic 10 × 10 km game area; positions are given in metres east/north of its centre.</summary>
internal static class World
{
    public static readonly GameArea Area = CreateArea();

    public static LocalProjection Projection => Area.Projection;

    public static GeoPoint At(double east, double north) => Projection.Unproject(new Coordinate(east, north));

    public static Point Local(double east, double north) => GeometryFactories.Local.CreatePoint(new Coordinate(east, north));

    public static Place PlaceAt(string id, double east, double north) => new(id, id.ToUpperInvariant(), At(east, north));

    public static bool Contains(Geometry local, double east, double north) => local.Covers(Local(east, north));

    private static GameArea CreateArea()
    {
        var projection = LocalProjection.CenteredOn(new GeoPoint(52.23, 21.01));
        var square = GeometryFactories.Local.ToGeometry(new Envelope(-5000, 5000, -5000, 5000));
        return GameArea.Create("Test", projection.Unproject(square), GameAreaSource.Drawn);
    }
}

public class QuestionGeometryTests
{
    private static Geometry Full => World.Area.LocalGeometry;

    private static Geometry Apply(QuestionSpec spec, QuestionAnswer answer) =>
        QuestionFactory.Create(spec, World.Projection).Apply(Full, answer);

    [Fact]
    public void Radar_yes_keeps_the_circle_and_no_removes_it()
    {
        var spec = new RadarSpec(World.At(0, 0), 1000);

        var yes = Apply(spec, QuestionAnswer.Yes);
        var no = Apply(spec, QuestionAnswer.No);

        Assert.InRange(yes.Area, Math.PI * 1000 * 1000 * 0.995, Math.PI * 1000 * 1000 * 1.001);
        Assert.True(World.Contains(yes, 900, 0));
        Assert.False(World.Contains(yes, 1100, 0));
        Assert.False(World.Contains(no, 900, 0));
        Assert.True(World.Contains(no, 1100, 0));
        Assert.Equal(Full.Area, yes.Area + no.Area, 1);
    }

    [Fact]
    public void Thermometer_splits_the_area_at_the_perpendicular_bisector()
    {
        // Seekers moved from 2 km west to 2 km east of the centre: the bisector is the north-south axis.
        var spec = new ThermometerSpec(World.At(-2000, 0), World.At(2000, 0));

        var hotter = Apply(spec, QuestionAnswer.Yes);
        var colder = Apply(spec, QuestionAnswer.No);

        Assert.True(World.Contains(hotter, 100, 4000));
        Assert.False(World.Contains(hotter, -100, -4000));
        Assert.True(World.Contains(colder, -100, 0));
        Assert.Equal(Full.Area / 2, hotter.Area, 0);
    }

    [Fact]
    public void Matching_uses_the_voronoi_cell_of_the_seekers_nearest_place()
    {
        Place[] museums = [World.PlaceAt("west", -3000, 0), World.PlaceAt("east", 3000, 0), World.PlaceAt("north", 0, 4000)];
        var spec = new MatchingSpec(World.At(-2500, 500), "museum", museums);

        var same = Apply(spec, QuestionAnswer.Yes);
        var different = Apply(spec, QuestionAnswer.No);

        Assert.Equal("west", QuestionTexts.SeekersPlace(spec)!.Id);
        Assert.True(World.Contains(same, -4000, -4000));
        Assert.False(World.Contains(same, 1000, 0)); // closer to "east"
        Assert.False(World.Contains(same, 0, 3500)); // closer to "north"
        Assert.True(World.Contains(different, 1000, 0));
        Assert.Equal(Full.Area, same.Area + different.Area, 1);
    }

    [Fact]
    public void Measuring_closer_means_within_the_seekers_distance_of_any_place()
    {
        Place[] hospitals = [World.PlaceAt("a", -3000, -3000), World.PlaceAt("b", 3000, 3000)];
        var spec = new MeasuringSpec(World.At(-3000, -2000), "hospital", hospitals); // 1 km from "a"

        var closer = Apply(spec, QuestionAnswer.Yes);

        Assert.True(World.Contains(closer, -3000, -2100));
        Assert.True(World.Contains(closer, 3000, 3900)); // near "b" counts too
        Assert.False(World.Contains(closer, 0, 0));
        Assert.InRange(closer.Area, 2 * Math.PI * 1000 * 1000 * 0.99, 2 * Math.PI * 1000 * 1000 * 1.001);
    }

    [Fact]
    public void Tentacles_answer_is_a_candidate_cell_inside_the_circle()
    {
        Place[] libraries =
        [
            World.PlaceAt("near-west", -800, 0),
            World.PlaceAt("near-east", 800, 0),
            World.PlaceAt("far", 4000, 4000), // outside the 2 km radius: not a candidate
        ];
        var spec = new TentaclesSpec(World.At(0, 0), "library", 2000, libraries);
        var question = QuestionFactory.Create(spec, World.Projection);

        var east = question.Apply(Full, QuestionAnswer.Place("near-east"));
        var outside = question.Apply(Full, QuestionAnswer.Place(null));

        Assert.Equal(["near-west", "near-east"], QuestionTexts.Options(spec).SkipLast(1).Select(o => o.Answer.PlaceId));
        Assert.True(World.Contains(east, 1500, 0));
        Assert.False(World.Contains(east, -100, 0));
        Assert.False(World.Contains(east, 2500, 0)); // outside the circle
        Assert.False(World.Contains(outside, 1500, 0));
        Assert.True(World.Contains(outside, 4000, 4000));
    }

    [Fact]
    public void District_matching_uses_the_district_polygons()
    {
        var west = District("west", new Envelope(-6000, 0, -6000, 6000));
        var east = District("east", new Envelope(0, 6000, -6000, 6000));
        var spec = new MatchingSpec(World.At(-1000, 0), PlaceCategories.DistrictId, [west, east]);
        var question = QuestionFactory.Create(spec, World.Projection);

        var same = question.Apply(Full, QuestionAnswer.Yes);

        Assert.True(World.Contains(same, -4000, 3000));
        Assert.False(World.Contains(same, 1000, 0));
        Assert.Equal(QuestionAnswer.Yes, question.SuggestAnswer(World.At(-10, 0)));
        Assert.Equal(QuestionAnswer.No, question.SuggestAnswer(World.At(10, 0)));
    }

    [Fact]
    public void Photo_does_not_change_the_area()
    {
        var result = Apply(new PhotoSpec("the nearest tree"), new QuestionAnswer(Text: "sent"));

        Assert.Equal(Full.Area, result.Area, 1);
    }

    /// <summary>
    /// The central guarantee: the suggested answer for the hiders' real position never eliminates that position,
    /// whatever the question. Checked on many random positions and questions.
    /// </summary>
    [Fact]
    public void Suggested_answers_never_eliminate_the_hiders_position()
    {
        var random = new Random(42);
        double R() => random.NextDouble() * 9000 - 4500;
        var places = Enumerable.Range(0, 12).Select(i => World.PlaceAt($"p{i}", R(), R())).ToList();

        for (var round = 0; round < 60; round++)
        {
            var (hx, hy) = (R(), R());
            var hider = World.At(hx, hy);
            QuestionSpec[] specs =
            [
                new RadarSpec(World.At(R(), R()), 500 + random.NextDouble() * 3000),
                new ThermometerSpec(World.At(R(), R()), World.At(R(), R())),
                new MatchingSpec(World.At(R(), R()), "museum", places),
                new MeasuringSpec(World.At(R(), R()), "park", places),
                new TentaclesSpec(World.At(R(), R()), "library", 1000 + random.NextDouble() * 3000, places),
            ];

            var possible = Full;
            foreach (var spec in specs)
            {
                var question = QuestionFactory.Create(spec, World.Projection);
                possible = question.Apply(possible, question.SuggestAnswer(hider));
                Assert.True(
                    possible.Distance(World.Local(hx, hy)) < 0.01,
                    $"Round {round}: {spec.Type} eliminated the hiders at ({hx:F0}, {hy:F0}).");
            }
        }
    }

    [Fact]
    public void Questions_round_trip_through_json_with_their_type()
    {
        var question = new AskedQuestion
        {
            Id = Guid.NewGuid(),
            Spec = new TentaclesSpec(World.At(1, 2), "zoo", 1500, [World.PlaceAt("z", 10, 10)]),
            AskedAt = DateTimeOffset.UnixEpoch,
            Answer = QuestionAnswer.Place("z"),
        };

        var json = JsonSerializer.Serialize(question, GameJson.Options);
        var back = JsonSerializer.Deserialize<AskedQuestion>(json, GameJson.Options)!;

        Assert.Contains("\"type\":\"tentacles\"", json);
        var spec = Assert.IsType<TentaclesSpec>(back.Spec);
        Assert.Equal(1500, spec.RadiusMeters);
        Assert.Equal("z", Assert.Single(spec.Places).Id);
        Assert.Equal("z", back.Answer!.PlaceId);
    }

    [Fact]
    public void Voronoi_handles_one_site_and_duplicate_sites()
    {
        var cover = new Envelope(-100, 100, -100, 100);

        var single = GeoOps.VoronoiCells([("only", new Coordinate(0, 0))], cover);
        var duplicates = GeoOps.VoronoiCells([("a", new Coordinate(0, 0)), ("b", new Coordinate(0, 0)), ("c", new Coordinate(50, 0))], cover);

        Assert.True(single["only"].Covers(World.Local(90, 90)));
        Assert.Same(duplicates["a"], duplicates["b"]);
        Assert.True(duplicates["c"].Covers(World.Local(90, 0)));
    }

    private static Place District(string id, Envelope local)
    {
        var polygon = World.Projection.Unproject(GeometryFactories.Local.ToGeometry(local));
        var center = polygon.InteriorPoint;
        return new Place(id, id, new GeoPoint(center.Y, center.X), GeoJson.Write(polygon));
    }
}

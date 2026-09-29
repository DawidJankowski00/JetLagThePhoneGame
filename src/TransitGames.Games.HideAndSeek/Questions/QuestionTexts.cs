using System.Globalization;
using TransitGames.Core.Geography;
using TransitGames.Geo.Projection;
using TransitGames.Games.HideAndSeek.Places;
using TransitGames.Games.HideAndSeek.Resources;

namespace TransitGames.Games.HideAndSeek.Questions;

/// <summary>A possible answer as offered to the hiders (or to the seekers entering it by hand).</summary>
public sealed record AnswerOption(string Label, QuestionAnswer Answer);

/// <summary>Localized texts of questions and answers (history, events, GPX waypoints).</summary>
public static class QuestionTexts
{
    public static string TypeName(QuestionType type) => type switch
    {
        QuestionType.Radar => HideAndSeekStrings.QuestionRadar,
        QuestionType.Thermometer => HideAndSeekStrings.QuestionThermometer,
        QuestionType.Matching => HideAndSeekStrings.QuestionMatching,
        QuestionType.Measuring => HideAndSeekStrings.QuestionMeasuring,
        QuestionType.Tentacles => HideAndSeekStrings.QuestionTentacles,
        QuestionType.Photo => HideAndSeekStrings.QuestionPhoto,
        _ => type.ToString(),
    };

    public static string CategoryName(string categoryId) =>
        HideAndSeekStrings.ResourceManager.GetString("Category_" + categoryId, HideAndSeekStrings.Culture) ?? categoryId;

    public static string Distance(double meters) =>
        meters < 1000
            ? string.Format(CultureInfo.CurrentCulture, "{0:0} m", meters)
            : string.Format(CultureInfo.CurrentCulture, "{0:0.0} km", meters / 1000);

    public static string Title(QuestionSpec spec) => spec switch
    {
        RadarSpec r => $"{TypeName(spec.Type)} · {Distance(r.RadiusMeters)}",
        MatchingSpec m => $"{TypeName(spec.Type)} · {CategoryName(m.CategoryId)}",
        MeasuringSpec m => $"{TypeName(spec.Type)} · {CategoryName(m.CategoryId)}",
        TentaclesSpec t => $"{TypeName(spec.Type)} · {CategoryName(t.CategoryId)} · {Distance(t.RadiusMeters)}",
        _ => TypeName(spec.Type),
    };

    /// <summary>The question as the seekers ask it.</summary>
    public static string Describe(QuestionSpec spec)
    {
        switch (spec)
        {
            case RadarSpec r:
                return string.Format(HideAndSeekStrings.AskRadar, Distance(r.RadiusMeters));
            case ThermometerSpec t:
                return string.Format(HideAndSeekStrings.AskThermometer, Distance(Meters(t.Start, t.End)));
            case MatchingSpec m:
                var place = SeekersPlace(m)?.Name ?? "?";
                return m.CategoryId == PlaceCategories.DistrictId
                    ? string.Format(HideAndSeekStrings.AskMatchingDistrict, place)
                    : string.Format(HideAndSeekStrings.AskMatching, CategoryName(m.CategoryId), place);
            case MeasuringSpec m:
                var question = (MeasuringQuestion)QuestionFactory.Create(m, LocalProjection.CenteredOn(m.SeekerLocation));
                return string.Format(HideAndSeekStrings.AskMeasuring, CategoryName(m.CategoryId), Distance(question.SeekersDistance ?? 0));
            case TentaclesSpec t:
                return string.Format(HideAndSeekStrings.AskTentacles, CategoryName(t.CategoryId), Distance(t.RadiusMeters));
            case PhotoSpec p:
                return string.Format(HideAndSeekStrings.AskPhoto, p.Subject);
            default:
                return TypeName(spec.Type);
        }
    }

    public static string DescribeAnswer(QuestionSpec spec, QuestionAnswer? answer)
    {
        if (answer is null)
        {
            return HideAndSeekStrings.AnswerPending;
        }

        return spec switch
        {
            TentaclesSpec t => answer.PlaceId is null
                ? HideAndSeekStrings.AnswerOutOfRange
                : t.Places.FirstOrDefault(p => p.Id == answer.PlaceId)?.Name ?? answer.PlaceId,
            PhotoSpec => string.IsNullOrWhiteSpace(answer.Text) ? HideAndSeekStrings.AnswerPhotoSent : answer.Text,
            _ => Options(spec).FirstOrDefault(o => o.Answer.IsYes == answer.IsYes)?.Label ?? string.Empty,
        };
    }

    /// <summary>Answers to choose from, in display order.</summary>
    public static IReadOnlyList<AnswerOption> Options(QuestionSpec spec)
    {
        switch (spec)
        {
            case RadarSpec:
                return [new(HideAndSeekStrings.AnswerRadarYes, QuestionAnswer.Yes), new(HideAndSeekStrings.AnswerRadarNo, QuestionAnswer.No)];
            case ThermometerSpec:
                return [new(HideAndSeekStrings.AnswerHotter, QuestionAnswer.Yes), new(HideAndSeekStrings.AnswerColder, QuestionAnswer.No)];
            case MatchingSpec:
                return [new(HideAndSeekStrings.AnswerSame, QuestionAnswer.Yes), new(HideAndSeekStrings.AnswerDifferent, QuestionAnswer.No)];
            case MeasuringSpec:
                return [new(HideAndSeekStrings.AnswerCloser, QuestionAnswer.Yes), new(HideAndSeekStrings.AnswerFarther, QuestionAnswer.No)];
            case TentaclesSpec t:
                var question = (TentaclesQuestion)QuestionFactory.Create(t, LocalProjection.CenteredOn(t.SeekerLocation));
                return
                [
                    .. question.Candidates.Select(p => new AnswerOption(p.Name, QuestionAnswer.Place(p.Id))),
                    new(HideAndSeekStrings.AnswerOutOfRange, QuestionAnswer.Place(null)),
                ];
            case PhotoSpec:
                return [new(HideAndSeekStrings.AnswerPhotoSent, new QuestionAnswer(Text: HideAndSeekStrings.AnswerPhotoSent))];
            default:
                return [];
        }
    }

    /// <summary>Where the question was asked (a GPX waypoint): the seekers' position.</summary>
    public static GeoPoint? Location(QuestionSpec spec) => spec switch
    {
        RadarSpec r => r.SeekerLocation,
        ThermometerSpec t => t.End,
        MatchingSpec m => m.SeekerLocation,
        MeasuringSpec m => m.SeekerLocation,
        TentaclesSpec t => t.SeekerLocation,
        _ => null,
    };

    /// <summary>The seekers' nearest (or containing, for districts) place of a matching question.</summary>
    public static Place? SeekersPlace(MatchingSpec spec)
    {
        var question = QuestionFactory.Create(spec, LocalProjection.CenteredOn(spec.SeekerLocation));
        return question switch
        {
            MatchingQuestion point => point.SeekersPlace,
            AreaMatchingQuestion area => area.SeekersPlace,
            _ => null,
        };
    }

    private static double Meters(GeoPoint a, GeoPoint b) => LocalProjection.CenteredOn(a).Distance(a, b);
}

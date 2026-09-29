using System.Text.Json.Serialization;
using TransitGames.Core.Geography;
using TransitGames.Games.HideAndSeek.Places;

namespace TransitGames.Games.HideAndSeek.Questions;

public enum QuestionType
{
    Radar,
    Thermometer,
    Matching,
    Measuring,
    Tentacles,
    Photo,
}

/// <summary>
/// Parameters of an asked question, as sent between devices and kept in the session state. Positions are the
/// seekers' positions when asking (GPS, optionally corrected on the map). Questions that refer to places carry the
/// places themselves, so both devices compute with exactly the same data, also offline.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RadarSpec), "radar")]
[JsonDerivedType(typeof(ThermometerSpec), "thermometer")]
[JsonDerivedType(typeof(MatchingSpec), "matching")]
[JsonDerivedType(typeof(MeasuringSpec), "measuring")]
[JsonDerivedType(typeof(TentaclesSpec), "tentacles")]
[JsonDerivedType(typeof(PhotoSpec), "photo")]
public abstract record QuestionSpec
{
    [JsonIgnore]
    public QuestionType Type => this switch
    {
        RadarSpec => QuestionType.Radar,
        ThermometerSpec => QuestionType.Thermometer,
        MatchingSpec => QuestionType.Matching,
        MeasuringSpec => QuestionType.Measuring,
        TentaclesSpec => QuestionType.Tentacles,
        _ => QuestionType.Photo,
    };
}

/// <summary>"Are you within <see cref="RadiusMeters"/> of me?"</summary>
public sealed record RadarSpec(GeoPoint SeekerLocation, double RadiusMeters) : QuestionSpec;

/// <summary>Seekers travelled from <see cref="Start"/> to <see cref="End"/>: "Am I hotter (closer to you) now?"</summary>
public sealed record ThermometerSpec(GeoPoint Start, GeoPoint End) : QuestionSpec;

/// <summary>"Is your nearest [category] the same as mine?" (districts: "Are you in the same district?")</summary>
public sealed record MatchingSpec(GeoPoint SeekerLocation, string CategoryId, IReadOnlyList<Place> Places) : QuestionSpec;

/// <summary>"Are you closer to your nearest [category] than I am to mine?"</summary>
public sealed record MeasuringSpec(GeoPoint SeekerLocation, string CategoryId, IReadOnlyList<Place> Places) : QuestionSpec;

/// <summary>"Of all [category] within <see cref="RadiusMeters"/> of me, which one are you closest to?"</summary>
public sealed record TentaclesSpec(GeoPoint SeekerLocation, string CategoryId, double RadiusMeters, IReadOnlyList<Place> Places) : QuestionSpec;

/// <summary>"Send us a photo of ..." — no geometry, only an entry in the history.</summary>
public sealed record PhotoSpec(string Subject) : QuestionSpec;

/// <summary>
/// The hiders' answer. <see cref="IsYes"/>: Radar = inside, Thermometer = hotter, Matching = same,
/// Measuring = closer. <see cref="PlaceId"/>: Tentacles (null = not within the radius). <see cref="Text"/>: Photo.
/// </summary>
public sealed record QuestionAnswer(bool? IsYes = null, string? PlaceId = null, string? Text = null)
{
    public static QuestionAnswer Yes { get; } = new(IsYes: true);

    public static QuestionAnswer No { get; } = new(IsYes: false);

    public static QuestionAnswer Place(string? placeId) => new(PlaceId: placeId);
}

/// <summary>A question in the game history.</summary>
public sealed record AskedQuestion
{
    public required Guid Id { get; init; }
    public required QuestionSpec Spec { get; init; }
    public required DateTimeOffset AskedAt { get; init; }
    public QuestionAnswer? Answer { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }

    /// <summary>The hiders refused to answer with a veto card; the question eliminates nothing.</summary>
    public bool Vetoed { get; init; }

    [JsonIgnore]
    public bool IsAnswered => Answer is not null || Vetoed;
}

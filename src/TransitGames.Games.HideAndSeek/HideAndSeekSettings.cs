using TransitGames.Core.Games;
using TransitGames.Games.HideAndSeek.Questions;

namespace TransitGames.Games.HideAndSeek;

public enum GameSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Whether a question type may be asked, what it earns the hiders, and how long they have to answer.</summary>
public sealed record QuestionRule(bool Enabled, int CardsDrawn, int CardsKept, TimeSpan AnswerTime);

public sealed class HideAndSeekSettings : GameSettings
{
    public GameSize Size { get; set; } = GameSize.Medium;

    /// <summary>Radius of the hiding zone around the chosen station, in meters.</summary>
    public double HidingZoneRadiusMeters { get; set; } = DefaultRadius(GameSize.Medium);

    public TimeSpan HidingTime { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>Hiders draw cards for every answered question (see <see cref="Questions"/>).</summary>
    public bool CardsEnabled { get; set; } = true;

    /// <summary>Custom deck (same JSON format as the built-in Cards/deck.json); null = built-in deck.</summary>
    public string? DeckJson { get; set; }

    /// <summary>Rules per question type; missing types use <see cref="DefaultRule"/>.</summary>
    public Dictionary<QuestionType, QuestionRule> Questions { get; set; } = DefaultRules();

    public static double DefaultRadius(GameSize size) => size switch
    {
        GameSize.Small => 500,
        GameSize.Medium => 1000,
        GameSize.Large => 1500,
        _ => 1000,
    };

    public QuestionRule Rule(QuestionType type) =>
        Questions.TryGetValue(type, out var rule) ? rule : DefaultRule(type);

    public static QuestionRule DefaultRule(QuestionType type) => type switch
    {
        QuestionType.Radar => new(true, 2, 1, TimeSpan.FromMinutes(5)),
        QuestionType.Thermometer => new(true, 2, 1, TimeSpan.FromMinutes(5)),
        QuestionType.Matching => new(true, 3, 1, TimeSpan.FromMinutes(5)),
        QuestionType.Measuring => new(true, 3, 1, TimeSpan.FromMinutes(5)),
        QuestionType.Tentacles => new(true, 4, 2, TimeSpan.FromMinutes(5)),
        QuestionType.Photo => new(true, 1, 1, TimeSpan.FromMinutes(15)),
        _ => new(true, 1, 1, TimeSpan.FromMinutes(5)),
    };

    public static Dictionary<QuestionType, QuestionRule> DefaultRules() =>
        Enum.GetValues<QuestionType>().ToDictionary(t => t, DefaultRule);
}

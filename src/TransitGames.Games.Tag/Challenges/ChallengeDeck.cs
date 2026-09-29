using System.Globalization;
using System.Reflection;
using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;

namespace TransitGames.Games.Tag.Challenges;

/// <summary>Optional fixed place of a challenge; such a challenge becomes a target of its own.</summary>
public sealed record ChallengeLocation(double Latitude, double Longitude, string? Name = null)
{
    public GeoPoint Point => new(Latitude, Longitude);
}

/// <summary>A challenge from the JSON list. Texts are per language ("pl", "en"...).</summary>
public sealed record ChallengeDefinition
{
    public required string Id { get; init; }

    /// <summary>Added to the reward of the target the challenge is done at.</summary>
    public int Reward { get; init; }

    public ChallengeLocation? Location { get; init; }
    public Dictionary<string, string> Title { get; init; } = [];
    public Dictionary<string, string> Text { get; init; } = [];

    public string LocalizedTitle => Localize(Title);

    public string LocalizedText => Localize(Text);

    private static string Localize(IReadOnlyDictionary<string, string> texts) =>
        texts.TryGetValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, out var text) ? text
        : texts.TryGetValue("pl", out var polish) ? polish
        : texts.Values.FirstOrDefault() ?? string.Empty;
}

/// <summary>
/// Challenges the runners do at targets, configurable as JSON. The built-in list is <c>Challenges/challenges.json</c>
/// (own texts); a custom one can be loaded with <see cref="Parse"/>.
/// </summary>
public sealed class ChallengeDeck
{
    public ChallengeDeck(IReadOnlyList<ChallengeDefinition> challenges) => Challenges = challenges;

    public static ChallengeDeck BuiltIn { get; } = LoadBuiltIn();

    public IReadOnlyList<ChallengeDefinition> Challenges { get; }

    /// <summary>Challenges that can be done anywhere; they are handed out to targets.</summary>
    public IEnumerable<ChallengeDefinition> Anywhere => Challenges.Where(c => c.Location is null);

    /// <summary>Challenges tied to a place; each becomes a target.</summary>
    public IEnumerable<ChallengeDefinition> Located => Challenges.Where(c => c.Location is not null);

    public static ChallengeDeck Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ChallengeFile>(json, GameJson.Options)
            ?? throw new JsonException("The challenge file is empty.");
        if (file.Challenges.Count == 0 || file.Challenges.GroupBy(c => c.Id).Any(g => g.Count() > 1))
        {
            throw new JsonException("A challenge list needs at least one challenge and unique ids.");
        }

        if (file.Challenges.Any(c => c.Reward < 0 || c.Title.Count == 0 || c.Text.Count == 0))
        {
            throw new JsonException("Every challenge needs a title, a text and a non-negative reward.");
        }

        return new ChallengeDeck(file.Challenges);
    }

    private static ChallengeDeck LoadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("challenges.json")
            ?? throw new InvalidOperationException("The built-in challenge list is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed class ChallengeFile
    {
        public List<ChallengeDefinition> Challenges { get; init; } = [];
    }
}

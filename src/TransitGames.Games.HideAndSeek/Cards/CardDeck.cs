using System.Globalization;
using System.Text.Json;
using TransitGames.Core.Games;

namespace TransitGames.Games.HideAndSeek.Cards;

public enum CardKind
{
    TimeBonus,
    Curse,
    PowerUp,
}

public static class CardEffects
{
    public const string Veto = "veto";
    public const string Draw = "draw";
}

/// <summary>A card definition from the deck JSON. Texts are per language ("pl", "en"...).</summary>
public sealed record CardDefinition
{
    public required string Id { get; init; }
    public required CardKind Kind { get; init; }
    public int Minutes { get; init; }
    public string? Effect { get; init; }
    public int Count { get; init; } = 1;
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
/// The hiders' deck, configurable as JSON. The built-in deck is <c>Cards/deck.json</c>; a custom one can be loaded
/// with <see cref="Parse"/>.
/// </summary>
public sealed class CardDeck
{
    private readonly Dictionary<string, CardDefinition> _cards;

    public CardDeck(IEnumerable<CardDefinition> cards) => _cards = cards.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public IReadOnlyCollection<CardDefinition> Cards => _cards.Values;

    public static CardDeck BuiltIn { get; } = LoadBuiltIn();

    public CardDefinition? Find(string id) => _cards.GetValueOrDefault(id);

    public CardDefinition Get(string id) =>
        Find(id) ?? throw new KeyNotFoundException($"Unknown card '{id}'.");

    /// <summary>All copies of all cards, shuffled deterministically by <paramref name="seed"/>.</summary>
    public List<string> Shuffle(int seed)
    {
        var pile = _cards.Values.SelectMany(c => Enumerable.Repeat(c.Id, Math.Max(0, c.Count))).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var random = new Random(seed);
        for (var i = pile.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (pile[i], pile[j]) = (pile[j], pile[i]);
        }

        return pile;
    }

    public static CardDeck Parse(string json)
    {
        var file = JsonSerializer.Deserialize<DeckFile>(json, GameJson.Options)
            ?? throw new JsonException("The deck file is empty.");
        if (file.Cards.Count == 0 || file.Cards.GroupBy(c => c.Id).Any(g => g.Count() > 1))
        {
            throw new JsonException("A deck needs at least one card and unique card ids.");
        }

        return new CardDeck(file.Cards);
    }

    private static CardDeck LoadBuiltIn()
    {
        using var stream = typeof(CardDeck).Assembly.GetManifestResourceStream("deck.json")
            ?? throw new InvalidOperationException("The built-in deck is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed record DeckFile(List<CardDefinition> Cards);
}

using System.Reflection;
using System.Text.Json;
using TransitGames.Core.Games;

namespace TransitGames.Games.Tag.Cards;

/// <summary>
/// The Challenge and Curse cards the runner draws from, configurable as JSON. The built-in deck is
/// <c>Cards/cards.json</c> (own texts); a custom one is loaded with <see cref="Parse"/>, which also accepts the
/// challenge list of the first Tag version.
/// </summary>
public sealed class CardDeck
{
    public CardDeck(IReadOnlyList<CardDefinition> cards) => Cards = cards;

    public static CardDeck BuiltIn { get; } = LoadBuiltIn();

    public IReadOnlyList<CardDefinition> Cards { get; }

    public CardDefinition? Find(string id) => Cards.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// A random card, preferring ones not drawn yet in this game; when all were drawn the deck is reshuffled.
    /// </summary>
    public CardDefinition Draw(IReadOnlyCollection<string> drawnIds, Random random)
    {
        var fresh = Cards.Where(c => !drawnIds.Contains(c.Id)).ToList();
        var pool = fresh.Count > 0 ? fresh : Cards;
        return pool[random.Next(pool.Count)];
    }

    public static CardDeck Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        List<CardDefinition> cards;
        if (root.ValueKind == JsonValueKind.Object && HasProperty(root, "cards"))
        {
            cards = root.Deserialize<DeckFile>(GameJson.Options)?.Cards ?? [];
        }
        else if (root.ValueKind == JsonValueKind.Object && HasProperty(root, "challenges"))
        {
            cards = [.. root.Deserialize<LegacyChallengeFile>(GameJson.Options)?.ToCards() ?? []];
        }
        else
        {
            throw new JsonException("A deck needs a \"cards\" list.");
        }

        Validate(cards);
        return new CardDeck(cards);
    }

    private static void Validate(IReadOnlyList<CardDefinition> cards)
    {
        if (cards.Count == 0 || cards.GroupBy(c => c.Id).Any(g => g.Count() > 1) || cards.Any(c => string.IsNullOrWhiteSpace(c.Id)))
        {
            throw new JsonException("A deck needs at least one card and unique ids.");
        }

        foreach (var card in cards)
        {
            if (card.Reward < 0 || card.Title.Values.All(string.IsNullOrWhiteSpace) || card.Description.Values.All(string.IsNullOrWhiteSpace))
            {
                throw new JsonException($"Card '{card.Id}' needs a title, a description and a non-negative reward.");
            }

            if (card.TimeLimitMinutes is <= 0)
            {
                throw new JsonException($"Card '{card.Id}': timeLimitMinutes must be positive.");
            }

            if (card.Type == CardType.Curse)
            {
                if (card.Duration is not null && !CurseDuration.TryParse(card.Duration, out _))
                {
                    throw new JsonException($"Card '{card.Id}': duration must be untilEndOfTurn, wholeGame or minutes:N.");
                }

                if (card.Restriction is { IsValid: false })
                {
                    throw new JsonException($"Card '{card.Id}': invalid restriction.");
                }
            }
            else if (card.Restriction is not null || card.Duration is not null)
            {
                throw new JsonException($"Card '{card.Id}': only curses have a duration and a restriction.");
            }
        }
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static CardDeck LoadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("cards.json")
            ?? throw new InvalidOperationException("The built-in deck is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed class DeckFile
    {
        public List<CardDefinition> Cards { get; init; } = [];
    }
}

using System.Text.Json.Serialization;

namespace TransitGames.Games.Tag.Cards;

/// <summary>
/// The challenge list of the first Tag version (<c>{"challenges": [{id, reward, title, text, location}]}</c>). Such a
/// file still loads: every challenge becomes a <see cref="CardType.Challenge"/> card (the location is dropped —
/// cards are done wherever the runner is).
/// </summary>
internal sealed class LegacyChallengeFile
{
    public List<LegacyChallenge> Challenges { get; init; } = [];

    public IEnumerable<CardDefinition> ToCards() => Challenges.Select(c => new CardDefinition
    {
        Id = c.Id,
        Type = CardType.Challenge,
        Title = c.Title,
        Description = c.Text,
        Reward = c.Reward,
    });

    internal sealed class LegacyChallenge
    {
        public string Id { get; init; } = string.Empty;
        public int Reward { get; init; }

        [JsonConverter(typeof(LocalizedTextConverter))]
        public Dictionary<string, string> Title { get; init; } = [];

        [JsonConverter(typeof(LocalizedTextConverter))]
        public Dictionary<string, string> Text { get; init; } = [];
    }
}

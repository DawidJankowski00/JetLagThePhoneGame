using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransitGames.Games.Tag.Coins;

namespace TransitGames.Games.Tag.Cards;

public enum CardType
{
    /// <summary>A task; the runner gets the reward after confirming it is done.</summary>
    Challenge,

    /// <summary>Gives the reward at once, but puts a handicap on the runner (<see cref="CardDefinition.Duration"/>).</summary>
    Curse,
}

public enum CurseDurationKind
{
    /// <summary>Until the runner is caught (or the game ends).</summary>
    UntilEndOfTurn,

    /// <summary>For the rest of the game — also in the player's later turns as the runner.</summary>
    WholeGame,

    /// <summary>A number of minutes from drawing the card.</summary>
    Minutes,
}

/// <summary>How long a curse lasts; written in the deck as <c>untilEndOfTurn</c>, <c>wholeGame</c> or <c>minutes:N</c>.</summary>
public readonly record struct CurseDuration(CurseDurationKind Kind, int Minutes = 0)
{
    public static CurseDuration UntilEndOfTurn { get; } = new(CurseDurationKind.UntilEndOfTurn);

    public static CurseDuration WholeGame { get; } = new(CurseDurationKind.WholeGame);

    public static CurseDuration ForMinutes(int minutes) => new(CurseDurationKind.Minutes, minutes);

    public static bool TryParse(string? text, out CurseDuration duration)
    {
        duration = UntilEndOfTurn;
        var value = text?.Trim() ?? string.Empty;
        if (value.Equals("untilEndOfTurn", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("wholeGame", StringComparison.OrdinalIgnoreCase))
        {
            duration = WholeGame;
            return true;
        }

        const string prefix = "minutes:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
        {
            duration = ForMinutes(minutes);
            return true;
        }

        return false;
    }

    public override string ToString() => Kind switch
    {
        CurseDurationKind.WholeGame => "wholeGame",
        CurseDurationKind.Minutes => string.Create(CultureInfo.InvariantCulture, $"minutes:{Minutes}"),
        _ => "untilEndOfTurn",
    };
}

public enum RestrictionKind
{
    /// <summary>The listed means of transport may not be used.</summary>
    BanTransport,

    /// <summary>Only the listed means of transport may be used.</summary>
    OnlyTransport,

    /// <summary>Every fare costs <see cref="CardRestriction.Factor"/> times more.</summary>
    FareMultiplier,

    /// <summary>No card may be drawn while the curse lasts.</summary>
    NoDraw,
}

/// <summary>
/// A handicap the app enforces while its curse is active. Curses without one are honour rules the app only shows.
/// </summary>
public sealed record CardRestriction
{
    public required RestrictionKind Kind { get; init; }

    /// <summary>Means of transport for <see cref="RestrictionKind.BanTransport"/> / <see cref="RestrictionKind.OnlyTransport"/>.</summary>
    public List<TransportMode> Modes { get; init; } = [];

    /// <summary>Fare multiplier for <see cref="RestrictionKind.FareMultiplier"/>.</summary>
    public double Factor { get; init; } = 1;

    public bool IsValid => Kind switch
    {
        RestrictionKind.BanTransport or RestrictionKind.OnlyTransport => Modes.Count > 0,
        RestrictionKind.FareMultiplier => Factor > 0 && double.IsFinite(Factor),
        _ => true,
    };
}

/// <summary>A card of the deck JSON. Texts are per language (<c>{"pl": "...", "en": "..."}</c>) or a plain string.</summary>
public sealed record CardDefinition
{
    public required string Id { get; init; }

    public required CardType Type { get; init; }

    [JsonConverter(typeof(LocalizedTextConverter))]
    public Dictionary<string, string> Title { get; init; } = [];

    [JsonConverter(typeof(LocalizedTextConverter))]
    public Dictionary<string, string> Description { get; init; } = [];

    /// <summary>Coins: for a challenge when done, for a curse at once.</summary>
    public int Reward { get; init; }

    /// <summary>Challenge: time to do it; after that it can no longer be completed.</summary>
    public int? TimeLimitMinutes { get; init; }

    /// <summary>Curse: <c>untilEndOfTurn</c> (default), <c>wholeGame</c> or <c>minutes:N</c>.</summary>
    public string? Duration { get; init; }

    /// <summary>Curse: what the app enforces; none = honour rule.</summary>
    public CardRestriction? Restriction { get; init; }

    [JsonIgnore]
    public CurseDuration CurseDuration => CurseDuration.TryParse(Duration ?? "untilEndOfTurn", out var parsed) ? parsed : CurseDuration.UntilEndOfTurn;

    [JsonIgnore]
    public string LocalizedTitle => Localize(Title);

    [JsonIgnore]
    public string LocalizedDescription => Localize(Description);

    internal static string Localize(IReadOnlyDictionary<string, string> texts) =>
        texts.TryGetValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, out var text) ? text
        : texts.TryGetValue("pl", out var polish) ? polish
        : texts.Values.FirstOrDefault() ?? string.Empty;
}

/// <summary>Reads a text either as <c>{"pl": "...", "en": "..."}</c> or as a plain string (any language).</summary>
internal sealed class LocalizedTextConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new Dictionary<string, string> { ["*"] = reader.GetString() ?? string.Empty };
        }

        var texts = new Dictionary<string, string>();
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A text must be a string or an object of languages.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var language = reader.GetString() ?? string.Empty;
            reader.Read();
            texts[language] = reader.GetString() ?? string.Empty;
        }

        return texts;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (language, text) in value)
        {
            writer.WriteString(language, text);
        }

        writer.WriteEndObject();
    }
}

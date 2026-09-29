using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransitGames.Core.Games;

/// <summary>Shared JSON options for everything persisted by games (settings, state, events, decks).</summary>
public static class GameJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

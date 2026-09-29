using System.Text.Json;
using System.Text.Json.Serialization;
using TransitGames.Core.Games;

namespace TransitGames.Sync;

/// <summary>JSON conventions of the online protocol; the server uses the same (camelCase, enums as strings).</summary>
public static class OnlineJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = GameJson.Options.PropertyNamingPolicy;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}

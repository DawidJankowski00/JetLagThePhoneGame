using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransitGames.Geo.Overpass;

public sealed class OverpassOptions
{
    /// <summary>Public Overpass instances, tried in order until one answers.</summary>
    public List<Uri> Endpoints { get; set; } =
    [
        new("https://overpass-api.de/api/interpreter"),
        new("https://maps.mail.ru/osm/tools/overpass/api/interpreter"),
    ];

    /// <summary>Identifies the app to the Overpass/OSM operators (required by their usage policies).</summary>
    public string UserAgent { get; set; } = GeoDefaults.UserAgent;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How many times the whole endpoint list is tried. Public instances often answer 504/429 for a few seconds
    /// under load, so a second round after <see cref="RetryDelay"/> usually succeeds.
    /// </summary>
    public int Rounds { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(4);
}

public interface IOverpassClient
{
    /// <exception cref="OverpassException">No endpoint returned a usable answer.</exception>
    Task<OverpassResponse> QueryAsync(string query, CancellationToken cancellationToken = default);
}

public sealed class OverpassClient : IOverpassClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OverpassOptions _options;

    public OverpassClient(HttpClient http, OverpassOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<OverpassResponse> QueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        for (var round = 0; round < Math.Max(1, _options.Rounds); round++)
        {
            if (round > 0)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            if (await TryEndpointsAsync(query, failures, cancellationToken).ConfigureAwait(false) is { } result)
            {
                return result;
            }
        }

        throw new OverpassException("No Overpass server answered. " + string.Join("; ", failures.Distinct()));
    }

    /// <summary>Tries every endpoint once; null when all failed with a retryable error (recorded in <paramref name="failures"/>).</summary>
    private async Task<OverpassResponse?> TryEndpointsAsync(string query, List<string> failures, CancellationToken cancellationToken)
    {
        foreach (var endpoint in _options.Endpoints)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]),
                };
                request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (IsRetryable(response.StatusCode))
                {
                    failures.Add($"{endpoint.Host}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new OverpassException($"Overpass rejected the query (HTTP {(int)response.StatusCode}).");
                }

                var result = await response.Content.ReadFromJsonAsync<OverpassResponse>(JsonOptions, timeout.Token)
                    .ConfigureAwait(false) ?? throw new OverpassException("Overpass returned an empty answer.");

                // Overpass reports query timeouts / memory errors with HTTP 200 and a "remark".
                if (result.Remark is { } remark && remark.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{endpoint.Host}: {remark}");
                    continue;
                }

                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{endpoint.Host}: timeout");
            }
            catch (HttpRequestException ex)
            {
                failures.Add($"{endpoint.Host}: {ex.Message}");
            }
            catch (JsonException ex)
            {
                failures.Add($"{endpoint.Host}: invalid response ({ex.Message})");
            }
        }

        return null;
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.GatewayTimeout or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError;
}

public sealed class OverpassException : Exception
{
    public OverpassException(string message)
        : base(message)
    {
    }
}

public sealed class OverpassResponse
{
    public List<OverpassElement> Elements { get; set; } = [];

    public string? Remark { get; set; }
}

public sealed class OverpassElement
{
    public string Type { get; set; } = "";

    public long Id { get; set; }

    public double? Lat { get; set; }

    public double? Lon { get; set; }

    /// <summary>Present for ways/relations queried with <c>out center</c>.</summary>
    public OverpassPoint? Center { get; set; }

    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>Present for relations queried with <c>out geom</c>.</summary>
    public List<OverpassMember>? Members { get; set; }

    public string? Tag(string key) => Tags is not null && Tags.TryGetValue(key, out var value) ? value : null;
}

public sealed class OverpassMember
{
    public string Type { get; set; } = "";

    [JsonPropertyName("ref")]
    public long Ref { get; set; }

    public string Role { get; set; } = "";

    /// <summary>Way geometry; individual points can be null when a node lies outside the query bbox.</summary>
    public List<OverpassPoint?>? Geometry { get; set; }
}

public sealed record OverpassPoint(double Lat, double Lon);

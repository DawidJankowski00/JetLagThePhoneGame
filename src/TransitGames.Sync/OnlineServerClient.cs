using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TransitGames.Core.Sync;

namespace TransitGames.Sync;

/// <summary>A call to the game server failed; <see cref="Code"/> is one of <see cref="OnlineErrorCodes"/> or "network".</summary>
public sealed class OnlineException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string NetworkError = "network";
    public const string IncompatibleServer = "incompatible";
    public const string UnknownGame = "unknown_game";

    public string Code { get; } = code;
}

public static class OnlineServerUrl
{
    /// <summary>
    /// Accepts what people type ("10.8.0.1:5080", "http://games.lan/") and returns "scheme://host[:port]" without
    /// a trailing slash, or null when it is not a usable http(s) address. Plain http is the default because the
    /// server is reached over a VPN.
    /// </summary>
    public static string? Normalize(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(uri.Host)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
    }
}

/// <summary>REST part of the online protocol: server info, creating and joining games.</summary>
public sealed class OnlineServerClient
{
    private readonly HttpClient _http;

    public OnlineServerClient(HttpClient http) => _http = http;

    public async Task<ServerInfo> GetInfoAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        var info = await SendAsync<ServerInfo>(HttpMethod.Get, serverUrl, "/api/info", null, cancellationToken);
        if (info.ProtocolVersion != OnlineProtocol.Version)
        {
            throw new OnlineException(
                OnlineException.IncompatibleServer,
                $"Server protocol {info.ProtocolVersion}, app protocol {OnlineProtocol.Version}.");
        }

        return info;
    }

    public Task<LobbyTicket> CreateLobbyAsync(string serverUrl, CreateLobbyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<LobbyTicket>(HttpMethod.Post, serverUrl, "/api/lobbies", request, cancellationToken);

    public Task<LobbyInfo> GetLobbyAsync(string serverUrl, string code, CancellationToken cancellationToken = default) =>
        SendAsync<LobbyInfo>(HttpMethod.Get, serverUrl, $"/api/lobbies/{Uri.EscapeDataString(OnlineProtocol.NormalizeCode(code))}", null, cancellationToken);

    public Task<JoinLobbyResult> JoinLobbyAsync(string serverUrl, string code, JoinLobbyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<JoinLobbyResult>(HttpMethod.Post, serverUrl, $"/api/lobbies/{Uri.EscapeDataString(OnlineProtocol.NormalizeCode(code))}/join", request, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string serverUrl, string path, object? body, CancellationToken cancellationToken)
    {
        var baseUrl = OnlineServerUrl.Normalize(serverUrl)
            ?? throw new OnlineException(OnlineErrorCodes.InvalidRequest, $"Invalid server address '{serverUrl}'.");
        using var request = new HttpRequestMessage(method, baseUrl + path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: OnlineJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new OnlineException(OnlineException.NetworkError, ex.Message, ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(OnlineJson.Options, cancellationToken).ConfigureAwait(false)
                    ?? throw new OnlineException(OnlineException.IncompatibleServer, "Empty response.");
            }

            OnlineError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<OnlineError>(OnlineJson.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // Not our server (or a proxy error page): report the status code below.
            }

            throw error is { Code: not null }
                ? new OnlineException(error.Code, error.Message)
                : new OnlineException(
                    response.StatusCode == HttpStatusCode.NotFound ? OnlineException.IncompatibleServer : OnlineException.NetworkError,
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }
}

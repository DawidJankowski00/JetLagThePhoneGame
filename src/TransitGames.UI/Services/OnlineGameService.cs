using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Sync;
using TransitGames.UI.Settings;

namespace TransitGames.UI.Services;

/// <summary>Creates online games on the server and joins them by code.</summary>
public sealed class OnlineGameService
{
    private readonly OnlineServerClient _server;
    private readonly OnlineSessionStore _store;
    private readonly OnlinePreferences _preferences;
    private readonly GameSessionManager _sessions;
    private readonly GameRegistry _registry;

    public OnlineGameService(
        OnlineServerClient server,
        OnlineSessionStore store,
        OnlinePreferences preferences,
        GameSessionManager sessions,
        GameRegistry registry)
    {
        _server = server;
        _store = store;
        _preferences = preferences;
        _sessions = sessions;
        _registry = registry;
    }

    /// <summary>
    /// Registers the game on the server first and only then starts it locally with the same session id, so a
    /// server error leaves nothing behind.
    /// </summary>
    public async Task<(IGameSession Session, LobbyTicket Ticket)> StartHostedAsync(IGameModule module, GameSettings settings, string roleId)
    {
        var serverUrl = RequireServer();
        var sessionId = Guid.NewGuid();
        settings.StartedAt = DateTimeOffset.UtcNow;
        var request = new CreateLobbyRequest(
            sessionId,
            module.Id,
            module.DisplayName,
            module.SerializeSettings(settings),
            module.Roles.Select(r => new LobbyRole(r.Id, r.DisplayName)).ToList(),
            [.. module.LocationSharing.Rules],
            roleId,
            _preferences.PlayerName);
        var ticket = await _server.CreateLobbyAsync(serverUrl, request);
        await _store.SaveLinkAsync(OnlineLink.From(serverUrl, ticket, _preferences.PlayerName));
        var session = await _sessions.StartNewAsync(module.Id, settings, roleId, sessionId);
        return (session, ticket);
    }

    public Task<LobbyInfo> FindAsync(string code) => _server.GetLobbyAsync(RequireServer(), code);

    /// <summary>Joins with the given role and starts the game locally with the host's settings (area, stations...).</summary>
    public async Task<IGameSession> JoinAsync(LobbyInfo lobby, string roleId)
    {
        var serverUrl = RequireServer();
        var module = _registry.TryGet(lobby.GameId, out var m) && m is not null
            ? m
            : throw new OnlineException(OnlineException.UnknownGame, lobby.GameName);
        var result = await _server.JoinLobbyAsync(serverUrl, lobby.Code, new JoinLobbyRequest(roleId, _preferences.PlayerName));
        var settings = module.DeserializeSettings(result.SettingsJson);
        await _store.SaveLinkAsync(OnlineLink.From(serverUrl, result.Ticket, _preferences.PlayerName));
        return await _sessions.StartNewAsync(module.Id, settings, roleId, result.Ticket.SessionId);
    }

    private string RequireServer() =>
        _preferences.IsConfigured
            ? _preferences.ServerUrl
            : throw new OnlineException(OnlineErrorCodes.InvalidRequest, Resources.UiStrings.OnlineServerMissing);
}

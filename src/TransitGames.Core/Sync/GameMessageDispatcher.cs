using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Sync;

/// <summary>
/// Applies a game's own sync messages to its session state. Registered by the game module; called for every
/// message of a session of that game (own messages included, because transports echo them), so handling must be
/// idempotent — typically keyed by <see cref="SyncMessage.Id"/> or by ids inside the payload.
/// </summary>
public interface IGameMessageHandler
{
    string GameId { get; }

    void Handle(IGameSession session, SyncMessage message);
}

/// <summary>
/// Routes messages from <see cref="IGameSyncService"/> to the <see cref="IGameMessageHandler"/> of the session's
/// game — also when that session is not on screen, so nothing sent by the other team is lost.
/// </summary>
public sealed class GameMessageDispatcher
{
    private readonly GameSessionManager _sessions;
    private readonly Dictionary<string, IGameMessageHandler> _handlers;
    private readonly ILogger<GameMessageDispatcher> _logger;

    public GameMessageDispatcher(
        IGameSyncService sync,
        GameSessionManager sessions,
        IEnumerable<IGameMessageHandler> handlers,
        ILogger<GameMessageDispatcher>? logger = null)
    {
        _sessions = sessions;
        _handlers = handlers.ToDictionary(h => h.GameId, StringComparer.Ordinal);
        _logger = logger ?? NullLogger<GameMessageDispatcher>.Instance;
        sync.MessageReceived += OnMessageReceived;
    }

    private async void OnMessageReceived(object? sender, SyncMessage message)
    {
        try
        {
            await DispatchAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling message {Type} failed", message.Type);
        }
    }

    public Task<bool> DispatchAsync(SyncMessage message) =>
        message.Type is SyncMessageTypes.Location or SyncMessageTypes.SessionFinished
            ? Task.FromResult(false) // handled by the app, not by games
            : _sessions.MutateAsync(message.SessionId, session =>
        {
            if (_handlers.TryGetValue(session.GameId, out var handler))
            {
                handler.Handle(session, message);
            }

            // After the handler, so it still sees the roles the message was sent under.
            if (message.RoleChange is { } change)
            {
                session.ApplyRoleChange(change, message.Timestamp);
            }
        });
}

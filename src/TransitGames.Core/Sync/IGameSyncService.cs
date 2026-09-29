namespace TransitGames.Core.Sync;

/// <summary>Who may see a published message. Makes location privacy explicit.</summary>
public enum SyncAudience
{
    /// <summary>Only devices of the same team.</summary>
    OwnTeam,

    /// <summary>All players, including the opposing team.</summary>
    Everyone,
}

/// <summary>A message exchanged between devices (question, answer, location ping, event...).</summary>
public sealed record SyncMessage
{
    public required Guid Id { get; init; }
    public required Guid SessionId { get; init; }
    public required string Type { get; init; }
    public required string SenderRoleId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required SyncAudience Audience { get; init; }

    /// <summary>JSON payload, schema defined by the game for the given <see cref="Type"/>.</summary>
    public required string PayloadJson { get; init; }
}

/// <summary>
/// Transport between devices. Offline play uses <see cref="LocalGameSyncService"/> (teams talk via a messenger
/// and enter data by hand); the online phase plugs a SignalR implementation behind the same interface.
/// </summary>
public interface IGameSyncService
{
    bool IsOnline { get; }

    event EventHandler<SyncMessage>? MessageReceived;

    Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default);
}

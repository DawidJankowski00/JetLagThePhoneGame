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

    /// <summary>Sending device; filled in by the server (clients cannot spoof it). Null offline.</summary>
    public Guid? SenderDeviceId { get; init; }

    /// <summary>Player name of the sending device; filled in by the server.</summary>
    public string? SenderName { get; init; }

    /// <summary>Roles change caused by this message (e.g. a catch swapping runners and chasers), see <see cref="Sync.RoleChange"/>.</summary>
    public RoleChange? RoleChange { get; init; }
}

public enum SyncConnectionState
{
    /// <summary>Offline game: nothing leaves the device.</summary>
    Local,
    Connecting,
    Connected,

    /// <summary>Connection lost (e.g. in the metro); messages are queued and sent after reconnecting.</summary>
    Reconnecting,
    Disconnected,
}

/// <summary>A device taking part in an online game.</summary>
public sealed record SyncPeer(Guid DeviceId, string Name, string RoleId, bool IsConnected);

/// <summary>
/// Transport between devices. Offline play uses <see cref="LocalGameSyncService"/> (teams talk via a messenger
/// and enter data by hand); online play uses the SignalR implementation from TransitGames.Sync.
/// Published messages are always echoed back through <see cref="MessageReceived"/>, so the local UI reacts to
/// its own messages exactly like to messages from other devices.
/// </summary>
public interface IGameSyncService
{
    bool IsOnline { get; }

    SyncConnectionState State { get; }

    /// <summary>Devices of the current online game (empty offline).</summary>
    IReadOnlyList<SyncPeer> Peers { get; }

    event EventHandler? StateChanged;

    event EventHandler? PeersChanged;

    event EventHandler<SyncMessage>? MessageReceived;

    Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default);
}

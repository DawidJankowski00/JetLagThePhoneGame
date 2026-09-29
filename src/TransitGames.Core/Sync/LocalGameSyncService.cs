namespace TransitGames.Core.Sync;

/// <summary>
/// Offline transport: nothing leaves the device. Every published message is echoed back so the local UI
/// reacts exactly as it would online, and kept in <see cref="Outbox"/> so the UI can show what should be
/// relayed to the other team manually (messenger, phone call).
/// </summary>
public sealed class LocalGameSyncService : IGameSyncService
{
    private readonly List<SyncMessage> _outbox = [];

    public bool IsOnline => false;

    public IReadOnlyList<SyncMessage> Outbox => _outbox;

    public event EventHandler<SyncMessage>? MessageReceived;

    public Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default)
    {
        _outbox.Clear();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default)
    {
        _outbox.Add(message);
        MessageReceived?.Invoke(this, message);
        return Task.CompletedTask;
    }
}

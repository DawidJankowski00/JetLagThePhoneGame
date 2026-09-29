namespace TransitGames.Core.Sync;

/// <summary>
/// The <see cref="IGameSyncService"/> that games use. It forwards to the transport of the current session:
/// <see cref="LocalGameSyncService"/> for offline games, the SignalR transport for online ones. Games never need
/// to know which one is active.
/// </summary>
public sealed class GameSyncRouter : IGameSyncService
{
    private readonly object _gate = new();
    private IGameSyncService _target;

    public GameSyncRouter(LocalGameSyncService local)
    {
        _target = local;
        Subscribe(local);
    }

    public IGameSyncService Target
    {
        get
        {
            lock (_gate)
            {
                return _target;
            }
        }
    }

    public bool IsOnline => Target.IsOnline;

    public SyncConnectionState State => Target.State;

    public IReadOnlyList<SyncPeer> Peers => Target.Peers;

    public event EventHandler? StateChanged;

    public event EventHandler? PeersChanged;

    public event EventHandler<SyncMessage>? MessageReceived;

    /// <summary>Switches to another transport; listeners keep their subscriptions.</summary>
    public void Use(IGameSyncService target)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_target, target))
            {
                return;
            }

            Unsubscribe(_target);
            _target = target;
            Subscribe(target);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        PeersChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default) =>
        Target.ConnectAsync(sessionId, roleId, cancellationToken);

    public Task DisconnectAsync() => Target.DisconnectAsync();

    public Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default) =>
        Target.PublishAsync(message, cancellationToken);

    private void Subscribe(IGameSyncService target)
    {
        target.StateChanged += OnStateChanged;
        target.PeersChanged += OnPeersChanged;
        target.MessageReceived += OnMessageReceived;
    }

    private void Unsubscribe(IGameSyncService target)
    {
        target.StateChanged -= OnStateChanged;
        target.PeersChanged -= OnPeersChanged;
        target.MessageReceived -= OnMessageReceived;
    }

    private void OnStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, e);

    private void OnPeersChanged(object? sender, EventArgs e) => PeersChanged?.Invoke(this, e);

    private void OnMessageReceived(object? sender, SyncMessage message) => MessageReceived?.Invoke(this, message);
}

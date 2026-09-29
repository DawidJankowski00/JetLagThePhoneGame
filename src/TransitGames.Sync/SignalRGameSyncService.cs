using System.Net;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransitGames.Core.Sync;

namespace TransitGames.Sync;

/// <summary>Transport tweaks, used by tests to route connections into an in-memory server.</summary>
public sealed class OnlineConnectionOptions
{
    public Action<HttpConnectionOptions>? ConfigureHttp { get; set; }

    /// <summary>Delays between reconnect attempts; the last one repeats forever.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; set; } =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
}

/// <summary>
/// Online transport over SignalR. Built for bad mobile connectivity:
/// <list type="bullet">
/// <item>published messages go to a persistent outbox first and are echoed locally at once; the outbox is sent
/// (in order, idempotently) whenever a connection is up;</item>
/// <item>it reconnects forever; after every (re)connect it catches up on missed messages by sequence number,
/// buffering live messages meanwhile so everything is delivered once and in order;</item>
/// <item>after connecting it also delivers the latest known locations of the other devices (so the map is
/// filled right after an app restart);</item>
/// <item>it follows role changes (<see cref="RoleChange"/>) carried by messages, like the server does, and updates
/// <see cref="Link"/> before the message is delivered to handlers.</item>
/// </list>
/// </summary>
public sealed class SignalRGameSyncService : IGameSyncService, IAsyncDisposable
{
    private readonly OnlineSessionStore _store;
    private readonly OnlineConnectionOptions _options;
    private readonly ILogger<SignalRGameSyncService> _logger;
    private readonly SemaphoreSlim _deliverLock = new(1, 1);
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly object _gate = new();
    private readonly List<SequencedMessage> _buffer = [];

    private HubConnection? _connection;
    private CancellationTokenSource? _lifetime;
    private OnlineLink? _link;
    private long _lastSequence;
    private bool _catchingUp;
    private bool _latestLocationsDelivered;

    public SignalRGameSyncService(OnlineSessionStore store, OnlineConnectionOptions? options = null, ILogger<SignalRGameSyncService>? logger = null)
    {
        _store = store;
        _options = options ?? new OnlineConnectionOptions();
        _logger = logger ?? NullLogger<SignalRGameSyncService>.Instance;
    }

    public bool IsOnline => true;

    public SyncConnectionState State { get; private set; } = SyncConnectionState.Disconnected;

    public IReadOnlyList<SyncPeer> Peers { get; private set; } = [];

    /// <summary>The link of the connected game (null when not connected to any).</summary>
    public OnlineLink? Link => _link;

    /// <summary>Set when the server no longer knows this device (e.g. the game was deleted).</summary>
    public bool IsRejected { get; private set; }

    public event EventHandler? StateChanged;

    public event EventHandler? PeersChanged;

    public event EventHandler<SyncMessage>? MessageReceived;

    /// <summary>Starts connecting to the game of <paramref name="sessionId"/>. Returns after the first attempt; retries go on in the background.</summary>
    public async Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var link = await _store.GetLinkAsync(sessionId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session {sessionId} is not an online game.");

        _link = link;
        _lastSequence = link.LastSequence;
        _latestLocationsDelivered = false;
        IsRejected = false;
        _lifetime = new CancellationTokenSource();

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(link.ServerUrl.TrimEnd('/') + OnlineProtocol.HubPath), http =>
            {
                http.AccessTokenProvider = () => Task.FromResult<string?>(link.Token);
                _options.ConfigureHttp?.Invoke(http);
            })
            .WithAutomaticReconnect(new RetryPolicy(_options.RetryDelays))
            .AddJsonProtocol(o => OnlineJson.Configure(o.PayloadSerializerOptions))
            .Build();

        connection.On<SequencedMessage>(OnlineProtocol.ReceiveMethod, OnLiveMessageAsync);
        connection.On<List<SyncPeer>>(OnlineProtocol.PeersMethod, peers =>
        {
            Peers = peers;
            PeersChanged?.Invoke(this, EventArgs.Empty);
        });
        connection.Reconnecting += _ =>
        {
            BeginCatchUp();
            SetState(SyncConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        connection.Reconnected += _ => OnConnectedAsync(connection);
        connection.Closed += error =>
        {
            // Automatic reconnect never gives up, so this is a stop or a rejected token; restart unless stopped.
            if (_lifetime is { IsCancellationRequested: false } lifetime && ReferenceEquals(connection, _connection))
            {
                _ = StartLoopAsync(connection, lifetime.Token);
            }

            return Task.CompletedTask;
        };
        _connection = connection;

        SetState(SyncConnectionState.Connecting);
        await StartLoopAsync(connection, _lifetime.Token, firstAttemptOnly: true).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        var connection = _connection;
        _connection = null;
        if (_lifetime is { } lifetime)
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            lifetime.Dispose();
            _lifetime = null;
        }

        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while closing the connection");
            }
        }

        _link = null;
        lock (_gate)
        {
            _buffer.Clear();
            _catchingUp = false;
        }

        Peers = [];
        SetState(SyncConnectionState.Disconnected);
    }

    public async Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default)
    {
        var link = _link ?? throw new InvalidOperationException("Not connected to an online game.");
        var stamped = message with
        {
            SessionId = link.SessionId,
            SenderRoleId = link.RoleId,
            SenderDeviceId = link.DeviceId,
            SenderName = link.PlayerName,
        };

        await _store.EnqueueAsync(stamped).ConfigureAwait(false);
        await FollowRoleChangeAsync(stamped).ConfigureAwait(false);
        MessageReceived?.Invoke(this, stamped);
        if (State == SyncConnectionState.Connected)
        {
            await FlushOutboxAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    private async Task StartLoopAsync(HubConnection connection, CancellationToken cancellationToken, bool firstAttemptOnly = false)
    {
        for (var attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                BeginCatchUp();
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
                await OnConnectedAsync(connection).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(ex, "The server rejected this device; the game no longer exists there");
                IsRejected = true;
                SetState(SyncConnectionState.Disconnected);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(ex, "Connecting to the game server failed (attempt {Attempt})", attempt + 1);
                SetState(SyncConnectionState.Reconnecting);
                if (firstAttemptOnly)
                {
                    // Keep trying in the background; the caller (UI) must not wait for the network.
                    _ = StartLoopAsync(connection, cancellationToken);
                    return;
                }

                try
                {
                    await Task.Delay(Delay(attempt + 1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan Delay(int attempt) => _options.RetryDelays[Math.Min(attempt, _options.RetryDelays.Count - 1)];

    private void BeginCatchUp()
    {
        lock (_gate)
        {
            _catchingUp = true;
        }
    }

    private async Task OnConnectedAsync(HubConnection connection)
    {
        try
        {
            SetState(SyncConnectionState.Connected);
            var missed = await connection.InvokeAsync<List<SequencedMessage>>(OnlineProtocol.SyncMethod, _lastSequence).ConfigureAwait(false);

            await _deliverLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await DeliverAsync(missed).ConfigureAwait(false);
                List<SequencedMessage> buffered;
                lock (_gate)
                {
                    buffered = [.. _buffer];
                    _buffer.Clear();
                    _catchingUp = false;
                }

                await DeliverAsync(buffered).ConfigureAwait(false);
            }
            finally
            {
                _deliverLock.Release();
            }

            if (!_latestLocationsDelivered)
            {
                // Positions may be older than the last sequence seen before a restart, so they bypass the
                // sequence filter; consumers keep only the newest position per device anyway.
                var locations = await connection.InvokeAsync<List<SequencedMessage>>(OnlineProtocol.LatestLocationsMethod).ConfigureAwait(false);
                _latestLocationsDelivered = true;
                foreach (var location in locations)
                {
                    MessageReceived?.Invoke(this, location.Message);
                }
            }

            await FlushOutboxAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The connection dropped again during catch-up; the next (re)connect repeats it.
            _logger.LogInformation(ex, "Catch-up after connecting failed");
        }
    }

    private async Task OnLiveMessageAsync(SequencedMessage message)
    {
        lock (_gate)
        {
            if (_catchingUp)
            {
                _buffer.Add(message);
                return;
            }
        }

        await _deliverLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DeliverAsync([message]).ConfigureAwait(false);
        }
        finally
        {
            _deliverLock.Release();
        }
    }

    /// <summary>Delivers in sequence order, once. Must be called under <see cref="_deliverLock"/>.</summary>
    private async Task DeliverAsync(IEnumerable<SequencedMessage> messages)
    {
        var link = _link;
        if (link is null)
        {
            return;
        }

        var last = _lastSequence;
        foreach (var message in messages.OrderBy(m => m.Sequence))
        {
            if (message.Sequence <= _lastSequence)
            {
                continue;
            }

            _lastSequence = message.Sequence;

            // Own messages were already echoed when published.
            if (message.Message.SenderDeviceId != link.DeviceId)
            {
                await FollowRoleChangeAsync(message.Message).ConfigureAwait(false);
                try
                {
                    MessageReceived?.Invoke(this, message.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A handler failed for message {Type}", message.Message.Type);
                }
            }
        }

        if (_lastSequence != last)
        {
            await _store.SetLastSequenceAsync(link.SessionId, _lastSequence).ConfigureAwait(false);
        }
    }

    /// <summary>Applies a role change to the link when it was made for the link's roles version (same rule as the server).</summary>
    private async Task FollowRoleChangeAsync(SyncMessage message)
    {
        if (message.RoleChange is not { } change || _link is not { } link || change.FromVersion != link.RolesVersion)
        {
            return;
        }

        var updated = link with { RoleId = change.Apply(link.RoleId), RolesVersion = link.RolesVersion + 1 };
        _link = updated;
        await _store.SetRoleAsync(updated.SessionId, updated.RoleId, updated.RolesVersion).ConfigureAwait(false);
        _logger.LogInformation("Role changed to {Role} (version {Version})", updated.RoleId, updated.RolesVersion);
    }

    private async Task FlushOutboxAsync()
    {
        if (_link is not { } link || _connection is not { } connection)
        {
            return;
        }

        await _flushLock.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var (id, message) in await _store.GetOutboxAsync(link.SessionId).ConfigureAwait(false))
            {
                if (connection.State != HubConnectionState.Connected)
                {
                    return;
                }

                try
                {
                    await connection.InvokeAsync<long>(OnlineProtocol.PublishMethod, message).ConfigureAwait(false);
                }
                catch (HubException ex)
                {
                    // The server refused the message itself (not the network): retrying would block the queue.
                    _logger.LogWarning(ex, "The server rejected message {Type}; dropping it", message.Type);
                }

                await _store.RemoveFromOutboxAsync(id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Sending queued messages failed; they stay queued");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void SetState(SyncConnectionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RetryPolicy(IReadOnlyList<TimeSpan> delays) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            delays[(int)Math.Min(retryContext.PreviousRetryCount, delays.Count - 1)];
    }
}

public static class SyncServiceCollectionExtensions
{
    /// <summary>Online transport, REST client and the persistent links/outbox. Requires AddTransitGamesCore.</summary>
    public static IServiceCollection AddTransitGamesSync(this IServiceCollection services)
    {
        services.AddSingleton<OnlineSessionStore>();
        services.AddSingleton(new OnlineConnectionOptions());
        services.AddSingleton<SignalRGameSyncService>();
        services.AddSingleton(_ => new OnlineServerClient(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }));
        return services;
    }
}

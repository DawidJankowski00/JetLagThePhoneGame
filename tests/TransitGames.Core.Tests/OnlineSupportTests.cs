using TransitGames.Core.Games;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;

namespace TransitGames.Core.Tests;

public sealed class OnlineSupportTests : IDisposable
{
    private readonly TempDatabase _file = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _file.Dispose();

    private static SyncMessage Message(string type) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = Guid.NewGuid(),
        Type = type,
        SenderRoleId = "a",
        Timestamp = DateTimeOffset.UtcNow,
        Audience = SyncAudience.Everyone,
        PayloadJson = "{}",
    };

    [Fact]
    public async Task Router_forwards_to_the_current_transport_and_keeps_subscribers()
    {
        var local = new LocalGameSyncService();
        var router = new GameSyncRouter(local);
        var received = new List<string>();
        var stateChanges = 0;
        router.MessageReceived += (_, m) => received.Add(m.Type);
        router.StateChanged += (_, _) => stateChanges++;

        await router.PublishAsync(Message("offline"));
        Assert.Equal(SyncConnectionState.Local, router.State);

        var online = new FakeTransport();
        router.Use(online);
        Assert.Equal(1, stateChanges);
        Assert.True(router.IsOnline);
        await router.PublishAsync(Message("online"));
        online.Deliver(Message("from-server"));

        // The old transport no longer reaches the subscribers.
        await local.PublishAsync(Message("stale"));

        Assert.Equal(["offline", "online", "from-server"], received);
        Assert.Single(online.Published);
    }

    [Fact]
    public async Task Session_can_be_created_with_a_given_id()
    {
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var manager = new GameSessionManager(new GameRegistry([new TestModule()]), new SqliteGameSessionStore(db), new SqliteGameEventLog(db), _clock);
        var sharedId = Guid.NewGuid();

        var session = await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "b", sharedId);

        Assert.Equal(sharedId, session.Id);
    }

    [Fact]
    public async Task Session_that_was_left_can_be_finished_by_id()
    {
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var store = new SqliteGameSessionStore(db);
        var log = new SqliteGameEventLog(db);
        var manager = new GameSessionManager(new GameRegistry([new TestModule()]), store, log, _clock);
        var finished = new List<Guid>();
        manager.SessionFinished += (_, s) => finished.Add(s.Id);

        var session = await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "a");
        manager.Close(); // the player left the game screen; the other device then ends the game

        await manager.FinishAsync(session.Id);
        await manager.FinishAsync(session.Id); // a second "finished" message changes nothing

        Assert.Equal(SessionStatus.Finished, (await store.LoadAsync(session.Id))!.Status);
        Assert.Null(await manager.TryResumeAsync());
        Assert.Equal([session.Id], finished);
        Assert.Contains(await log.GetEventsAsync(session.Id), e => e.Type == Events.GameEventTypes.SessionFinished);
    }

    [Fact]
    public void Settings_round_trip_through_the_module_for_joining_devices()
    {
        IGameModule module = new TestModule();

        var json = module.SerializeSettings(new TestSettings { Rounds = 9 });
        var settings = Assert.IsType<TestSettings>(module.DeserializeSettings(json));

        Assert.Equal(9, settings.Rounds);
        Assert.Equal(GameModule<TestSettings, TestSession>.LiveLocationInterval, module.GetLocationShareInterval(settings));
    }

    [Theory]
    [InlineData("k7m-2qx", "K7M2QX")]
    [InlineData(" K7 M2 QX ", "K7M2QX")]
    public void Game_codes_are_normalized(string typed, string expected) =>
        Assert.Equal(expected, OnlineProtocol.NormalizeCode(typed));

    private sealed class FakeTransport : IGameSyncService
    {
        public List<SyncMessage> Published { get; } = [];

        public bool IsOnline => true;

        public SyncConnectionState State => SyncConnectionState.Connected;

        public IReadOnlyList<SyncPeer> Peers => [];

        public event EventHandler? StateChanged;

        public event EventHandler? PeersChanged;

        public event EventHandler<SyncMessage>? MessageReceived;

        public Task ConnectAsync(Guid sessionId, string roleId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            PeersChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task PublishAsync(SyncMessage message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            MessageReceived?.Invoke(this, message);
            return Task.CompletedTask;
        }

        public void Deliver(SyncMessage message) => MessageReceived?.Invoke(this, message);
    }
}

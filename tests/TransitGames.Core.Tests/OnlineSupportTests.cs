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
    public async Task Joining_device_counts_time_from_the_hosts_start()
    {
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var manager = new GameSessionManager(new GameRegistry([new TestModule()]), new SqliteGameSessionStore(db), new SqliteGameEventLog(db), _clock);
        var hostStart = _clock.UtcNow.AddMinutes(-7);

        var joined = await manager.StartNewAsync(TestModule.GameId, new TestSettings { StartedAt = hostStart }, "b");

        Assert.Equal(hostStart, joined.CreatedAt);
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
    public async Task Dispatcher_applies_game_messages_also_to_a_session_that_is_not_on_screen()
    {
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var store = new SqliteGameSessionStore(db);
        var manager = new GameSessionManager(new GameRegistry([new TestModule()]), store, new SqliteGameEventLog(db), _clock);
        var local = new LocalGameSyncService();
        _ = new GameSyncDispatcherProbe(local, manager);

        var session = await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "a");
        await local.PublishAsync(Message("point") with { SessionId = session.Id });
        Assert.Equal(1, ((TestSession)session).Score);

        manager.Close();
        await local.PublishAsync(Message("point") with { SessionId = session.Id });
        await local.PublishAsync(Message(SyncMessageTypes.Location) with { SessionId = session.Id }); // app message: ignored

        // Applying to a saved session is asynchronous (load, change, save).
        var score = 0;
        for (var attempt = 0; attempt < 100 && score != 2; attempt++)
        {
            await Task.Delay(20);
            score = ((TestSession)new TestModule().RestoreSession((await store.LoadAsync(session.Id))!)).Score;
        }

        Assert.Equal(2, score);
    }

    [Fact]
    public async Task Role_changes_carried_by_messages_apply_once_and_are_saved()
    {
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var store = new SqliteGameSessionStore(db);
        var manager = new GameSessionManager(new GameRegistry([new TestModule()]), store, new SqliteGameEventLog(db), _clock);
        var local = new LocalGameSyncService();
        _ = new GameSyncDispatcherProbe(local, manager);
        var session = await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "a");

        var swap = Message("point") with { SessionId = session.Id, RoleChange = RoleChange.Swap(0, "a", "b") };
        await local.PublishAsync(swap);
        await local.PublishAsync(swap with { Id = Guid.NewGuid() }); // same change reported twice

        Assert.Equal(("b", 1), (session.LocalRoleId, session.RolesVersion));
        Assert.Equal(2, ((TestSession)session).Score); // the game handled both messages
        GameSessionSnapshot? saved = null;
        for (var attempt = 0; attempt < 100 && saved?.RolesVersion != 1; attempt++)
        {
            await Task.Delay(20);
            saved = await store.LoadAsync(session.Id);
        }

        Assert.Equal(("b", 1), (saved!.LocalRoleId, saved.RolesVersion));
    }

    [Fact]
    public void Role_changes_must_permute_the_roles()
    {
        Assert.True(RoleChange.Swap(0, "a", "b").IsPermutationOf(["a", "b"]));
        Assert.False(new RoleChange(0, new Dictionary<string, string> { ["a"] = "b" }).IsPermutationOf(["a", "b"]));
        Assert.False(RoleChange.Swap(0, "a", "x").IsPermutationOf(["a", "b"]));
    }

    private sealed class PointHandler : IGameMessageHandler
    {
        public string GameId => TestModule.GameId;

        public void Handle(IGameSession session, SyncMessage message)
        {
            if (message.Type == "point")
            {
                ((TestSession)session).AddPoint();
            }
        }
    }

    private sealed class GameSyncDispatcherProbe(IGameSyncService sync, GameSessionManager manager)
    {
        public GameMessageDispatcher Dispatcher { get; } = new(sync, manager, [new PointHandler()]);
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

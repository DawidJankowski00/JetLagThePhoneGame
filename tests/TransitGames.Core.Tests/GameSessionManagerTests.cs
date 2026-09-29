using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Tests;

public sealed class GameSessionManagerTests : IDisposable
{
    private readonly TempDatabase _file = new();
    private readonly FakeClock _clock = new();

    public void Dispose() => _file.Dispose();

    private (GameSessionManager Manager, IGameEventLog Log, IGameSessionStore Store) Create()
    {
        // A fresh AppDatabase per call simulates the process being killed and restarted.
        var db = new AppDatabase(new DatabaseOptions(_file.Path));
        var store = new SqliteGameSessionStore(db);
        var log = new SqliteGameEventLog(db);
        var registry = new GameRegistry([new TestModule()]);
        return (new GameSessionManager(registry, store, log, _clock), log, store);
    }

    [Fact]
    public async Task StartedSession_IsResumedAfterRestart_WithState()
    {
        var (manager, _, _) = Create();
        var session = (TestSession)await manager.StartNewAsync(TestModule.GameId, new TestSettings { Rounds = 7 }, "b");
        session.AddPoint();
        session.AddPoint();
        await manager.SaveAsync();

        var (restarted, _, _) = Create();
        var resumed = await restarted.TryResumeAsync();

        var typed = Assert.IsType<TestSession>(resumed);
        Assert.Equal(session.Id, typed.Id);
        Assert.Equal(2, typed.Score);
        Assert.Equal(7, typed.Settings.Rounds);
        Assert.Equal("b", typed.LocalRoleId);
        Assert.Equal(SessionStatus.Running, typed.Status);
    }

    [Fact]
    public async Task FinishedSession_IsNotResumed_ButStaysInHistory()
    {
        var (manager, _, store) = Create();
        await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "a");
        await manager.FinishAsync();

        var (restarted, _, _) = Create();

        Assert.Null(await restarted.TryResumeAsync());
        var history = await store.ListAsync();
        Assert.Single(history);
        Assert.Equal(SessionStatus.Finished, history[0].Status);
    }

    [Fact]
    public async Task SessionEvents_AreWrittenToTheLog()
    {
        var (manager, log, _) = Create();
        var session = await manager.StartNewAsync(TestModule.GameId, new TestSettings(), "a");
        await manager.FinishAsync();

        // Event appends are fire-and-forget from the session's point of view.
        IReadOnlyList<GameEvent> events = [];
        for (var i = 0; i < 50 && events.Count < 2; i++)
        {
            await Task.Delay(20);
            events = await log.GetEventsAsync(session.Id);
        }

        Assert.Equal([GameEventTypes.SessionStarted, GameEventTypes.SessionFinished], events.Select(e => e.Type));
    }
}

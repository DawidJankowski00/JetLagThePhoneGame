using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;

namespace TransitGames.Core.Tests;

public sealed class SessionMigrationTests : IDisposable
{
    private readonly TempDatabase _file = new();

    public void Dispose() => _file.Dispose();

    private static GameSessionSnapshot Snapshot(string gameId, string settings) => new()
    {
        SessionId = Guid.NewGuid(),
        GameId = gameId,
        LocalRoleId = "a",
        Status = SessionStatus.Running,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        SettingsJson = settings,
        StateJson = "{}",
    };

    [Fact]
    public async Task Migrations_upgrade_the_sessions_of_their_game_once()
    {
        var old = Snapshot(TestModule.GameId, """{"old":true}""");
        var other = Snapshot("other", """{"old":true}""");
        await new SqliteGameSessionStore(new AppDatabase(new DatabaseOptions(_file.Path))).SaveAsync(old);
        await new SqliteGameSessionStore(new AppDatabase(new DatabaseOptions(_file.Path))).SaveAsync(other);

        var migration = new CountingMigration();
        var store = new SqliteGameSessionStore(new AppDatabase(new DatabaseOptions(_file.Path)), [migration]);
        var upgraded = await store.LoadAsync(old.SessionId);
        var untouched = await store.LoadAsync(other.SessionId);
        await new SqliteGameSessionStore(new AppDatabase(new DatabaseOptions(_file.Path)), [migration]).ListAsync(); // app restarted

        Assert.Equal((SessionStatus.Finished, """{"new":true}"""), (upgraded!.Status, upgraded.SettingsJson));
        Assert.Equal("""{"old":true}""", untouched!.SettingsJson);
        Assert.Equal(1, migration.Runs);
        Assert.Equal("other", (await store.GetLatestUnfinishedAsync())!.GameId); // the migrated game is not resumed
    }

    [Fact]
    public async Task Rows_from_before_the_device_columns_read_as_hosted_without_a_device()
    {
        var store = new SqliteGameSessionStore(new AppDatabase(new DatabaseOptions(_file.Path)));
        var snapshot = Snapshot(TestModule.GameId, "{}");
        await store.SaveAsync(snapshot);

        var loaded = (await store.LoadAsync(snapshot.SessionId))!;

        Assert.Null(loaded.LocalDeviceId);
        Assert.True(loaded.IsHost);
    }

    private sealed class CountingMigration : IGameSessionMigration
    {
        public int Runs { get; private set; }

        public string Id => "test.v2";

        public string GameId => TestModule.GameId;

        public GameSessionSnapshot? Migrate(GameSessionSnapshot snapshot)
        {
            Runs++;
            return snapshot.SettingsJson.Contains("old", StringComparison.Ordinal)
                ? snapshot with { SettingsJson = """{"new":true}""", Status = SessionStatus.Finished }
                : null;
        }
    }
}

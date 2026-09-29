using TransitGames.Core.Games;
using TransitGames.Core.Sessions;
using TransitGames.Core.Timing;

namespace TransitGames.Core.Tests;

internal sealed class TestSettings : GameSettings
{
    public int Rounds { get; set; } = 3;
}

internal sealed class TestState
{
    public int Score { get; set; }
}

internal sealed class TestSession : GameSessionBase<TestSettings, TestState>
{
    public TestSession(TestSettings settings, string role)
        : base(TestModule.GameId, settings, role)
    {
    }

    public TestSession(GameSessionSnapshot snapshot, TestSettings settings)
        : base(snapshot, settings)
    {
    }

    public int Score => State.Score;

    public void AddPoint() => Mutate(s => s.Score++);
}

internal sealed class TestModule : GameModule<TestSettings, TestSession>
{
    public const string GameId = "test";

    public override string Id => GameId;
    public override string DisplayName => "Test";
    public override string Description => "Test game";
    public override string Icon => "T";
    public override int MinPlayers => 2;
    public override IReadOnlyList<GameRole> Roles { get; } = [new("a", "A", ""), new("b", "B", "")];

    protected override TestSession CreateSession(TestSettings settings, string localRoleId) => new(settings, localRoleId);

    protected override TestSession RestoreSession(GameSessionSnapshot snapshot, TestSettings settings) =>
        new(snapshot, settings);
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

internal sealed class TempDatabase : IDisposable
{
    public TempDatabase() =>
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"transitgames-test-{Guid.NewGuid():N}.db3");

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly; temp files are cleaned up by the OS.
        }
    }
}

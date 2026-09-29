using TransitGames.Core.Geography;
using TransitGames.Core.Persistence;

namespace TransitGames.Core.Tests;

public class GameAreaRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListsMostRecentlyUsedFirst_AndUpdatesOnResave()
    {
        using var temp = new TempDatabase();
        await using var db = new AppDatabase(new DatabaseOptions(temp.Path));
        var repo = new SqliteGameAreaRepository(db);
        var a = new GameAreaDefinition("A", "{}", GameAreaSource.Drawn);
        var b = new GameAreaDefinition("B", "{}", GameAreaSource.OsmBoundary);

        await repo.SaveAsync("a", a, T0);
        await repo.SaveAsync("b", b, T0.AddHours(1));
        await repo.SaveAsync("a", a, T0.AddHours(2));

        var list = await repo.ListAsync();
        Assert.Equal(["a", "b"], list.Select(x => x.Key));
        Assert.Equal(T0.AddHours(2), list[0].LastUsedAt);
        Assert.Equal(GameAreaSource.OsmBoundary, list[1].Definition.Source);

        await repo.DeleteAsync("a");
        Assert.Single(await repo.ListAsync());
    }
}

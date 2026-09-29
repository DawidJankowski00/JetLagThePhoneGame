using TransitGames.Core.Games;

namespace TransitGames.Core.Tests;

public class GameRegistryTests
{
    [Fact]
    public void Get_ReturnsRegisteredModule()
    {
        var registry = new GameRegistry([new TestModule()]);

        Assert.Equal(TestModule.GameId, registry.Get(TestModule.GameId).Id);
        Assert.Single(registry.Modules);
    }

    [Fact]
    public void DuplicateIds_Throw()
    {
        Assert.Throws<InvalidOperationException>(() => new GameRegistry([new TestModule(), new TestModule()]));
    }

    [Fact]
    public void UnknownId_Throws()
    {
        var registry = new GameRegistry([new TestModule()]);

        Assert.Throws<KeyNotFoundException>(() => registry.Get("nope"));
        Assert.False(registry.TryGet("nope", out _));
    }

    [Fact]
    public void CreateSession_RejectsUnknownRole()
    {
        IGameModule module = new TestModule();

        Assert.Throws<ArgumentException>(() => module.CreateSession(module.CreateDefaultSettings(), "zzz"));
    }
}

using TransitGames.Core.Games;

namespace TransitGames.Games.Tag.Tests;

public class TagModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExposesRunnerAndChaserRoles()
    {
        var module = new TagModule();

        Assert.Equal([TagRoles.Runner, TagRoles.Chaser], module.Roles.Select(r => r.Id));
    }

    [Fact]
    public void Settings_SurviveSnapshotRoundTrip()
    {
        IGameModule module = new TagModule();
        var settings = new TagSettings { CatchRadiusMeters = 80, RevealInterval = TimeSpan.FromMinutes(10) };
        var session = module.CreateSession(settings, TagRoles.Chaser);
        session.Start(Now);

        var restored = (TagSession)module.RestoreSession(session.CreateSnapshot(Now));

        Assert.Equal(80, restored.Settings.CatchRadiusMeters);
        Assert.Equal(TimeSpan.FromMinutes(10), restored.Settings.RevealInterval);
    }

    [Fact]
    public void Runners_are_revealed_online_at_the_reveal_interval()
    {
        IGameModule module = new TagModule();

        Assert.Equal(TimeSpan.FromMinutes(7), module.GetLocationShareInterval(new TagSettings { RevealInterval = TimeSpan.FromMinutes(7) }));
    }

    [Fact]
    public void ChaserSeesRunners_RunnersNeverSeeChaser()
    {
        var sharing = new TagModule().LocationSharing;

        Assert.True(sharing.CanSee(TagRoles.Chaser, TagRoles.Runner));
        Assert.False(sharing.CanSee(TagRoles.Runner, TagRoles.Chaser));
        Assert.False(sharing.IsShared(TagRoles.Chaser));
        Assert.Throws<LocationPrivacyException>(() => sharing.EnsureMayShare(TagRoles.Chaser));
    }
}

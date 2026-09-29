using TransitGames.Core.Games;

namespace TransitGames.Games.HideAndSeek.Tests;

public class HideAndSeekModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExposesHiderAndSeekerRoles()
    {
        var module = new HideAndSeekModule();

        Assert.Equal([HideAndSeekRoles.Hider, HideAndSeekRoles.Seeker], module.Roles.Select(r => r.Id));
        Assert.False(string.IsNullOrWhiteSpace(module.DisplayName));
    }

    [Fact]
    public void Settings_SurviveSnapshotRoundTrip()
    {
        IGameModule module = new HideAndSeekModule();
        var settings = new HideAndSeekSettings
        {
            Size = GameSize.Small,
            HidingZoneRadiusMeters = 500,
            HidingTime = TimeSpan.FromMinutes(30),
        };
        var session = module.CreateSession(settings, HideAndSeekRoles.Seeker);
        session.Start(Now);

        var restored = (HideAndSeekSession)module.RestoreSession(session.CreateSnapshot(Now));

        Assert.Equal(GameSize.Small, restored.Settings.Size);
        Assert.Equal(500, restored.Settings.HidingZoneRadiusMeters);
        Assert.Equal(TimeSpan.FromMinutes(30), restored.Settings.HidingTime);
        Assert.Equal(HideAndSeekRoles.Seeker, restored.LocalRoleId);
    }

    [Fact]
    public void HiderSeesSeekers_SeekersNeverSeeHider()
    {
        var sharing = new HideAndSeekModule().LocationSharing;

        Assert.True(sharing.CanSee(HideAndSeekRoles.Hider, HideAndSeekRoles.Seeker));
        Assert.False(sharing.CanSee(HideAndSeekRoles.Seeker, HideAndSeekRoles.Hider));
        Assert.False(sharing.IsShared(HideAndSeekRoles.Hider));
        Assert.Throws<LocationPrivacyException>(() => sharing.EnsureMayShare(HideAndSeekRoles.Hider));
    }
}

using TransitGames.Core.Games;

namespace TransitGames.Core.Tests;

public class LocationSharingPolicyTests
{
    private readonly LocationSharingPolicy _policy = new([new LocationSharingRule("a", "b")]);

    [Fact]
    public void Sharing_IsOneWay()
    {
        Assert.True(_policy.CanSee("a", "b"));
        Assert.False(_policy.CanSee("b", "a"));
    }

    [Fact]
    public void RoleAlwaysSeesItself()
    {
        Assert.True(_policy.CanSee("a", "a"));
        Assert.True(LocationSharingPolicy.None.CanSee("b", "b"));
    }

    [Fact]
    public void EnsureMayShare_ThrowsForPrivateRole()
    {
        _policy.EnsureMayShare("b");
        var ex = Assert.Throws<LocationPrivacyException>(() => _policy.EnsureMayShare("a"));
        Assert.Equal("a", ex.RoleId);
    }

    [Fact]
    public void DefaultPolicy_SharesNothing()
    {
        Assert.False(new TestModule().LocationSharing.CanSee("a", "b"));
        Assert.False(LocationSharingPolicy.None.IsShared("a"));
    }

    [Fact]
    public void SelfRule_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new LocationSharingPolicy([new LocationSharingRule("a", "a")]));
    }

    [Fact]
    public void Registry_RejectsRuleWithUnknownRole()
    {
        var module = new TestModule { Sharing = new([new LocationSharingRule("a", "ghost")]) };

        Assert.Throws<InvalidOperationException>(() => new GameRegistry([module]));
    }
}

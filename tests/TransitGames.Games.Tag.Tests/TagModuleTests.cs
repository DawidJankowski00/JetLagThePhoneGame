using TransitGames.Core.Games;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;

namespace TransitGames.Games.Tag.Tests;

public class TagModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExposesRunnerAndChaserRoles_AndIsOnlineOnly()
    {
        var module = new TagModule();

        Assert.Equal([TagRoles.Runner, TagRoles.Chaser], module.Roles.Select(r => r.Id));
        Assert.True(module.RequiresOnline);
    }

    [Fact]
    public void Settings_SurviveSnapshotRoundTrip()
    {
        IGameModule module = new TagModule();
        var settings = new TagSettings { CatchRadiusMeters = 80, FareModel = FareModel.FixedPerRide, DestinationMode = DestinationMode.RandomStations };
        settings.FixedFares[TransportMode.Metro] = 9;
        var session = module.CreateSession(settings, TagRoles.Chaser);
        session.InitializeDevice(Guid.NewGuid(), isHost: false);
        session.Start(Now);

        var restored = (TagSession)module.RestoreSession(session.CreateSnapshot(Now));

        Assert.Equal(80, restored.Settings.CatchRadiusMeters);
        Assert.Equal(FareModel.FixedPerRide, restored.Settings.FareModel);
        Assert.Equal(9, restored.Settings.FixedFares[TransportMode.Metro]);
        Assert.Equal(DestinationMode.RandomStations, restored.Settings.DestinationMode);
        Assert.Equal(session.LocalDeviceId, restored.LocalDeviceId);
        Assert.False(restored.IsHost);
    }

    [Fact]
    public void A_running_game_survives_the_app_being_killed()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);
        table.Draw("cu-double", TagTable.Start.AddMinutes(3));
        IGameModule module = new TagModule();

        var restored = (TagSession)module.RestoreSession(table.Of(table.Ids[1]).CreateSnapshot(TagTable.Start.AddMinutes(4)));

        Assert.True(restored.IsLocalRunner);
        Assert.Equal(25, restored.Balance(table.Ids[1]));
        Assert.Single(restored.ActiveCurses(table.Ids[1], TagTable.Start.AddMinutes(5)));
        Assert.Equal(TagPhase.HeadStart, restored.Phase(TagTable.Start.AddMinutes(5)));
    }

    [Fact]
    public void The_runner_is_revealed_online_at_the_reveal_interval()
    {
        IGameModule module = new TagModule();

        Assert.Equal(TimeSpan.FromMinutes(7), module.GetLocationShareInterval(new TagSettings { RevealInterval = TimeSpan.FromMinutes(7) }));
    }

    [Fact]
    public void ChasersSeeTheRunner_TheRunnerNeverSeesChasers()
    {
        var sharing = new TagModule().LocationSharing;

        Assert.True(sharing.CanSee(TagRoles.Chaser, TagRoles.Runner));
        Assert.False(sharing.CanSee(TagRoles.Runner, TagRoles.Chaser));
        Assert.False(sharing.IsShared(TagRoles.Chaser));
        Assert.Throws<LocationPrivacyException>(() => sharing.EnsureMayShare(TagRoles.Chaser));
    }

    [Fact]
    public void Destinations_are_hidden_from_others_until_the_end_when_the_setting_says_so()
    {
        var table = new TagTable(players: 2, s => s.DestinationsVisible = false);
        table.StartGame(runner: table.Ids[0]);
        var host = table.Host;

        Assert.True(host.CanSeeDestination(host.FindPlayer(table.Ids[0])!));
        Assert.False(host.CanSeeDestination(host.FindPlayer(table.Ids[1])!));

        table.Send(Sync.TagMessages.EndGame(host, new Sync.EndPayload(TagTable.Start.AddHours(2), TagTestData.Center, EndPointSource.Manual)));
        Assert.True(host.CanSeeDestination(host.FindPlayer(table.Ids[1])!));
    }
}

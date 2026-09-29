using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class TagNotificationTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;
    private static readonly IGameModule Module = new TagModule();

    [Fact]
    public void A_turn_plans_the_end_of_the_head_start_and_of_the_game()
    {
        var table = new TagTable();
        Assert.Empty(Module.PlanNotifications(table.Host, isOnline: true, Start)); // setup
        table.StartGame(runner: table.Ids[1]);

        var plan = Module.PlanNotifications(table.Host, isOnline: true, Start.AddMinutes(1));

        Assert.Equal(["t0.headstart", "timeup"], plan.Select(n => n.Key));
        Assert.Equal([Start.AddMinutes(30), Start.AddHours(2)], plan.Select(n => n.At!.Value));
    }

    [Fact]
    public void After_a_catch_the_next_head_start_is_planned_and_at_the_end_nothing()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);
        table.Catch(Start.AddMinutes(40), table.Ids[0]);

        var nextTurn = Module.PlanNotifications(table.Host, isOnline: true, Start.AddMinutes(41));
        table.Send(TagMessages.EndGame(table.Host, new EndPayload(Start.AddHours(2), TagTestData.Center, EndPointSource.Manual)));
        var over = Module.PlanNotifications(table.Host, isOnline: true, Start.AddHours(2));

        Assert.Equal(["t1.headstart", "timeup"], nextTurn.Select(n => n.Key));
        Assert.Equal(Start.AddMinutes(70), nextTurn[0].At);
        Assert.Empty(over);
    }

    [Fact]
    public void News_covers_catches_turns_and_the_end_and_catch_requests_reach_only_the_runner()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);
        var events = new List<GameEvent>();
        table.Host.EventRaised += (_, e) => events.Add(e);

        table.Send(TagMessages.RequestCatch(table.Host, new CatchRequestPayload(0, Guid.NewGuid(), table.Ids[0], Start.AddMinutes(35))));
        table.Catch(Start.AddMinutes(36), table.Ids[0]);
        table.Send(TagMessages.EndGame(table.Host, new EndPayload(Start.AddHours(2), TagTestData.Center, EndPointSource.Manual)));

        Assert.Equal(
            [TagEventTypes.CatchRequested, TagEventTypes.Caught, TagEventTypes.TurnStarted, TagEventTypes.TimeUp, TagEventTypes.GameEnded],
            events.Select(e => e.Type));
        Assert.NotNull(Module.NotificationFor(events[0], TagRoles.Runner));
        Assert.Null(Module.NotificationFor(events[0], TagRoles.Chaser));
        Assert.Equal(["caught", "turn", "end"], events.Skip(1).Select(e => Module.NotificationFor(e, TagRoles.Chaser)?.Key).OfType<string>());
        Assert.NotNull(Module.NotificationForLocation(TagRoles.Runner, TagRoles.Chaser));
        Assert.Null(Module.NotificationForLocation(TagRoles.Chaser, TagRoles.Runner));
    }

    [Fact]
    public void Turn_events_tell_the_track_export_which_role_this_device_had()
    {
        var table = new TagTable(players: 2);
        var events = new List<GameEvent>();
        table.Host.EventRaised += (_, e) => events.Add(e);

        table.StartGame(runner: table.Ids[0]);
        table.Catch(Start.AddMinutes(40), table.Ids[1]);

        var roles = events.Where(e => e.Data?.ContainsKey(GameEventData.LocalRole) == true).Select(e => e.Data![GameEventData.LocalRole]);
        Assert.Equal([TagRoles.Runner, TagRoles.Chaser], roles);
        Assert.All(events.Where(e => e.Type == TagEventTypes.TurnStarted), e => Assert.NotNull(e.Location));
    }

    [Fact]
    public void The_runners_own_timeline_has_places_that_are_never_sent()
    {
        var table = new TagTable(players: 2);
        table.StartGame(runner: table.Ids[0]);
        var runner = table.Runner;
        var runnerEvents = new List<GameEvent>();
        var chaserEvents = new List<GameEvent>();
        runner.EventRaised += (_, e) => runnerEvents.Add(e);
        table.Of(table.Ids[1]).EventRaised += (_, e) => chaserEvents.Add(e);
        var here = TagTestData.At(300, 300);

        runner.UpdateOwnPosition(here);
        var ride = runner.CreateRideStart(Coins.TransportMode.Bus, Start.AddMinutes(1));
        var message = TagMessages.StartRide(runner, ride);
        table.Send(message);

        Assert.DoesNotContain("latitude", message.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(here, Assert.Single(runnerEvents).Location);
        Assert.Null(Assert.Single(chaserEvents).Location);
    }

    [Fact]
    public void The_result_names_the_winner_and_ranks_the_destinations()
    {
        var table = new TagTable(players: 3);
        table.StartGame(runner: table.Ids[0]);
        table.Send(TagMessages.EndGame(table.Host, new EndPayload(Start.AddHours(2), TagTestData.At(3900, 0), EndPointSource.Gps)));

        var result = Module.DescribeResult(table.Host, Start.AddHours(3));

        Assert.Contains("P3", result.Headline);
        Assert.StartsWith("1. P3", result.Lines[0].Label);
        Assert.Contains("🏆", result.Lines[0].Label);
        Assert.Equal(3 + 1 + 3, result.Lines.Count); // ranking, one turn, balances
    }
}

using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class TagNotificationTests
{
    private static readonly DateTimeOffset Start = TagTestData.Start;
    private static readonly IGameModule Module = new TagModule();

    [Fact]
    public void Online_the_round_plans_the_end_of_the_head_start_and_of_the_time_only()
    {
        var chaser = TagTestData.NewSession(TagRoles.Chaser);

        var plan = Module.PlanNotifications(chaser, isOnline: true, Start.AddMinutes(1));

        Assert.Equal(["r0.headstart", "r0.timeup"], plan.Select(n => n.Key));
        Assert.Equal(Start.AddMinutes(10), plan[0].At);
        Assert.Equal(Start.AddMinutes(60), plan[1].At);
    }

    [Fact]
    public void Offline_every_ping_of_the_round_is_planned_too()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);

        // Round of 60 min, pings every 15 min: at 15, 30 and 45 (the one at 60 is the end of the round).
        var plan = Module.PlanNotifications(runner, isOnline: false, Start.AddMinutes(20));

        Assert.Equal(["r0.ping2", "r0.ping3", "r0.timeup"], plan.Select(n => n.Key));
        Assert.Equal([Start.AddMinutes(30), Start.AddMinutes(45)], plan.Take(2).Select(n => n.At!.Value));
    }

    [Fact]
    public void After_the_round_ends_the_next_round_is_planned_and_at_game_over_nothing()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);
        TagTestData.Deliver(runner, TagMessages.EndRound(runner, runner.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), null)));

        var nextRound = Module.PlanNotifications(runner, isOnline: true, Start.AddMinutes(31));
        TagTestData.Deliver(runner, TagMessages.EndRound(runner, runner.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(50), null)));
        var gameOver = Module.PlanNotifications(runner, isOnline: true, Start.AddMinutes(51));

        Assert.Equal(["r1.headstart", "r1.timeup"], nextRound.Select(n => n.Key));
        Assert.Equal(Start.AddMinutes(40), nextRound[0].At);
        Assert.Empty(gameOver);
    }

    [Fact]
    public void News_covers_captures_and_round_changes_and_pings_only_reach_the_chasers()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);
        var events = new List<GameEvent>();
        runner.EventRaised += (_, e) => events.Add(e);
        TagTestData.Deliver(runner, TagMessages.CaptureTarget(runner, new CapturePayload(0, "t1", Start.AddMinutes(20))));
        TagTestData.Deliver(runner, TagMessages.EndRound(runner, runner.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), null)));

        var news = events.Select(e => Module.NotificationFor(e, TagRoles.Chaser)?.Key).ToList();

        Assert.Equal(3, news.Count);
        Assert.StartsWith("target.", news[0]);
        Assert.Equal(["round.end", "round.start"], news.Skip(1));
        Assert.NotNull(Module.NotificationForLocation(TagRoles.Runner, TagRoles.Chaser));
        Assert.Null(Module.NotificationForLocation(TagRoles.Chaser, TagRoles.Runner));
    }

    [Fact]
    public void The_result_names_the_winner_and_stops_counting_when_the_game_is_finished()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);
        TagTestData.Deliver(runner, TagMessages.CaptureTarget(runner, new CapturePayload(0, "t1", Start.AddMinutes(20))));
        TagTestData.Deliver(runner, TagMessages.EndRound(runner, runner.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), null)));

        var running = Module.DescribeResult(runner, Start.AddMinutes(40));
        runner.Finish(Start.AddMinutes(45)); // the second round lasted 15 min
        var later = Module.DescribeResult(runner, Start.AddHours(5));

        // Team 1: 30 min + 10 points; team 2 (now running): 10 min so far, 15 min when finished.
        Assert.Contains("40 : 10", running.Headline);
        Assert.Contains("40 : 15", later.Headline);
        Assert.Contains(TagTexts.TeamName(TagTeam.First), later.Headline);
        Assert.Equal(4, later.Lines.Count); // two teams + two rounds
        Assert.Contains(Resources.TagStrings.EndStopped, later.Lines[3].Value);
        Assert.True(runner.Scoreboard(Start.AddHours(5)).Rounds[1].WasStopped);
    }
}

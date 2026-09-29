using TransitGames.Games.Tag.Players;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class TagStartTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;

    [Fact]
    public void The_start_draws_one_runner_and_every_device_agrees()
    {
        var table = new TagTable(players: 4);

        table.StartGame(seed: 7);

        var runnerId = table.Host.CurrentTurn!.RunnerId;
        Assert.Contains(runnerId, table.Ids);
        foreach (var device in table.Devices)
        {
            Assert.Equal(runnerId, device.CurrentTurn!.RunnerId);
            Assert.Equal(device.LocalPlayerId == runnerId ? TagRoles.Runner : TagRoles.Chaser, device.LocalRoleId);
            Assert.Equal(1, device.RolesVersion);
            Assert.Equal(4, device.Players.Count);
            Assert.All(device.Players, p => Assert.Equal(20, device.Balance(p.Id)));
            Assert.Equal(TagTestData.Center, device.CurrentTurn.StartPoint);
        }

        Assert.Single(table.Devices, d => d.IsLocalRunner);
    }

    [Fact]
    public void The_first_runner_is_random_over_many_games()
    {
        var players = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var random = new Random(3);

        var picked = Enumerable.Range(0, 300).Select(_ => RunnerSelection.PickFirst(players, random)).ToList();

        Assert.All(players, p => Assert.InRange(picked.Count(x => x == p), 60, 140));
    }

    [Fact]
    public void The_host_cannot_start_while_a_player_has_no_destination()
    {
        var table = new TagTable(players: 2);
        table.Send(TagMessages.SetDestination(table.Host, new DestinationPayload(table.Ids[0], table.DestinationOf(0), "D1"), Start));

        var start = table.Host.CreateStart(Start, TagTestData.Center, [.. table.Ids.Select(id => (id, table.Name(id)))], new Random(1));

        Assert.Null(start);
        Assert.Null(table.Devices[1].CreateStart(Start, TagTestData.Center, [.. table.Ids.Select(id => (id, "x"))], new Random(1))); // not the host
    }

    [Fact]
    public void Self_picked_destinations_can_only_be_set_by_their_player()
    {
        var table = new TagTable(players: 2);

        Assert.True(table.Devices[1].CanSetDestination(table.Ids[1]));
        Assert.False(table.Devices[1].CanSetDestination(table.Ids[0]));
        Assert.False(table.Host.CanSetDestination(table.Ids[1]));
    }
}

public class TagCatchTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;

    [Fact]
    public void Chasers_cannot_chase_during_the_head_start()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);
        var chaser = table.Host;

        Assert.Equal(TagPhase.HeadStart, chaser.Phase(Start.AddMinutes(29)));
        Assert.True(chaser.IsChaseLocked(Start.AddMinutes(29)));
        Assert.False(chaser.CanRequestCatch(Start.AddMinutes(29)));
        Assert.False(table.Runner.CanConfirmCatch(Start.AddMinutes(29)));
        Assert.False(chaser.CanConfirmCatch(Start.AddMinutes(29))); // not even the host
        Assert.Throws<InvalidOperationException>(() => table.Runner.CreateCatch(Start.AddMinutes(10), table.Ids[0], TagTestData.Center, new Random(1)));

        Assert.Equal(TagPhase.Chase, chaser.Phase(Start.AddMinutes(30)));
        Assert.True(chaser.CanRequestCatch(Start.AddMinutes(30)));
        Assert.True(table.Runner.CanConfirmCatch(Start.AddMinutes(30)));
        Assert.False(table.Runner.CanRequestCatch(Start.AddMinutes(30)));
    }

    [Fact]
    public void The_runner_moves_immediately_and_may_ride_and_draw_during_the_head_start()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);

        Assert.Equal(DrawBlock.None, table.Runner.DrawBlockReason(Start.AddMinutes(1)));
        Assert.True(table.Runner.QuoteRide(Coins.TransportMode.Bus, Start.AddMinutes(1)).IsAllowed);
    }

    [Fact]
    public void After_a_catch_the_catcher_runs_next_when_the_setting_says_so()
    {
        var table = new TagTable(players: 4, s => s.NextRunner = NextRunnerMode.Catcher);
        table.StartGame(runner: table.Ids[0]);
        var caughtAt = Start.AddMinutes(50);
        var catchPoint = TagTestData.At(700, -300);

        table.Catch(caughtAt, catcher: table.Ids[2], catchPoint);

        foreach (var device in table.Devices)
        {
            var (first, second) = (device.Turns[0], device.Turns[1]);
            Assert.Equal((TurnEndReason.Caught, table.Ids[2], caughtAt), (first.EndReason!.Value, first.CatcherId!.Value, first.EndedAt!.Value));
            Assert.Equal(table.Ids[2], second.RunnerId);
            Assert.Equal(caughtAt, second.StartedAt);
            Assert.Equal(catchPoint, second.StartPoint); // the next turn starts where the catch happened
            Assert.Equal(TagPhase.HeadStart, device.Phase(caughtAt.AddMinutes(29))); // the chasers wait again
        }

        Assert.Equal(TagRoles.Chaser, table.Of(table.Ids[0]).LocalRoleId); // caught runner chases now
        Assert.Equal(TagRoles.Runner, table.Of(table.Ids[2]).LocalRoleId);
        Assert.Equal(TagRoles.Chaser, table.Of(table.Ids[1]).LocalRoleId);
        Assert.Equal(TagRoles.Chaser, table.Of(table.Ids[3]).LocalRoleId);
    }

    [Fact]
    public void Random_next_runner_is_always_one_of_the_chasers()
    {
        var players = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var random = new Random(5);

        var picks = Enumerable.Range(0, 200)
            .Select(_ => RunnerSelection.PickNext(players, players[0], players[1], NextRunnerMode.Random, random))
            .ToList();

        Assert.DoesNotContain(players[0], picks);
        Assert.All(players.Skip(1), p => Assert.Contains(p, picks)); // not always the catcher
        Assert.Equal(players[2], RunnerSelection.PickNext(players, players[0], players[2], NextRunnerMode.Catcher, random));
        Assert.NotEqual(players[0], RunnerSelection.PickNext(players, players[0], null, NextRunnerMode.Catcher, random)); // catcher unknown: random chaser
    }

    [Fact]
    public void Coins_are_kept_across_turns_and_the_new_runner_gets_the_turn_bonus()
    {
        var table = new TagTable(players: 3, s => s.NextRunner = NextRunnerMode.Catcher);
        table.StartGame(runner: table.Ids[0]);
        var runner = table.Runner;
        var ride = runner.CreateRideStart(Coins.TransportMode.Train, Start.AddMinutes(5)); // 2 up front
        table.Send(TagMessages.StartRide(runner, ride));
        table.Send(TagMessages.EndRide(runner, runner.CreateRideEnd(runner.OpenRide(runner.LocalPlayerId)!, 3000, Start.AddMinutes(15)))); // 6 in total

        table.Catch(Start.AddMinutes(40), catcher: table.Ids[1]);

        foreach (var device in table.Devices)
        {
            Assert.Equal(14, device.Balance(table.Ids[0])); // the caught runner keeps what is left
            Assert.Equal(30, device.Balance(table.Ids[1])); // 20 + bonus 10
            Assert.Equal(20, device.Balance(table.Ids[2])); // untouched
        }

        var bonus = Assert.Single(table.Host.Transactions, t => t.Kind == CoinTransactionKind.TurnBonus);
        Assert.Equal((table.Ids[1], 1), (bonus.PlayerId, bonus.Turn));
    }

    [Fact]
    public void Top_up_mode_fills_the_balance_to_the_minimum()
    {
        var table = new TagTable(players: 3, s =>
        {
            s.NextRunner = NextRunnerMode.Catcher;
            s.TurnBonusMode = TurnBonusMode.TopUp;
            s.TopUpMinimum = 25;
            s.StartingCoins = 8;
        });
        table.StartGame(runner: table.Ids[0]);

        table.Catch(Start.AddMinutes(40), catcher: table.Ids[1]);
        Assert.Equal(25, table.Host.Balance(table.Ids[1]));

        table.Catch(Start.AddMinutes(80), catcher: table.Ids[0]);
        Assert.Equal(25, table.Host.Balance(table.Ids[0])); // 8 → 25

        Assert.Equal(0, table.Host.TurnBonusFor(30)); // already above: nothing
    }

    [Fact]
    public void The_host_can_force_a_catch_and_two_confirmations_of_one_turn_count_once()
    {
        var table = new TagTable(players: 3, s => s.NextRunner = NextRunnerMode.Catcher);
        table.StartGame(runner: table.Ids[2]);
        var at = Start.AddMinutes(45);

        var forced = table.Host.CreateCatch(at, table.Ids[1], TagTestData.At(10, 10), new Random(1));
        var byRunner = table.Runner.CreateCatch(at.AddSeconds(20), table.Ids[0], TagTestData.At(20, 20), new Random(1));
        table.Send(TagMessages.ConfirmCatch(table.Host, forced, table.Ids[2]));
        table.Send(TagMessages.ConfirmCatch(table.Of(table.Ids[2]), byRunner, table.Ids[2]));

        Assert.True(forced.Forced);
        Assert.False(byRunner.Forced);
        foreach (var device in table.Devices)
        {
            Assert.Equal(2, device.Turns.Count);
            Assert.Equal(table.Ids[1], device.CurrentTurn!.RunnerId);
            Assert.True(device.Turns[0].Forced);
        }

        Assert.Equal(TagRoles.Runner, table.Of(table.Ids[1]).LocalRoleId);
        Assert.Single(table.Devices, d => d.LocalRoleId == TagRoles.Runner);
    }

    [Fact]
    public void A_chaser_asks_and_the_runner_sees_the_request()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[0]);
        var chaser = table.Of(table.Ids[1]);

        table.Send(TagMessages.RequestCatch(chaser, new CatchRequestPayload(0, Guid.NewGuid(), chaser.LocalPlayerId, Start.AddMinutes(35))));

        var request = Assert.Single(table.Runner.PendingCatchRequests);
        Assert.Equal(table.Ids[1], request.ChaserId);
        table.Catch(Start.AddMinutes(36), request.ChaserId);
        Assert.Empty(table.Host.PendingCatchRequests); // belongs to the ended turn
    }
}

public class TagEndTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;

    [Fact]
    public void Reaching_the_own_destination_wins()
    {
        var table = new TagTable();
        table.StartGame(runner: table.Ids[1]);
        var runner = table.Runner;
        var near = TagTestData.At(3000 + 60, 40); // ~72 m from D2 (radius 100 m)

        Assert.False(runner.CanReportArrival(TagTestData.At(3000 + 300, 0), Start.AddMinutes(20)));
        Assert.False(runner.CanReportArrival(table.DestinationOf(0), Start.AddMinutes(20))); // someone else's destination
        Assert.True(runner.CanReportArrival(near, Start.AddMinutes(20)));
        table.Send(TagMessages.ReportArrival(runner, runner.CreateArrival(Start.AddMinutes(20))));

        foreach (var device in table.Devices)
        {
            var results = device.Results(Start.AddMinutes(30));
            Assert.Equal(TagPhase.GameOver, device.Phase(Start.AddMinutes(21)));
            Assert.Equal(GameEndReason.ReachedDestination, results.Reason);
            Assert.Equal(table.Ids[1], Assert.Single(results.Winners).Id);
            Assert.Equal(TurnEndReason.ReachedDestination, results.Turns[0].Reason);
            Assert.Equal(TimeSpan.FromMinutes(20), results.Turns[0].Duration);
        }
    }

    [Fact]
    public void At_the_time_limit_the_destination_nearest_to_the_runner_wins()
    {
        var table = new TagTable(players: 3);
        table.StartGame(runner: table.Ids[0]);
        var limit = Start.AddHours(2);
        var runnerThere = TagTestData.At(3900, 0); // D1 at 2 km, D2 at 3 km, D3 at 4 km

        Assert.Equal(TagPhase.TimeUp, table.Host.Phase(limit));
        Assert.True(table.Runner.CanSettleTimeUp(limit.AddSeconds(5)));
        table.Send(TagMessages.EndGame(table.Runner, new EndPayload(limit.AddSeconds(5), runnerThere, EndPointSource.Gps)));

        var results = table.Devices[2].Results(limit.AddMinutes(1));
        Assert.Equal(table.Ids[2], Assert.Single(results.Winners).Id); // the winner may be a chaser
        Assert.Equal([table.Ids[2], table.Ids[1], table.Ids[0]], results.Ranking.Select(r => r.Player.Id));
        Assert.InRange(results.Ranking[0].DistanceMeters!.Value, 95, 105);
        Assert.InRange(results.Ranking[2].DistanceMeters!.Value, 1890, 1910);
        Assert.Equal(limit, table.Host.End!.At); // ends at the limit, not when the message was sent
        Assert.Equal(TurnEndReason.TimeUp, results.Turns[0].Reason);
    }

    [Fact]
    public void Destinations_equally_far_from_the_runner_draw()
    {
        var table = new TagTable(players: 3);
        var runnerAt = new Core.Geography.GeoPoint(52.25, 21.0);

        // D2 and D3 lie symmetrically north and south of where the runner ends, D1 further away.
        table.StartGame(runner: table.Ids[0], destinations: i => new Core.Geography.GeoPoint(52.25 + (i == 0 ? 0.05 : i == 1 ? 0.02 : -0.02), 21.0));
        table.Send(TagMessages.EndGame(table.Host, new EndPayload(Start.AddHours(2), runnerAt, EndPointSource.Manual)));

        var results = table.Host.Results(Start.AddHours(3));
        Assert.True(results.IsDraw);
        Assert.Equal([table.Ids[1], table.Ids[2]], results.Winners.Select(w => w.Id).Order());
        Assert.Equal(EndPointSource.Manual, results.PointSource);
        Assert.Contains("P2", TagTexts.Headline(results));
        Assert.Contains("P3", TagTexts.Headline(results));
    }

    [Fact]
    public void Only_the_runner_or_the_host_settles_a_game_whose_time_is_up()
    {
        var table = new TagTable(players: 3);
        table.StartGame(runner: table.Ids[1]);
        var limit = Start.AddHours(2);

        Assert.False(table.Runner.CanSettleTimeUp(limit.AddMinutes(-1)));
        Assert.True(table.Host.CanSettleTimeUp(limit));
        Assert.False(table.Devices[2].CanSettleTimeUp(limit));
    }

    [Fact]
    public void The_results_list_every_turn_with_coins_spent_and_earned()
    {
        var table = new TagTable(players: 2);
        table.StartGame(runner: table.Ids[0]);
        var runner = table.Runner;
        table.Send(TagMessages.StartRide(runner, runner.CreateRideStart(Coins.TransportMode.Bus, Start.AddMinutes(1))));
        table.Draw("cu-honour", Start.AddMinutes(10));
        table.Catch(Start.AddMinutes(45), table.Ids[1]); // closes the open ride: only the advance is paid

        var results = table.Host.Results(Start.AddMinutes(60));
        var (first, second) = (results.Turns[0], results.Turns[1]);
        Assert.Equal((1, 1, 0, TimeSpan.FromMinutes(45)), (first.Spent, first.Earned, first.Bonus, first.Duration));
        Assert.Equal("P2", first.Catcher!.Name);
        Assert.Equal((10, true, TimeSpan.FromMinutes(15)), (second.Bonus, second.IsRunning, second.Duration));
        Assert.Null(results.Reason);
    }
}

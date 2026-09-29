using System.Text.Json;
using NetTopologySuite.Geometries;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sync;
using TransitGames.Games.Tag.Challenges;
using TransitGames.Games.Tag.Sync;
using TransitGames.Games.Tag.Targets;
using TransitGames.Geo.Areas;
using TransitGames.Geo.Projection;

namespace TransitGames.Games.Tag.Tests;

internal static class TagTestData
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly GeoPoint Center = new(52.2297, 21.0122);
    public static readonly LocalProjection Projection = LocalProjection.CenteredOn(Center);

    /// <summary>A 6 × 6 km square around <see cref="Center"/>.</summary>
    public static GameArea Area() =>
        GameArea.Create("Test", Projection.Unproject(GeometryFactories.Local.ToGeometry(new Envelope(-3000, 3000, -3000, 3000))), GameAreaSource.Drawn);

    public static GeoPoint At(double east, double north) => Projection.Unproject(new Coordinate(east, north));

    public static TagSettings Settings(Action<TagSettings>? change = null)
    {
        var settings = new TagSettings
        {
            RoundDuration = TimeSpan.FromMinutes(60),
            HeadStart = TimeSpan.FromMinutes(10),
            RevealInterval = TimeSpan.FromMinutes(15),
            TargetRadiusMeters = 100,
            Rounds = 2,
            Targets =
            [
                new TagTarget("t1", "Park", At(1000, 0), 10),
                new TagTarget("t2", "Muzeum", At(-1000, 0), 5),
            ],
            FinalTarget = new TagTarget("final", "Meta", At(0, 2500), 0),
            StartedAt = Start,
        };
        change?.Invoke(settings);
        return settings;
    }

    public static TagSession NewSession(string role, TagSettings? settings = null)
    {
        IGameModule module = new TagModule();
        var session = (TagSession)module.CreateSession(settings ?? Settings(), role, Guid.NewGuid());
        session.Start(Start);
        return session;
    }

    /// <summary>What the app does with a message: the game's handler, then the role change it carries.</summary>
    public static void Deliver(TagSession session, SyncMessage message)
    {
        var json = JsonSerializer.Deserialize<SyncMessage>(JsonSerializer.Serialize(message, GameJson.Options), GameJson.Options)!;
        new TagMessageHandler().Handle(session, json);
        if (json.RoleChange is { } change)
        {
            session.ApplyRoleChange(change, json.Timestamp);
        }
    }
}

public class TagRoundTests
{
    private static readonly DateTimeOffset Start = TagTestData.Start;

    [Fact]
    public void A_round_goes_from_head_start_to_chase_to_time_up()
    {
        var session = TagTestData.NewSession(TagRoles.Runner);

        Assert.Equal(TagTeam.First, session.CurrentRound.Runners);
        Assert.Equal(TagPhase.HeadStart, session.Phase(Start.AddMinutes(5)));
        Assert.Equal(TagPhase.Chase, session.Phase(Start.AddMinutes(10)));
        Assert.Equal(TagPhase.TimeUp, session.Phase(Start.AddMinutes(60)));
        Assert.False(session.CanEndByCatch(Start.AddMinutes(5))); // chasers still wait
        Assert.True(session.CanEndByCatch(Start.AddMinutes(20)));
    }

    [Fact]
    public void Catch_ends_the_round_and_swaps_roles_on_both_devices()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);
        var chaser = TagTestData.NewSession(TagRoles.Chaser);
        var caughtAt = Start.AddMinutes(42);

        var message = TagMessages.EndRound(chaser, chaser.CreateRoundEnd(RoundEndReason.Caught, caughtAt, TagTestData.At(10, 10)));
        TagTestData.Deliver(chaser, message);
        TagTestData.Deliver(runner, message);

        Assert.Equal(TagRoles.Chaser, runner.LocalRoleId);
        Assert.Equal(TagRoles.Runner, chaser.LocalRoleId);
        Assert.Equal(TagTeam.First, runner.LocalTeam); // teams stay, roles swap
        Assert.Equal(TagTeam.Second, chaser.LocalTeam);
        foreach (var session in new[] { runner, chaser })
        {
            var first = session.Rounds[0];
            Assert.Equal((caughtAt, RoundEndReason.Caught), (first.EndedAt!.Value, first.EndReason!.Value));
            Assert.Null(first.EndPoint); // sent by a chaser: no position
            Assert.Equal((1, TagTeam.Second, caughtAt), (session.CurrentRound.Index, session.CurrentRound.Runners, session.CurrentRound.StartedAt));
            Assert.Equal(TagPhase.HeadStart, session.Phase(caughtAt.AddMinutes(1)));
        }
    }

    [Fact]
    public void Two_catch_reports_of_the_same_round_count_once()
    {
        var runner = TagTestData.NewSession(TagRoles.Runner);
        var chaser = TagTestData.NewSession(TagRoles.Chaser);
        var fromRunner = TagMessages.EndRound(runner, runner.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), TagTestData.At(5, 5)));
        var fromChaser = TagMessages.EndRound(chaser, chaser.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(31), null));

        foreach (var session in new[] { runner, chaser })
        {
            TagTestData.Deliver(session, fromRunner);
            TagTestData.Deliver(session, fromChaser);
            Assert.Equal(2, session.Rounds.Count);
            Assert.Equal(1, session.RolesVersion);
        }

        Assert.Equal(TagRoles.Chaser, runner.LocalRoleId);
        Assert.Equal(TagTestData.At(5, 5), runner.Rounds[0].EndPoint); // runners may report where
    }

    [Fact]
    public void The_last_round_ends_the_game_without_swapping()
    {
        var session = TagTestData.NewSession(TagRoles.Runner, TagTestData.Settings(s => s.Rounds = 1));

        var message = TagMessages.EndRound(session, session.CreateRoundEnd(RoundEndReason.TimeUp, Start.AddMinutes(90), null));
        TagTestData.Deliver(session, message);

        Assert.Null(message.RoleChange);
        Assert.Equal(TagRoles.Runner, session.LocalRoleId);
        Assert.Equal(TagPhase.GameOver, session.Phase(Start.AddMinutes(91)));
        Assert.Equal(Start.AddMinutes(60), session.Rounds[0].EndedAt); // time up counts until the round limit only
    }

    [Fact]
    public void A_device_joining_after_a_swap_keeps_its_role_while_catching_up()
    {
        // Joins as a runner after round 1 ended: that makes it team 2.
        IGameModule module = new TagModule();
        var joined = (TagSession)module.CreateSession(TagTestData.Settings(), TagRoles.Runner, Guid.NewGuid());
        joined.InitializeRolesVersion(1);
        joined.Start(Start.AddMinutes(50));
        Assert.Equal(TagTeam.Second, joined.LocalTeam);

        var host = TagTestData.NewSession(TagRoles.Chaser);
        TagTestData.Deliver(joined, TagMessages.EndRound(host, host.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(40), null)));

        Assert.Equal(TagRoles.Runner, joined.LocalRoleId);
        Assert.Equal((1, TagTeam.Second), (joined.CurrentRound.Index, joined.CurrentRound.Runners));
    }

    [Fact]
    public void Reveal_times_follow_the_interval_from_the_round_start()
    {
        var session = TagTestData.NewSession(TagRoles.Runner);

        Assert.Equal(Start.AddMinutes(15), session.NextRevealAt(Start.AddMinutes(1)));
        Assert.Equal(Start.AddMinutes(30), session.NextRevealAt(Start.AddMinutes(15)));
    }

    [Fact]
    public void State_survives_a_snapshot_round_trip()
    {
        IGameModule module = new TagModule();
        var session = TagTestData.NewSession(TagRoles.Runner);
        TagTestData.Deliver(session, TagMessages.CaptureTarget(session, new CapturePayload(0, "t1", Start.AddMinutes(20))));
        TagTestData.Deliver(session, TagMessages.EndRound(session, session.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), null)));

        var restored = (TagSession)module.RestoreSession(session.CreateSnapshot(Start.AddMinutes(31)));

        Assert.Equal((TagRoles.Chaser, 1), (restored.LocalRoleId, restored.RolesVersion));
        Assert.Equal(2, restored.Rounds.Count);
        Assert.Equal("t1", Assert.Single(restored.Rounds[0].Captures).TargetId);
        Assert.Equal("Park", restored.Settings.Targets[0].Name);
    }
}

public class TagTargetTests
{
    private static readonly DateTimeOffset Start = TagTestData.Start;

    [Fact]
    public void Targets_are_captured_once_per_round_and_only_during_it()
    {
        var session = TagTestData.NewSession(TagRoles.Runner);
        Assert.True(session.CanCapture("t1", Start.AddMinutes(20)));

        var capture = TagMessages.CaptureTarget(session, new CapturePayload(0, "t1", Start.AddMinutes(20)));
        TagTestData.Deliver(session, capture);
        TagTestData.Deliver(session, capture);
        TagTestData.Deliver(session, TagMessages.CaptureTarget(session, new CapturePayload(0, "unknown", Start.AddMinutes(21))));

        Assert.Equal(10, Assert.Single(session.CurrentRound.Captures).Reward);
        Assert.False(session.CanCapture("t1", Start.AddMinutes(22)));
        Assert.Equal(["t2"], session.ActiveTargets.Select(t => t.Id));

        // Next round: every target is available again, for the other team.
        TagTestData.Deliver(session, TagMessages.EndRound(session, session.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(30), null)));
        Assert.Equal(["t1", "t2"], session.ActiveTargets.Select(t => t.Id));
        Assert.False(session.CanCapture("t1", Start.AddMinutes(31))); // chasers now
    }

    [Fact]
    public void Targets_in_reach_and_the_final_target_use_the_target_radius()
    {
        var session = TagTestData.NewSession(TagRoles.Runner);

        Assert.Equal(["t1"], session.TargetsInReach(TagTestData.At(1050, 30)).Select(t => t.Id));
        Assert.Empty(session.TargetsInReach(TagTestData.At(1200, 0)));
        Assert.True(session.IsAtFinal(TagTestData.At(0, 2450)));
        Assert.False(session.IsAtFinal(TagTestData.At(0, 2000)));
    }

    [Fact]
    public void Score_is_minutes_survived_plus_target_points_plus_final_bonus()
    {
        var session = TagTestData.NewSession(TagRoles.Runner, TagTestData.Settings(s => s.FinalBonus = 30));
        TagTestData.Deliver(session, TagMessages.CaptureTarget(session, new CapturePayload(0, "t1", Start.AddMinutes(15))));
        TagTestData.Deliver(session, TagMessages.EndRound(session, session.CreateRoundEnd(RoundEndReason.ReachedFinal, Start.AddMinutes(45), null)));
        TagTestData.Deliver(session, TagMessages.EndRound(session, session.CreateRoundEnd(RoundEndReason.Caught, Start.AddMinutes(45 + 20), null)));

        var board = session.Scoreboard(Start.AddMinutes(100));

        Assert.Equal(45 + 10 + 30, board.Teams.Single(t => t.Team == TagTeam.First).Score);
        Assert.Equal(20, board.Teams.Single(t => t.Team == TagTeam.Second).Score);
        Assert.Equal(TagTeam.First, board.Leader);
        Assert.Equal([RoundEndReason.ReachedFinal, RoundEndReason.Caught], board.Rounds.Select(r => r.Reason!.Value));
    }

    [Fact]
    public void In_coins_mode_rewards_are_coins_to_spend_not_points()
    {
        var session = TagTestData.NewSession(TagRoles.Runner, TagTestData.Settings(s =>
        {
            s.RewardKind = RewardKind.Coins;
            s.StartingCoins = 4;
        }));
        TagTestData.Deliver(session, TagMessages.CaptureTarget(session, new CapturePayload(0, "t2", Start.AddMinutes(15))));
        var spend = TagMessages.SpendCoins(session, new SpendPayload(0, Guid.NewGuid(), 3, "Tramwaj", Start.AddMinutes(16)));
        TagTestData.Deliver(session, spend);
        TagTestData.Deliver(session, spend);

        Assert.Equal(4 + 5 - 3, session.CoinBalance);
        Assert.Equal(SyncAudience.OwnTeam, spend.Audience);
        Assert.Equal(0, session.Scoreboard(Start.AddMinutes(20)).Rounds[0].TargetPoints);
    }

    [Fact]
    public void Generated_targets_are_inside_spread_and_deterministic()
    {
        var area = TagTestData.Area();
        var candidates = new List<TargetCandidate>();
        for (var e = -4000; e <= 4000; e += 250)
        {
            for (var n = -4000; n <= 4000; n += 250)
            {
                candidates.Add(new TargetCandidate($"s{e}_{n}", $"S {e} {n}", TagTestData.At(e, n)));
            }
        }

        var targets = TargetGenerator.Generate(candidates, area, 6, seed: 7);

        Assert.Equal(6, targets.Count);
        Assert.All(targets, t => Assert.True(area.Contains(t.Location)));
        var closest = targets.SelectMany(a => targets.Where(b => b != a).Select(b => a.Location.DistanceTo(b.Location))).Min();
        Assert.True(closest >= 1000, $"targets too close: {closest:F0} m");
        Assert.All(targets, t => Assert.Contains(t.Reward, TargetGenerator.RewardSteps));
        Assert.Equal(targets.Select(t => t.Id), TargetGenerator.Generate(candidates, area, 6, seed: 7).Select(t => t.Id));
    }

    [Fact]
    public void With_few_candidates_all_of_them_become_targets()
    {
        var area = TagTestData.Area();
        var candidates = new[] { new TargetCandidate("a", "A", TagTestData.At(0, 0)), new TargetCandidate("b", "B", TagTestData.At(50, 0)) };

        Assert.Equal(2, TargetGenerator.Generate(candidates, area, 5, seed: 1).Count);
    }

    [Fact]
    public void Challenges_are_handed_out_and_located_ones_become_targets()
    {
        var area = TagTestData.Area();
        var inside = TagTestData.At(500, 500);
        var deck = ChallengeDeck.Parse($$"""
            { "challenges": [
              { "id": "a", "reward": 2, "title": { "pl": "A" }, "text": { "pl": "Zrób A" } },
              { "id": "here", "reward": 4, "title": { "pl": "Tu" }, "text": { "pl": "Zrób to tutaj" },
                "location": { "latitude": {{inside.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "longitude": {{inside.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "name": "Pomnik" } },
              { "id": "far", "reward": 4, "title": { "pl": "Daleko" }, "text": { "pl": "Poza obszarem" },
                "location": { "latitude": 10, "longitude": 10 } }
            ] }
            """);
        var targets = new[] { new TagTarget("t1", "T1", TagTestData.At(0, 0), 5) };

        var result = TargetGenerator.WithChallenges(targets, area, deck, new Random(1));

        Assert.Equal(("a", 7), (result[0].Challenge!.Id, result[0].Reward));
        var located = Assert.Single(result, t => t.Id == "challenge/here");
        Assert.Equal(("Pomnik", 14), (located.Name, located.Reward));
        Assert.DoesNotContain(result, t => t.Id == "challenge/far");
    }

    [Fact]
    public void Built_in_challenges_load_and_invalid_lists_are_rejected()
    {
        Assert.True(ChallengeDeck.BuiltIn.Challenges.Count >= 5);
        Assert.All(ChallengeDeck.BuiltIn.Challenges, c => Assert.False(string.IsNullOrEmpty(c.LocalizedText)));
        Assert.Throws<JsonException>(() => ChallengeDeck.Parse("""{ "challenges": [] }"""));
        Assert.Throws<JsonException>(() => ChallengeDeck.Parse("""{ "challenges": [ { "id": "x", "reward": 1, "title": {}, "text": {} } ] }"""));
    }
}

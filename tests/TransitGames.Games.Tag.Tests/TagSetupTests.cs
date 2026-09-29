using System.Text.Json;
using System.Text.Json.Nodes;
using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Core.Stations;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Legacy;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class DestinationTests
{
    private static readonly GeoPoint Start = TagTestData.Center;

    /// <summary>A 10 × 10 grid of stations, 1 km apart, around the centre.</summary>
    private static List<DestinationCandidate> Grid() =>
        [.. from x in Enumerable.Range(-5, 10)
            from y in Enumerable.Range(-5, 10)
            select new DestinationCandidate($"s{x}_{y}", $"S {x},{y}", TagTestData.At(x * 1000, y * 1000))];

    [Fact]
    public void Drawn_destinations_respect_the_distance_band_and_spacing()
    {
        var rules = new DestinationRules(2000, 5000, 2500);

        for (var seed = 0; seed < 20; seed++)
        {
            var drawn = DestinationGenerator.Draw(Grid(), Start, 4, rules, new Random(seed));

            Assert.NotNull(drawn);
            Assert.Equal(4, drawn.Count);
            Assert.All(drawn, d => Assert.InRange(d.Location.DistanceTo(Start), 2000, 5000));
            Assert.All(
                from a in drawn from b in drawn where a != b select a.Location.DistanceTo(b.Location),
                distance => Assert.True(distance >= 2500));
        }
    }

    [Fact]
    public void The_draw_is_deterministic_for_a_seed()
    {
        var rules = new DestinationRules(1000, 0, 1000);

        Assert.Equal(
            DestinationGenerator.Draw(Grid(), Start, 3, rules, new Random(9))!.Select(d => d.Id),
            DestinationGenerator.Draw(Grid(), Start, 3, rules, new Random(9))!.Select(d => d.Id));
    }

    [Fact]
    public void Impossible_rules_give_no_destinations()
    {
        Assert.Null(DestinationGenerator.Draw(Grid(), Start, 3, new DestinationRules(20_000, 0, 0), new Random(1)));
        Assert.Null(DestinationGenerator.Draw(Grid(), Start, 30, new DestinationRules(0, 3000, 2000), new Random(1)));
    }

    [Fact]
    public void Random_station_mode_draws_one_destination_per_player_at_the_start()
    {
        var table = new TagTable(players: 3, s =>
        {
            s.DestinationMode = DestinationMode.RandomStations;
            s.DestinationMinDistanceMeters = 1500;
            s.DestinationMaxDistanceMeters = 6000;
            s.DestinationMinSpacingMeters = 1000;
            s.Stations = [.. Grid().Select(c => new Station { Id = c.Id, Name = c.Name, Location = c.Location })];
        });

        var start = table.Host.CreateStart(TagTable.Start, Start, [.. table.Ids.Select(id => (id, table.Name(id)))], new Random(4));
        table.Send(TagMessages.StartGame(table.Host, start!));

        Assert.All(table.Devices[2].Players, p => Assert.NotNull(p.Destination));
        Assert.Equal(3, table.Devices[2].Players.Select(p => p.Destination!.Name).Distinct().Count());
    }

    [Fact]
    public void In_host_mode_only_the_host_sets_destinations()
    {
        var table = new TagTable(players: 2, s => s.DestinationMode = DestinationMode.HostSets);

        Assert.True(table.Host.CanSetDestination(table.Ids[1]));
        Assert.False(table.Devices[1].CanSetDestination(table.Ids[1]));
    }
}

public class LegacyTagTests
{
    private const string OldSettings = """
        {"roundDuration":"01:00:00","headStart":"00:10:00","revealInterval":"00:15:00","rounds":2,"rewardKind":"Points","finalBonus":30,
         "targets":[{"id":"t1","name":"Park","location":{"latitude":52.2,"longitude":21.0},"reward":10}],
         "area":null,"stations":[],"startedAt":"2026-09-01T12:00:00+00:00"}
        """;

    private const string OldState = """
        {"rounds":[
          {"index":0,"runners":"First","startedAt":"2026-09-01T12:00:00+00:00","endedAt":"2026-09-01T12:30:00+00:00","endReason":"Caught",
           "captures":[{"targetId":"t1","at":"2026-09-01T12:20:00+00:00","reward":10}],"spends":[]},
          {"index":1,"runners":"Second","startedAt":"2026-09-01T12:30:00+00:00","endedAt":null,"endReason":null,"captures":[],"spends":[]}],
         "pings":[],"lastOwnPingAt":null}
        """;

    private static GameSessionSnapshot OldSnapshot(SessionStatus status) => new()
    {
        SessionId = Guid.NewGuid(),
        GameId = TagModule.GameId,
        LocalRoleId = TagRoles.Chaser,
        RolesVersion = 1,
        Status = status,
        CreatedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 9, 1, 12, 45, 0, TimeSpan.Zero),
        SettingsJson = OldSettings,
        StateJson = OldState,
    };

    [Fact]
    public void An_unfinished_old_game_is_closed_and_opens_read_only_with_its_old_score()
    {
        var migrated = new LegacyTagMigration().Migrate(OldSnapshot(SessionStatus.Running));

        Assert.NotNull(migrated);
        Assert.Equal(SessionStatus.Finished, migrated.Status);
        Assert.Equal(migrated.UpdatedAt, migrated.FinishedAt);
        IGameModule module = new TagModule();
        var session = (TagSession)module.RestoreSession(migrated);
        Assert.True(session.Settings.IsLegacy);
        Assert.Equal(TimeSpan.FromMinutes(10), session.Settings.HeadStart); // old settings kept where they still fit
        Assert.Equal(TagPhase.GameOver, session.Phase(DateTimeOffset.UtcNow));

        // Team 1 (this device: chaser after one swap = runner in round 1): 30 min + 10 points; team 2: 15 min until the last save.
        var result = module.DescribeResult(session, DateTimeOffset.UtcNow);
        Assert.Contains("40 : 15", result.Headline);
        Assert.Contains(Resources.TagStrings.LegacyYou, result.Headline);
        Assert.Contains(result.Lines, l => l.Label == Resources.TagStrings.LegacyNoteTitle);
    }

    [Fact]
    public void New_games_are_left_alone_and_messages_do_not_touch_legacy_games()
    {
        IGameModule module = new TagModule();
        var session = module.CreateSession(new TagSettings(), TagRoles.Chaser);
        session.Start(DateTimeOffset.UtcNow);
        Assert.Null(new LegacyTagMigration().Migrate(session.CreateSnapshot(DateTimeOffset.UtcNow)));

        var legacy = (TagSession)module.RestoreSession(new LegacyTagMigration().Migrate(OldSnapshot(SessionStatus.Finished))!);
        new TagMessageHandler().Handle(legacy, TagMessages.AdjustCoins(legacy, new AdjustPayload(Guid.NewGuid(), Guid.Empty, 5, "x", DateTimeOffset.UtcNow)));
        Assert.Empty(legacy.Transactions);
    }

    [Fact]
    public void The_migrated_settings_carry_the_new_rules_version()
    {
        var migrated = new LegacyTagMigration().Migrate(OldSnapshot(SessionStatus.Finished))!;

        var settings = JsonNode.Parse(migrated.SettingsJson)!.AsObject();
        Assert.Equal(TagSettings.CurrentRulesVersion, (int)settings["rulesVersion"]!);
        Assert.True((bool)settings["isLegacy"]!);
        Assert.NotNull(JsonSerializer.Deserialize<TagState>(migrated.StateJson, GameJson.Options)!.LegacyResult);
    }
}

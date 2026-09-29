using TransitGames.Core.Games;
using TransitGames.Core.Stations;
using TransitGames.Core.Sync;
using TransitGames.Games.HideAndSeek.Cards;
using TransitGames.Games.HideAndSeek.Elimination;
using TransitGames.Geo.Places;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Sync;

namespace TransitGames.Games.HideAndSeek.Tests;

public class EliminationTests
{
    private static readonly IReadOnlyList<Station> Stations =
    [
        Station("center", 0, 0),
        Station("west", -3000, 0),
        Station("east", 3000, 0),
        Station("north", 0, 3000),
    ];

    [Fact]
    public void Without_answers_all_stations_remain_and_only_zones_are_possible()
    {
        var result = EliminationEngine.Compute(World.Area, Stations, 500, []);

        Assert.Equal((4, 4), (result.RemainingStations, result.TotalStations));
        Assert.InRange(result.PossibleLocal.Area, 4 * Math.PI * 500 * 500 * 0.99, 4 * Math.PI * 500 * 500 * 1.001);
    }

    [Fact]
    public void Answers_exclude_stations_whose_zone_no_longer_intersects()
    {
        var questions = new[]
        {
            Asked(new RadarSpec(World.At(-3000, 0), 1000), QuestionAnswer.No, minute: 1),
            Asked(new ThermometerSpec(World.At(0, -1000), World.At(0, 1000)), QuestionAnswer.No, minute: 2), // colder: south
        };

        var result = EliminationEngine.Compute(World.Area, Stations, 500, questions);

        Assert.Equal(["center", "east"], result.Stations.Where(s => s.IsPossible).Select(s => s.Station.Id));
        Assert.Equal(2, result.RemainingStations);
    }

    [Fact]
    public void State_is_a_function_of_the_question_list()
    {
        var radarNo = Asked(new RadarSpec(World.At(0, 0), 1000), QuestionAnswer.No, minute: 1);
        var unanswered = Asked(new RadarSpec(World.At(3000, 0), 1000), null, minute: 2);
        var vetoed = Asked(new RadarSpec(World.At(0, 3000), 1000), QuestionAnswer.No, minute: 3) with { Vetoed = true };

        var before = EliminationEngine.Compute(World.Area, Stations, 500, [radarNo, unanswered, vetoed]);
        var corrected = EliminationEngine.Compute(World.Area, Stations, 500, [radarNo with { Answer = QuestionAnswer.Yes }]);
        var removed = EliminationEngine.Compute(World.Area, Stations, 500, []);

        Assert.Equal(3, before.RemainingStations); // only "center" excluded; unanswered and vetoed change nothing
        Assert.Equal(["center"], corrected.Stations.Where(s => s.IsPossible).Select(s => s.Station.Id));
        Assert.Equal(4, removed.RemainingStations);
    }

    private static Station Station(string id, double east, double north) =>
        new() { Id = id, Name = id, Location = World.At(east, north), Modes = TransitModes.Subway };

    internal static AskedQuestion Asked(QuestionSpec spec, QuestionAnswer? answer, int minute) => new()
    {
        Id = Guid.NewGuid(),
        Spec = spec,
        AskedAt = DateTimeOffset.UnixEpoch.AddMinutes(minute),
        Answer = answer,
    };
}

public class SessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static HideAndSeekSession NewSession(string role, Action<HideAndSeekSettings>? configure = null)
    {
        var settings = new HideAndSeekSettings { HidingTime = TimeSpan.FromMinutes(30) };
        configure?.Invoke(settings);
        var session = (HideAndSeekSession)new HideAndSeekModule().CreateSession(settings, role);
        session.Start(Start);
        return session;
    }

    private static void Deliver(HideAndSeekSession session, SyncMessage message) =>
        new HideAndSeekMessageHandler().Handle(session, message);

    [Fact]
    public void Phases_follow_the_hiding_time_and_found()
    {
        var session = NewSession(HideAndSeekRoles.Seeker);

        Assert.Equal(HideAndSeekPhase.Hiding, session.Phase(Start.AddMinutes(29)));
        Assert.Equal(HideAndSeekPhase.Seeking, session.Phase(Start.AddMinutes(30)));

        session.ApplyFound(Start.AddMinutes(95), null);
        Assert.Equal(HideAndSeekPhase.Found, session.Phase(Start.AddMinutes(96)));
        Assert.Equal(TimeSpan.FromMinutes(65), session.HiddenFor(Start.AddHours(5)));
    }

    [Fact]
    public void Question_and_answer_messages_are_applied_once()
    {
        var seeker = NewSession(HideAndSeekRoles.Seeker);
        var asked = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 1000), null, 40);
        var question = HideAndSeekMessages.AskQuestion(seeker, asked);
        var answer = HideAndSeekMessages.AnswerQuestion(seeker, new AnswerPayload(asked.Id, QuestionAnswer.Yes, false, Start.AddMinutes(42)));

        Deliver(seeker, question);
        Deliver(seeker, question); // echo + catch-up replay
        Deliver(seeker, answer);
        Deliver(seeker, answer);

        var stored = Assert.Single(seeker.Questions);
        Assert.Equal(QuestionAnswer.Yes, stored.Answer);
    }

    [Fact]
    public void Hiders_draw_cards_once_per_answered_question_and_keep_the_chosen_ones()
    {
        var hider = NewSession(HideAndSeekRoles.Hider);
        var asked = EliminationTests.Asked(new MatchingSpec(World.At(0, 0), "museum", [World.PlaceAt("m", 1, 1)]), null, 40);
        hider.ApplyQuestion(asked);

        hider.ApplyAnswer(asked.Id, QuestionAnswer.No, false, Start.AddMinutes(41));
        hider.ApplyAnswer(asked.Id, QuestionAnswer.Yes, false, Start.AddMinutes(42)); // a correction does not pay twice

        var draw = Assert.Single(hider.PendingDraws);
        Assert.Equal(3, draw.Cards.Count); // matching: draw 3, keep 1
        Assert.Throws<ArgumentException>(() => hider.KeepCards(asked.Id, [0, 1]));
        hider.KeepCards(asked.Id, [2]);
        Assert.Equal([draw.Cards[2]], hider.Hand);
        Assert.Empty(hider.PendingDraws);
    }

    [Fact]
    public void Seekers_do_not_draw_cards()
    {
        var seeker = NewSession(HideAndSeekRoles.Seeker);
        var asked = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 500), null, 40);
        seeker.ApplyQuestion(asked);
        seeker.ApplyAnswer(asked.Id, QuestionAnswer.No, false, Start.AddMinutes(41));

        Assert.Empty(seeker.PendingDraws);
        Assert.Empty(seeker.Hand);
    }

    [Fact]
    public void Time_bonus_cards_add_to_the_result_on_both_devices()
    {
        var hider = NewSession(HideAndSeekRoles.Hider, s => s.DeckJson = """
            { "cards": [ { "id": "b7", "kind": "TimeBonus", "minutes": 7, "count": 3, "title": { "pl": "+7" } } ] }
            """);
        var seeker = NewSession(HideAndSeekRoles.Seeker);
        var asked = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 500), null, 40);
        hider.ApplyQuestion(asked);
        hider.ApplyAnswer(asked.Id, QuestionAnswer.No, false, Start.AddMinutes(41));
        hider.KeepCards(asked.Id, [0]);

        var play = hider.PlayCard(0, Start.AddMinutes(50))!;
        var message = HideAndSeekMessages.PlayCard(hider, play);
        Deliver(hider, message);
        Deliver(seeker, message);
        Deliver(seeker, message);

        Assert.Equal(7, hider.BonusMinutes);
        Assert.Equal(7, seeker.BonusMinutes);
        Assert.Equal(TimeSpan.FromMinutes(20 + 7), seeker.HiddenFor(Start.AddMinutes(50)));
    }

    [Fact]
    public void Veto_cards_are_used_when_answering_and_the_question_eliminates_nothing()
    {
        var hider = NewSession(HideAndSeekRoles.Hider, s => s.DeckJson = """
            { "cards": [ { "id": "v", "kind": "PowerUp", "effect": "veto", "count": 4 } ] }
            """);
        var first = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 500), null, 40);
        hider.ApplyQuestion(first);
        hider.ApplyAnswer(first.Id, QuestionAnswer.No, false, Start.AddMinutes(41));
        hider.KeepCards(first.Id, [0]);
        Assert.True(hider.HasVeto);
        Assert.Throws<InvalidOperationException>(() => hider.PlayCard(0, Start));

        var second = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 900), null, 45);
        hider.ApplyQuestion(second);
        Assert.True(hider.UseVeto());
        hider.ApplyAnswer(second.Id, null, true, Start.AddMinutes(46));

        Assert.False(hider.HasVeto);
        Assert.True(hider.FindQuestion(second.Id)!.Vetoed);
        Assert.Empty(hider.PendingDraws); // no reward for a vetoed question
    }

    [Fact]
    public void Found_message_from_the_hiders_never_carries_their_position()
    {
        var hider = NewSession(HideAndSeekRoles.Hider);
        var message = HideAndSeekMessages.FoundHiders(hider, new FoundPayload(Start.AddHours(1), World.At(10, 10)));

        Assert.Null(HideAndSeekMessages.Read<FoundPayload>(message).Location);
    }

    [Fact]
    public void Removing_a_question_is_a_team_correction()
    {
        var seeker = NewSession(HideAndSeekRoles.Seeker);
        var asked = EliminationTests.Asked(new PhotoSpec("tree"), null, 40);
        seeker.ApplyQuestion(asked);

        var message = HideAndSeekMessages.RemoveQuestion(seeker, asked.Id, Start);
        Deliver(seeker, message);

        Assert.Equal(SyncAudience.OwnTeam, message.Audience);
        Assert.Empty(seeker.Questions);
    }

    [Fact]
    public void Session_state_survives_a_snapshot_round_trip()
    {
        IGameModule module = new HideAndSeekModule();
        var session = NewSession(HideAndSeekRoles.Hider);
        session.ChooseStation("st-1");
        session.ApplyQuestion(EliminationTests.Asked(new TentaclesSpec(World.At(0, 0), "zoo", 2000, [World.PlaceAt("z", 5, 5)]), null, 40));

        var restored = (HideAndSeekSession)module.RestoreSession(session.CreateSnapshot(Start));

        Assert.Equal("st-1", restored.HiderStationId);
        Assert.IsType<TentaclesSpec>(Assert.Single(restored.Questions).Spec);
    }
}

public class DeckTests
{
    [Fact]
    public void Built_in_deck_loads_with_all_card_kinds()
    {
        var deck = CardDeck.BuiltIn;

        Assert.Contains(deck.Cards, c => c.Kind == CardKind.TimeBonus && c.Minutes > 0);
        Assert.Contains(deck.Cards, c => c.Kind == CardKind.Curse);
        Assert.Contains(deck.Cards, c => c.Effect == CardEffects.Veto);
        Assert.All(deck.Cards, c => Assert.False(string.IsNullOrWhiteSpace(c.LocalizedTitle)));
    }

    [Fact]
    public void Shuffle_is_deterministic_and_contains_every_copy()
    {
        var deck = CardDeck.BuiltIn;

        var first = deck.Shuffle(7);

        Assert.Equal(first, deck.Shuffle(7));
        Assert.NotEqual(first, deck.Shuffle(8));
        Assert.Equal(deck.Cards.Sum(c => c.Count), first.Count);
    }

    [Fact]
    public void Invalid_decks_are_rejected() =>
        Assert.Throws<System.Text.Json.JsonException>(() => CardDeck.Parse("""{ "cards": [] }"""));
}

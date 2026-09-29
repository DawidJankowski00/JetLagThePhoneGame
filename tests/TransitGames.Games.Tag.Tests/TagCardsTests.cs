using System.Text.Json;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Players;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class TagCardsTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;

    private static TagTable Started(int players = 2)
    {
        var table = new TagTable(players, s => s.NextRunner = NextRunnerMode.Catcher);
        table.StartGame(runner: table.Ids[0]);
        return table;
    }

    [Fact]
    public void A_curse_pays_at_once_and_goes_to_the_active_curses()
    {
        var table = Started();

        table.Draw("cu-double", Start.AddMinutes(5));

        var curse = Assert.Single(table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(6)));
        Assert.Equal(("cu-double", Start.AddMinutes(50)), (curse.Card.Id, curse.ExpiresAt!.Value));
        Assert.Equal(25, table.Host.Balance(table.Ids[0]));
    }

    [Fact]
    public void Minute_curses_expire_after_their_minutes()
    {
        var table = Started();
        table.Draw("cu-double", Start.AddMinutes(5));

        Assert.Single(table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(49)));
        Assert.Empty(table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(50)));
    }

    [Fact]
    public void Turn_curses_expire_at_the_catch_whole_game_curses_follow_the_player()
    {
        var table = Started();
        table.Draw("cu-turn", Start.AddMinutes(5));
        table.Draw("cu-game", Start.AddMinutes(6));
        Assert.Equal(["cu-turn", "cu-game"], table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(7)).Select(c => c.Card.Id));

        table.Catch(Start.AddMinutes(40), table.Ids[1]);
        var afterCatch = table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(41));
        Assert.Equal("cu-game", Assert.Single(afterCatch).Card.Id);

        table.Catch(Start.AddMinutes(80), table.Ids[0]); // runner again: the whole-game curse bites
        Assert.Equal(RideDenial.TransportBanned, table.Runner.QuoteRide(TransportMode.Train, Start.AddMinutes(81)).Denial);
        Assert.True(table.Runner.QuoteRide(TransportMode.Metro, Start.AddMinutes(81)).IsAllowed); // the turn curse is gone
    }

    [Fact]
    public void Turn_curse_has_no_fixed_expiry_and_whole_game_curse_ends_with_the_game()
    {
        var table = Started();
        table.Draw("cu-turn", Start.AddMinutes(5));
        table.Draw("cu-game", Start.AddMinutes(6));

        var curses = table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(7));

        Assert.Null(curses[0].ExpiresAt);
        Assert.Equal(Start.AddHours(2), curses[1].ExpiresAt);
    }

    [Fact]
    public void A_no_draw_curse_blocks_drawing_while_it_lasts()
    {
        var table = Started();
        table.Draw("cu-nodraw", Start.AddMinutes(5));

        Assert.Equal(DrawBlock.Cursed, table.Runner.DrawBlockReason(Start.AddMinutes(24)));
        Assert.Equal(DrawBlock.None, table.Runner.DrawBlockReason(Start.AddMinutes(25)));
    }

    [Fact]
    public void Honour_curses_are_listed_without_a_restriction()
    {
        var table = Started();
        table.Draw("cu-honour", Start.AddMinutes(5));

        var curse = Assert.Single(table.Host.ActiveCurses(table.Ids[0], Start.AddMinutes(6)));
        Assert.Null(curse.Card.Restriction);
        Assert.Null(TagTexts.Restriction(curse.Card.Restriction));
        Assert.Empty(table.Host.ActiveRestrictions(table.Ids[0], Start.AddMinutes(6)));
    }

    [Fact]
    public void One_challenge_at_a_time_and_completing_it_pays()
    {
        var table = Started();
        table.Draw("ch-free", Start.AddMinutes(5));
        var drawn = table.Host.DrawnCards[^1];

        Assert.Equal(DrawBlock.ActiveChallenge, table.Runner.DrawBlockReason(Start.AddMinutes(6)));
        Assert.Equal(20, table.Host.Balance(table.Ids[0])); // not yet
        table.Send(TagMessages.ResolveCard(table.Runner, new CardResolvePayload(drawn.Id, true, Start.AddMinutes(15))));
        table.Send(TagMessages.ResolveCard(table.Runner, new CardResolvePayload(drawn.Id, true, Start.AddMinutes(15)))); // retried

        Assert.Equal(23, table.Host.Balance(table.Ids[0]));
        Assert.Equal(DrawnCardStatus.Completed, table.Host.DrawnCards[^1].Status);
        Assert.Equal(DrawBlock.None, table.Runner.DrawBlockReason(Start.AddMinutes(16)));
    }

    [Fact]
    public void Giving_up_a_challenge_starts_the_draw_cooldown()
    {
        var table = Started();
        table.Draw("ch-free", Start.AddMinutes(5));
        var drawn = table.Host.DrawnCards[^1];

        table.Send(TagMessages.ResolveCard(table.Runner, new CardResolvePayload(drawn.Id, false, Start.AddMinutes(8))));

        Assert.Equal(DrawBlock.Cooldown, table.Runner.DrawBlockReason(Start.AddMinutes(17)));
        Assert.Equal(Start.AddMinutes(18), table.Runner.DrawCooldownEndsAt(Start.AddMinutes(17)));
        Assert.Equal(DrawBlock.None, table.Runner.DrawBlockReason(Start.AddMinutes(18)));
        Assert.Equal(20, table.Host.Balance(table.Ids[0]));
    }

    [Fact]
    public void A_timed_challenge_cannot_be_completed_late_and_its_expiry_counts_as_giving_up()
    {
        var table = Started();
        table.Draw("ch-timed", Start.AddMinutes(5)); // 10 min
        var drawn = table.Host.DrawnCards[^1];

        Assert.False(table.Runner.CanCompleteChallenge(drawn, Start.AddMinutes(15)));
        table.Send(TagMessages.ResolveCard(table.Runner, new CardResolvePayload(drawn.Id, true, Start.AddMinutes(16))));

        Assert.Equal(20, table.Host.Balance(table.Ids[0]));
        Assert.Null(table.Runner.ActiveChallenge(Start.AddMinutes(16)));
        Assert.Equal(DrawBlock.Cooldown, table.Runner.DrawBlockReason(Start.AddMinutes(16))); // until 15 + 10
        Assert.Equal(DrawBlock.None, table.Runner.DrawBlockReason(Start.AddMinutes(25)));
    }

    [Fact]
    public void Only_the_runner_draws()
    {
        var table = Started();

        Assert.All(table.Chasers, c => Assert.Equal(DrawBlock.NotRunner, c.DrawBlockReason(Start.AddMinutes(1))));
        var payload = table.Runner.CreateDraw(Start.AddMinutes(1), new Random(2));
        Assert.NotNull(table.Runner.Deck.Find(payload.CardId));
    }

    [Fact]
    public void Drawing_prefers_cards_not_drawn_yet()
    {
        var deck = CardDeck.Parse(TagTestData.DeckJson);
        var random = new Random(1);
        var drawn = new List<string>();

        for (var i = 0; i < deck.Cards.Count; i++)
        {
            drawn.Add(deck.Draw(drawn, random).Id);
        }

        Assert.Equal(deck.Cards.Select(c => c.Id).Order(), drawn.Order());
        Assert.Contains(deck.Draw(drawn, random).Id, drawn); // all drawn: reshuffled
    }

    [Fact]
    public void The_built_in_deck_has_challenges_and_enforceable_curses()
    {
        var deck = CardDeck.BuiltIn;

        Assert.Contains(deck.Cards, c => c.Type == CardType.Challenge);
        Assert.Contains(deck.Cards, c => c.Restriction?.Kind == RestrictionKind.OnlyTransport);
        Assert.Contains(deck.Cards, c => c.Restriction?.Kind == RestrictionKind.FareMultiplier && c.CurseDuration == CurseDuration.ForMinutes(45));
        Assert.Contains(deck.Cards, c => c.Type == CardType.Curse && c.Restriction is null);
        Assert.All(deck.Cards, c => Assert.False(string.IsNullOrWhiteSpace(c.LocalizedTitle)));
    }

    [Fact]
    public void The_challenge_list_of_the_first_version_loads_as_challenge_cards()
    {
        const string json = """
            { "challenges": [ { "id": "photo", "reward": 2, "title": { "pl": "Zdjęcie" }, "text": { "pl": "Zróbcie zdjęcie." },
                                "location": { "latitude": 52.2, "longitude": 21.0 } } ] }
            """;

        var card = Assert.Single(CardDeck.Parse(json).Cards);

        Assert.Equal(("photo", CardType.Challenge, 2, "Zróbcie zdjęcie."), (card.Id, card.Type, card.Reward, card.Description["pl"]));
    }

    [Theory]
    [InlineData("""{ "cards": [] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "type": "curse", "title": "T", "description": "D", "duration": "forever" } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "type": "curse", "title": "T", "description": "D", "restriction": { "kind": "banTransport" } } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "type": "challenge", "title": "T", "description": "D", "duration": "wholeGame" } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "type": "challenge", "title": "T", "description": "D", "reward": -1 } ] }""")]
    [InlineData("""{ "things": [] }""")]
    public void Invalid_decks_are_rejected(string json) =>
        Assert.Throws<JsonException>(() => CardDeck.Parse(json));

    [Theory]
    [InlineData("untilEndOfTurn", CurseDurationKind.UntilEndOfTurn, 0)]
    [InlineData("wholeGame", CurseDurationKind.WholeGame, 0)]
    [InlineData("minutes:45", CurseDurationKind.Minutes, 45)]
    public void Curse_durations_parse_and_print(string text, CurseDurationKind kind, int minutes)
    {
        Assert.True(CurseDuration.TryParse(text, out var duration));
        Assert.Equal(new CurseDuration(kind, minutes), duration);
        Assert.Equal(text, duration.ToString());
    }
}

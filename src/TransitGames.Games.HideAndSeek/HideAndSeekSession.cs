using TransitGames.Core.Events;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Games.HideAndSeek.Cards;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Resources;

namespace TransitGames.Games.HideAndSeek;

public enum HideAndSeekPhase
{
    /// <summary>Hiders travel to their zone; seekers wait.</summary>
    Hiding,

    /// <summary>Seekers ask questions; the hiders' time runs.</summary>
    Seeking,

    /// <summary>The hiders were found; the round is over.</summary>
    Found,
}

/// <summary>A card the hiders played (known to both teams: bonuses count in the result, curses bind the seekers).</summary>
public sealed record CardPlay(Guid Id, string CardId, CardKind Kind, int Minutes, DateTimeOffset PlayedAt);

/// <summary>Cards drawn for an answered question, waiting for the hiders to choose which to keep.</summary>
public sealed record PendingDraw(Guid QuestionId, List<string> Cards, int Keep);

public sealed class HideAndSeekState
{
    public List<AskedQuestion> Questions { get; set; } = [];

    public List<CardPlay> CardsPlayed { get; set; } = [];

    public DateTimeOffset? FoundAt { get; set; }

    /// <summary>Hiders' device only; never sent to the seekers.</summary>
    public string? HiderStationId { get; set; }

    /// <summary>Seekers' device: start point of a thermometer being measured.</summary>
    public GeoPoint? ThermometerStart { get; set; }

    // Hiders' cards (hiders' device only).
    public bool DeckShuffled { get; set; }

    public List<string> Deck { get; set; } = [];

    public List<string> Hand { get; set; } = [];

    public List<PendingDraw> PendingDraws { get; set; } = [];

    public HashSet<Guid> RewardedQuestions { get; set; } = [];
}

/// <summary>
/// One round of Hide and Seek with fixed roles. The question list is the source of truth for the map: the
/// possible area is recomputed from it (see Elimination.EliminationEngine). All changes coming from the other team
/// arrive as sync messages and are applied idempotently (see HideAndSeekMessageHandler).
/// </summary>
public sealed class HideAndSeekSession : GameSessionBase<HideAndSeekSettings, HideAndSeekState>
{
    public const int DrawPowerUpCards = 2;

    private CardDeck? _deck;

    public HideAndSeekSession(Guid id, HideAndSeekSettings settings, string localRoleId)
        : base(id, HideAndSeekModule.GameId, settings, localRoleId)
    {
    }

    public HideAndSeekSession(GameSessionSnapshot snapshot, HideAndSeekSettings settings)
        : base(snapshot, settings)
    {
    }

    public bool IsHider => LocalRoleId == HideAndSeekRoles.Hider;

    public DateTimeOffset HidingEndsAt => CreatedAt + Settings.HidingTime;

    public IReadOnlyList<AskedQuestion> Questions => State.Questions;

    public IReadOnlyList<CardPlay> CardsPlayed => State.CardsPlayed;

    public DateTimeOffset? FoundAt => State.FoundAt;

    public string? HiderStationId => State.HiderStationId;

    public GeoPoint? ThermometerStart => State.ThermometerStart;

    public IReadOnlyList<string> Hand => State.Hand;

    public IReadOnlyList<PendingDraw> PendingDraws => State.PendingDraws;

    public int CardsLeftInDeck => State.DeckShuffled ? State.Deck.Count : Deck.Cards.Sum(c => c.Count);

    public CardDeck Deck => _deck ??= Settings.DeckJson is { Length: > 0 } json ? CardDeck.Parse(json) : CardDeck.BuiltIn;

    public int BonusMinutes => State.CardsPlayed.Where(p => p.Kind == CardKind.TimeBonus).Sum(p => p.Minutes);

    public HideAndSeekPhase Phase(DateTimeOffset now) =>
        State.FoundAt is not null ? HideAndSeekPhase.Found
        : now < HidingEndsAt ? HideAndSeekPhase.Hiding
        : HideAndSeekPhase.Seeking;

    /// <summary>The hiders' score: time from the end of the hiding period until found (or now), plus bonuses.</summary>
    public TimeSpan HiddenFor(DateTimeOffset now)
    {
        var end = State.FoundAt ?? now;
        var seeking = end - HidingEndsAt;
        return (seeking < TimeSpan.Zero ? TimeSpan.Zero : seeking) + TimeSpan.FromMinutes(BonusMinutes);
    }

    public AskedQuestion? FindQuestion(Guid id) => State.Questions.FirstOrDefault(q => q.Id == id);

    public void ChooseStation(string stationId) => Mutate(s => s.HiderStationId = stationId);

    public void SetThermometerStart(GeoPoint? start) => Mutate(s => s.ThermometerStart = start);

    /// <summary>Adds a question (asked here or received). Idempotent by question id.</summary>
    public void ApplyQuestion(AskedQuestion question)
    {
        if (FindQuestion(question.Id) is not null)
        {
            return;
        }

        Mutate(s =>
        {
            s.Questions.Add(question with { Answer = null, AnsweredAt = null, Vetoed = false });
            if (question.Spec is ThermometerSpec)
            {
                s.ThermometerStart = null;
            }
        });
        Raise(GameEvent.Create(
            Id,
            question.AskedAt,
            HideAndSeekEvents.QuestionAsked,
            QuestionTexts.Title(question.Spec),
            QuestionTexts.Describe(question.Spec),
            QuestionTexts.Location(question.Spec),
            HideAndSeekRoles.Seeker,
            new Dictionary<string, string> { ["questionId"] = question.Id.ToString() }));
    }

    /// <summary>
    /// Sets (or corrects) the answer of a question. On the hiders' device the first real answer draws cards for
    /// the hiders according to the question's rule.
    /// </summary>
    public void ApplyAnswer(Guid questionId, QuestionAnswer? answer, bool vetoed, DateTimeOffset answeredAt)
    {
        var question = FindQuestion(questionId);
        if (question is null || (question.Answer == answer && question.Vetoed == vetoed))
        {
            return;
        }

        var wasAnswered = question.IsAnswered;
        Mutate(s =>
        {
            var index = s.Questions.FindIndex(q => q.Id == questionId);
            s.Questions[index] = question with { Answer = vetoed ? null : answer, Vetoed = vetoed, AnsweredAt = answeredAt };
            if (IsHider && Settings.CardsEnabled && !vetoed && s.RewardedQuestions.Add(questionId))
            {
                var rule = Settings.Rule(question.Spec.Type);
                var drawn = Draw(s, rule.CardsDrawn);
                if (drawn.Count > 0)
                {
                    s.PendingDraws.Add(new PendingDraw(questionId, drawn, Math.Min(rule.CardsKept, drawn.Count)));
                }
            }
        });

        if (!wasAnswered)
        {
            Raise(GameEvent.Create(
                Id,
                answeredAt,
                HideAndSeekEvents.QuestionAnswered,
                QuestionTexts.Title(question.Spec),
                vetoed ? HideAndSeekStrings.AnswerVetoed : QuestionTexts.DescribeAnswer(question.Spec, answer),
                actorRoleId: HideAndSeekRoles.Hider,
                data: new Dictionary<string, string> { ["questionId"] = questionId.ToString() }));
        }
    }

    /// <summary>Seekers' correction: removes a question from the history (the map is recomputed).</summary>
    public void RemoveQuestion(Guid questionId)
    {
        if (FindQuestion(questionId) is not null)
        {
            Mutate(s => s.Questions.RemoveAll(q => q.Id == questionId));
        }
    }

    /// <summary>Hiders keep the chosen cards of a draw (indexes into <see cref="PendingDraw.Cards"/>).</summary>
    public void KeepCards(Guid questionId, IReadOnlyCollection<int> indexes)
    {
        var pending = State.PendingDraws.FirstOrDefault(p => p.QuestionId == questionId);
        if (pending is null || indexes.Count != pending.Keep || indexes.Any(i => i < 0 || i >= pending.Cards.Count) || indexes.Distinct().Count() != indexes.Count)
        {
            throw new ArgumentException($"Choose exactly {pending?.Keep} different cards.", nameof(indexes));
        }

        Mutate(s =>
        {
            s.Hand.AddRange(indexes.Order().Select(i => pending.Cards[i]));
            s.PendingDraws.Remove(pending);
        });
    }

    /// <summary>
    /// Plays a card from the hand. Returns what to publish to the other team (null for cards that only act
    /// locally, e.g. "draw two"). A veto is played through <see cref="UseVeto"/>.
    /// </summary>
    public CardPlay? PlayCard(int handIndex, DateTimeOffset now)
    {
        if (handIndex < 0 || handIndex >= State.Hand.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(handIndex));
        }

        var card = Deck.Get(State.Hand[handIndex]);
        if (card.Effect == CardEffects.Veto)
        {
            throw new InvalidOperationException("A veto is played when answering a question.");
        }

        CardPlay? play = null;
        Mutate(s =>
        {
            s.Hand.RemoveAt(handIndex);
            if (card.Effect == CardEffects.Draw)
            {
                s.Hand.AddRange(Draw(s, DrawPowerUpCards));
            }
            else
            {
                play = new CardPlay(Guid.NewGuid(), card.Id, card.Kind, card.Minutes, now);
            }
        });
        return play;
    }

    /// <summary>Removes a veto card from the hand; false when there is none.</summary>
    public bool UseVeto()
    {
        var index = State.Hand.FindIndex(id => Deck.Find(id)?.Effect == CardEffects.Veto);
        if (index < 0)
        {
            return false;
        }

        Mutate(s => s.Hand.RemoveAt(index));
        return true;
    }

    public bool HasVeto => State.Hand.Any(id => Deck.Find(id)?.Effect == CardEffects.Veto);

    /// <summary>Records a played card (on both devices). Idempotent by play id.</summary>
    public void ApplyCardPlayed(CardPlay play)
    {
        if (State.CardsPlayed.Any(p => p.Id == play.Id))
        {
            return;
        }

        Mutate(s => s.CardsPlayed.Add(play));
        var card = Deck.Find(play.CardId);
        Raise(GameEvent.Create(
            Id,
            play.PlayedAt,
            GameEventTypes.CardPlayed,
            card?.LocalizedTitle ?? play.CardId,
            card?.LocalizedText,
            actorRoleId: HideAndSeekRoles.Hider));
    }

    /// <summary>The seekers found the hiders; the round ends. Idempotent.</summary>
    public void ApplyFound(DateTimeOffset at, GeoPoint? location)
    {
        if (State.FoundAt is not null)
        {
            return;
        }

        Mutate(s => s.FoundAt = at);
        Raise(GameEvent.Create(
            Id,
            at,
            GameEventTypes.Caught,
            HideAndSeekStrings.EventFound,
            string.Format(HideAndSeekStrings.ResultFormat, FormatDuration(HiddenFor(at))),
            location));
    }

    public static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";

    private List<string> Draw(HideAndSeekState state, int count)
    {
        if (!state.DeckShuffled)
        {
            state.Deck = Deck.Shuffle(Id.GetHashCode());
            state.DeckShuffled = true;
        }

        var drawn = state.Deck.Take(count).ToList();
        state.Deck.RemoveRange(0, drawn.Count);
        return drawn;
    }
}

public static class HideAndSeekEvents
{
    public const string QuestionAsked = GameEventTypes.QuestionAsked;
    public const string QuestionAnswered = GameEventTypes.QuestionAnswered;
}

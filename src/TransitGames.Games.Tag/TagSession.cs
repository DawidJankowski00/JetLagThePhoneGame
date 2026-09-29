using TransitGames.Core.Events;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Players;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag;

/// <summary>An active curse of a player and when it runs out (null: when the player is caught).</summary>
public sealed record ActiveCurse(DrawnCard Drawn, CardDefinition Card, DateTimeOffset? ExpiresAt);

/// <summary>Why the runner may not draw a card now.</summary>
public enum DrawBlock
{
    None,
    NotRunner,
    WrongPhase,

    /// <summary>A challenge is still active (one at a time).</summary>
    ActiveChallenge,

    /// <summary>After giving up a challenge.</summary>
    Cooldown,

    /// <summary>A curse forbids drawing.</summary>
    Cursed,
}

/// <summary>
/// A game of Tag: one runner, the other players chase. The runner pays coins for rides, earns them with cards and
/// wins by reaching their own destination. A catch makes one of the chasers the next runner, starting where the catch
/// happened; everybody keeps their coins. When the time runs out, the player whose destination is nearest to the
/// runner wins.
/// <para>
/// The state is the same on every device: it only changes through sync messages (own ones are echoed), applied
/// idempotently. Randomness (first runner, next runner, cards) is drawn by the sending device and carried in the
/// message. Players are identified by their online device id.
/// </para>
/// </summary>
public sealed class TagSession : GameSessionBase<TagSettings, TagState>
{
    private CardDeck? _deck;
    private GeoPoint? _ownPosition;

    public TagSession(Guid id, TagSettings settings, string localRoleId)
        : base(id, TagModule.GameId, settings, localRoleId)
    {
    }

    public TagSession(GameSessionSnapshot snapshot, TagSettings settings)
        : base(snapshot, settings)
    {
    }

    public CardDeck Deck => _deck ??= Settings.LoadCards();

    public Core.Games.GameResult? LegacyResult => State.LegacyResult;

    /// <summary>This device's player (offline tests: <see cref="Guid.Empty"/>).</summary>
    public Guid LocalPlayerId => LocalDeviceId ?? Guid.Empty;

    public TagPlayer? LocalPlayer => FindPlayer(LocalPlayerId);

    public bool IsStarted => State.StartedAt is not null;

    public DateTimeOffset? StartedAt => State.StartedAt;

    public GeoPoint? StartPoint => State.StartPoint;

    public DateTimeOffset? GameEndsAt => State.StartedAt + Settings.GameDuration;

    public IReadOnlyList<TagPlayer> Players => State.Players;

    public IReadOnlyList<TagTurn> Turns => State.Turns;

    public TagTurn? CurrentTurn => State.Turns.Count > 0 ? State.Turns[^1] : null;

    public TagPlayer? Runner => CurrentTurn is { } turn ? FindPlayer(turn.RunnerId) : null;

    public bool IsLocalRunner => CurrentTurn is { } turn && turn.RunnerId == LocalPlayerId && State.End is null;

    /// <summary>Before the start: whether this device's player is in the game (after the start only the roster plays).</summary>
    public bool IsLocalPlayerInGame => !IsStarted || LocalPlayer is not null;

    public DateTimeOffset? HeadStartEndsAt => CurrentTurn is { } turn ? turn.StartedAt + Settings.HeadStart : null;

    public TagGameEnd? End => State.End;

    public IReadOnlyDictionary<Guid, TagDestination> PendingDestinations => State.PendingDestinations;

    public IReadOnlyList<CoinTransaction> Transactions => State.Transactions;

    public IReadOnlyList<TagRide> Rides => State.Rides;

    public IReadOnlyList<DrawnCard> DrawnCards => State.Cards;

    public TagPlayer? FindPlayer(Guid id) => State.Players.FirstOrDefault(p => p.Id == id);

    public TagPhase Phase(DateTimeOffset now) =>
        State.End is not null || Status == SessionStatus.Finished ? TagPhase.GameOver
        : CurrentTurn is not { } turn ? TagPhase.Setup
        : now >= GameEndsAt ? TagPhase.TimeUp
        : now < turn.StartedAt + Settings.HeadStart ? TagPhase.HeadStart
        : TagPhase.Chase;

    /// <summary>The chasers may not chase (e.g. confirm a catch) during the head start.</summary>
    public bool IsChaseLocked(DateTimeOffset now) => Phase(now) == TagPhase.HeadStart;

    /// <summary>The runner's device knows where it is; used for the locations of its own timeline entries only.</summary>
    public void UpdateOwnPosition(GeoPoint point) => _ownPosition = point;

    // ---- Coins -----------------------------------------------------------------------------------------------

    public int Balance(Guid playerId) => State.Transactions.Where(t => t.PlayerId == playerId).Sum(t => t.Amount);

    public IEnumerable<CoinTransaction> TransactionsOf(Guid playerId) => State.Transactions.Where(t => t.PlayerId == playerId);

    /// <summary>Coins the new runner of a later turn gets: fixed, or the top-up to the minimum.</summary>
    public int TurnBonusFor(int balance) => Settings.TurnBonusMode == TurnBonusMode.TopUp
        ? Math.Max(0, Settings.TopUpMinimum - balance)
        : Math.Max(0, Settings.TurnBonus);

    // ---- Cards and curses ------------------------------------------------------------------------------------

    public CardDefinition? CardOf(DrawnCard drawn) => Deck.Find(drawn.CardId);

    /// <summary>When an active challenge can no longer be completed (null: no time limit).</summary>
    public DateTimeOffset? ChallengeDeadline(DrawnCard drawn) =>
        CardOf(drawn)?.TimeLimitMinutes is { } minutes ? drawn.DrawnAt.AddMinutes(minutes) : null;

    /// <summary>The runner's challenge in progress (not done, not given up, not expired).</summary>
    public DrawnCard? ActiveChallenge(DateTimeOffset now) =>
        CurrentTurn is not { } turn ? null
        : State.Cards.LastOrDefault(c => c.Type == CardType.Challenge && c.Status == DrawnCardStatus.Active
            && c.PlayerId == turn.RunnerId && c.Turn == turn.Index && !(ChallengeDeadline(c) <= now));

    /// <summary>Curses of <paramref name="playerId"/> active at <paramref name="now"/>, with their expiry.</summary>
    public IReadOnlyList<ActiveCurse> ActiveCurses(Guid playerId, DateTimeOffset now)
    {
        var result = new List<ActiveCurse>();
        foreach (var drawn in State.Cards.Where(c => c.Type == CardType.Curse && c.PlayerId == playerId))
        {
            if (CardOf(drawn) is not { } card)
            {
                continue;
            }

            var duration = card.CurseDuration;
            var active = duration.Kind switch
            {
                CurseDurationKind.UntilEndOfTurn => CurrentTurn is { } turn && turn.Index == drawn.Turn && turn.EndedAt is null,
                CurseDurationKind.Minutes => now < drawn.DrawnAt.AddMinutes(duration.Minutes),
                _ => true,
            };
            if (active && State.End is null)
            {
                result.Add(new ActiveCurse(
                    drawn,
                    card,
                    duration.Kind switch
                    {
                        CurseDurationKind.Minutes => drawn.DrawnAt.AddMinutes(duration.Minutes),
                        CurseDurationKind.WholeGame => GameEndsAt,
                        _ => null,
                    }));
            }
        }

        return result;
    }

    /// <summary>Handicaps the app enforces on <paramref name="playerId"/> now.</summary>
    public IReadOnlyList<CardRestriction> ActiveRestrictions(Guid playerId, DateTimeOffset now) =>
        [.. ActiveCurses(playerId, now).Select(c => c.Card.Restriction).OfType<CardRestriction>()];

    /// <summary>Until when drawing is blocked after the last challenge was given up or expired.</summary>
    public DateTimeOffset? DrawCooldownEndsAt(DateTimeOffset now)
    {
        if (CurrentTurn is not { } turn)
        {
            return null;
        }

        var ends = State.Cards
            .Where(c => c.Type == CardType.Challenge && c.PlayerId == turn.RunnerId)
            .Select(c => c.Status == DrawnCardStatus.Abandoned ? c.ResolvedAt
                : c.Status == DrawnCardStatus.Active && ChallengeDeadline(c) is { } deadline && deadline <= now ? deadline
                : null)
            .OfType<DateTimeOffset>()
            .Select(at => at + Settings.DrawCooldown)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        return ends > now ? ends : null;
    }

    public DrawBlock DrawBlockReason(DateTimeOffset now) =>
        !IsLocalRunner ? DrawBlock.NotRunner
        : Phase(now) is not (TagPhase.HeadStart or TagPhase.Chase) ? DrawBlock.WrongPhase
        : ActiveChallenge(now) is not null ? DrawBlock.ActiveChallenge
        : ActiveRestrictions(LocalPlayerId, now).Any(r => r.Kind == RestrictionKind.NoDraw) ? DrawBlock.Cursed
        : DrawCooldownEndsAt(now) is not null ? DrawBlock.Cooldown
        : DrawBlock.None;

    public CardDrawPayload CreateDraw(DateTimeOffset now, Random random)
    {
        if (DrawBlockReason(now) is var block and not DrawBlock.None)
        {
            throw new InvalidOperationException($"Cannot draw now: {block}.");
        }

        var drawnIds = State.Cards.Select(c => c.CardId).ToHashSet();
        return new CardDrawPayload(CurrentTurn!.Index, Guid.NewGuid(), Deck.Draw(drawnIds, random).Id, now);
    }

    public bool CanCompleteChallenge(DrawnCard drawn, DateTimeOffset now) =>
        IsLocalRunner && drawn.Status == DrawnCardStatus.Active && drawn.Type == CardType.Challenge
        && drawn.Turn == CurrentTurn?.Index && !(ChallengeDeadline(drawn) <= now) && Phase(now) is TagPhase.HeadStart or TagPhase.Chase;

    // ---- Rides -----------------------------------------------------------------------------------------------

    public TagRide? OpenRide(Guid playerId) => State.Rides.LastOrDefault(r => r.PlayerId == playerId && r.IsOpen);

    /// <summary>What a ride with <paramref name="mode"/> would cost the local runner now, and whether it may start.</summary>
    public FareQuote QuoteRide(TransportMode mode, DateTimeOffset now, int? manualFare = null) =>
        FareCalculator.Quote(Settings, mode, Balance(LocalPlayerId), ActiveRestrictions(LocalPlayerId, now), manualFare);

    public RideStartPayload CreateRideStart(TransportMode mode, DateTimeOffset now, int? manualFare = null)
    {
        if (!IsLocalRunner || Phase(now) is not (TagPhase.HeadStart or TagPhase.Chase) || OpenRide(LocalPlayerId) is not null)
        {
            throw new InvalidOperationException("Only the runner can start a ride, one at a time.");
        }

        var quote = QuoteRide(mode, now, manualFare);
        if (!quote.IsAllowed)
        {
            throw new InvalidOperationException($"The ride is not allowed: {quote.Denial}.");
        }

        return new RideStartPayload(CurrentTurn!.Index, Guid.NewGuid(), mode, Settings.FareModel, quote.Upfront, quote.Multiplier, now);
    }

    /// <param name="distanceMeters">Length of the ride's GPS track (per-km fares).</param>
    public RideEndPayload CreateRideEnd(TagRide ride, double distanceMeters, DateTimeOffset now) =>
        new(ride.Id, distanceMeters, FareCalculator.TotalFare(Settings, ride.Mode, ride.FareModel, ride.Upfront, ride.Multiplier, distanceMeters), now);

    // ---- Catch, arrival, end ---------------------------------------------------------------------------------

    /// <summary>A chaser may ask the runner to confirm a catch once the head start is over.</summary>
    public bool CanRequestCatch(DateTimeOffset now) =>
        !IsLocalRunner && LocalPlayer is not null && Phase(now) == TagPhase.Chase;

    /// <summary>The runner confirms a catch; the host may force it when the runner's phone does not.</summary>
    public bool CanConfirmCatch(DateTimeOffset now) => (IsLocalRunner || IsHost) && Phase(now) == TagPhase.Chase;

    /// <summary>Catch requests of the current turn, newest first.</summary>
    public IReadOnlyList<CatchRequest> PendingCatchRequests =>
        CurrentTurn is { EndedAt: null } turn ? [.. State.CatchRequests.Where(r => r.Turn == turn.Index).OrderByDescending(r => r.At)] : [];

    public CatchPayload CreateCatch(DateTimeOffset now, Guid? catcherId, GeoPoint point, Random random)
    {
        if (!CanConfirmCatch(now))
        {
            throw new InvalidOperationException("A catch can only be confirmed by the runner or the host after the head start.");
        }

        var turn = CurrentTurn!;
        var next = RunnerSelection.PickNext([.. State.Players.Select(p => p.Id)], turn.RunnerId, catcherId, Settings.NextRunner, random);
        return new CatchPayload(turn.Index, now, catcherId, point, next, Forced: !IsLocalRunner);
    }

    public bool IsAtOwnDestination(GeoPoint point) =>
        LocalPlayer?.Destination is { } destination && destination.Location.DistanceTo(point) <= Settings.DestinationRadiusMeters;

    public bool CanReportArrival(GeoPoint point, DateTimeOffset now) =>
        IsLocalRunner && Phase(now) is TagPhase.HeadStart or TagPhase.Chase && IsAtOwnDestination(point);

    public ArrivedPayload CreateArrival(DateTimeOffset now) => new(CurrentTurn!.Index, now);

    /// <summary>The runner (GPS) or the host (last known position / map) settles a game whose time ran out.</summary>
    public bool CanSettleTimeUp(DateTimeOffset now) => Phase(now) == TagPhase.TimeUp && (IsLocalRunner || IsHost);

    // ---- Setup -----------------------------------------------------------------------------------------------

    /// <summary>Whether the local device may set the destination of <paramref name="playerId"/> before the start.</summary>
    public bool CanSetDestination(Guid playerId) => !IsStarted && Settings.DestinationMode switch
    {
        DestinationMode.SelfPick => playerId == LocalPlayerId,
        DestinationMode.HostSets => IsHost,
        _ => false,
    };

    /// <summary>
    /// The start the host sends: the joined players (roster), their destinations (picked, or drawn now from the game's
    /// stations) and the first runner drawn at random.
    /// </summary>
    /// <returns>The payload, or null when a player has no destination or the stations do not allow a fair draw.</returns>
    public StartPayload? CreateStart(DateTimeOffset now, GeoPoint startPoint, IReadOnlyList<(Guid Id, string Name)> roster, Random random)
    {
        if (IsStarted || !IsHost || roster.Count < 2)
        {
            return null;
        }

        IReadOnlyList<TagDestination?> destinations;
        if (Settings.DestinationMode == DestinationMode.RandomStations)
        {
            var candidates = Settings.Stations.Where(s => s.IsEnabled).Select(s => new DestinationCandidate(s.Id, s.Name, s.Location)).ToList();
            var drawn = DestinationGenerator.Draw(candidates, startPoint, roster.Count, Settings.DestinationRules, random);
            if (drawn is null)
            {
                return null;
            }

            destinations = [.. drawn.Select(d => new TagDestination(d.Location, d.Name))];
        }
        else
        {
            destinations = [.. roster.Select(p => State.PendingDestinations.GetValueOrDefault(p.Id))];
            if (destinations.Any(d => d is null))
            {
                return null;
            }
        }

        var players = roster.Select((p, i) => new StartPlayer(p.Id, p.Name, destinations[i]!.Location, destinations[i]!.Name)).ToList();
        var runner = RunnerSelection.PickFirst([.. roster.Select(p => p.Id)], random);
        return new StartPayload(now, startPoint, players, runner);
    }

    // ---- Applying messages -----------------------------------------------------------------------------------

    public void ApplyDestination(DestinationPayload payload)
    {
        if (IsStarted)
        {
            return;
        }

        Mutate(s => s.PendingDestinations[payload.PlayerId] = new TagDestination(payload.Location, payload.Name));
    }

    public void ApplyStart(StartPayload start)
    {
        if (IsStarted || start.Players.Count < 2 || start.Players.All(p => p.Id != start.RunnerId))
        {
            return;
        }

        Mutate(s =>
        {
            s.StartedAt = start.StartedAt;
            s.StartPoint = start.StartPoint;
            s.Players = [.. start.Players.Select(p => new TagPlayer
            {
                Id = p.Id,
                Name = p.Name,
                Destination = p.Destination is { } location ? new TagDestination(location, p.DestinationName ?? string.Empty) : null,
            })];
            s.Turns.Add(new TagTurn { Index = 0, RunnerId = start.RunnerId, StartedAt = start.StartedAt, StartPoint = start.StartPoint });
            foreach (var player in s.Players)
            {
                AddTransaction(s, player.Id, start.StartedAt, Settings.StartingCoins, CoinTransactionKind.Start, 0);
            }
        });

        Raise(GameEvent.Create(Id, start.StartedAt, TagEventTypes.GameStarted, TagStrings.EventGameStarted, location: start.StartPoint));
        RaiseTurnStarted(CurrentTurn!);
    }

    public void ApplyRideStart(RideStartPayload payload)
    {
        if (CurrentTurn is not { } turn || payload.Turn != turn.Index || turn.EndedAt is not null
            || State.Rides.Any(r => r.Id == payload.RideId))
        {
            return;
        }

        Mutate(s =>
        {
            s.Rides.Add(new TagRide
            {
                Id = payload.RideId,
                PlayerId = turn.RunnerId,
                Turn = turn.Index,
                Mode = payload.Mode,
                FareModel = payload.FareModel,
                StartedAt = payload.At,
                Upfront = payload.Upfront,
                Multiplier = payload.Multiplier,
            });
            AddTransaction(s, turn.RunnerId, payload.At, -payload.Upfront, CoinTransactionKind.Ride, turn.Index, TagTexts.Transport(payload.Mode), payload.RideId);
        });

        Raise(GameEvent.Create(
            Id,
            payload.At,
            TagEventTypes.RideStarted,
            string.Format(TagStrings.EventRideStartedFormat, TagTexts.Transport(payload.Mode)),
            string.Format(TagStrings.CoinsFormat, payload.Upfront),
            OwnLocation(turn.RunnerId),
            TagRoles.Runner));
    }

    public void ApplyRideEnd(RideEndPayload payload)
    {
        if (State.Rides.FirstOrDefault(r => r.Id == payload.RideId) is not { IsOpen: true } ride)
        {
            return;
        }

        Mutate(s =>
        {
            CloseRide(s, ride, payload.At, payload.DistanceMeters, payload.TotalFare);
        });

        Raise(GameEvent.Create(
            Id,
            payload.At,
            TagEventTypes.RideEnded,
            string.Format(TagStrings.EventRideEndedFormat, TagTexts.Transport(ride.Mode)),
            string.Format(TagStrings.RideSummaryFormat, TagTexts.Distance(payload.DistanceMeters), payload.TotalFare),
            OwnLocation(ride.PlayerId),
            TagRoles.Runner));
    }

    public void ApplyCardDraw(CardDrawPayload payload)
    {
        if (CurrentTurn is not { } turn || payload.Turn != turn.Index || turn.EndedAt is not null
            || State.Cards.Any(c => c.Id == payload.DrawId) || Deck.Find(payload.CardId) is not { } card)
        {
            return;
        }

        Mutate(s =>
        {
            s.Cards.Add(new DrawnCard
            {
                Id = payload.DrawId,
                CardId = card.Id,
                Type = card.Type,
                PlayerId = turn.RunnerId,
                Turn = turn.Index,
                DrawnAt = payload.At,
                Status = card.Type == CardType.Curse ? DrawnCardStatus.Completed : DrawnCardStatus.Active,
                ResolvedAt = card.Type == CardType.Curse ? payload.At : null,
            });
            if (card.Type == CardType.Curse && card.Reward > 0)
            {
                AddTransaction(s, turn.RunnerId, payload.At, card.Reward, CoinTransactionKind.Curse, turn.Index, card.LocalizedTitle, payload.DrawId);
            }
        });

        var format = card.Type == CardType.Curse ? TagStrings.EventCurseDrawnFormat : TagStrings.EventChallengeDrawnFormat;
        Raise(GameEvent.Create(
            Id, payload.At, TagEventTypes.CardDrawn, string.Format(format, card.LocalizedTitle), card.LocalizedDescription, OwnLocation(turn.RunnerId), TagRoles.Runner));
    }

    public void ApplyCardResolve(CardResolvePayload payload)
    {
        if (State.Cards.FirstOrDefault(c => c.Id == payload.DrawId) is not { Status: DrawnCardStatus.Active, Type: CardType.Challenge } drawn
            || CardOf(drawn) is not { } card
            || payload.Completed && ChallengeDeadline(drawn) is { } deadline && payload.At > deadline)
        {
            return;
        }

        Mutate(s =>
        {
            drawn.Status = payload.Completed ? DrawnCardStatus.Completed : DrawnCardStatus.Abandoned;
            drawn.ResolvedAt = payload.At;
            if (payload.Completed && card.Reward > 0)
            {
                AddTransaction(s, drawn.PlayerId, payload.At, card.Reward, CoinTransactionKind.Challenge, drawn.Turn, card.LocalizedTitle, drawn.Id);
            }
        });

        Raise(payload.Completed
            ? GameEvent.Create(Id, payload.At, TagEventTypes.ChallengeCompleted, string.Format(TagStrings.EventChallengeCompletedFormat, card.LocalizedTitle),
                string.Format(TagStrings.CoinsGainedFormat, card.Reward), OwnLocation(drawn.PlayerId), TagRoles.Runner)
            : GameEvent.Create(Id, payload.At, TagEventTypes.ChallengeAbandoned, string.Format(TagStrings.EventChallengeAbandonedFormat, card.LocalizedTitle),
                actorRoleId: TagRoles.Runner));
    }

    public void ApplyCatchRequest(CatchRequestPayload payload)
    {
        if (CurrentTurn is not { } turn || payload.Turn != turn.Index || turn.EndedAt is not null
            || State.CatchRequests.Any(r => r.Id == payload.RequestId))
        {
            return;
        }

        Mutate(s => s.CatchRequests.Add(new CatchRequest { Id = payload.RequestId, ChaserId = payload.ChaserId, Turn = payload.Turn, At = payload.At }));
        Raise(GameEvent.Create(
            Id,
            payload.At,
            TagEventTypes.CatchRequested,
            string.Format(TagStrings.EventCatchRequestedFormat, NameOf(payload.ChaserId)),
            actorRoleId: TagRoles.Chaser));
    }

    public void ApplyCatch(CatchPayload payload)
    {
        if (CurrentTurn is not { } turn || payload.Turn != turn.Index || turn.EndedAt is not null || State.End is not null
            || FindPlayer(payload.NextRunnerId) is null || payload.NextRunnerId == turn.RunnerId)
        {
            return; // already handled (e.g. the host forced it while the runner confirmed)
        }

        var caught = turn.RunnerId;
        var survived = payload.At - turn.StartedAt;
        Mutate(s =>
        {
            turn.EndedAt = payload.At;
            turn.EndReason = TurnEndReason.Caught;
            turn.CatcherId = payload.CatcherId;
            turn.Forced = payload.Forced;
            CloseTurn(s, turn, payload.At);

            var next = new TagTurn { Index = turn.Index + 1, RunnerId = payload.NextRunnerId, StartedAt = payload.At, StartPoint = payload.Point };
            s.Turns.Add(next);
            var bonus = TurnBonusFor(Balance(next.RunnerId));
            if (bonus > 0)
            {
                AddTransaction(s, next.RunnerId, payload.At, bonus, CoinTransactionKind.TurnBonus, next.Index);
            }
        });

        Raise(GameEvent.Create(
            Id,
            payload.At,
            TagEventTypes.Caught,
            payload.CatcherId is { } catcher
                ? string.Format(TagStrings.EventCaughtByFormat, NameOf(caught), NameOf(catcher), TagTexts.Duration(survived))
                : string.Format(TagStrings.EventCaughtFormat, NameOf(caught), TagTexts.Duration(survived)),
            payload.Forced ? TagStrings.EventCatchForced : null,
            payload.Point));
        RaiseTurnStarted(CurrentTurn!);
    }

    public void ApplyArrived(ArrivedPayload payload)
    {
        if (CurrentTurn is not { } turn || payload.Turn != turn.Index || turn.EndedAt is not null || State.End is not null)
        {
            return;
        }

        var destination = FindPlayer(turn.RunnerId)?.Destination;
        Mutate(s =>
        {
            turn.EndedAt = payload.At;
            turn.EndReason = TurnEndReason.ReachedDestination;
            CloseTurn(s, turn, payload.At);
            s.End = new TagGameEnd
            {
                At = payload.At,
                Reason = GameEndReason.ReachedDestination,
                RunnerId = turn.RunnerId,
                RunnerPoint = destination?.Location,
                Source = EndPointSource.Gps,
            };
        });

        Raise(GameEvent.Create(
            Id, payload.At, TagEventTypes.Arrived, string.Format(TagStrings.EventArrivedFormat, NameOf(turn.RunnerId)), destination?.Name, destination?.Location, TagRoles.Runner));
        RaiseGameEnded();
    }

    public void ApplyEnd(EndPayload payload)
    {
        if (CurrentTurn is not { } turn || State.End is not null || GameEndsAt is not { } endsAt)
        {
            return;
        }

        // The game ends at the time limit, whenever the message was sent.
        var at = payload.At < endsAt ? payload.At : endsAt;
        Mutate(s =>
        {
            if (turn.EndedAt is null)
            {
                turn.EndedAt = at;
                turn.EndReason = TurnEndReason.TimeUp;
                CloseTurn(s, turn, at);
            }

            s.End = new TagGameEnd { At = at, Reason = GameEndReason.TimeUp, RunnerId = turn.RunnerId, RunnerPoint = payload.Point, Source = payload.Source };
        });

        Raise(GameEvent.Create(
            Id,
            at,
            TagEventTypes.TimeUp,
            TagStrings.EventTimeUp,
            TagTexts.PointSource(payload.Source))); // the place is on the game end entry
        RaiseGameEnded();
    }

    public void ApplyAdjust(AdjustPayload payload)
    {
        if (!IsStarted || FindPlayer(payload.PlayerId) is null || payload.Amount == 0 || State.Transactions.Any(t => t.Id == payload.Id))
        {
            return;
        }

        Mutate(s => s.Transactions.Add(new CoinTransaction
        {
            Id = payload.Id,
            PlayerId = payload.PlayerId,
            At = payload.At,
            Amount = payload.Amount,
            Kind = CoinTransactionKind.HostAdjustment,
            Turn = CurrentTurn?.Index ?? 0,
            Note = payload.Reason,
        }));
        Raise(GameEvent.Create(
            Id,
            payload.At,
            TagEventTypes.CoinsAdjusted,
            string.Format(TagStrings.EventCoinsAdjustedFormat, NameOf(payload.PlayerId), TagTexts.SignedCoins(payload.Amount)),
            payload.Reason));
    }

    // ---- Results ---------------------------------------------------------------------------------------------

    public TagResults Results(DateTimeOffset now) => TagScoring.Results(this, now);

    /// <summary>Whether the local player may see <paramref name="player"/>'s destination now.</summary>
    public bool CanSeeDestination(TagPlayer player) =>
        Settings.DestinationsVisible || player.Id == LocalPlayerId || State.End is not null || Status == SessionStatus.Finished;

    public string NameOf(Guid playerId) => FindPlayer(playerId)?.Name ?? TagStrings.UnknownPlayer;

    /// <summary>The runner's position is revealed every reveal interval from the start of the turn.</summary>
    public DateTimeOffset? NextRevealAt(DateTimeOffset now)
    {
        var interval = Settings.RevealInterval;
        if (interval <= TimeSpan.Zero || CurrentTurn is not { } turn || Phase(now) is TagPhase.GameOver or TagPhase.TimeUp)
        {
            return null;
        }

        var elapsed = Math.Max(0, (now - turn.StartedAt).Ticks);
        return turn.StartedAt + TimeSpan.FromTicks((elapsed / interval.Ticks + 1) * interval.Ticks);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    /// <summary>The end of a turn closes its open ride (no further fare) and voids its unfinished challenge.</summary>
    private static void CloseTurn(TagState state, TagTurn turn, DateTimeOffset at)
    {
        foreach (var ride in state.Rides.Where(r => r.Turn == turn.Index && r.IsOpen).ToList())
        {
            CloseRide(state, ride, at, ride.DistanceMeters ?? 0, ride.Upfront);
        }

        foreach (var card in state.Cards.Where(c => c.Turn == turn.Index && c.Status == DrawnCardStatus.Active))
        {
            card.Status = DrawnCardStatus.Void;
            card.ResolvedAt = at;
        }
    }

    private static void CloseRide(TagState state, TagRide ride, DateTimeOffset at, double distanceMeters, int totalFare)
    {
        ride.EndedAt = at;
        ride.DistanceMeters = distanceMeters;
        ride.TotalFare = totalFare;
        var settlement = ride.Upfront - totalFare;
        if (settlement != 0)
        {
            AddTransaction(state, ride.PlayerId, at, settlement, CoinTransactionKind.Ride, ride.Turn, TagTexts.Transport(ride.Mode), ride.Id);
        }
    }

    private static void AddTransaction(
        TagState state, Guid playerId, DateTimeOffset at, int amount, CoinTransactionKind kind, int turn, string? note = null, Guid? refId = null) =>
        state.Transactions.Add(new CoinTransaction
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            At = at,
            Amount = amount,
            Kind = kind,
            Turn = turn,
            Note = note,
            RefId = refId,
        });

    /// <summary>Own GPS for entries about the local player (never sent; other devices log them without a place).</summary>
    private GeoPoint? OwnLocation(Guid playerId) => playerId == LocalPlayerId ? _ownPosition : null;

    private void RaiseTurnStarted(TagTurn turn)
    {
        var localRole = turn.RunnerId == LocalPlayerId ? TagRoles.Runner : TagRoles.Chaser;
        Raise(GameEvent.Create(
            Id,
            turn.StartedAt,
            TagEventTypes.TurnStarted,
            string.Format(TagStrings.EventTurnStartedFormat, turn.Index + 1, NameOf(turn.RunnerId)),
            location: turn.StartPoint,
            data: new Dictionary<string, string> { [GameEventData.LocalRole] = localRole }));
    }

    private void RaiseGameEnded()
    {
        if (State.End is not { } end)
        {
            return;
        }

        var results = TagScoring.Results(this, end.At);
        Raise(GameEvent.Create(
            Id,
            end.At,
            TagEventTypes.GameEnded,
            TagTexts.Headline(results),
            location: end.RunnerPoint,
            data: new Dictionary<string, string> { [GameEventData.LocalRole] = string.Empty }));
    }
}

public static class TagEventTypes
{
    public const string GameStarted = "tag.game.started";
    public const string TurnStarted = "tag.turn.started";
    public const string Caught = "tag.caught";
    public const string CatchRequested = "tag.catch.requested";
    public const string RideStarted = "tag.ride.started";
    public const string RideEnded = "tag.ride.ended";
    public const string CardDrawn = "tag.card.drawn";
    public const string ChallengeCompleted = "tag.challenge.completed";
    public const string ChallengeAbandoned = "tag.challenge.abandoned";
    public const string Arrived = "tag.arrived";
    public const string TimeUp = "tag.timeup";
    public const string GameEnded = "tag.game.ended";
    public const string CoinsAdjusted = "tag.coins.adjusted";
}

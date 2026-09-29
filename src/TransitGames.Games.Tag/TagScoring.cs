using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;

namespace TransitGames.Games.Tag;

/// <summary>A player's destination and its distance from the runner at the end (null: no position or no destination).</summary>
public sealed record DestinationRank(TagPlayer Player, double? DistanceMeters, bool IsWinner);

/// <summary>One turn as shown in the results.</summary>
/// <param name="Spent">Coins the runner paid for rides in this turn.</param>
/// <param name="Earned">Coins the runner got from cards in this turn.</param>
/// <param name="Bonus">Turn bonus the runner got at its start.</param>
/// <param name="IsRunning">The turn has not ended (the game was stopped, or is still on).</param>
public sealed record TurnSummary(
    int Index,
    TagPlayer Runner,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    TurnEndReason? Reason,
    TagPlayer? Catcher,
    bool Forced,
    int Spent,
    int Earned,
    int Bonus,
    bool IsRunning);

/// <summary>Results of a game (or its standing while it runs).</summary>
public sealed record TagResults(
    IReadOnlyList<TagPlayer> Winners,
    GameEndReason? Reason,
    EndPointSource? PointSource,
    IReadOnlyList<DestinationRank> Ranking,
    IReadOnlyList<TurnSummary> Turns)
{
    public bool IsOver => Reason is not null;

    public bool IsDraw => Winners.Count > 1;
}

/// <summary>Who wins and the history of the turns.</summary>
public static class TagScoring
{
    /// <summary>
    /// Destinations ranked by straight-line (geodesic) distance from <paramref name="runnerPoint"/>. Distances are
    /// compared in whole meters, so destinations equally far (a draw) all win. Players without a destination rank last.
    /// </summary>
    public static IReadOnlyList<DestinationRank> Rank(IEnumerable<TagPlayer> players, GeoPoint runnerPoint)
    {
        var ranked = players
            .Select(p => (Player: p, Distance: p.Destination is { } d ? Math.Round(d.Location.DistanceTo(runnerPoint)) : (double?)null))
            .OrderBy(x => x.Distance is null)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Player.Name, StringComparer.CurrentCulture)
            .ToList();
        var best = ranked.Select(x => x.Distance).FirstOrDefault(d => d is not null);
        return [.. ranked.Select(x => new DestinationRank(x.Player, x.Distance, best is not null && x.Distance == best))];
    }

    public static TagResults Results(TagSession session, DateTimeOffset now)
    {
        if (session.FinishedAt is { } finished && finished < now)
        {
            now = finished;
        }

        var end = session.End;
        IReadOnlyList<DestinationRank> ranking = end?.RunnerPoint is { } point
            ? Rank(session.Players, point)
            : [.. session.Players.Select(p => new DestinationRank(p, null, false))];

        IReadOnlyList<TagPlayer> winners = end switch
        {
            { Reason: GameEndReason.ReachedDestination } => [.. session.Players.Where(p => p.Id == end.RunnerId)],
            { Reason: GameEndReason.TimeUp } => [.. ranking.Where(r => r.IsWinner).Select(r => r.Player)],
            _ => [],
        };
        if (end?.Reason == GameEndReason.ReachedDestination)
        {
            ranking = [.. ranking.Select(r => r with { IsWinner = r.Player.Id == end.RunnerId })];
        }

        var turns = session.Turns.Select(turn => Summarize(session, turn, now)).ToList();
        return new TagResults(winners, end?.Reason, end?.Source, ranking, turns);
    }

    private static TurnSummary Summarize(TagSession session, TagTurn turn, DateTimeOffset now)
    {
        var runner = session.FindPlayer(turn.RunnerId) ?? new TagPlayer { Id = turn.RunnerId, Name = session.NameOf(turn.RunnerId) };
        var own = session.Transactions.Where(t => t.PlayerId == turn.RunnerId && t.Turn == turn.Index).ToList();
        var end = turn.EndedAt ?? (session.GameEndsAt is { } limit && limit < now ? limit : now);
        return new TurnSummary(
            turn.Index,
            runner,
            turn.StartedAt,
            end > turn.StartedAt ? end - turn.StartedAt : TimeSpan.Zero,
            turn.EndReason,
            turn.CatcherId is { } catcher ? session.FindPlayer(catcher) : null,
            turn.Forced,
            -own.Where(t => t.Kind == CoinTransactionKind.Ride).Sum(t => t.Amount),
            own.Where(t => t.Kind is CoinTransactionKind.Challenge or CoinTransactionKind.Curse).Sum(t => t.Amount),
            own.Where(t => t.Kind == CoinTransactionKind.TurnBonus).Sum(t => t.Amount),
            turn.EndedAt is null && session.Status != SessionStatus.Finished);
    }
}

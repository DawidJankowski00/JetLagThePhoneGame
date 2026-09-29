using TransitGames.Core.Events;
using TransitGames.Core.Geography;
using TransitGames.Core.Sessions;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Sync;
using TransitGames.Games.Tag.Targets;

namespace TransitGames.Games.Tag;

/// <summary>The two teams, named by the role they start with; roles swap every round.</summary>
public enum TagTeam
{
    /// <summary>Runs in rounds 1, 3...</summary>
    First,

    /// <summary>Runs in rounds 2, 4...</summary>
    Second,
}

public enum RoundEndReason
{
    Caught,

    /// <summary>The round time ran out: the runners survived.</summary>
    TimeUp,

    /// <summary>The runners reached the final target.</summary>
    ReachedFinal,
}

public enum TagPhase
{
    /// <summary>Start of a round: runners get away, chasers wait.</summary>
    HeadStart,
    Chase,

    /// <summary>The round time ran out; the next round starts when a player confirms.</summary>
    TimeUp,

    /// <summary>All rounds played.</summary>
    GameOver,
}

public sealed class TargetCapture
{
    public string TargetId { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public int Reward { get; set; }
}

public sealed class CoinSpend
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public int Amount { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class TagRound
{
    public int Index { get; set; }
    public TagTeam Runners { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public RoundEndReason? EndReason { get; set; }

    /// <summary>Where the round ended; only known when the runners reported it (the chasers' position is never sent).</summary>
    public GeoPoint? EndPoint { get; set; }

    public List<TargetCapture> Captures { get; set; } = [];
    public List<CoinSpend> Spends { get; set; } = [];
}

/// <summary>A runners' position entered by hand by the chasers (offline game: received via a messenger).</summary>
public sealed class ManualPing
{
    public DateTimeOffset At { get; set; }
    public GeoPoint Point { get; set; }
}

public sealed class TagState
{
    public List<TagRound> Rounds { get; set; } = [];

    /// <summary>Chasers' device, offline game.</summary>
    public List<ManualPing> Pings { get; set; } = [];

    /// <summary>Runners' device, offline game: when they last sent their position by hand.</summary>
    public DateTimeOffset? LastOwnPingAt { get; set; }
}

public sealed record RoundResult(
    int Index,
    TagTeam Runners,
    TimeSpan Survived,
    RoundEndReason? Reason,
    bool IsOver,
    int Captures,
    int TargetPoints,
    int FinalBonus,
    int Score)
{
    /// <summary>The game was finished while this round was still running.</summary>
    public bool WasStopped { get; init; }
}

public sealed record TeamResult(TagTeam Team, TimeSpan Survived, int Captures, int Points, int Score);

public sealed record TagScoreboard(IReadOnlyList<RoundResult> Rounds, IReadOnlyList<TeamResult> Teams)
{
    /// <summary>The team with the higher score; null on a draw.</summary>
    public TagTeam? Leader =>
        Teams.Count == 2 && Teams[0].Score != Teams[1].Score ? Teams.MaxBy(t => t.Score)!.Team : null;
}

/// <summary>
/// A game of Tag: rounds in which one team runs (captures targets, avoids the chasers) and the other chases; after
/// every round the roles swap (a <see cref="Core.Sync.RoleChange"/> sent with the round end). Score of a round =
/// minutes survived + target points (in points mode) + final bonus. Changes from other devices arrive as sync
/// messages and are applied idempotently (see <see cref="TagMessageHandler"/>).
/// </summary>
public sealed class TagSession : GameSessionBase<TagSettings, TagState>
{
    public TagSession(Guid id, TagSettings settings, string localRoleId)
        : base(id, TagModule.GameId, settings, localRoleId)
    {
    }

    public TagSession(GameSessionSnapshot snapshot, TagSettings settings)
        : base(snapshot, settings)
    {
    }

    public bool IsRunner => LocalRoleId == TagRoles.Runner;

    /// <summary>
    /// The local team. Roles swap once per round, so the team follows from the role and the number of swaps
    /// (also right for a device that joined after some rounds).
    /// </summary>
    public TagTeam LocalTeam => IsRunner == (RolesVersion % 2 == 0) ? TagTeam.First : TagTeam.Second;

    public IReadOnlyList<TagRound> Rounds => State.Rounds;

    public TagRound CurrentRound => State.Rounds.Count > 0
        ? State.Rounds[^1]
        : new TagRound { Runners = TagTeam.First, StartedAt = CreatedAt };

    public bool IsLastRound => CurrentRound.Index + 1 >= Settings.Rounds;

    public DateTimeOffset HeadStartEndsAt => CurrentRound.StartedAt + Settings.HeadStart;

    public DateTimeOffset RoundEndsAt => CurrentRound.StartedAt + Settings.RoundDuration;

    public IReadOnlyList<ManualPing> Pings => State.Pings;

    public DateTimeOffset? LastOwnPingAt => State.LastOwnPingAt;

    public static TagTeam RunnersOf(int roundIndex) => roundIndex % 2 == 0 ? TagTeam.First : TagTeam.Second;

    public TagPhase Phase(DateTimeOffset now)
    {
        var round = CurrentRound;
        if (round.EndedAt is not null)
        {
            return TagPhase.GameOver; // a round that ended with rounds left is followed by the next one at once
        }

        return now < HeadStartEndsAt ? TagPhase.HeadStart
            : now >= RoundEndsAt ? TagPhase.TimeUp
            : TagPhase.Chase;
    }

    public TagTarget? FindTarget(string targetId) =>
        Settings.Targets.FirstOrDefault(t => t.Id == targetId)
        ?? (Settings.FinalTarget?.Id == targetId ? Settings.FinalTarget : null);

    public bool IsCaptured(string targetId) => CurrentRound.Captures.Any(c => c.TargetId == targetId);

    /// <summary>Targets not captured yet in this round.</summary>
    public IEnumerable<TagTarget> ActiveTargets => Settings.Targets.Where(t => !IsCaptured(t.Id));

    /// <summary>Active targets within <see cref="TagSettings.TargetRadiusMeters"/> of <paramref name="point"/>, nearest first.</summary>
    public IReadOnlyList<TagTarget> TargetsInReach(GeoPoint point) => ActiveTargets
        .Select(t => (Target: t, Distance: t.Location.DistanceTo(point)))
        .Where(x => x.Distance <= Settings.TargetRadiusMeters)
        .OrderBy(x => x.Distance)
        .Select(x => x.Target)
        .ToList();

    public bool IsAtFinal(GeoPoint point) =>
        Settings.FinalTarget is { } final && final.Location.DistanceTo(point) <= Settings.TargetRadiusMeters;

    /// <summary>Whether the local team may capture <paramref name="targetId"/> now.</summary>
    public bool CanCapture(string targetId, DateTimeOffset now) =>
        IsRunner && Phase(now) is TagPhase.HeadStart or TagPhase.Chase
        && Settings.Targets.Any(t => t.Id == targetId) && !IsCaptured(targetId);

    public bool CanEndByCatch(DateTimeOffset now) => Phase(now) == TagPhase.Chase;

    /// <summary>
    /// The end of the current round as the local device reports it. When rounds are left, the next one starts at
    /// once (<see cref="RoundEndPayload.NextStartsAt"/>) and the message swaps the roles.
    /// </summary>
    public RoundEndPayload CreateRoundEnd(RoundEndReason reason, DateTimeOffset now, GeoPoint? point)
    {
        var endedAt = reason == RoundEndReason.TimeUp ? RoundEndsAt : now;
        return new RoundEndPayload(CurrentRound.Index, reason, endedAt, point, IsLastRound ? null : now);
    }

    public void ApplyCapture(CapturePayload capture)
    {
        var round = CurrentRound;
        if (capture.Round != round.Index || round.EndedAt is not null || IsCaptured(capture.TargetId)
            || FindTarget(capture.TargetId) is not { } target || target == Settings.FinalTarget)
        {
            return;
        }

        Mutate(s => s.Rounds[^1].Captures.Add(new TargetCapture { TargetId = target.Id, At = capture.At, Reward = target.Reward }));
        Raise(GameEvent.Create(
            Id,
            capture.At,
            TagEventTypes.TargetCaptured,
            string.Format(TagStrings.EventTargetCaptured, target.Name),
            target.Challenge?.LocalizedTitle,
            target.Location,
            TagRoles.Runner));
    }

    public void ApplyRoundEnd(RoundEndPayload end)
    {
        var round = CurrentRound;
        if (end.Round != round.Index || round.EndedAt is not null)
        {
            return; // already ended (e.g. both teams pressed "caught")
        }

        Mutate(s =>
        {
            var current = s.Rounds[^1];
            current.EndedAt = end.EndedAt;
            current.EndReason = end.Reason;
            current.EndPoint = end.Point;
            if (end.NextStartsAt is { } next && current.Index + 1 < Settings.Rounds)
            {
                s.Rounds.Add(new TagRound { Index = current.Index + 1, Runners = RunnersOf(current.Index + 1), StartedAt = next });
            }
        });

        var survived = TagTexts.Duration(end.EndedAt - round.StartedAt);
        var (type, title) = end.Reason switch
        {
            RoundEndReason.Caught => (TagEventTypes.Caught, string.Format(TagStrings.EventCaught, survived)),
            RoundEndReason.ReachedFinal => (TagEventTypes.ReachedFinal, string.Format(TagStrings.EventReachedFinal, survived)),
            _ => (TagEventTypes.TimeUp, string.Format(TagStrings.EventTimeUp, survived)),
        };
        var location = end.Reason == RoundEndReason.ReachedFinal ? Settings.FinalTarget?.Location : end.Point;
        Raise(GameEvent.Create(Id, end.EndedAt, type, title, location: location));

        if (CurrentRound.Index != round.Index)
        {
            Raise(GameEvent.Create(
                Id,
                CurrentRound.StartedAt,
                TagEventTypes.RoundStarted,
                string.Format(TagStrings.EventRoundStarted, CurrentRound.Index + 1, TagTexts.TeamName(CurrentRound.Runners))));
        }
    }

    public void ApplySpend(SpendPayload spend)
    {
        var round = CurrentRound;
        if (spend.Round != round.Index || round.Spends.Any(s => s.Id == spend.Id) || spend.Amount <= 0)
        {
            return;
        }

        Mutate(s => s.Rounds[^1].Spends.Add(new CoinSpend { Id = spend.Id, At = spend.At, Amount = spend.Amount, Note = spend.Note }));
    }

    /// <summary>Runners' coins in the current round (<see cref="RewardKind.Coins"/>).</summary>
    public int CoinBalance =>
        Settings.StartingCoins + CurrentRound.Captures.Sum(c => c.Reward) - CurrentRound.Spends.Sum(s => s.Amount);

    /// <summary>Chasers, offline: a runners' position received via a messenger.</summary>
    public void AddPing(GeoPoint point, DateTimeOffset at)
    {
        Mutate(s => s.Pings.Add(new ManualPing { At = at, Point = point }));
        Raise(GameEvent.Create(Id, at, TagEventTypes.Ping, TagStrings.EventPingEntered, location: point, actorRoleId: TagRoles.Runner));
    }

    /// <summary>Runners, offline: they sent their position by hand.</summary>
    public void MarkOwnPing(DateTimeOffset at) => Mutate(s => s.LastOwnPingAt = at);

    /// <summary>The newest manual ping of the current round.</summary>
    public ManualPing? LastPing => State.Pings.LastOrDefault(p => p.At >= CurrentRound.StartedAt);

    /// <summary>When the runners' position is due next: every reveal interval from the round start.</summary>
    public DateTimeOffset? NextRevealAt(DateTimeOffset now)
    {
        var interval = Settings.RevealInterval;
        if (interval <= TimeSpan.Zero || Phase(now) == TagPhase.GameOver)
        {
            return null;
        }

        var start = CurrentRound.StartedAt;
        var elapsed = Math.Max(0, (now - start).Ticks);
        return start + TimeSpan.FromTicks((elapsed / interval.Ticks + 1) * interval.Ticks);
    }

    /// <summary>Scores until <paramref name="now"/>; a finished game counts until it was finished.</summary>
    public TagScoreboard Scoreboard(DateTimeOffset now)
    {
        if (FinishedAt is { } finished && finished < now)
        {
            now = finished;
        }

        var rounds = State.Rounds.Select(r => Result(r, now)).ToList();
        var teams = new[] { TagTeam.First, TagTeam.Second }
            .Select(team =>
            {
                var own = rounds.Where(r => r.Runners == team).ToList();
                return new TeamResult(
                    team,
                    TimeSpan.FromTicks(own.Sum(r => r.Survived.Ticks)),
                    own.Sum(r => r.Captures),
                    own.Sum(r => r.TargetPoints + r.FinalBonus),
                    own.Sum(r => r.Score));
            })
            .ToList();
        return new TagScoreboard(rounds, teams);
    }

    private RoundResult Result(TagRound round, DateTimeOffset now)
    {
        var roundEnd = round.StartedAt + Settings.RoundDuration;
        var end = round.EndedAt ?? (now < roundEnd ? now : roundEnd);
        var survived = end > round.StartedAt ? end - round.StartedAt : TimeSpan.Zero;
        var targetPoints = Settings.RewardKind == RewardKind.Points ? round.Captures.Sum(c => c.Reward) : 0;
        var finalBonus = round.EndReason == RoundEndReason.ReachedFinal ? Settings.FinalBonus : 0;
        return new RoundResult(
            round.Index,
            round.Runners,
            survived,
            round.EndReason,
            round.EndedAt is not null || now >= roundEnd || FinishedAt is not null,
            round.Captures.Count,
            targetPoints,
            finalBonus,
            (int)survived.TotalMinutes + targetPoints + finalBonus)
        {
            WasStopped = round.EndedAt is null && FinishedAt is { } finished && finished < roundEnd,
        };
    }

    protected override void OnStarted(DateTimeOffset now) =>
        Mutate(s => s.Rounds.Add(new TagRound { Index = 0, Runners = TagTeam.First, StartedAt = CreatedAt }));
}

public static class TagEventTypes
{
    public const string TargetCaptured = "tag.target.captured";
    public const string Caught = "tag.caught";
    public const string TimeUp = "tag.timeup";
    public const string ReachedFinal = "tag.final";
    public const string RoundStarted = "tag.round.started";
    public const string Ping = "tag.ping";
}

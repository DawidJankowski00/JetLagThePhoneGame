using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Notifications;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag;

public sealed class TagModule : GameModule<TagSettings, TagSession>
{
    public const string GameId = "tag";

    /// <summary>Keeps the pending reminders well under the iOS limit of 64 even with a short ping interval.</summary>
    private const int MaxPlannedPings = 24;

    public override string Id => GameId;
    public override string DisplayName => TagStrings.GameName;
    public override string Description => TagStrings.GameDescription;
    public override string Icon => "🏃";
    public override int MinPlayers => 2;

    public override IReadOnlyList<GameRole> Roles { get; } =
    [
        new(TagRoles.Runner, TagStrings.RoleRunner, TagStrings.RoleRunnerDescription),
        new(TagRoles.Chaser, TagStrings.RoleChaser, TagStrings.RoleChaserDescription),
    ];

    /// <summary>Chasers may see the runners (pings); the chasers' location is never shared.</summary>
    public override LocationSharingPolicy LocationSharing { get; } =
        new([new LocationSharingRule(TagRoles.Chaser, TagRoles.Runner)]);

    /// <summary>Runners are revealed to the chasers every <see cref="TagSettings.RevealInterval"/>, not live.</summary>
    protected override TimeSpan GetLocationShareInterval(TagSettings settings) => settings.RevealInterval;

    protected override TagSession CreateSession(TagSettings settings, string localRoleId, Guid sessionId) =>
        new(sessionId, settings, localRoleId);

    protected override TagSession RestoreSession(GameSessionSnapshot snapshot, TagSettings settings) =>
        new(snapshot, settings);

    public override void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<IGameMessageHandler, TagMessageHandler>();

    /// <summary>
    /// Reminders of the current round: end of the head start, end of the round time and — offline, where pings are
    /// sent by hand — every ping.
    /// </summary>
    protected override IReadOnlyList<GameNotification> PlanNotifications(TagSession session, bool isOnline, DateTimeOffset now)
    {
        var round = session.CurrentRound;
        if (session.Status != SessionStatus.Running || round.EndedAt is not null || session.Phase(now) is TagPhase.GameOver or TagPhase.TimeUp)
        {
            return [];
        }

        var plan = new List<GameNotification>();
        var prefix = $"r{round.Index}.";
        if (session.HeadStartEndsAt > now)
        {
            plan.Add(new(
                prefix + "headstart",
                TagStrings.NotifyHeadStartTitle,
                session.IsRunner ? TagStrings.NotifyHeadStartRunner : TagStrings.NotifyHeadStartChaser,
                session.HeadStartEndsAt));
        }

        var interval = session.Settings.RevealInterval;
        if (!isOnline && interval > TimeSpan.Zero && session.NextRevealAt(now) is { } first)
        {
            var body = session.IsRunner ? TagStrings.NotifyPingRunner : TagStrings.NotifyPingChaser;
            for (var due = first; due < session.RoundEndsAt && plan.Count <= MaxPlannedPings; due += interval)
            {
                var index = (due - round.StartedAt).Ticks / interval.Ticks;
                plan.Add(new($"{prefix}ping{index}", TagStrings.NotifyPingTitle, body, due));
            }
        }

        plan.Add(new(prefix + "timeup", TagStrings.NotifyTimeUpTitle, TagStrings.NotifyTimeUpBody, session.RoundEndsAt));
        return plan;
    }

    /// <summary>Captures (also automatic ones on the runners' phone) and round changes; manual pings are local.</summary>
    public override GameNotification? NotificationFor(GameEvent gameEvent, string localRoleId) => gameEvent.Type switch
    {
        TagEventTypes.TargetCaptured => new("target." + gameEvent.Id, gameEvent.Title, gameEvent.Details ?? TagStrings.GameName),
        TagEventTypes.Caught or TagEventTypes.TimeUp or TagEventTypes.ReachedFinal =>
            new("round.end", TagStrings.NotifyRoundEndTitle, gameEvent.Title),
        TagEventTypes.RoundStarted => new("round.start", TagStrings.NotifyRoundStartTitle, gameEvent.Title),
        _ => null,
    };

    /// <summary>Chasers: the runners' online ping arrived.</summary>
    public override GameNotification? NotificationForLocation(string senderRoleId, string localRoleId) =>
        senderRoleId == TagRoles.Runner && localRoleId == TagRoles.Chaser
            ? new("ping", TagStrings.NotifyPingReceivedTitle, TagStrings.NotifyPingReceivedBody)
            : null;

    protected override GameResult DescribeResult(TagSession session, DateTimeOffset now)
    {
        var board = session.Scoreboard(now);
        var first = board.Teams.First(t => t.Team == TagTeam.First).Score;
        var second = board.Teams.First(t => t.Team == TagTeam.Second).Score;
        var local = session.LocalTeam;
        var over = session.Status == SessionStatus.Finished || session.Phase(now) == TagPhase.GameOver;
        var headline = !over ? string.Format(TagStrings.ResultStandingFormat, first, second)
            : board.Leader is { } leader ? string.Format(TagStrings.ResultWinnerFormat, TagTexts.TeamName(leader, local), first, second)
            : string.Format(TagStrings.ResultDrawFormat, first, second);

        var teams = board.Teams.Select(t => new GameResultLine(
            TagTexts.TeamName(t.Team, local),
            string.Format(TagStrings.ScoreFormat, t.Score) + " · "
                + string.Format(TagStrings.TeamDetailsFormat, TagTexts.Duration(t.Survived), t.Captures, t.Points)));
        var rounds = board.Rounds.Select(r => new GameResultLine(
            string.Format(TagStrings.RoundRowTitleFormat, r.Index + 1, TagTexts.TeamName(r.Runners, local)),
            string.Format(
                TagStrings.RoundRowDetailsFormat,
                TagTexts.Duration(r.Survived),
                TagTexts.RoundEnd(r),
                r.Captures,
                r.TargetPoints + r.FinalBonus)
                + " · " + string.Format(TagStrings.ScoreFormat, r.Score)));
        return new GameResult(headline, [.. teams, .. rounds]);
    }
}

public static class TagRoles
{
    public const string Runner = "runner";
    public const string Chaser = "chaser";
}

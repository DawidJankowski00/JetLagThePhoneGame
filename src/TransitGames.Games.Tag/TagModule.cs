using Microsoft.Extensions.DependencyInjection;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Notifications;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Games.Tag.Legacy;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag;

public sealed class TagModule : GameModule<TagSettings, TagSession>
{
    public const string GameId = "tag";

    public override string Id => GameId;
    public override string DisplayName => TagStrings.GameName;
    public override string Description => TagStrings.GameDescription;
    public override string Icon => "🏃";
    public override int MinPlayers => 2;

    /// <summary>Every player has their own coins, destination and turns: all devices share one state online.</summary>
    public override bool RequiresOnline => true;

    /// <summary>Everybody joins as a chaser; the start draws the runner.</summary>
    public override string? InitialRoleId => TagRoles.Chaser;

    public override IReadOnlyList<GameRole> Roles { get; } =
    [
        new(TagRoles.Runner, TagStrings.RoleRunner, TagStrings.RoleRunnerDescription),
        new(TagRoles.Chaser, TagStrings.RoleChaser, TagStrings.RoleChaserDescription),
    ];

    /// <summary>Chasers may see the runner (pings); the chasers' location is never shared.</summary>
    public override LocationSharingPolicy LocationSharing { get; } =
        new([new LocationSharingRule(TagRoles.Chaser, TagRoles.Runner)]);

    /// <summary>The runner is revealed to the chasers every <see cref="TagSettings.RevealInterval"/>, not live.</summary>
    protected override TimeSpan GetLocationShareInterval(TagSettings settings) => settings.RevealInterval;

    protected override TagSession CreateSession(TagSettings settings, string localRoleId, Guid sessionId) =>
        new(sessionId, settings, localRoleId);

    protected override TagSession RestoreSession(GameSessionSnapshot snapshot, TagSettings settings) =>
        new(snapshot, settings);

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IGameMessageHandler, TagMessageHandler>();
        services.AddSingleton<IGameSessionMigration, LegacyTagMigration>();
    }

    /// <summary>End of the head start and the time limit of the game.</summary>
    protected override IReadOnlyList<GameNotification> PlanNotifications(TagSession session, bool isOnline, DateTimeOffset now)
    {
        if (session.Status != SessionStatus.Running || session.CurrentTurn is not { } turn
            || session.Phase(now) is TagPhase.GameOver or TagPhase.TimeUp or TagPhase.Setup)
        {
            return [];
        }

        var plan = new List<GameNotification>();
        if (session.HeadStartEndsAt is { } headStartEnd && headStartEnd > now)
        {
            plan.Add(new(
                $"t{turn.Index}.headstart",
                TagStrings.NotifyHeadStartTitle,
                session.IsLocalRunner ? TagStrings.NotifyHeadStartRunner : TagStrings.NotifyHeadStartChaser,
                headStartEnd));
        }

        if (session.GameEndsAt is { } end && end > now)
        {
            plan.Add(new("timeup", TagStrings.NotifyTimeUpTitle, TagStrings.NotifyTimeUpBody, end));
        }

        return plan;
    }

    /// <summary>Catches, new turns, the end of the game, and — for the runner — catch requests.</summary>
    public override GameNotification? NotificationFor(GameEvent gameEvent, string localRoleId) => gameEvent.Type switch
    {
        TagEventTypes.Caught => new("caught", TagStrings.NotifyCaughtTitle, gameEvent.Title),
        TagEventTypes.TurnStarted => new("turn", TagStrings.NotifyTurnTitle, gameEvent.Title),
        TagEventTypes.GameEnded => new("end", TagStrings.NotifyGameEndedTitle, gameEvent.Title),
        TagEventTypes.CatchRequested when localRoleId == TagRoles.Runner => new("catch.request." + gameEvent.Id, TagStrings.NotifyCatchRequestTitle, gameEvent.Title),
        TagEventTypes.CardDrawn when localRoleId == TagRoles.Chaser => new("card." + gameEvent.Id, TagStrings.NotifyCardTitle, gameEvent.Title),
        _ => null,
    };

    /// <summary>Chasers: the runner's ping arrived.</summary>
    public override GameNotification? NotificationForLocation(string senderRoleId, string localRoleId) =>
        senderRoleId == TagRoles.Runner && localRoleId == TagRoles.Chaser
            ? new("ping", TagStrings.NotifyPingReceivedTitle, TagStrings.NotifyPingReceivedBody)
            : null;

    protected override GameResult DescribeResult(TagSession session, DateTimeOffset now)
    {
        if (session.LegacyResult is { } legacy)
        {
            return legacy;
        }

        if (!session.IsStarted)
        {
            return new GameResult(TagStrings.ResultNotStarted, []);
        }

        var results = session.Results(now);
        var ranking = results.Ranking.Select((r, i) => new GameResultLine(
            string.Format(TagStrings.RankRowFormat, i + 1, r.Player.Name) + (r.IsWinner ? " 🏆" : string.Empty),
            r.DistanceMeters is { } distance
                ? string.Format(TagStrings.RankDistanceFormat, r.Player.Destination?.Name ?? string.Empty, TagTexts.Distance(distance))
                : r.Player.Destination?.Name ?? string.Empty));
        var turns = results.Turns.Select(t => new GameResultLine(
            string.Format(TagStrings.TurnRowTitleFormat, t.Index + 1, t.Runner.Name),
            string.Format(TagStrings.TurnRowDetailsFormat, TagTexts.Duration(t.Duration), TagTexts.TurnEnd(t), t.Spent, t.Earned + t.Bonus)));
        var balances = session.Players.Select(p => new GameResultLine(p.Name, TagTexts.Coins(session.Balance(p.Id))));
        return new GameResult(TagTexts.Headline(results), [.. ranking, .. turns, .. balances]);
    }
}

public static class TagRoles
{
    public const string Runner = "runner";
    public const string Chaser = "chaser";
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Core.Notifications;
using TransitGames.Core.Sessions;
using TransitGames.Core.Sync;
using TransitGames.Geo.Places;
using TransitGames.Games.HideAndSeek.Sync;
using TransitGames.Games.HideAndSeek.Resources;

namespace TransitGames.Games.HideAndSeek;

public sealed class HideAndSeekModule : GameModule<HideAndSeekSettings, HideAndSeekSession>
{
    public const string GameId = "hide-and-seek";

    /// <summary>How long before the end of the hiding time the hiders are reminded.</summary>
    private static readonly TimeSpan HidingWarning = TimeSpan.FromMinutes(5);

    public override string Id => GameId;
    public override string DisplayName => HideAndSeekStrings.GameName;
    public override string Description => HideAndSeekStrings.GameDescription;
    public override string Icon => "🔍";
    public override int MinPlayers => 2;

    public override IReadOnlyList<GameRole> Roles { get; } =
    [
        new(HideAndSeekRoles.Hider, HideAndSeekStrings.RoleHider, HideAndSeekStrings.RoleHiderDescription),
        new(HideAndSeekRoles.Seeker, HideAndSeekStrings.RoleSeeker, HideAndSeekStrings.RoleSeekerDescription),
    ];

    /// <summary>Hiders may see the seekers; the hiders' location is never shared.</summary>
    public override LocationSharingPolicy LocationSharing { get; } =
        new([new LocationSharingRule(HideAndSeekRoles.Hider, HideAndSeekRoles.Seeker)]);

    protected override HideAndSeekSession CreateSession(HideAndSeekSettings settings, string localRoleId, Guid sessionId) =>
        new(sessionId, settings, localRoleId);

    protected override HideAndSeekSession RestoreSession(GameSessionSnapshot snapshot, HideAndSeekSettings settings) =>
        new(snapshot, settings);

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IGameMessageHandler, HideAndSeekMessageHandler>();
    }

    /// <summary>Reminders before and at the end of the hiding time.</summary>
    protected override IReadOnlyList<GameNotification> PlanNotifications(HideAndSeekSession session, bool isOnline, DateTimeOffset now)
    {
        if (session.Status != SessionStatus.Running || session.Phase(now) != HideAndSeekPhase.Hiding)
        {
            return [];
        }

        var plan = new List<GameNotification>();
        var warning = session.HidingEndsAt - HidingWarning;
        if (session.IsHider && session.Settings.HidingTime > 2 * HidingWarning && warning > now)
        {
            plan.Add(new("hiding.soon", HideAndSeekStrings.NotifyHidingSoonTitle, HideAndSeekStrings.NotifyHidingSoonBody, warning));
        }

        plan.Add(new(
            "hiding.end",
            HideAndSeekStrings.NotifyHidingOverTitle,
            session.IsHider ? HideAndSeekStrings.NotifyHidingOverHider : HideAndSeekStrings.NotifyHidingOverSeeker,
            session.HidingEndsAt));
        return plan;
    }

    /// <summary>News from the other team: questions for the hiders; answers, cards and the end for the seekers.</summary>
    public override GameNotification? NotificationFor(GameEvent gameEvent, string localRoleId)
    {
        var hider = localRoleId == HideAndSeekRoles.Hider;
        return gameEvent.Type switch
        {
            HideAndSeekEvents.QuestionAsked when hider => new(
                "q." + gameEvent.Id,
                string.Format(HideAndSeekStrings.NotifyQuestionFormat, gameEvent.Title),
                gameEvent.Details ?? string.Empty),
            HideAndSeekEvents.QuestionAnswered when !hider => new(
                "a." + gameEvent.Id,
                string.Format(HideAndSeekStrings.NotifyAnswerFormat, gameEvent.Title),
                gameEvent.Details ?? string.Empty),
            GameEventTypes.CardPlayed when !hider => new(
                "card." + gameEvent.Id,
                string.Format(HideAndSeekStrings.NotifyCardFormat, gameEvent.Title),
                gameEvent.Details ?? string.Empty),
            GameEventTypes.Caught when hider => new("found", HideAndSeekStrings.NotifyFoundTitle, gameEvent.Details ?? string.Empty),
            _ => null,
        };
    }

    protected override GameResult DescribeResult(HideAndSeekSession session, DateTimeOffset now)
    {
        var hidden = HideAndSeekSession.FormatDuration(session.HiddenFor(now));
        var headline = session.FoundAt is not null ? string.Format(HideAndSeekStrings.ResultFormat, hidden)
            : session.Status == SessionStatus.Finished ? string.Format(HideAndSeekStrings.ResultNotFoundFormat, hidden)
            : session.Phase(now) == HideAndSeekPhase.Hiding ? HideAndSeekStrings.ResultHiding
            : string.Format(HideAndSeekStrings.ResultSeekingFormat, hidden);

        var lines = new List<GameResultLine>
        {
            new(HideAndSeekStrings.ResultHidingTime, string.Format(HideAndSeekStrings.MinutesFormat, (int)session.Settings.HidingTime.TotalMinutes)),
            new(HideAndSeekStrings.ResultQuestions, string.Format(
                HideAndSeekStrings.ResultQuestionsFormat,
                session.Questions.Count,
                session.Questions.Count(q => q.IsAnswered))),
            new(HideAndSeekStrings.ResultCards, session.CardsPlayed.Count.ToString(CultureInfo.CurrentCulture)),
        };
        if (session.BonusMinutes > 0)
        {
            lines.Add(new(HideAndSeekStrings.ResultBonus, string.Format(HideAndSeekStrings.MinutesFormat, session.BonusMinutes)));
        }

        if (session.HiderStationId is { } stationId && session.Settings.Stations.FirstOrDefault(s => s.Id == stationId) is { } station)
        {
            lines.Add(new(HideAndSeekStrings.ResultStation, station.Name));
        }

        return new GameResult(headline, lines);
    }
}

public static class HideAndSeekRoles
{
    public const string Hider = "hider";
    public const string Seeker = "seeker";
}

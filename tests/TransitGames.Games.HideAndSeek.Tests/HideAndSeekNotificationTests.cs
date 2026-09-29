using TransitGames.Core.Events;
using TransitGames.Core.Games;
using TransitGames.Games.HideAndSeek.Questions;

namespace TransitGames.Games.HideAndSeek.Tests;

public class HideAndSeekNotificationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly IGameModule Module = new HideAndSeekModule();

    private static HideAndSeekSession NewSession(string role)
    {
        var session = (HideAndSeekSession)Module.CreateSession(new HideAndSeekSettings { HidingTime = TimeSpan.FromMinutes(30) }, role);
        session.Start(Start);
        return session;
    }

    [Fact]
    public void Hiders_are_reminded_before_and_at_the_end_of_the_hiding_time_seekers_at_the_end()
    {
        var hider = Module.PlanNotifications(NewSession(HideAndSeekRoles.Hider), false, Start.AddMinutes(1));
        var seeker = Module.PlanNotifications(NewSession(HideAndSeekRoles.Seeker), false, Start.AddMinutes(1));
        var late = Module.PlanNotifications(NewSession(HideAndSeekRoles.Hider), false, Start.AddMinutes(27));
        var seeking = Module.PlanNotifications(NewSession(HideAndSeekRoles.Hider), false, Start.AddMinutes(31));

        Assert.Equal(["hiding.soon", "hiding.end"], hider.Select(n => n.Key));
        Assert.Equal([Start.AddMinutes(25), Start.AddMinutes(30)], hider.Select(n => n.At!.Value));
        Assert.Equal(["hiding.end"], seeker.Select(n => n.Key));
        Assert.Equal(["hiding.end"], late.Select(n => n.Key));
        Assert.Empty(seeking);
    }

    [Fact]
    public void Questions_are_news_for_the_hiders_answers_for_the_seekers()
    {
        var hider = NewSession(HideAndSeekRoles.Hider);
        var events = new List<GameEvent>();
        hider.EventRaised += (_, e) => events.Add(e);
        var asked = EliminationTests.Asked(new RadarSpec(World.At(0, 0), 1000), null, 40);
        hider.ApplyQuestion(asked);
        hider.ApplyAnswer(asked.Id, QuestionAnswer.No, false, Start.AddMinutes(45));

        var question = events.Single(e => e.Type == HideAndSeekEvents.QuestionAsked);
        var answer = events.Single(e => e.Type == HideAndSeekEvents.QuestionAnswered);

        Assert.NotNull(Module.NotificationFor(question, HideAndSeekRoles.Hider));
        Assert.Null(Module.NotificationFor(question, HideAndSeekRoles.Seeker));
        Assert.NotNull(Module.NotificationFor(answer, HideAndSeekRoles.Seeker));
        Assert.Null(Module.NotificationFor(answer, HideAndSeekRoles.Hider));
    }

    [Fact]
    public void The_result_counts_until_found_or_until_the_game_was_finished()
    {
        var found = NewSession(HideAndSeekRoles.Seeker);
        found.ApplyFound(Start.AddMinutes(95), null);
        var notFound = NewSession(HideAndSeekRoles.Seeker);
        notFound.Finish(Start.AddMinutes(80));

        var foundResult = Module.DescribeResult(found, Start.AddHours(5));
        var notFoundResult = Module.DescribeResult(notFound, Start.AddHours(5));

        Assert.Contains("1:05:00", foundResult.Headline);
        Assert.Contains("0:50:00", notFoundResult.Headline);
        Assert.NotEqual(foundResult.Headline, notFoundResult.Headline.Replace("0:50:00", "1:05:00"));
    }
}

using TransitGames.Core.Notifications;

namespace TransitGames.Core.Tests;

public class NotificationScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Game = Guid.NewGuid();

    private static GameNotification Reminder(string key, int minutes, string title = "T") => new(key, title, "B", Now.AddMinutes(minutes));

    [Fact]
    public void A_new_plan_is_scheduled_and_the_same_plan_again_changes_nothing()
    {
        var schedule = new NotificationSchedule();

        var first = schedule.Replace(Game, [Reminder("end", 30), Reminder("soon", 25)], Now);
        var again = schedule.Replace(Game, [Reminder("end", 30), Reminder("soon", 25)], Now.AddMinutes(1));

        Assert.Equal(2, first.ToSchedule.Count);
        Assert.Empty(first.ToCancel);
        Assert.Empty(again.ToSchedule);
        Assert.Empty(again.ToCancel);
    }

    [Fact]
    public void Changed_reminders_are_scheduled_again_and_dropped_ones_cancelled()
    {
        var schedule = new NotificationSchedule();
        schedule.Replace(Game, [Reminder("r0.headstart", 10), Reminder("r0.timeup", 60)], Now);

        // The round ended during the head start: the next round has its own reminders; the time-up one moved.
        var changes = schedule.Replace(Game, [Reminder("r1.headstart", 15), Reminder("r0.timeup", 90)], Now.AddMinutes(5));

        Assert.Equal([NotificationSchedule.Id(Game, "r0.headstart")], changes.ToCancel);
        Assert.Equal(
            [NotificationSchedule.Id(Game, "r0.timeup"), NotificationSchedule.Id(Game, "r1.headstart")],
            changes.ToSchedule.Select(c => c.Key).Order());
        Assert.Equal(Now.AddMinutes(90), schedule.Entries[NotificationSchedule.Id(Game, "r0.timeup")].At);
    }

    [Fact]
    public void Reminders_already_due_are_neither_scheduled_nor_cancelled()
    {
        var schedule = new NotificationSchedule();
        schedule.Replace(Game, [Reminder("soon", 5)], Now);

        // Five minutes later the reminder has fired; the game no longer plans it — nothing to cancel.
        var changes = schedule.Replace(Game, [Reminder("late", -1), Reminder("end", 10)], Now.AddMinutes(5));

        Assert.Empty(changes.ToCancel);
        Assert.Equal([NotificationSchedule.Id(Game, "end")], changes.ToSchedule.Select(c => c.Key));
        Assert.DoesNotContain(NotificationSchedule.Id(Game, "soon"), schedule.Entries.Keys);
    }

    [Fact]
    public void Planning_one_game_leaves_the_reminders_of_another_alone_until_it_is_cleared()
    {
        var other = Guid.NewGuid();
        var schedule = new NotificationSchedule();
        schedule.Replace(other, [Reminder("end", 30)], Now);

        var changes = schedule.Replace(Game, [], Now);
        var cleared = schedule.Clear(other);

        Assert.Empty(changes.ToCancel);
        Assert.Equal([NotificationSchedule.Id(other, "end")], cleared);
        Assert.Empty(schedule.Entries);
    }

    [Fact]
    public void The_schedule_survives_serialization_and_damaged_data_gives_an_empty_one()
    {
        var schedule = new NotificationSchedule();
        schedule.Replace(Game, [Reminder("end", 30, "Koniec")], Now);

        var restored = NotificationSchedule.Parse(schedule.Serialize());

        Assert.Equal(schedule.Entries, restored.Entries);
        Assert.Empty(restored.Replace(Game, [Reminder("end", 30, "Koniec")], Now).ToSchedule);
        Assert.Empty(NotificationSchedule.Parse("{not json").Entries);
        Assert.Empty(NotificationSchedule.Parse(null).Entries);
        Assert.Equal([NotificationSchedule.Id(Game, "end")], restored.ClearAll());
    }
}

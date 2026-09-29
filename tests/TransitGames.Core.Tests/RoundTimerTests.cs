using System.Text.Json;
using TransitGames.Core.Games;
using TransitGames.Core.Timing;

namespace TransitGames.Core.Tests;

public class RoundTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Countdown_TracksRemainingTime()
    {
        var timer = RoundTimer.Countdown(TimeSpan.FromMinutes(30));
        timer.Start(T0);

        Assert.Equal(TimeSpan.FromMinutes(20), timer.Remaining(T0.AddMinutes(10)));
        Assert.False(timer.IsExpired(T0.AddMinutes(29)));
        Assert.True(timer.IsExpired(T0.AddMinutes(30)));
        Assert.Equal(TimeSpan.Zero, timer.Remaining(T0.AddHours(2)));
    }

    [Fact]
    public void Pause_StopsTheClock()
    {
        var timer = RoundTimer.Stopwatch();
        timer.Start(T0);
        timer.Pause(T0.AddMinutes(5));
        timer.Resume(T0.AddMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(10), timer.Elapsed(T0.AddMinutes(20)));
        Assert.Null(timer.Remaining(T0));
    }

    [Fact]
    public void SurvivesSerialization()
    {
        var timer = RoundTimer.Countdown(TimeSpan.FromMinutes(45));
        timer.Start(T0);

        var json = JsonSerializer.Serialize(timer, GameJson.Options);
        var restored = JsonSerializer.Deserialize<RoundTimer>(json, GameJson.Options)!;

        Assert.Equal(timer.Remaining(T0.AddMinutes(7)), restored.Remaining(T0.AddMinutes(7)));
        Assert.True(restored.IsRunning);
    }
}

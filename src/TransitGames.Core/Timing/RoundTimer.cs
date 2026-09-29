namespace TransitGames.Core.Timing;

/// <summary>
/// A pausable countdown/stopwatch expressed purely as timestamps so it survives the app being killed.
/// JSON-serializable; store it inside session state.
/// </summary>
public sealed class RoundTimer
{
    /// <summary>Countdown length; null means an open-ended stopwatch.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>When the current running stretch started (null while paused or not started).</summary>
    public DateTimeOffset? RunningSince { get; set; }

    /// <summary>Time accumulated in previous running stretches.</summary>
    public TimeSpan AccumulatedBeforePause { get; set; }

    public bool IsStarted { get; set; }

    public bool IsRunning => RunningSince is not null;

    public static RoundTimer Countdown(TimeSpan duration) => new() { Duration = duration };

    public static RoundTimer Stopwatch() => new();

    public void Start(DateTimeOffset now)
    {
        if (IsStarted)
        {
            return;
        }

        IsStarted = true;
        RunningSince = now;
    }

    public void Pause(DateTimeOffset now)
    {
        if (RunningSince is { } since)
        {
            AccumulatedBeforePause += now - since;
            RunningSince = null;
        }
    }

    public void Resume(DateTimeOffset now)
    {
        if (IsStarted && RunningSince is null)
        {
            RunningSince = now;
        }
    }

    public TimeSpan Elapsed(DateTimeOffset now)
    {
        var running = RunningSince is { } since && now > since ? now - since : TimeSpan.Zero;
        return AccumulatedBeforePause + running;
    }

    /// <summary>Remaining time of a countdown (never negative); null for a stopwatch.</summary>
    public TimeSpan? Remaining(DateTimeOffset now)
    {
        if (Duration is not { } duration)
        {
            return null;
        }

        var remaining = duration - Elapsed(now);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public bool IsExpired(DateTimeOffset now) => Duration is { } d && Elapsed(now) >= d;
}

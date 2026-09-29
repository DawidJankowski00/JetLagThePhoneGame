namespace TransitGames.Games.Tag.Players;

/// <summary>Who becomes the runner after a catch (game setting).</summary>
public enum NextRunnerMode
{
    /// <summary>A random chaser.</summary>
    Random,

    /// <summary>The chaser who made the catch (a random chaser when unknown).</summary>
    Catcher,
}

/// <summary>
/// Draws the runners. The device that sends the start or the catch draws and sends the result, so all devices agree.
/// </summary>
public static class RunnerSelection
{
    public static Guid PickFirst(IReadOnlyList<Guid> players, Random random)
    {
        if (players.Count < 2)
        {
            throw new ArgumentException("Tag needs at least two players.", nameof(players));
        }

        return players[random.Next(players.Count)];
    }

    /// <summary>The next runner: one of the chasers, never the runner who was just caught.</summary>
    public static Guid PickNext(IReadOnlyList<Guid> players, Guid caughtRunner, Guid? catcher, NextRunnerMode mode, Random random)
    {
        var chasers = players.Where(p => p != caughtRunner).ToList();
        if (chasers.Count == 0)
        {
            throw new ArgumentException("There is no chaser to become the runner.", nameof(players));
        }

        return mode == NextRunnerMode.Catcher && catcher is { } id && chasers.Contains(id)
            ? id
            : chasers[random.Next(chasers.Count)];
    }
}

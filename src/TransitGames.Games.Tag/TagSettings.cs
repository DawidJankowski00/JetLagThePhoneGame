using TransitGames.Core.Games;

namespace TransitGames.Games.Tag;

public sealed class TagSettings : GameSettings
{
    public TimeSpan RoundDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>How often the runners' position is revealed to the chasers.</summary>
    public TimeSpan RevealInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Distance at which a runner counts as caught / a target as reached.</summary>
    public double CatchRadiusMeters { get; set; } = 50;

    public int TargetCount { get; set; } = 5;
}

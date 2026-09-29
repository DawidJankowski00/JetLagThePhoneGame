using TransitGames.Core.Games;
using TransitGames.Games.Tag.Challenges;
using TransitGames.Games.Tag.Targets;

namespace TransitGames.Games.Tag;

/// <summary>Where the targets of the game come from (the list itself is <see cref="TagSettings.Targets"/>).</summary>
public enum TargetSource
{
    /// <summary>Random, spread-out stations of the game.</summary>
    Stations,

    /// <summary>Random, spread-out OpenStreetMap places of a category (parks, museums...).</summary>
    Places,

    /// <summary>Placed by hand on the map.</summary>
    Manual,
}

/// <summary>What target rewards are: points added to the score, or coins the runners spend on fares.</summary>
public enum RewardKind
{
    Points,
    Coins,
}

public sealed class TagSettings : GameSettings
{
    /// <summary>Maximum length of a round; runners not caught by then survived the whole round.</summary>
    public TimeSpan RoundDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>At the start of every round the chasers wait this long before chasing.</summary>
    public TimeSpan HeadStart { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the runners' position is revealed to the chasers.</summary>
    public TimeSpan RevealInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Distance at which a runner counts as caught. A rule for the players: the chasers' position is never sent to
    /// the runners, so no device can check it.
    /// </summary>
    public double CatchRadiusMeters { get; set; } = 50;

    /// <summary>Runners closer than this to a target may capture it (automatically, when <see cref="AutoCapture"/>).</summary>
    public double TargetRadiusMeters { get; set; } = 150;

    /// <summary>Number of rounds; teams swap roles after each (2 = each team runs once).</summary>
    public int Rounds { get; set; } = 2;

    /// <summary>Capture targets without a challenge (and reach the final target) as soon as the runners' GPS is in range.</summary>
    public bool AutoCapture { get; set; } = true;

    public RewardKind RewardKind { get; set; } = RewardKind.Points;

    /// <summary>Coins the runners have at the start of their round (<see cref="RewardKind.Coins"/>).</summary>
    public int StartingCoins { get; set; } = 10;

    /// <summary>Points for reaching the final target.</summary>
    public int FinalBonus { get; set; } = 30;

    public TargetSource TargetSource { get; set; } = TargetSource.Stations;

    /// <summary>Place category for <see cref="TargetSource.Places"/> (see TransitGames.Geo.Places.PlaceCategories).</summary>
    public string PlaceCategoryId { get; set; } = "park";

    /// <summary>How many targets to draw (stations, places).</summary>
    public int TargetCount { get; set; } = 6;

    /// <summary>Give targets a challenge from the challenge list (to be done on the spot to capture them).</summary>
    public bool ChallengesEnabled { get; set; } = true;

    /// <summary>Custom challenge list (JSON, same format as the built-in <c>Challenges/challenges.json</c>); null = built-in.</summary>
    public string? ChallengesJson { get; set; }

    /// <summary>Targets of the game, the same in every round. Each carries its challenge, so all devices agree.</summary>
    public List<TagTarget> Targets { get; set; } = [];

    /// <summary>Optional final target the runners try to reach; reaching it ends their round with a bonus.</summary>
    public TagTarget? FinalTarget { get; set; }

    public ChallengeDeck LoadChallenges() => ChallengesJson is null ? ChallengeDeck.BuiltIn : ChallengeDeck.Parse(ChallengesJson);
}

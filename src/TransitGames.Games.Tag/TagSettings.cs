using TransitGames.Core.Games;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Players;

namespace TransitGames.Games.Tag;

/// <summary>What the new runner gets at the start of every turn after the first (game setting).</summary>
public enum TurnBonusMode
{
    /// <summary>A fixed number of coins (<see cref="TagSettings.TurnBonus"/>).</summary>
    Fixed,

    /// <summary>Tops the balance up to <see cref="TagSettings.TopUpMinimum"/> (nothing when it is already there).</summary>
    TopUp,
}

/// <summary>
/// Rules of a game of Tag. Fixed when the host creates the game; everything decided later (players, destinations,
/// start point, runners) travels in sync messages.
/// </summary>
public sealed class TagSettings : GameSettings
{
    /// <summary>Settings of the first Tag version (teams, targets) have no <see cref="RulesVersion"/>; see Legacy.</summary>
    public const int CurrentRulesVersion = 2;

    public int RulesVersion { get; set; } = CurrentRulesVersion;

    /// <summary>A game of the first Tag version, migrated for the history: read-only, only its stored result is shown.</summary>
    public bool IsLegacy { get; set; }

    /// <summary>Game time limit from the start; then the destination nearest to the runner wins.</summary>
    public TimeSpan GameDuration { get; set; } = TimeSpan.FromHours(3);

    /// <summary>At the start of every turn the chasers wait this long.</summary>
    public TimeSpan HeadStart { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often the runner's position is revealed to the chasers.</summary>
    public TimeSpan RevealInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Distance at which the runner counts as caught — a rule for the players (chasers' positions are never sent).</summary>
    public double CatchRadiusMeters { get; set; } = 50;

    /// <summary>Coins every player has at the start (X).</summary>
    public int StartingCoins { get; set; } = 20;

    public TurnBonusMode TurnBonusMode { get; set; } = TurnBonusMode.Fixed;

    /// <summary>Coins for the new runner at the start of every later turn (<see cref="TurnBonusMode.Fixed"/>, &gt; 0).</summary>
    public int TurnBonus { get; set; } = 10;

    /// <summary>Balance the new runner is topped up to (<see cref="TurnBonusMode.TopUp"/>).</summary>
    public int TopUpMinimum { get; set; } = 15;

    public NextRunnerMode NextRunner { get; set; } = NextRunnerMode.Random;

    public DestinationMode DestinationMode { get; set; } = DestinationMode.SelfPick;

    /// <summary>Drawn destinations: minimum distance from the start.</summary>
    public double DestinationMinDistanceMeters { get; set; } = 2000;

    /// <summary>Drawn destinations: maximum distance from the start (0 = no limit).</summary>
    public double DestinationMaxDistanceMeters { get; set; } = 10000;

    /// <summary>Drawn destinations: minimum distance between two of them.</summary>
    public double DestinationMinSpacingMeters { get; set; } = 1000;

    /// <summary>Everybody sees all destinations; false = each player sees only their own until the game ends.</summary>
    public bool DestinationsVisible { get; set; } = true;

    /// <summary>The runner has reached their destination within this distance (then confirms it).</summary>
    public double DestinationRadiusMeters { get; set; } = 100;

    public FareModel FareModel { get; set; } = FareModel.PerKm;

    /// <summary>Coins per km by means of transport (<see cref="FareModel.PerKm"/>).</summary>
    public Dictionary<TransportMode, double> PerKmRates { get; set; } = new(FareCalculator.DefaultPerKmRates);

    /// <summary>Coins per ride by means of transport (<see cref="FareModel.FixedPerRide"/>).</summary>
    public Dictionary<TransportMode, int> FixedFares { get; set; } = new(FareCalculator.DefaultFixedFares);

    /// <summary>After giving up a challenge (or letting it expire) the runner may not draw for this long.</summary>
    public TimeSpan DrawCooldown { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Custom deck (JSON, same format as the built-in <c>Cards/cards.json</c>); null = built-in.</summary>
    public string? CardsJson { get; set; }

    public DestinationRules DestinationRules =>
        new(DestinationMinDistanceMeters, DestinationMaxDistanceMeters, DestinationMinSpacingMeters);

    public CardDeck LoadCards() => CardsJson is null ? CardDeck.BuiltIn : CardDeck.Parse(CardsJson);
}

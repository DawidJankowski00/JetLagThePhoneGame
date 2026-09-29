using TransitGames.Core.Games;
using TransitGames.Core.Geography;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;

namespace TransitGames.Games.Tag;

public enum TagPhase
{
    /// <summary>Before the host starts the game: players join and pick destinations.</summary>
    Setup,

    /// <summary>Start of a turn: the runner gets away, the chasers wait.</summary>
    HeadStart,
    Chase,

    /// <summary>The time limit passed; waiting for the runner's position to settle the game.</summary>
    TimeUp,
    GameOver,
}

public enum TurnEndReason
{
    Caught,
    ReachedDestination,
    TimeUp,
}

public enum GameEndReason
{
    /// <summary>The runner reached their destination and wins.</summary>
    ReachedDestination,

    /// <summary>The time limit passed: the destination nearest to the runner wins.</summary>
    TimeUp,
}

/// <summary>Where the runner's position at the end of the game comes from.</summary>
public enum EndPointSource
{
    /// <summary>The runner's GPS at the end.</summary>
    Gps,

    /// <summary>The last position the chasers received (the runner's phone did not answer).</summary>
    LastKnown,

    /// <summary>Marked on the map by the host.</summary>
    Manual,
}

public enum CoinTransactionKind
{
    Start,
    TurnBonus,
    Ride,
    Challenge,
    Curse,
    HostAdjustment,
}

public enum DrawnCardStatus
{
    Active,
    Completed,

    /// <summary>Given up by the runner (starts the draw cooldown).</summary>
    Abandoned,

    /// <summary>The turn ended before the challenge was done.</summary>
    Void,
}

public sealed record TagDestination(GeoPoint Location, string Name);

public sealed class TagPlayer
{
    /// <summary>The player's device id in the online game.</summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public TagDestination? Destination { get; set; }
}

public sealed class TagTurn
{
    public int Index { get; set; }
    public Guid RunnerId { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>The game's start point, then the place of each catch.</summary>
    public GeoPoint StartPoint { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
    public TurnEndReason? EndReason { get; set; }
    public Guid? CatcherId { get; set; }

    /// <summary>The host confirmed the catch because the runner's phone did not.</summary>
    public bool Forced { get; set; }
}

/// <summary>One change of a player's coins; the balance is the sum of them.</summary>
public sealed class CoinTransaction
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public DateTimeOffset At { get; set; }
    public int Amount { get; set; }
    public CoinTransactionKind Kind { get; set; }
    public int Turn { get; set; }
    public string? Note { get; set; }

    /// <summary>The ride or drawn card the transaction belongs to.</summary>
    public Guid? RefId { get; set; }
}

public sealed class TagRide
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public int Turn { get; set; }
    public TransportMode Mode { get; set; }
    public FareModel FareModel { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int Upfront { get; set; }
    public double Multiplier { get; set; } = 1;
    public double? DistanceMeters { get; set; }
    public int? TotalFare { get; set; }

    public bool IsOpen => EndedAt is null;
}

public sealed class DrawnCard
{
    public Guid Id { get; set; }
    public string CardId { get; set; } = string.Empty;
    public CardType Type { get; set; }
    public Guid PlayerId { get; set; }
    public int Turn { get; set; }
    public DateTimeOffset DrawnAt { get; set; }
    public DrawnCardStatus Status { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class CatchRequest
{
    public Guid Id { get; set; }
    public Guid ChaserId { get; set; }
    public int Turn { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class TagGameEnd
{
    public DateTimeOffset At { get; set; }
    public GameEndReason Reason { get; set; }
    public Guid RunnerId { get; set; }
    public GeoPoint? RunnerPoint { get; set; }
    public EndPointSource Source { get; set; }
}

/// <summary>State of a game of Tag, the same on every device (built from the same messages).</summary>
public sealed class TagState
{
    /// <summary>Destinations picked before the start (by the players themselves or the host), by player id.</summary>
    public Dictionary<Guid, TagDestination> PendingDestinations { get; set; } = [];

    public List<TagPlayer> Players { get; set; } = [];
    public DateTimeOffset? StartedAt { get; set; }
    public GeoPoint? StartPoint { get; set; }
    public List<TagTurn> Turns { get; set; } = [];
    public List<CoinTransaction> Transactions { get; set; } = [];
    public List<TagRide> Rides { get; set; } = [];
    public List<DrawnCard> Cards { get; set; } = [];
    public List<CatchRequest> CatchRequests { get; set; } = [];
    public TagGameEnd? End { get; set; }

    /// <summary>Result of a game of the first Tag version (see <see cref="TagSettings.IsLegacy"/>).</summary>
    public GameResult? LegacyResult { get; set; }
}

using System.Globalization;
using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Destinations;
using TransitGames.Games.Tag.Players;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag;

/// <summary>Localized texts built from game data.</summary>
public static class TagTexts
{
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}:{duration.Seconds:00}");
    }

    public static string Distance(double meters) =>
        meters >= 1000
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} km", meters / 1000)
            : string.Format(CultureInfo.CurrentCulture, "{0:0} m", meters);

    public static string Coins(int amount) => string.Format(TagStrings.CoinsFormat, amount);

    /// <summary>"+5" / "−3" coins.</summary>
    public static string SignedCoins(int amount) =>
        string.Format(TagStrings.CoinsFormat, amount > 0 ? "+" + amount.ToString(CultureInfo.CurrentCulture) : "−" + Math.Abs(amount).ToString(CultureInfo.CurrentCulture));

    public static string Transport(TransportMode mode) =>
        TagStrings.ResourceManager.GetString("Transport_" + mode, TagStrings.Culture) ?? mode.ToString();

    public static string FareModel(FareModel model) => model switch
    {
        Tag.Coins.FareModel.PerKm => TagStrings.FareModelPerKm,
        Tag.Coins.FareModel.FixedPerRide => TagStrings.FareModelFixed,
        _ => TagStrings.FareModelManual,
    };

    public static string DestinationMode(DestinationMode mode) => mode switch
    {
        Destinations.DestinationMode.SelfPick => TagStrings.DestinationModeSelf,
        Destinations.DestinationMode.RandomStations => TagStrings.DestinationModeRandom,
        _ => TagStrings.DestinationModeHost,
    };

    public static string NextRunner(NextRunnerMode mode) =>
        mode == NextRunnerMode.Catcher ? TagStrings.NextRunnerCatcher : TagStrings.NextRunnerRandom;

    public static string TurnBonus(TurnBonusMode mode) =>
        mode == TurnBonusMode.TopUp ? TagStrings.TurnBonusTopUp : TagStrings.TurnBonusFixed;

    public static string PointSource(EndPointSource source) => source switch
    {
        EndPointSource.Gps => TagStrings.PointSourceGps,
        EndPointSource.LastKnown => TagStrings.PointSourceLastKnown,
        _ => TagStrings.PointSourceManual,
    };

    public static string TransactionKind(CoinTransactionKind kind) => kind switch
    {
        CoinTransactionKind.Start => TagStrings.TransactionStart,
        CoinTransactionKind.TurnBonus => TagStrings.TransactionTurnBonus,
        CoinTransactionKind.Ride => TagStrings.TransactionRide,
        CoinTransactionKind.Challenge => TagStrings.TransactionChallenge,
        CoinTransactionKind.Curse => TagStrings.TransactionCurse,
        _ => TagStrings.TransactionHost,
    };

    public static string CardType(CardType type) => type == Cards.CardType.Curse ? TagStrings.CardTypeCurse : TagStrings.CardTypeChallenge;

    public static string CurseDuration(CurseDuration duration) => duration.Kind switch
    {
        CurseDurationKind.WholeGame => TagStrings.DurationWholeGame,
        CurseDurationKind.Minutes => string.Format(TagStrings.DurationMinutesFormat, duration.Minutes),
        _ => TagStrings.DurationUntilEndOfTurn,
    };

    /// <summary>What the app enforces, e.g. "Forbidden: metro"; null for honour rules.</summary>
    public static string? Restriction(CardRestriction? restriction) => restriction?.Kind switch
    {
        RestrictionKind.BanTransport => string.Format(TagStrings.RestrictionBanFormat, Modes(restriction.Modes)),
        RestrictionKind.OnlyTransport => string.Format(TagStrings.RestrictionOnlyFormat, Modes(restriction.Modes)),
        RestrictionKind.FareMultiplier => string.Format(TagStrings.RestrictionMultiplierFormat, restriction.Factor),
        RestrictionKind.NoDraw => TagStrings.RestrictionNoDraw,
        _ => null,
    };

    public static string TurnEnd(TurnSummary turn) =>
        turn.IsRunning ? TagStrings.TurnRunning
        : turn.Reason switch
        {
            TurnEndReason.Caught => turn.Catcher is { } catcher
                ? string.Format(TagStrings.TurnCaughtByFormat, catcher.Name)
                : TagStrings.TurnCaught,
            TurnEndReason.ReachedDestination => TagStrings.TurnReachedDestination,
            TurnEndReason.TimeUp => TagStrings.TurnTimeUp,
            _ => TagStrings.TurnStopped,
        };

    /// <summary>One line about the outcome: winner(s) and why, or that the game is still on.</summary>
    public static string Headline(TagResults results) => results switch
    {
        { Reason: GameEndReason.ReachedDestination, Winners: [var winner] } => string.Format(TagStrings.ResultArrivedFormat, winner.Name),
        { Reason: GameEndReason.TimeUp, Winners.Count: > 1 } => string.Format(TagStrings.ResultDrawFormat, string.Join(", ", results.Winners.Select(w => w.Name))),
        { Reason: GameEndReason.TimeUp, Winners: [var winner] } => string.Format(TagStrings.ResultNearestFormat, winner.Name),
        { Reason: not null } => TagStrings.ResultNoWinner,
        _ => string.Format(TagStrings.ResultRunningFormat, results.Turns.Count, results.Turns.LastOrDefault()?.Runner.Name ?? "?"),
    };

    private static string Modes(IEnumerable<TransportMode> modes) => string.Join(", ", modes.Select(Transport));
}

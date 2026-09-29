using TransitGames.Games.Tag.Cards;

namespace TransitGames.Games.Tag.Coins;

/// <summary>Means of transport the runner pays for. Walking is free and is not a ride.</summary>
public enum TransportMode
{
    Bus,
    Tram,
    Metro,
    Train,
    Ferry,
    Other,
}

/// <summary>How fares are charged (game setting).</summary>
public enum FareModel
{
    /// <summary>Rate per km of the ride's GPS track; the first km is charged up front, the rest when the ride ends.</summary>
    PerKm,

    /// <summary>A fixed fare per ride, by means of transport, charged when the ride starts.</summary>
    FixedPerRide,

    /// <summary>The runner types the fare when the ride starts.</summary>
    Manual,
}

/// <summary>Why a ride cannot start.</summary>
public enum RideDenial
{
    None,

    /// <summary>A curse forbids this means of transport.</summary>
    TransportBanned,

    /// <summary>The runner cannot pay the upfront fare.</summary>
    NotEnoughCoins,
}

/// <summary>What starting a ride costs now.</summary>
/// <param name="Upfront">Charged when the ride starts (per km: the advance for the first km).</param>
/// <param name="Multiplier">Product of active fare multipliers; kept with the ride for its settlement.</param>
public sealed record FareQuote(TransportMode Mode, int Upfront, double Multiplier, RideDenial Denial)
{
    public bool IsAllowed => Denial == RideDenial.None;
}

/// <summary>
/// Fare rules. Deterministic: every device computes the same numbers, but the runner's device computes them once
/// and sends the result, so a later change of rules never rewrites history.
/// </summary>
public static class FareCalculator
{
    /// <summary>Absorbs floating-point noise before rounding up (2.0000000001 coins is 2, not 3).</summary>
    private const double Epsilon = 1e-9;

    public static IReadOnlyDictionary<TransportMode, double> DefaultPerKmRates { get; } = new Dictionary<TransportMode, double>
    {
        [TransportMode.Bus] = 1,
        [TransportMode.Tram] = 1,
        [TransportMode.Metro] = 1.5,
        [TransportMode.Train] = 2,
        [TransportMode.Ferry] = 2,
        [TransportMode.Other] = 3,
    };

    public static IReadOnlyDictionary<TransportMode, int> DefaultFixedFares { get; } = new Dictionary<TransportMode, int>
    {
        [TransportMode.Bus] = 3,
        [TransportMode.Tram] = 3,
        [TransportMode.Metro] = 4,
        [TransportMode.Train] = 6,
        [TransportMode.Ferry] = 5,
        [TransportMode.Other] = 8,
    };

    public static double PerKmRate(TagSettings settings, TransportMode mode) =>
        settings.PerKmRates.TryGetValue(mode, out var rate) ? rate : DefaultPerKmRates[mode];

    public static int FixedFare(TagSettings settings, TransportMode mode) =>
        settings.FixedFares.TryGetValue(mode, out var fare) ? fare : DefaultFixedFares[mode];

    /// <summary>Whether active restrictions allow <paramref name="mode"/>.</summary>
    public static bool IsAllowed(TransportMode mode, IEnumerable<CardRestriction> restrictions) =>
        restrictions.All(r => r.Kind switch
        {
            RestrictionKind.BanTransport => !r.Modes.Contains(mode),
            RestrictionKind.OnlyTransport => r.Modes.Contains(mode),
            _ => true,
        });

    /// <summary>Product of the active fare multipliers (1 without any).</summary>
    public static double Multiplier(IEnumerable<CardRestriction> restrictions) =>
        restrictions.Where(r => r.Kind == RestrictionKind.FareMultiplier).Aggregate(1.0, (product, r) => product * r.Factor);

    /// <summary>What starting a ride with <paramref name="mode"/> costs and whether it is allowed.</summary>
    /// <param name="manualFare">The fare typed by the runner (<see cref="FareModel.Manual"/>).</param>
    public static FareQuote Quote(
        TagSettings settings,
        TransportMode mode,
        int balance,
        IReadOnlyCollection<CardRestriction> restrictions,
        int? manualFare = null)
    {
        var multiplier = Multiplier(restrictions);
        var baseFare = settings.FareModel switch
        {
            FareModel.PerKm => PerKmRate(settings, mode),
            FareModel.FixedPerRide => FixedFare(settings, mode),
            _ => Math.Max(0, manualFare ?? 0),
        };
        var upfront = RoundUp(baseFare * multiplier);
        var denial = !IsAllowed(mode, restrictions) ? RideDenial.TransportBanned
            : upfront > balance ? RideDenial.NotEnoughCoins
            : RideDenial.None;
        return new FareQuote(mode, upfront, multiplier, denial);
    }

    /// <summary>
    /// Total fare of a finished ride. Per km: rate × km × multiplier (at least 1 coin), settled against the advance
    /// — the difference may make the balance negative (paid back from later rewards) or refund part of the advance.
    /// Other models: the upfront fare is the whole fare.
    /// </summary>
    public static int TotalFare(TagSettings settings, TransportMode mode, FareModel model, int upfront, double multiplier, double distanceMeters)
    {
        if (model != FareModel.PerKm)
        {
            return upfront;
        }

        var km = Math.Max(0, distanceMeters) / 1000;
        return Math.Max(1, RoundUp(PerKmRate(settings, mode) * km * multiplier));
    }

    private static int RoundUp(double coins) => (int)Math.Ceiling(Math.Max(0, coins) - Epsilon);
}

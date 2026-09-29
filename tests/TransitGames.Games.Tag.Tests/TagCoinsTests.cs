using TransitGames.Games.Tag.Cards;
using TransitGames.Games.Tag.Coins;
using TransitGames.Games.Tag.Sync;

namespace TransitGames.Games.Tag.Tests;

public class TagCoinsTests
{
    private static readonly DateTimeOffset Start = TagTable.Start;

    private static TagTable Started(Action<TagSettings>? change = null)
    {
        var table = new TagTable(players: 2, change);
        table.StartGame(runner: table.Ids[0]);
        return table;
    }

    private static void Ride(TagTable table, TransportMode mode, double meters, DateTimeOffset at, int? manual = null)
    {
        var runner = table.Runner;
        table.Send(TagMessages.StartRide(runner, runner.CreateRideStart(mode, at, manual)));
        table.Send(TagMessages.EndRide(runner, runner.CreateRideEnd(runner.OpenRide(runner.LocalPlayerId)!, meters, at.AddMinutes(10))));
    }

    [Fact]
    public void Per_km_charges_the_first_km_up_front_and_settles_the_rest_from_the_track()
    {
        var table = Started();
        var runner = table.Runner;

        var start = runner.CreateRideStart(TransportMode.Metro, Start.AddMinutes(1)); // 1.5 coins/km
        table.Send(TagMessages.StartRide(runner, start));
        Assert.Equal(2, start.Upfront); // 1.5 rounded up
        Assert.Equal(18, runner.Balance(runner.LocalPlayerId));

        table.Send(TagMessages.EndRide(runner, runner.CreateRideEnd(runner.OpenRide(runner.LocalPlayerId)!, 4200, Start.AddMinutes(12))));

        var ride = Assert.Single(table.Host.Rides);
        Assert.Equal((7, 4200.0), (ride.TotalFare!.Value, ride.DistanceMeters!.Value)); // ceil(1.5 × 4.2 = 6.3)
        Assert.Equal(13, table.Host.Balance(table.Ids[0]));
        Assert.Equal([-2, -5], table.Host.TransactionsOf(table.Ids[0]).Where(t => t.Kind == CoinTransactionKind.Ride).Select(t => t.Amount));
    }

    [Fact]
    public void A_short_ride_refunds_part_of_the_advance_but_costs_at_least_one_coin()
    {
        var table = Started(s => s.PerKmRates[TransportMode.Train] = 3);

        Ride(table, TransportMode.Train, 200, Start.AddMinutes(1)); // 3 up front, 0.6 → 1

        Assert.Equal(19, table.Host.Balance(table.Ids[0]));
    }

    [Fact]
    public void Fixed_fares_are_charged_once_when_the_ride_starts()
    {
        var table = Started(s => s.FareModel = FareModel.FixedPerRide);

        Ride(table, TransportMode.Train, 25_000, Start.AddMinutes(1)); // 6 whatever the distance

        Assert.Equal(14, table.Host.Balance(table.Ids[0]));
        Assert.Single(table.Host.TransactionsOf(table.Ids[0]), t => t.Kind == CoinTransactionKind.Ride);
    }

    [Fact]
    public void Manual_fares_use_the_amount_typed_by_the_runner()
    {
        var table = Started(s => s.FareModel = FareModel.Manual);

        Ride(table, TransportMode.Other, 5000, Start.AddMinutes(1), manual: 7);

        Assert.Equal(13, table.Host.Balance(table.Ids[0]));
    }

    [Fact]
    public void Chasers_ride_for_free()
    {
        var table = Started();

        Assert.All(table.Chasers, c => Assert.Throws<InvalidOperationException>(() => c.CreateRideStart(TransportMode.Bus, Start.AddMinutes(1))));
        Assert.Equal(20, table.Host.Balance(table.Ids[1]));
    }

    [Fact]
    public void Without_enough_coins_a_paid_ride_cannot_start()
    {
        var table = Started(s => s.StartingCoins = 1);
        var runner = table.Runner;

        Assert.Equal(RideDenial.NotEnoughCoins, runner.QuoteRide(TransportMode.Metro, Start.AddMinutes(1)).Denial); // needs 2
        Assert.True(runner.QuoteRide(TransportMode.Bus, Start.AddMinutes(1)).IsAllowed); // needs 1
        Assert.Throws<InvalidOperationException>(() => runner.CreateRideStart(TransportMode.Metro, Start.AddMinutes(1)));
    }

    [Fact]
    public void A_long_ride_may_end_below_zero_and_later_rewards_pay_it_back()
    {
        var table = Started(s => s.StartingCoins = 2);

        Ride(table, TransportMode.Train, 10_000, Start.AddMinutes(1)); // 2 up front, 20 in total
        Assert.Equal(-18, table.Host.Balance(table.Ids[0]));
        Assert.Equal(RideDenial.NotEnoughCoins, table.Runner.QuoteRide(TransportMode.Bus, Start.AddMinutes(20)).Denial);

        table.Draw("cu-game", Start.AddMinutes(20)); // +6
        Assert.Equal(-12, table.Host.Balance(table.Ids[0]));
    }

    [Fact]
    public void A_fare_multiplier_curse_raises_the_advance_and_the_settlement()
    {
        var table = Started();
        table.Draw("cu-double", Start.AddMinutes(1)); // +5, fares ×2 for 45 min
        var runner = table.Runner;

        var quote = runner.QuoteRide(TransportMode.Bus, Start.AddMinutes(2));
        Ride(table, TransportMode.Bus, 3400, Start.AddMinutes(2)); // up front 2, total ceil(1 × 3.4 × 2) = 7

        Assert.Equal((2, 2.0), (quote.Upfront, quote.Multiplier));
        Assert.Equal(20 + 5 - 7, table.Host.Balance(table.Ids[0]));
        Assert.Equal(1.0, runner.QuoteRide(TransportMode.Bus, Start.AddMinutes(46)).Multiplier); // expired
    }

    [Fact]
    public void Multipliers_of_several_curses_multiply()
    {
        var restrictions = new[]
        {
            new CardRestriction { Kind = RestrictionKind.FareMultiplier, Factor = 2 },
            new CardRestriction { Kind = RestrictionKind.FareMultiplier, Factor = 1.5 },
        };

        var quote = FareCalculator.Quote(new TagSettings(), TransportMode.Tram, 100, restrictions);

        Assert.Equal(3.0, quote.Multiplier);
        Assert.Equal(3, quote.Upfront);
        Assert.Equal(11, FareCalculator.TotalFare(new TagSettings(), TransportMode.Tram, FareModel.PerKm, quote.Upfront, quote.Multiplier, 3500)); // ceil(10.5)
    }

    [Fact]
    public void Transport_curses_forbid_means_of_transport()
    {
        var table = Started();
        table.Draw("cu-turn", Start.AddMinutes(1)); // buses and trams only
        var runner = table.Runner;

        Assert.Equal(RideDenial.TransportBanned, runner.QuoteRide(TransportMode.Metro, Start.AddMinutes(2)).Denial);
        Assert.True(runner.QuoteRide(TransportMode.Tram, Start.AddMinutes(2)).IsAllowed);
    }

    [Fact]
    public void The_host_corrects_a_balance_with_a_reason()
    {
        var table = Started();

        var id = Guid.NewGuid();
        table.Send(TagMessages.AdjustCoins(table.Host, new AdjustPayload(id, table.Ids[1], -4, "Skrót przez park", Start.AddMinutes(5))));
        table.Send(TagMessages.AdjustCoins(table.Host, new AdjustPayload(id, table.Ids[1], -4, "Skrót przez park", Start.AddMinutes(5)))); // retried

        Assert.Equal(16, table.Devices[1].Balance(table.Ids[1]));
        var entry = Assert.Single(table.Devices[1].TransactionsOf(table.Ids[1]), t => t.Kind == CoinTransactionKind.HostAdjustment);
        Assert.Equal("Skrót przez park", entry.Note);
    }

    [Fact]
    public void Every_coin_operation_is_a_transaction()
    {
        var table = Started();
        Ride(table, TransportMode.Bus, 2500, Start.AddMinutes(1));
        table.Draw("ch-free", Start.AddMinutes(15));
        var challenge = table.Host.DrawnCards[^1];
        table.Send(TagMessages.ResolveCard(table.Runner, new CardResolvePayload(challenge.Id, true, Start.AddMinutes(20))));
        table.Draw("cu-honour", Start.AddMinutes(21));
        table.Catch(Start.AddMinutes(40), table.Ids[1]);

        Assert.Equal(
            [CoinTransactionKind.Start, CoinTransactionKind.Start, CoinTransactionKind.Ride, CoinTransactionKind.Ride,
             CoinTransactionKind.Challenge, CoinTransactionKind.Curse, CoinTransactionKind.TurnBonus],
            table.Host.Transactions.Select(t => t.Kind));
        Assert.Equal(table.Host.Players.Sum(p => table.Host.Balance(p.Id)), table.Host.Transactions.Sum(t => t.Amount));
    }
}

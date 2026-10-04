using FxSandbox.Core;
using FxSandbox.Simulation;

namespace FxSandbox.Api.Tests;

public class StateDtoTests
{
    /// <summary>Always returns the same sample: 1.0 moves every rate up by the max step, 0.0 down, 0.5 holds.</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    private static decimal DecimalPlaces(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    [Fact]
    public void From_rounds_money_to_two_decimal_places_but_the_domain_keeps_full_precision()
    {
        var engine = new SandboxEngine(
            new Portfolio(),
            new RateSimulator(new Dictionary<Pair, decimal> { [Pair.UsdEur] = 0.8885m }, new FixedRandom(1.0)));

        // Long 1,000 at 0.8885; one tick up to 0.88939 gives a long unrealised P&L (about 0.9995...).
        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.9m);
        var open = engine.Tick();
        // A marketable sell of 400 fills at the market rate straight away, closing part of the long and realising P&L into cash.
        engine.Place(Pair.UsdEur, Side.Sell, 400m, 0.8m);
        var snapshot = engine.Tick();

        var portfolio = snapshot.Portfolio;
        Assert.True(DecimalPlaces(open.Portfolio.UnrealisedPnl) > 2, "domain unrealised P&L keeps full precision");
        Assert.True(DecimalPlaces(portfolio.Cash) > 2, "domain cash keeps full precision after realising P&L");
        Assert.True(DecimalPlaces(portfolio.Equity) > 2);

        foreach (var state in new[] { StateDto.From(open), StateDto.From(snapshot) })
        {
            Assert.True(DecimalPlaces(state.Cash) <= 2);
            Assert.True(DecimalPlaces(state.Equity) <= 2);
            Assert.True(DecimalPlaces(state.UnrealisedPnl) <= 2);
            Assert.All(state.Positions, p => Assert.True(DecimalPlaces(p.UnrealisedPnl) <= 2));
        }

        var dto = StateDto.From(snapshot);
        Assert.Equal(Math.Round(portfolio.Cash, 2, MidpointRounding.AwayFromZero), dto.Cash);
        Assert.Equal(Math.Round(portfolio.Equity, 2, MidpointRounding.AwayFromZero), dto.Equity);
        Assert.Equal(
            Math.Round(Assert.Single(portfolio.Positions).UnrealisedPnl, 2, MidpointRounding.AwayFromZero),
            Assert.Single(dto.Positions).UnrealisedPnl);
    }
}

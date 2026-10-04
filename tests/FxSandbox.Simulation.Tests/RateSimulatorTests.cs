using FxSandbox.Core;

namespace FxSandbox.Simulation.Tests;

public class RateSimulatorTests
{
    private static readonly Dictionary<Pair, decimal> Seeds = new()
    {
        [Pair.UsdEur] = 0.8885m,
        [Pair.UsdGbp] = 0.7552m,
        [Pair.UsdChf] = 0.8288m,
    };

    private static decimal ValueOf(IReadOnlyList<Rate> rates, Pair pair) => rates.Single(r => r.Pair == pair).Value;

    [Fact]
    public void Current_returns_seed_rates_before_any_step()
    {
        var sim = new RateSimulator(Seeds, new Random(1));

        foreach (var (pair, value) in Seeds)
            Assert.Equal(value, ValueOf(sim.Current, pair));
    }

    [Fact]
    public void Each_step_moves_every_pair_by_at_most_the_max_step()
    {
        var sim = new RateSimulator(Seeds, new Random(42));
        var previous = Seeds.ToDictionary(kv => kv.Key, kv => kv.Value);

        for (var i = 0; i < 10_000; i++)
        {
            var next = sim.Step();
            foreach (var pair in Seeds.Keys)
            {
                var ratio = ValueOf(next, pair) / previous[pair];
                Assert.InRange(ratio, 1 - RateSimulator.MaxStep, 1 + RateSimulator.MaxStep);
                previous[pair] = ValueOf(next, pair);
            }
        }
    }

    [Fact]
    public void Same_seed_gives_the_same_path()
    {
        var a = new RateSimulator(Seeds, new Random(7));
        var b = new RateSimulator(Seeds, new Random(7));

        for (var i = 0; i < 100; i++)
        {
            var ra = a.Step();
            var rb = b.Step();
            foreach (var pair in Seeds.Keys)
                Assert.Equal(ValueOf(ra, pair), ValueOf(rb, pair));
        }
    }

    [Fact]
    public void Different_seeds_give_different_paths()
    {
        var a = new RateSimulator(Seeds, new Random(1));
        var b = new RateSimulator(Seeds, new Random(2));

        for (var i = 0; i < 10; i++)
        {
            a.Step();
            b.Step();
        }

        Assert.NotEqual(ValueOf(a.Current, Pair.UsdEur), ValueOf(b.Current, Pair.UsdEur));
    }

    [Fact]
    public void Pairs_move_independently()
    {
        // Same starting rate for every pair, so any divergence comes from independent draws.
        var seeds = Enum.GetValues<Pair>().ToDictionary(p => p, _ => 1m);
        var sim = new RateSimulator(seeds, new Random(3));

        var rates = sim.Step();

        Assert.Equal(3, rates.Select(r => r.Value).Distinct().Count());
    }

    [Fact]
    public void Rates_stay_positive_and_pairs_not_listed_are_not_simulated()
    {
        var sim = new RateSimulator(
            new Dictionary<Pair, decimal> { [Pair.UsdEur] = 0.9m },
            new Random(5));

        for (var i = 0; i < 1_000; i++)
            sim.Step();

        var rate = Assert.Single(sim.Current);
        Assert.Equal(Pair.UsdEur, rate.Pair);
        Assert.True(rate.Value > 0);
    }

    [Fact]
    public void Constructor_rejects_empty_or_non_positive_seeds()
    {
        Assert.Throws<ArgumentException>(() => new RateSimulator(new Dictionary<Pair, decimal>(), new Random(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RateSimulator(new Dictionary<Pair, decimal> { [Pair.UsdEur] = 0m }, new Random(1)));
    }
}

using FxSandbox.Core;

namespace FxSandbox.Simulation;

/// <summary>
/// Per-pair random walk: <c>newRate = oldRate × (1 + Δ)</c> with Δ uniform in [-MaxStep, +MaxStep].
/// Not thread-safe; the owning engine serialises access.
/// </summary>
public sealed class RateSimulator
{
    public const decimal MaxStep = 0.001m;

    private readonly Random _random;
    private readonly Dictionary<Pair, decimal> _rates;

    public RateSimulator(IReadOnlyDictionary<Pair, decimal> seedRates, Random random)
    {
        ArgumentNullException.ThrowIfNull(seedRates);
        ArgumentNullException.ThrowIfNull(random);
        if (seedRates.Count == 0)
            throw new ArgumentException("At least one seed rate is required.", nameof(seedRates));
        foreach (var (pair, value) in seedRates)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(seedRates), $"Seed rate for {pair} must be positive.");
        }

        _random = random;
        _rates = new Dictionary<Pair, decimal>(seedRates);
    }

    /// <summary>The latest rate for each pair.</summary>
    public IReadOnlyList<Rate> Current => _rates.Select(kv => new Rate(kv.Key, kv.Value)).ToArray();

    /// <summary>Advances every pair by one independent step and returns the new rates.</summary>
    public IReadOnlyList<Rate> Step()
    {
        foreach (var pair in _rates.Keys.ToArray())
        {
            var delta = (decimal)(_random.NextDouble() * 2 - 1) * MaxStep;
            _rates[pair] *= 1 + delta;
        }

        return Current;
    }
}

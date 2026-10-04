using FxSandbox.Core;

namespace FxSandbox.Simulation;

/// <summary>Simulation settings, bound from the "Simulation" section of appsettings.json.</summary>
public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    public int TickIntervalMs { get; set; } = 1000;

    public Dictionary<Pair, decimal> SeedRates { get; set; } = new()
    {
        [Pair.UsdEur] = 0.8885m,
        [Pair.UsdGbp] = 0.7552m,
        [Pair.UsdChf] = 0.8288m,
    };
}

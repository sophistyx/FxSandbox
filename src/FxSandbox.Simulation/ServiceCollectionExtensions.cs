using FxSandbox.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FxSandbox.Simulation;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="SandboxEngine"/> singleton and the <see cref="TickService"/> that drives it, with
    /// <see cref="SimulationOptions"/> bound from the "Simulation" section of <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddSandboxSimulation(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SimulationOptions>(configuration.GetSection(SimulationOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var seeds = sp.GetRequiredService<IOptions<SimulationOptions>>().Value.SeedRates;
            return new SandboxEngine(() => new Portfolio(), () => new RateSimulator(seeds, Random.Shared));
        });
        services.AddHostedService<TickService>();
        return services;
    }
}

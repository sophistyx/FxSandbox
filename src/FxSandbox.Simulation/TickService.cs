using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FxSandbox.Simulation;

/// <summary>Drives <see cref="SandboxEngine.Tick"/> on a <see cref="PeriodicTimer"/>.</summary>
public sealed class TickService(SandboxEngine engine, IOptions<SimulationOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.TickIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) engine.Tick();
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
    }
}

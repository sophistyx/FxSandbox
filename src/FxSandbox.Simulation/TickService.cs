using FxSandbox.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FxSandbox.Simulation;

/// <summary>Drives <see cref="SandboxEngine.Tick"/> on a <see cref="PeriodicTimer"/>.</summary>
public sealed class TickService(
    SandboxEngine engine,
    IOptions<SimulationOptions> options,
    ILogger<TickService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation(
            "Simulation starting: tick interval {TickIntervalMs} ms, seed rates {SeedRates}",
            settings.TickIntervalMs,
            string.Join(", ", settings.SeedRates.Select(kv => $"{kv.Key}={kv.Value}")));

        engine.OrderFilled += OnOrderFilled;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.TickIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var snapshot = engine.Tick();
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("Ticked: {Rates}", string.Join(", ", snapshot.Rates.Select(r => $"{r.Pair}={r.Value}")));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Keep the simulation alive; one bad tick shouldn't stop the rates.
                    logger.LogError(ex, "Tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
        finally
        {
            engine.OrderFilled -= OnOrderFilled;
        }
    }

    private void OnOrderFilled(Order order) =>
        logger.LogInformation(
            "Order {OrderId} filled: {Side} {Quantity} {Pair} @ {LimitPrice}",
            order.Id, order.Side, order.Quantity, order.Pair, order.LimitPrice);
}

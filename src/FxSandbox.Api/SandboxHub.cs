using FxSandbox.Core;
using FxSandbox.Simulation;
using Microsoft.AspNetCore.SignalR;

namespace FxSandbox.Api;

/// <summary>
/// Push-only hub at <see cref="Route"/>. Clients receive <see cref="SnapshotMethod"/> (a <see cref="StateDto"/>)
/// after every tick and <see cref="OrderFilledMethod"/> (an <see cref="Order"/>) when an order fills.
/// </summary>
public sealed class SandboxHub : Hub
{
    public const string Route = "/hubs/sandbox";
    public const string SnapshotMethod = "snapshot";
    public const string OrderFilledMethod = "orderFilled";
}

/// <summary>Forwards <see cref="SandboxEngine"/> events to all connected hub clients.</summary>
public sealed class SandboxHubPublisher(
    SandboxEngine engine,
    IHubContext<SandboxHub> hub,
    ILogger<SandboxHubPublisher> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        engine.Ticked += OnTicked;
        engine.OrderFilled += OnOrderFilled;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        engine.Ticked -= OnTicked;
        engine.OrderFilled -= OnOrderFilled;
        return Task.CompletedTask;
    }

    // Handlers run on the tick/request thread, so don't block it on the network: send and log failures.
    private void OnTicked(SandboxSnapshot snapshot) =>
        Forward(hub.Clients.All.SendAsync(SandboxHub.SnapshotMethod, StateDto.From(snapshot)));

    private void OnOrderFilled(Order order) =>
        Forward(hub.Clients.All.SendAsync(SandboxHub.OrderFilledMethod, order));

    private void Forward(Task send) =>
        send.ContinueWith(
            t => logger.LogWarning(t.Exception, "Failed to push to SignalR clients"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}

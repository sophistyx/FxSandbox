using System.Text.Json.Serialization;
using FxSandbox.Simulation;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace FxSandbox.Api.Tests;

public sealed class SandboxHubTests(SandboxApiTests.Factory factory) : IClassFixture<SandboxApiTests.Factory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Connected_client_receives_snapshot_after_a_tick()
    {
        await using var connection = BuildConnection();
        var received = new TaskCompletionSource<StateMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<StateMessage>(SandboxHub.SnapshotMethod, s => received.TrySetResult(s));
        await connection.StartAsync();

        factory.Services.GetRequiredService<SandboxEngine>().Tick();

        var state = await received.Task.WaitAsync(Timeout);
        Assert.Equal(3, state.Rates.Count);
        Assert.All(state.Rates, r => Assert.NotEmpty(r.History));
    }

    [Fact]
    public async Task Connected_client_receives_order_filled_event()
    {
        await using var connection = BuildConnection();
        var received = new TaskCompletionSource<FilledOrder>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<FilledOrder>(SandboxHub.OrderFilledMethod, o => received.TrySetResult(o));
        await connection.StartAsync();

        // Far above any simulated rate, so a Buy is marketable and fills on placement.
        var engine = factory.Services.GetRequiredService<SandboxEngine>();
        var placed = engine.Place(Core.Pair.UsdEur, Core.Side.Buy, 100m, 5m);

        var order = await received.Task.WaitAsync(Timeout);
        Assert.Equal(placed.Order!.Id, order.Id);
        Assert.Equal("Filled", order.Status);
    }

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, SandboxHub.Route),
                o =>
                {
                    // TestServer has no real socket: use long polling over its in-memory handler.
                    o.Transports = HttpTransportType.LongPolling;
                    o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    private sealed record RateMessage(string Pair, decimal Rate, List<decimal> History);

    private sealed record StateMessage(decimal Cash, decimal Equity, List<RateMessage> Rates);

    private sealed record FilledOrder(int Id, string Status);
}

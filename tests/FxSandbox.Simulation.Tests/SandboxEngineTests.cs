using FxSandbox.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FxSandbox.Simulation.Tests;

public class SandboxEngineTests
{
    private static readonly Dictionary<Pair, decimal> Seeds = new()
    {
        [Pair.UsdEur] = 0.8885m,
        [Pair.UsdGbp] = 0.7552m,
        [Pair.UsdChf] = 0.8288m,
    };

    /// <summary>Always returns the same sample: 1.0 moves every rate up by the max step, 0.0 down, 0.5 holds.</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }

    private static SandboxEngine Engine(Random? random = null, int historyLength = SandboxEngine.DefaultHistoryLength) =>
        new(new Portfolio(), new RateSimulator(Seeds, random ?? new FixedRandom(1.0)), historyLength);

    private static decimal RateOf(SandboxSnapshot s, Pair pair) => s.Rates.Single(r => r.Pair == pair).Value;

    [Fact]
    public void Tick_fills_a_sell_order_once_the_rate_rises_to_its_limit()
    {
        var engine = Engine(new FixedRandom(1.0)); // +0.1% per tick: 0.8885 -> 0.88939 (5 dp)
        var placed = engine.Place(Pair.UsdEur, Side.Sell, 1_000m, 0.889m).Order!;

        var snapshot = engine.Tick();

        Assert.Equal(OrderStatus.Filled, snapshot.Portfolio.Orders.Single(o => o.Id == placed.Id).Status);
        var position = Assert.Single(snapshot.Portfolio.Positions).Position;
        Assert.Equal(-1_000m, position.Quantity);
        Assert.Equal(0.889m, position.EntryPrice);
    }

    [Fact]
    public void Tick_leaves_orders_the_rate_has_not_crossed_pending()
    {
        var engine = Engine(new FixedRandom(1.0));
        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.88m); // rate only rises, so this buy never fills

        var snapshot = engine.Tick();

        Assert.Equal(OrderStatus.Pending, Assert.Single(snapshot.Portfolio.Orders).Status);
        Assert.Empty(snapshot.Portfolio.Positions);
    }

    [Fact]
    public void Tick_only_fills_orders_on_the_pair_that_crossed()
    {
        var engine = Engine(new FixedRandom(1.0));
        engine.Place(Pair.UsdEur, Side.Sell, 1_000m, 0.889m);
        engine.Place(Pair.UsdGbp, Side.Sell, 1_000m, 0.9m);

        var orders = engine.Tick().Portfolio.Orders;

        Assert.Equal(OrderStatus.Filled, orders[0].Status);
        Assert.Equal(OrderStatus.Pending, orders[1].Status);
    }

    [Fact]
    public void Place_fills_a_marketable_order_immediately_at_its_limit()
    {
        var engine = Engine();

        var order = engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.9m).Order!; // market 0.8885 <= 0.9

        var snapshot = engine.GetSnapshot();
        Assert.Equal(OrderStatus.Filled, snapshot.Portfolio.Orders.Single(o => o.Id == order.Id).Status);
        Assert.Equal(0.9m, Assert.Single(snapshot.Portfolio.Positions).Position.EntryPrice);
    }

    [Fact]
    public void Place_rejection_returns_the_error_and_raises_no_events()
    {
        var engine = Engine();
        var raised = 0;
        engine.OrderPlaced += _ => raised++;
        engine.OrderFilled += _ => raised++;

        var result = engine.Place(Pair.UsdEur, Side.Buy, 20_000m, 0.8m);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Cancel_cancels_pending_orders_and_reports_filled_or_unknown_ones()
    {
        var engine = Engine();
        var pending = engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.5m).Order!;
        var filled = engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.9m).Order!;

        Assert.Equal(CancelResult.Cancelled, engine.Cancel(pending.Id));
        Assert.Equal(CancelResult.NotPending, engine.Cancel(filled.Id));
        Assert.Equal(CancelResult.NotFound, engine.Cancel(9999));
    }

    [Fact]
    public void Events_fire_in_order_for_place_fill_cancel_and_tick()
    {
        var engine = Engine();
        var log = new List<string>();
        engine.OrderPlaced += o => log.Add($"placed {o.Id} {o.Status}");
        engine.OrderFilled += o => log.Add($"filled {o.Id} {o.Status}");
        engine.OrderCancelled += o => log.Add($"cancelled {o.Id} {o.Status}");
        engine.Ticked += _ => log.Add("ticked");

        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.9m); // 1001: marketable
        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.5m); // 1002: pending
        engine.Cancel(1002);
        engine.Tick();

        Assert.Equal(
            ["placed 1001 Pending", "filled 1001 Filled", "placed 1002 Pending", "cancelled 1002 Cancelled", "ticked"],
            log);
    }

    [Fact]
    public void Events_are_raised_outside_the_lock_so_subscribers_can_call_back_in()
    {
        var engine = Engine();
        SandboxSnapshot? seen = null;
        var thread = new Thread(() => { });
        engine.OrderFilled += _ =>
        {
            // Another thread needs the engine's lock; it would block forever if we still held it.
            thread = new Thread(() => seen = engine.GetSnapshot());
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        };

        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 0.9m);

        Assert.NotNull(seen);
    }

    [Fact]
    public void History_keeps_only_the_most_recent_rates_per_pair()
    {
        var engine = Engine(historyLength: 3);

        for (var i = 0; i < 5; i++) engine.Tick();
        var snapshot = engine.GetSnapshot();

        foreach (var pair in Seeds.Keys)
        {
            Assert.Equal(3, snapshot.History[pair].Count);
            Assert.Equal(RateOf(snapshot, pair), snapshot.History[pair][^1]);
        }
    }

    [Fact]
    public void Initial_snapshot_has_seed_rates_and_starting_cash()
    {
        var snapshot = Engine().GetSnapshot();

        Assert.Equal(Portfolio.DefaultStartingCash, snapshot.Portfolio.Equity);
        Assert.Equal(0.8885m, RateOf(snapshot, Pair.UsdEur));
        Assert.All(snapshot.History.Values, h => Assert.Single(h));
    }

    [Fact]
    public async Task Parallel_place_cancel_and_tick_never_double_fill_and_keep_equity_consistent()
    {
        var engine = Engine(new Random(7));
        var fills = new System.Collections.Concurrent.ConcurrentBag<int>();
        engine.OrderFilled += o => fills.Add(o.Id);

        var tasks = new List<Task>
        {
            Task.Run(() => { for (var i = 0; i < 500; i++) engine.Tick(); }),
        };
        for (var t = 0; t < 4; t++)
        {
            var side = t % 2 == 0 ? Side.Buy : Side.Sell;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    // Limits sit right around the market so many orders fill while others get cancelled.
                    var limit = 0.8885m * (1 + (i % 7 - 3) * 0.001m);
                    var placed = engine.Place(Pair.UsdEur, side, 100m, limit).Order;
                    if (placed is not null && i % 3 == 0) engine.Cancel(placed.Id);
                }
            }));
        }

        await Task.WhenAll(tasks);
        var snapshot = engine.GetSnapshot();

        Assert.Equal(fills.Count, fills.Distinct().Count());
        var filled = snapshot.Portfolio.Orders.Where(o => o.Status == OrderStatus.Filled).Select(o => o.Id).Order();
        Assert.Equal(fills.Order(), filled);

        var net = snapshot.Portfolio.Orders
            .Where(o => o.Status == OrderStatus.Filled)
            .Sum(o => o.Side == Side.Buy ? o.Quantity : -o.Quantity);
        Assert.Equal(net, snapshot.Portfolio.Positions.Sum(p => p.Position.Quantity));
        Assert.Equal(snapshot.Portfolio.Cash + snapshot.Portfolio.UnrealisedPnl, snapshot.Portfolio.Equity);
    }

    [Fact]
    public void Reset_restores_seed_rates_capital_and_order_ids_and_publishes()
    {
        var engine = new SandboxEngine(() => new Portfolio(), () => new RateSimulator(Seeds, new FixedRandom(1.0)));
        engine.Place(Pair.UsdEur, Side.Buy, 1_000m, 5m);
        engine.Tick();
        SandboxSnapshot? published = null;
        engine.Ticked += s => published = s;

        var snapshot = engine.Reset();

        Assert.Same(snapshot, published);
        Assert.Empty(snapshot.Portfolio.Orders);
        Assert.Empty(snapshot.Portfolio.Positions);
        Assert.Equal(10_000m, snapshot.Portfolio.Equity);
        Assert.Equal(0.8885m, RateOf(snapshot, Pair.UsdEur));
        Assert.Single(snapshot.History[Pair.UsdEur]);
        Assert.Equal(1001, engine.Place(Pair.UsdEur, Side.Buy, 100m, 0.01m).Order!.Id);
    }

    [Fact]
    public void Reset_throws_when_built_without_factories()
    {
        Assert.Throws<InvalidOperationException>(() => Engine().Reset());
    }

    [Fact]
    public async Task TickService_ticks_the_engine_until_stopped()
    {
        var engine = Engine();
        var ticked = new TaskCompletionSource();
        engine.Ticked += _ => ticked.TrySetResult();
        var service = new TickService(engine, Options.Create(new SimulationOptions { TickIntervalMs = 10 }), NullLogger<TickService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await ticked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(engine.GetSnapshot().History[Pair.UsdEur].Count > 1);
    }
}

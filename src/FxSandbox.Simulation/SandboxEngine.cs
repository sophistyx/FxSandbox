using FxSandbox.Core;

namespace FxSandbox.Simulation;

/// <summary>Immutable view of the whole sandbox: portfolio, latest rates and a recent rate window per pair.</summary>
public sealed record SandboxSnapshot(
    Snapshot Portfolio,
    IReadOnlyList<Rate> Rates,
    IReadOnlyDictionary<Pair, IReadOnlyList<decimal>> History);

/// <summary>
/// The only mutator of the <see cref="Portfolio"/> and <see cref="RateSimulator"/>. Place, cancel and tick are
/// serialised under one lock, so an order can't be filled twice or cancelled mid-fill. Events are raised after
/// the lock is released so subscribers can call back into the engine without deadlocking.
/// </summary>
public sealed class SandboxEngine
{
    public const int DefaultHistoryLength = 60;

    private readonly object _gate = new();
    private readonly Portfolio _portfolio;
    private readonly RateSimulator _simulator;
    private readonly int _historyLength;
    private readonly Dictionary<Pair, Queue<decimal>> _history = [];

    public SandboxEngine(Portfolio portfolio, RateSimulator simulator, int historyLength = DefaultHistoryLength)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(simulator);
        ArgumentOutOfRangeException.ThrowIfLessThan(historyLength, 1);

        _portfolio = portfolio;
        _simulator = simulator;
        _historyLength = historyLength;

        var rates = _simulator.Current;
        Record(rates);
        _portfolio.Mark(rates);
    }

    /// <summary>Raised after every tick with the resulting state.</summary>
    public event Action<SandboxSnapshot>? Ticked;

    /// <summary>Raised when an order is accepted, always before any <see cref="OrderFilled"/> for it.</summary>
    public event Action<Order>? OrderPlaced;

    /// <summary>Raised with the order as filled.</summary>
    public event Action<Order>? OrderFilled;

    public event Action<Order>? OrderCancelled;

    public SandboxSnapshot GetSnapshot()
    {
        lock (_gate) return BuildSnapshot();
    }

    /// <summary>Places an order; if it is already marketable it fills immediately, at its limit price.</summary>
    public PlaceResult Place(Pair pair, Side side, decimal quantity, decimal limitPrice)
    {
        PlaceResult result;
        var events = new List<Action>();
        lock (_gate)
        {
            result = _portfolio.Place(pair, side, quantity, limitPrice);
            if (result.Order is { } order)
            {
                events.Add(() => OrderPlaced?.Invoke(order));
                var rate = _simulator.Current.Single(r => r.Pair == pair);
                if (FillRule.CanFill(order, rate)) Fill(order, events);
            }
        }

        Publish(events);
        return result;
    }

    public CancelResult Cancel(int orderId)
    {
        CancelResult result;
        var events = new List<Action>();
        lock (_gate)
        {
            result = _portfolio.Cancel(orderId);
            if (result == CancelResult.Cancelled)
            {
                var order = _portfolio.Snapshot().Orders.Single(o => o.Id == orderId);
                events.Add(() => OrderCancelled?.Invoke(order));
            }
        }

        Publish(events);
        return result;
    }

    /// <summary>Advances the rates, fills every pending order the new rates cross, and publishes the result.</summary>
    public SandboxSnapshot Tick()
    {
        SandboxSnapshot snapshot;
        var events = new List<Action>();
        lock (_gate)
        {
            var rates = _simulator.Step();
            Record(rates);
            _portfolio.Mark(rates);

            foreach (var order in _portfolio.Snapshot().Orders.Where(o => o.Status == OrderStatus.Pending))
            {
                var rate = rates.Single(r => r.Pair == order.Pair);
                if (FillRule.CanFill(order, rate)) Fill(order, events);
            }

            snapshot = BuildSnapshot();
            events.Add(() => Ticked?.Invoke(snapshot));
        }

        Publish(events);
        return snapshot;
    }

    private void Fill(Order order, List<Action> events)
    {
        var filled = _portfolio.ApplyFill(order.Id).Order;
        events.Add(() => OrderFilled?.Invoke(filled));
    }

    private void Record(IEnumerable<Rate> rates)
    {
        foreach (var rate in rates)
        {
            if (!_history.TryGetValue(rate.Pair, out var window)) _history[rate.Pair] = window = new Queue<decimal>();
            window.Enqueue(rate.Value);
            while (window.Count > _historyLength) window.Dequeue();
        }
    }

    private SandboxSnapshot BuildSnapshot() => new(
        _portfolio.Snapshot(),
        _simulator.Current,
        _history.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<decimal>)kv.Value.ToArray()));

    private static void Publish(List<Action> events)
    {
        foreach (var raise in events) raise();
    }
}

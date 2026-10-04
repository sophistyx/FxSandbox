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
    private readonly Func<Portfolio>? _portfolioFactory;
    private readonly Func<RateSimulator>? _simulatorFactory;
    private readonly int _historyLength;
    private readonly Dictionary<Pair, Queue<decimal>> _history = [];
    private Portfolio _portfolio;
    private RateSimulator _simulator;

    /// <summary>Creates an engine that can't be <see cref="Reset"/>.</summary>
    public SandboxEngine(Portfolio portfolio, RateSimulator simulator, int historyLength = DefaultHistoryLength)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(simulator);
        ArgumentOutOfRangeException.ThrowIfLessThan(historyLength, 1);

        _portfolio = portfolio;
        _simulator = simulator;
        _historyLength = historyLength;
        Start();
    }

    /// <summary>Creates an engine whose state can be rebuilt from the factories by <see cref="Reset"/>.</summary>
    public SandboxEngine(
        Func<Portfolio> portfolioFactory,
        Func<RateSimulator> simulatorFactory,
        int historyLength = DefaultHistoryLength)
        : this(
            (portfolioFactory ?? throw new ArgumentNullException(nameof(portfolioFactory)))(),
            (simulatorFactory ?? throw new ArgumentNullException(nameof(simulatorFactory)))(),
            historyLength)
    {
        _portfolioFactory = portfolioFactory;
        _simulatorFactory = simulatorFactory;
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

    /// <summary>Discards all orders, positions and rate history and starts again from the seed rates and capital.</summary>
    /// <exception cref="InvalidOperationException">The engine was built without factories.</exception>
    public SandboxSnapshot Reset()
    {
        if (_portfolioFactory is null || _simulatorFactory is null)
            throw new InvalidOperationException("This engine was created without factories and can't be reset.");

        SandboxSnapshot snapshot;
        lock (_gate)
        {
            _portfolio = _portfolioFactory();
            _simulator = _simulatorFactory();
            _history.Clear();
            Start();
            snapshot = BuildSnapshot();
        }

        Ticked?.Invoke(snapshot);
        return snapshot;
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

    private void Start()
    {
        var rates = _simulator.Current;
        Record(rates);
        _portfolio.Mark(rates);
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

namespace FxSandbox.Core;

/// <summary>
/// Orders, net positions and cash for a single trader. Not thread-safe: callers serialise access.
/// Orders fill at their limit price unless the caller supplies a better one; placing does not reserve cash.
/// </summary>
public sealed class Portfolio
{
    public const decimal DefaultStartingCash = 10_000m;
    private const int FirstOrderId = 1001;

    private readonly List<Order> _orders = [];
    private readonly Dictionary<Pair, Position> _positions = [];
    private Dictionary<Pair, decimal> _rates = [];
    private decimal _cash;
    private int _nextId = FirstOrderId;

    public Portfolio(decimal startingCash = DefaultStartingCash) => _cash = startingCash;

    public PlaceResult Place(Pair pair, Side side, decimal quantity, decimal limitPrice)
    {
        if (quantity <= 0) return new(null, "Quantity must be greater than zero.");
        if (limitPrice <= 0) return new(null, "Limit price must be greater than zero.");

        var exposure = quantity
            + _positions.Values.Sum(p => Math.Abs(p.Quantity))
            + _orders.Where(o => o.Status == OrderStatus.Pending).Sum(o => o.Quantity);
        var equity = Equity();
        if (exposure > equity)
            return new(null, $"Order would take total exposure to {exposure:N2} USD, above equity of {equity:N2} USD.");

        var order = new Order(_nextId++, pair, side, quantity, limitPrice);
        _orders.Add(order);
        return new(order, null);
    }

    public CancelResult Cancel(int orderId)
    {
        var index = _orders.FindIndex(o => o.Id == orderId);
        if (index < 0) return CancelResult.NotFound;
        if (_orders[index].Status != OrderStatus.Pending) return CancelResult.NotPending;

        _orders[index] = _orders[index] with { Status = OrderStatus.Cancelled };
        return CancelResult.Cancelled;
    }

    /// <summary>
    /// Fills a pending order, netting it into the pair's position. <paramref name="price"/> defaults to the
    /// order's limit; an order that was already marketable when placed passes the market rate, which is at
    /// least as good as the limit.
    /// </summary>
    /// <exception cref="InvalidOperationException">The order is unknown or not pending.</exception>
    public FillResult ApplyFill(int orderId, decimal? price = null)
    {
        var index = _orders.FindIndex(o => o.Id == orderId);
        if (index < 0) throw new InvalidOperationException($"Order {orderId} not found.");
        if (_orders[index].Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Order {orderId} is not pending.");

        var filled = _orders[index] with { Status = OrderStatus.Filled };
        _orders[index] = filled;

        var delta = filled.Side == Side.Buy ? filled.Quantity : -filled.Quantity;
        var realised = Net(filled.Pair, delta, price ?? filled.LimitPrice);
        _cash += realised;
        return new(filled, realised);
    }

    /// <summary>Records the latest rates and returns the resulting snapshot.</summary>
    public Snapshot Mark(IEnumerable<Rate> rates)
    {
        foreach (var rate in rates) _rates[rate.Pair] = rate.Value;
        return Snapshot();
    }

    public Snapshot Snapshot()
    {
        var positions = _positions.Values
            .OrderBy(p => p.Pair)
            .Select(p =>
            {
                decimal? rate = _rates.TryGetValue(p.Pair, out var r) ? r : null;
                return new PositionSnapshot(p, rate, rate is { } v ? Pnl(p.Quantity, p.EntryPrice, v) : 0m);
            })
            .ToList();
        var unrealised = positions.Sum(p => p.UnrealisedPnl);
        return new Snapshot(_cash, unrealised, _cash + unrealised, positions, _orders.ToList());
    }

    private decimal Equity() => Snapshot().Equity;

    /// <summary>USD P&amp;L of a signed USD quantity opened at <paramref name="entry"/>, valued at <paramref name="rate"/>.</summary>
    private static decimal Pnl(decimal quantity, decimal entry, decimal rate) => quantity * (1 - entry / rate);

    /// <summary>Applies a signed fill to the pair's position; returns the P&amp;L realised.</summary>
    private decimal Net(Pair pair, decimal delta, decimal price)
    {
        if (!_positions.TryGetValue(pair, out var position))
        {
            _positions[pair] = new Position(pair, delta, price);
            return 0m;
        }

        if (Math.Sign(position.Quantity) == Math.Sign(delta))
        {
            var quantity = position.Quantity + delta;
            var entry = (Math.Abs(position.Quantity) * position.EntryPrice + Math.Abs(delta) * price) / Math.Abs(quantity);
            _positions[pair] = position with { Quantity = quantity, EntryPrice = entry };
            return 0m;
        }

        var closed = Math.Min(Math.Abs(position.Quantity), Math.Abs(delta));
        var realised = Pnl(Math.Sign(position.Quantity) * closed, position.EntryPrice, price);
        var remaining = position.Quantity + delta;

        if (remaining == 0) _positions.Remove(pair);
        else if (Math.Sign(remaining) == Math.Sign(position.Quantity)) _positions[pair] = position with { Quantity = remaining };
        else _positions[pair] = new Position(pair, remaining, price);
        return realised;
    }
}

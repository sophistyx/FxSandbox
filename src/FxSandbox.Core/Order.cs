namespace FxSandbox.Core;

/// <param name="Quantity">USD notional, always positive.</param>
/// <param name="LimitPrice">Quote-currency units per 1 USD, always positive.</param>
public sealed record Order(
    int Id,
    Pair Pair,
    Side Side,
    decimal Quantity,
    decimal LimitPrice,
    OrderStatus Status = OrderStatus.Pending);

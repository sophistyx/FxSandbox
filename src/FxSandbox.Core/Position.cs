namespace FxSandbox.Core;

/// <param name="Quantity">Signed USD notional: positive is long USD, negative is short.</param>
/// <param name="EntryPrice">Weighted-average entry rate.</param>
public sealed record Position(Pair Pair, decimal Quantity, decimal EntryPrice)
{
    public bool IsLong => Quantity > 0;
}

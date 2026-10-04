namespace FxSandbox.Core;

public static class FillRule
{
    /// <summary>Whether the order can fill at this rate: Buy when the rate falls to the limit or lower, Sell when it rises to the limit or higher. Equal fills.</summary>
    public static bool CanFill(Order order, Rate rate) =>
        order.Status == OrderStatus.Pending
        && order.Pair == rate.Pair
        && order.Side switch
        {
            Side.Buy => rate.Value <= order.LimitPrice,
            Side.Sell => rate.Value >= order.LimitPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
}

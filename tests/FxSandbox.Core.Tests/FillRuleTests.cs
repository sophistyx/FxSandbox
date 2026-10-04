namespace FxSandbox.Core.Tests;

public class FillRuleTests
{
    private static Order MakeOrder(Side side, decimal limit, Pair pair = Pair.UsdEur, OrderStatus status = OrderStatus.Pending) =>
        new(1001, pair, side, 1000m, limit, status);

    [Theory]
    [InlineData(0.8800, true)]   // below limit
    [InlineData(0.8885, true)]   // equal fills
    [InlineData(0.8886, false)]  // above limit
    public void Buy_fills_when_rate_at_or_below_limit(decimal rate, bool expected) =>
        Assert.Equal(expected, FillRule.CanFill(MakeOrder(Side.Buy, 0.8885m), new Rate(Pair.UsdEur, rate)));

    [Theory]
    [InlineData(0.8900, true)]   // above limit
    [InlineData(0.8885, true)]   // equal fills
    [InlineData(0.8884, false)]  // below limit
    public void Sell_fills_when_rate_at_or_above_limit(decimal rate, bool expected) =>
        Assert.Equal(expected, FillRule.CanFill(MakeOrder(Side.Sell, 0.8885m), new Rate(Pair.UsdEur, rate)));

    [Fact]
    public void Rate_for_other_pair_never_fills() =>
        Assert.False(FillRule.CanFill(MakeOrder(Side.Buy, 0.8885m), new Rate(Pair.UsdGbp, 0.1m)));

    [Theory]
    [InlineData(OrderStatus.Filled)]
    [InlineData(OrderStatus.Cancelled)]
    public void Non_pending_orders_never_fill(OrderStatus status) =>
        Assert.False(FillRule.CanFill(MakeOrder(Side.Buy, 0.8885m, status: status), new Rate(Pair.UsdEur, 0.5m)));
}

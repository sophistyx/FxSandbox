namespace FxSandbox.Core.Tests;

public class PortfolioTests
{
    private static int PlaceOk(Portfolio p, Side side, decimal qty, decimal limit, Pair pair = Pair.UsdEur)
    {
        var result = p.Place(pair, side, qty, limit);
        Assert.True(result.IsSuccess, result.Error);
        return result.Order!.Id;
    }

    private static Position OnlyPosition(Portfolio p) => Assert.Single(p.Snapshot().Positions).Position;

    [Fact]
    public void Place_creates_pending_order_with_sequential_ids()
    {
        var p = new Portfolio();
        var first = p.Place(Pair.UsdEur, Side.Buy, 1000m, 0.8m);
        var second = p.Place(Pair.UsdGbp, Side.Sell, 500m, 0.7m);

        Assert.Equal(1001, first.Order!.Id);
        Assert.Equal(1002, second.Order!.Id);
        Assert.All(p.Snapshot().Orders, o => Assert.Equal(OrderStatus.Pending, o.Status));
    }

    [Theory]
    [InlineData(0, 0.8)]
    [InlineData(-1, 0.8)]
    [InlineData(1000, 0)]
    [InlineData(1000, -0.5)]
    public void Place_rejects_non_positive_quantity_or_limit(decimal qty, decimal limit)
    {
        var p = new Portfolio();
        var result = p.Place(Pair.UsdEur, Side.Buy, qty, limit);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Empty(p.Snapshot().Orders);
    }

    [Fact]
    public void Place_allows_exposure_equal_to_equity_and_rejects_above()
    {
        var p = new Portfolio(10_000m);
        PlaceOk(p, Side.Buy, 6_000m, 0.8m);
        PlaceOk(p, Side.Sell, 4_000m, 0.9m, Pair.UsdGbp); // exactly 10,000

        Assert.False(p.Place(Pair.UsdChf, Side.Buy, 0.01m, 0.8m).IsSuccess);
    }

    [Fact]
    public void Place_counts_open_positions_toward_exposure()
    {
        var p = new Portfolio(10_000m);
        p.ApplyFill(PlaceOk(p, Side.Buy, 7_000m, 0.8m));

        Assert.False(p.Place(Pair.UsdEur, Side.Buy, 3_001m, 0.8m).IsSuccess);
        Assert.True(p.Place(Pair.UsdEur, Side.Buy, 3_000m, 0.8m).IsSuccess);
    }

    [Fact]
    public void Cancel_pending_order()
    {
        var p = new Portfolio();
        var id = PlaceOk(p, Side.Buy, 1000m, 0.8m);

        Assert.Equal(CancelResult.Cancelled, p.Cancel(id));
        Assert.Equal(OrderStatus.Cancelled, p.Snapshot().Orders.Single().Status);
    }

    [Fact]
    public void Cancel_frees_capacity()
    {
        var p = new Portfolio(10_000m);
        var id = PlaceOk(p, Side.Buy, 10_000m, 0.8m);
        Assert.False(p.Place(Pair.UsdEur, Side.Buy, 1m, 0.8m).IsSuccess);

        p.Cancel(id);

        Assert.True(p.Place(Pair.UsdEur, Side.Buy, 1m, 0.8m).IsSuccess);
    }

    [Fact]
    public void Cancel_unknown_or_non_pending_is_refused()
    {
        var p = new Portfolio();
        var filled = PlaceOk(p, Side.Buy, 1000m, 0.8m);
        p.ApplyFill(filled);
        var cancelled = PlaceOk(p, Side.Buy, 1000m, 0.8m);
        p.Cancel(cancelled);

        Assert.Equal(CancelResult.NotFound, p.Cancel(42));
        Assert.Equal(CancelResult.NotPending, p.Cancel(filled));
        Assert.Equal(CancelResult.NotPending, p.Cancel(cancelled));
    }

    [Fact]
    public void ApplyFill_opens_long_position_at_limit_price()
    {
        var p = new Portfolio();
        var result = p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));

        Assert.Equal(OrderStatus.Filled, result.Order.Status);
        Assert.Equal(0m, result.RealisedPnl);
        Assert.Equal(new Position(Pair.UsdEur, 1000m, 0.8m), OnlyPosition(p));
    }

    [Fact]
    public void ApplyFill_sell_opens_short_position()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Sell, 1000m, 0.9m));

        Assert.Equal(new Position(Pair.UsdEur, -1000m, 0.9m), OnlyPosition(p));
    }

    [Fact]
    public void ApplyFill_twice_or_unknown_throws()
    {
        var p = new Portfolio();
        var id = PlaceOk(p, Side.Buy, 1000m, 0.8m);
        p.ApplyFill(id);

        Assert.Throws<InvalidOperationException>(() => p.ApplyFill(id));
        Assert.Throws<InvalidOperationException>(() => p.ApplyFill(999));
    }

    [Fact]
    public void Adding_to_position_uses_weighted_average_entry()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        var result = p.ApplyFill(PlaceOk(p, Side.Buy, 3000m, 0.9m));

        Assert.Equal(0m, result.RealisedPnl);
        Assert.Equal(new Position(Pair.UsdEur, 4000m, 0.875m), OnlyPosition(p)); // (1000*0.8 + 3000*0.9) / 4000
    }

    [Fact]
    public void Partial_close_realises_pnl_and_keeps_entry()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        var result = p.ApplyFill(PlaceOk(p, Side.Sell, 400m, 1.0m));

        Assert.Equal(80m, result.RealisedPnl); // 400 * (1 - 0.8/1.0)
        Assert.Equal(new Position(Pair.UsdEur, 600m, 0.8m), OnlyPosition(p));
        Assert.Equal(10_080m, p.Snapshot().Cash);
    }

    [Fact]
    public void Full_close_removes_position_and_realises_loss()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        var result = p.ApplyFill(PlaceOk(p, Side.Sell, 1000m, 0.64m));

        Assert.Equal(-250m, result.RealisedPnl); // 1000 * (1 - 0.8/0.64)
        Assert.Empty(p.Snapshot().Positions);
        Assert.Equal(9_750m, p.Snapshot().Cash);
    }

    [Fact]
    public void Flip_long_to_short_realises_closed_part_and_reopens_remainder_at_fill_price()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        var result = p.ApplyFill(PlaceOk(p, Side.Sell, 1500m, 1.0m));

        Assert.Equal(200m, result.RealisedPnl); // only the 1000 closed
        Assert.Equal(new Position(Pair.UsdEur, -500m, 1.0m), OnlyPosition(p));
    }

    [Fact]
    public void Flip_short_to_long()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Sell, 1000m, 1.0m));
        var result = p.ApplyFill(PlaceOk(p, Side.Buy, 1500m, 0.8m));

        Assert.Equal(250m, result.RealisedPnl); // -1000 * (1 - 1.0/0.8)
        Assert.Equal(new Position(Pair.UsdEur, 500m, 0.8m), OnlyPosition(p));
    }

    [Fact]
    public void Positions_are_netted_per_pair()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m, Pair.UsdEur));
        p.ApplyFill(PlaceOk(p, Side.Sell, 1000m, 0.7m, Pair.UsdGbp));

        Assert.Equal(2, p.Snapshot().Positions.Count);
    }

    [Fact]
    public void Mark_computes_unrealised_pnl_and_equity()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        p.ApplyFill(PlaceOk(p, Side.Sell, 1000m, 0.5m, Pair.UsdGbp));

        var snapshot = p.Mark([new Rate(Pair.UsdEur, 1.0m), new Rate(Pair.UsdGbp, 0.4m)]);

        var eur = snapshot.Positions.Single(x => x.Position.Pair == Pair.UsdEur);
        var gbp = snapshot.Positions.Single(x => x.Position.Pair == Pair.UsdGbp);
        Assert.Equal(200m, eur.UnrealisedPnl);   // 1000 * (1 - 0.8/1.0)
        Assert.Equal(250m, gbp.UnrealisedPnl);   // -1000 * (1 - 0.5/0.4): short gains as USD weakens
        Assert.Equal(1.0m, eur.Rate);
        Assert.Equal(450m, snapshot.UnrealisedPnl);
        Assert.Equal(10_450m, snapshot.Equity);
        Assert.Equal(10_000m, snapshot.Cash);
    }

    [Fact]
    public void Unmarked_position_has_zero_unrealised_pnl()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));

        var snapshot = p.Snapshot();

        Assert.Null(snapshot.Positions.Single().Rate);
        Assert.Equal(0m, snapshot.UnrealisedPnl);
        Assert.Equal(10_000m, snapshot.Equity);
    }

    [Fact]
    public void Equity_includes_realised_and_unrealised()
    {
        var p = new Portfolio();
        p.ApplyFill(PlaceOk(p, Side.Buy, 1000m, 0.8m));
        p.ApplyFill(PlaceOk(p, Side.Sell, 400m, 1.0m)); // +80 realised, 600 left

        var snapshot = p.Mark([new Rate(Pair.UsdEur, 1.0m)]);

        Assert.Equal(120m, snapshot.UnrealisedPnl); // 600 * 0.2
        Assert.Equal(10_200m, snapshot.Equity);
    }

    [Fact]
    public void Place_uses_marked_equity_for_capital_check()
    {
        var p = new Portfolio(10_000m);
        p.ApplyFill(PlaceOk(p, Side.Buy, 5_000m, 0.8m));
        p.Mark([new Rate(Pair.UsdEur, 0.4m)]); // unrealised = 5000 * (1 - 2) = -5000, equity 5000

        Assert.False(p.Place(Pair.UsdEur, Side.Buy, 1m, 0.4m).IsSuccess);
    }

    [Fact]
    public void Snapshot_is_immutable_copy()
    {
        var p = new Portfolio();
        PlaceOk(p, Side.Buy, 1000m, 0.8m);
        var before = p.Snapshot();

        PlaceOk(p, Side.Buy, 1000m, 0.8m);

        Assert.Single(before.Orders);
    }
}

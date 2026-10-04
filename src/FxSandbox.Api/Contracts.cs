using FxSandbox.Core;
using FxSandbox.Simulation;

namespace FxSandbox.Api;

/// <summary>Body of <c>POST /api/orders</c>. Pair and side are nullable so a missing value is reported as a validation error.</summary>
public sealed record PlaceOrderRequest(Pair? Pair, Side? Side, decimal Quantity, decimal LimitPrice);

public sealed record RateDto(Pair Pair, decimal Rate, IReadOnlyList<decimal> History);

/// <param name="Quantity">Signed USD notional: positive is long, negative is short.</param>
public sealed record PositionDto(
    Pair Pair,
    string Direction,
    decimal Quantity,
    decimal EntryPrice,
    decimal? Rate,
    decimal UnrealisedPnl);

public sealed record StateDto(
    decimal Cash,
    decimal Equity,
    decimal UnrealisedPnl,
    IReadOnlyList<RateDto> Rates,
    IReadOnlyList<PositionDto> Positions,
    IReadOnlyList<Order> Orders)
{
    public static StateDto From(SandboxSnapshot s) => new(
        s.Portfolio.Cash,
        s.Portfolio.Equity,
        s.Portfolio.UnrealisedPnl,
        s.Rates
            .Select(r => new RateDto(r.Pair, r.Value, s.History.GetValueOrDefault(r.Pair) ?? []))
            .ToArray(),
        s.Portfolio.Positions
            .Select(p => new PositionDto(
                p.Position.Pair,
                p.Position.IsLong ? "Long" : "Short",
                p.Position.Quantity,
                p.Position.EntryPrice,
                p.Rate,
                p.UnrealisedPnl))
            .ToArray(),
        s.Portfolio.Orders);
}

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
    /// <summary>The domain keeps full decimal precision; money is rounded to cents only here, at the API boundary.</summary>
    public static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static StateDto From(SandboxSnapshot s) => new(
        RoundMoney(s.Portfolio.Cash),
        RoundMoney(s.Portfolio.Equity),
        RoundMoney(s.Portfolio.UnrealisedPnl),
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
                RoundMoney(p.UnrealisedPnl)))
            .ToArray(),
        s.Portfolio.Orders);
}

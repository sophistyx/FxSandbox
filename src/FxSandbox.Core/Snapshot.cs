namespace FxSandbox.Core;

/// <param name="Rate">Latest marked rate, or null if the pair has not been marked yet.</param>
/// <param name="UnrealisedPnl">USD P&amp;L at <paramref name="Rate"/>; zero when unmarked.</param>
public sealed record PositionSnapshot(Position Position, decimal? Rate, decimal UnrealisedPnl);

/// <summary>Immutable view of the portfolio, safe to share across threads.</summary>
public sealed record Snapshot(
    decimal Cash,
    decimal UnrealisedPnl,
    decimal Equity,
    IReadOnlyList<PositionSnapshot> Positions,
    IReadOnlyList<Order> Orders);

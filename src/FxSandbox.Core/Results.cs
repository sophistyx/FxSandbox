namespace FxSandbox.Core;

/// <summary>Outcome of <see cref="Portfolio.Place"/>: exactly one of Order or Error is set.</summary>
public sealed record PlaceResult(Order? Order, string? Error)
{
    public bool IsSuccess => Order is not null;
}

public enum CancelResult
{
    Cancelled,
    NotFound,
    NotPending,
}

/// <param name="Order">The order as filled.</param>
/// <param name="RealisedPnl">USD P&amp;L realised into cash by this fill (zero if it only opened or added).</param>
public sealed record FillResult(Order Order, decimal RealisedPnl);

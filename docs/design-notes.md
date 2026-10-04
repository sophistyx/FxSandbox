# Design notes

## Slice 1: Core types and fill rule

- **`decimal` for all prices and quantities.** Avoids binary floating-point error in P&L and limit comparisons, where an equal-to-limit boundary must be exact. Cost: slightly slower arithmetic, irrelevant at this scale.
- **Immutable records for `Order` and `Position`; `Rate` is a `readonly record struct`.** Snapshots can be shared across threads without copying, which suits the later lock-plus-snapshot concurrency design. State changes use `with`. Alternative: mutable classes, rejected because they make race conditions easier to introduce.
- **`FillRule.CanFill` is a pure static function.** It takes an order and a rate and has no state, so it is trivially testable and reusable by the engine and tests. It also checks that the order is pending and the pair matches, so callers cannot fill a cancelled or filled order, or match it against the wrong pair, by mistake.
- **Equal-to-limit fills.** Buy fills when `rate <= limit`, Sell when `rate >= limit`. This follows the confirmed spec decision and is covered by boundary tests on both sides.
- **Signed `Position.Quantity` (long positive, short negative).** One net position per pair needs no separate direction field, and netting becomes plain addition. Trade-off: callers must remember the sign convention, noted in the doc comment.

## Slice 2: Core portfolio engine

- **`Portfolio` is a plain single-threaded class; the engine (slice 4) owns the lock.** Keeps the maths testable without concurrency noise.
- **Result types instead of exceptions for expected failures.** `Place` returns `PlaceResult` (order or error message) and `Cancel` returns `CancelResult` (Cancelled/NotFound/NotPending), so the API can map them to 400/404/409. `ApplyFill` throws, because the engine only fills orders it just matched under the lock, so a failure is a bug.
- **`ApplyFill(orderId)` fills at the order's limit price.** Matches the plan's fill-price simplification. Whether an order is fillable is the engine's job via `FillRule`.
- **Equity for the capital check uses the last marked rates.** `Mark` stores them; before any mark, unrealised P&L is zero. Exposure = new qty + open position notional + pending notional, rejected only if it exceeds equity (equal is allowed).
- **Netting.** Same direction: weighted-average entry. Opposite: realise P&L on the closed part at the fill price; a remainder in the old direction keeps its entry, a flip reopens at the fill price. P&L is `qty × (1 − entry / rate)` in USD.

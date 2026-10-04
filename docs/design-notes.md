# Design notes

## Slice 1: Core types and fill rule

- **`decimal` for all prices and quantities.** Avoids binary floating-point error in P&L and limit comparisons, where an equal-to-limit boundary must be exact. Cost: slightly slower arithmetic, irrelevant at this scale.
- **Immutable records for `Order` and `Position`; `Rate` is a `readonly record struct`.** Snapshots can be shared across threads without copying, which suits the later lock-plus-snapshot concurrency design. State changes use `with`. Alternative: mutable classes, rejected because they make race conditions easier to introduce.
- **`FillRule.CanFill` is a pure static function.** It takes an order and a rate and has no state, so it is trivially testable and reusable by the engine and tests. It also checks that the order is pending and the pair matches, so callers cannot fill a cancelled or filled order, or match it against the wrong pair, by mistake.
- **Equal-to-limit fills.** Buy fills when `rate <= limit`, Sell when `rate >= limit`. This follows the confirmed spec decision and is covered by boundary tests on both sides.
- **Signed `Position.Quantity` (long positive, short negative).** One net position per pair needs no separate direction field, and netting becomes plain addition. Trade-off: callers must remember the sign convention, noted in the doc comment.

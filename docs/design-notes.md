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

## Slice 3: Rate simulation

- **`RateSimulator` is a plain, non-thread-safe class taking an injected `Random`.** A seeded `Random` makes paths deterministic in tests. Randomness is a real seam, so it is injected; the engine (slice 4) serialises access under its lock rather than the simulator locking itself.
- **One shared `Random`, one draw per pair per step.** Pairs are independent random walks without a generator each. Trade-off: the pairs' paths depend on enumeration order for a given seed, which is stable for a fixed set of pairs.
- **Δ is `(NextDouble() × 2 − 1) × 0.001`, i.e. [-0.001, +0.001).** `NextDouble` excludes 1, so the upper bound is approached but not reached, which is immaterial. The draw is converted to `decimal` before multiplying, so rates stay `decimal` throughout. Rates are not rounded, so the walk is exactly `old × (1 + Δ)`; display rounding is a UI concern.
- **Multiplicative steps keep rates positive**, and every step is a bounded ±0.1% move. There is no mean reversion, so rates can drift far over long runs, which matches the spec's random walk.
- **Config via `SimulationOptions` and an `appsettings.json` section; binding is deferred to slice 4.** Seed rates and tick interval are data, not code. The options class carries the 2 Oct close rates as defaults. Alternative: a dedicated options package or `IOptions` now, rejected until the hosted service needs it.

## Slice 4: Sandbox service and concurrency

- **One lock in `SandboxEngine` serialises place, cancel and tick.** `Portfolio` and `RateSimulator` stay single-threaded, so a pending order is filled or cancelled exactly once and each tick sees a consistent order set. Operations are tiny and in-memory, so contention is negligible. Alternative: a channel-based actor, rejected as overkill here.
- **Events are queued under the lock and raised after it is released.** A subscriber (later the SignalR hub) can call back into the engine without deadlocking, and a slow subscriber does not hold up other callers. Trade-off: two threads' events can be delivered out of order relative to each other, which is acceptable because every `Ticked` carries a full snapshot.
- **Marketable orders fill inside `Place`, at their limit.** This follows the plan's fill-price simplification and avoids a window where a crossed order sits pending until the next tick. `OrderPlaced` is always raised before `OrderFilled` for the same order.
- **`SandboxSnapshot` bundles portfolio, rates and a rolling window of the last 60 rates per pair.** The UI gets sparkline history from the same immutable object, so it needs no separate client store. Trade-off: each snapshot copies the window (small and bounded).
- **`TickService` is a thin `PeriodicTimer` loop and all logic lives in the engine.** The engine is testable by calling `Tick()` directly, with a fixed-value `Random` stub for deterministic paths. Tick and fill events are not yet wrapped in error handling, so a throwing subscriber would stop the service; this is revisited if the hub needs it.

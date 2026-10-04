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

## Slice 5: REST API

- **Two-layer validation.** The endpoint checks pair, side, quantity and limit price and returns a `ValidationProblem` keyed by field (`quantity`, `limitPrice`), which the order form (slice 9) can map to inputs. Engine rejections, such as the capital check, have no single field, so they return a plain ProblemDetails 400 titled "Order rejected". Trade-off: the positive-number rules exist in both the API and `Portfolio`, but `Portfolio` stays safe when used on its own.
- **`SandboxEngine.Reset()` rebuilds state from factories.** `POST /api/reset` needs fresh orders, ids, positions and rate history, and `Portfolio` and `RateSimulator` have no reset of their own. A second constructor takes factories, and the original constructor still works but can't be reset. Reset publishes a `Ticked` snapshot so subscribers resync. Alternative: swap the whole engine in DI, rejected because other singletons would hold a stale reference.
- **Exception handling keeps ProblemDetails 500s.** Malformed bodies (bad JSON, unknown enum names) throw `BadHttpRequestException`, which `UseExceptionHandler` would otherwise turn into a 500. `ExceptionHandlerOptions.StatusCodeSelector` maps those to their own 4xx and leaves everything else as a 500. Enums serialise as strings and reject integers, so `"Buy"` is the only accepted spelling.
- **State DTO flattens the snapshot.** `StateDto` carries cash, equity, unrealised P&L, rates with their history window, positions (with a Long/Short direction) and orders. The API shape is decoupled from Core records, so Core can change without breaking the UI. Place and cancel return the order as it stands after the call, so an order that fills on placement is returned as `Filled`.
- **Registration lives in `AddSandboxSimulation(IConfiguration)`.** The Simulation project owns its wiring and `Program.cs` stays a short composition root, at the cost of one extra package (`Options.ConfigurationExtensions`). Api tests remove `TickService` and tick the engine by hand for determinism. CORS allows the Vite origin `localhost:5173`.

## Slice 6: SignalR hub

- **A push-only hub, with a hosted `SandboxHubPublisher` bridging engine events.** `SandboxHub` has no client-callable methods; the publisher subscribes to `Ticked` and `OrderFilled` and sends through `IHubContext`. The engine stays free of SignalR and the Api project owns the transport. Alternative: have the engine take the hub context, rejected because it would couple Simulation to ASP.NET.
- **Each tick pushes a full `snapshot` (the same `StateDto` as `GET /api/state`) plus a discrete `orderFilled`.** The UI writes the snapshot straight into the query cache with no merge logic, and a dropped message is healed by the next tick. Trade-off: a payload per tick that includes the 60-point history windows, which is small at this scale. Deltas were rejected as needless complexity.
- **Sends are fire-and-forget with failures logged.** Event handlers run on the tick or request thread, so awaiting the network would let a slow client delay ticks. Trade-off: a failed send is only logged, not retried, which is acceptable because the next snapshot supersedes it.
- **No snapshot on connect; clients fetch `GET /api/state` first and again on reconnect.** This keeps the hub trivial and reuses the REST contract. `POST /api/reset` raises `Ticked`, so clients resync after a reset.
- **Wire format matches REST.** Enums are sent as strings via the hub's JSON protocol options, and CORS now allows credentials because SignalR's negotiate request needs them from the Vite origin. The test connects over long polling through the in-memory `TestServer`, which has no real sockets, and covers both `snapshot` and `orderFilled`.

## Slice 7: UI foundation

- **TanStack Query owns server state under one `['state']` key.** `useSandboxState` wraps `useQuery`, and components read slices of it. This is the cache slice 8's SignalR snapshots will write into with `setQueryData`, so there is no second client store. `refetchOnWindowFocus` is off because pushes keep the cache current. Alternative: Redux or Zustand, rejected as duplicating server state.
- **A Vite dev proxy forwards `/api` to `localhost:5290`.** The client uses relative URLs, so there is no base-URL config and the browser sees one origin in development. Trade-off: the proxy is dev-only, and a production build would need the same routing at the host. The existing CORS policy stays for the SignalR connection, which will need a `/hubs` entry or a direct URL in slice 8.
- **A small typed `fetch` client with an `ApiError`.** `api.ts` mirrors the backend DTOs by hand and parses ProblemDetails into `status`, `message` and per-field `errors`, which slice 9 maps onto form fields. Alternative: generate types from the OpenAPI document, deferred as heavier than three endpoints justify. Trade-off: the types can drift from the API until then.
- **shadcn components were read before use.** The Base UI variants differ from Radix: `Badge` takes a `render` prop (via `useRender`) instead of `asChild`, and `Card` has a `size` prop. `table` is generated now for slice 10 but not yet used.
- **Display formatting is separated from data.** Money goes through `Intl.NumberFormat`, and P&L colour is chosen by sign with a dark-mode variant. The Live badge is static until slice 8 wires connection state. The summary cards show placeholders while the first fetch is pending.

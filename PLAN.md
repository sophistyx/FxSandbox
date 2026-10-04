# FX Sandbox implementation plan (PLAN.md)

## Context

Build the FX sandbox from `local/spec.md`: USD 10,000 starting capital, three simulated pairs (USD/EUR, USD/GBP, USD/CHF), limit orders that fill when the simulated rate reaches the limit, and a React UI showing the order book, open positions and unrealised P&L. The repo is an empty scaffold (ASP.NET Core minimal API on .NET 10, placeholder Core/Simulation projects, Vite + React 19 + shadcn on Base UI). The UI target is `reference/ui-prototype.png`, mapped to shadcn components in `reference/ui-component-map.png`. The data flow in `docs/notes.md` is TanStack Query plus SignalR pushing into the query cache.

`CLAUDE.md` says to work from `PLAN.md`, one phase at a time. **Slice 0** copies this plan to `PLAN.md` with checkboxes. Each slice below is one phase: it builds, passes tests, and is committed by the user.

## Slices

If time is short, drop slice 11 first and keep the polish slice.

Backend first (Core is pure and testable), then UI. Only add dependencies a slice needs.

0. [x] **Plan file.** Add `PLAN.md` (checkbox per slice) and remove the `Class1.cs` placeholders when the first real type lands.
1. [x] **Core types and fill rule.** `Pair`, `Side`, `OrderStatus`, `Order`, `Position`, `Rate` in `FxSandbox.Core`, using `decimal`. A `FillRule.CanFill(order, rate)` function: Buy fills when `rate <= limit`, Sell when `rate >= limit`. Tests: boundaries (equal fills), both sides.
2. [x] **Core portfolio engine.** A synchronous, single-threaded `Portfolio` class with:
   - `Place`: validates the quantity and limit price, then applies the capital check below.
   - `Cancel`: pending orders only.
   - `ApplyFill`: nets per pair, with a weighted-average entry. An opposite fill reduces, closes or flips the position and realises P&L into cash.
   - `Mark(rates)`: unrealised P&L and equity.
   - Immutable `Snapshot`.
   - Tests: place, cancel, reject over-limit, add to position, partial close, full close, flip long to short, realised and unrealised P&L maths, equity.
3. [x] **Rate simulation.** `RateSimulator` in `FxSandbox.Simulation`: `newRate = old × (1 + Δ)`, with `Δ` uniform in [-0.001, 0.001] from an injectable `Random`. Seed rates and the tick interval come from `appsettings.json`. Tests: the bound holds over many steps, a fixed seed is deterministic, and the pairs are independent.
4. [x] **Sandbox service and concurrency.** `SandboxEngine` and the hosted tick service live in `FxSandbox.Simulation` (it references Core). The Api project only hosts endpoints and the hub. `SandboxEngine` wraps `Portfolio` and `RateSimulator`. A single lock guards the state. A tick advances the rates, matches pending orders and applies fills. A `PeriodicTimer` `BackgroundService` drives ticks. Events (`Ticked`, `OrderFilled`, `OrderPlaced`, `OrderCancelled`) are published after the lock is released. Tests: a tick fills crossed orders, plus one minimal parallel place/cancel/tick stress test asserting no double fill and consistent equity.
5. [ ] **REST API.** Replace the weather sample.
   - Endpoints: `GET /api/state` (snapshot: cash, equity, unrealised P&L, rates plus recent history, positions, orders), `POST /api/orders`, `DELETE /api/orders/{id}`, `POST /api/reset`.
   - Validation errors return ProblemDetails 400, and cancelling a filled order returns 409.
   - Enable JSON enum strings, CORS for the Vite origin and OpenAPI.
   - Tests use `WebApplicationFactory` in `FxSandbox.Api.Tests`. Replace `UnitTest1.cs` with real tests, and drop the empty placeholder test files.
6. [ ] **SignalR hub.** `/hubs/sandbox` pushes `snapshot` after each tick and discrete `orderFilled` events. One minimal test: connect a test client and receive a tick.
7. [ ] **UI foundation.**
   - Add `@tanstack/react-query`, a Vite dev proxy to `localhost:5290`, a typed API client, and `QueryClientProvider`.
   - Add the shadcn `card`, `badge` and `table` components, checking the generated Base UI source first.
   - Build the header ("FX sandbox" plus Live badge) and the four summary cards, driven by `useQuery(['state'])`.
8. [ ] **Live updates and tickers.**
   - Add `@microsoft/signalr`. A connection hook calls `queryClient.setQueryData(['state'], snapshot)` and refetches on reconnect. The Live badge shows the connection state.
   - Build the rate ticker cards with a tiny SVG sparkline, % change, and a green/red flash on each tick.
9. [ ] **Order form.**
   - Add `react-hook-form`, `zod` and `sonner`, plus the shadcn `select`, `toggle-group`, `input`, `button` and `form` components.
   - The form has a pair select, a buy/sell toggle, quantity (USD) and limit price.
   - The helper line reads "Fills when rate falls to X or lower" (Buy) or "rises to X or higher" (Sell).
   - `useMutation` posts the order; server rejections show as a toast and field errors.
10. [ ] **Positions, order book and fill toasts.**
    - Positions table: long/short badge, qty, entry, rate, P&L.
    - Order book with an All/Pending/Filled `Tabs` filter and a Cancel button inside an `AlertDialog` (add `tabs` and `alert-dialog`).
    - `orderFilled` events raise a sonner toast ("Order 1004 filled at 0.7420").
11. [ ] **Creative extra (optional; skip if time is short).** A larger price chart for the selected pair (shadcn `chart`/Recharts) with a dashed horizontal line at each pending limit price.
12. [ ] **Polish.** Dark mode (`theme-provider.tsx` already exists, so add a toggle and check the contrast of green/red and badges in both themes), loading/empty/error states, accessibility pass, README (run instructions, architecture, Decisions, assumptions), and the tidy-up of `CLAUDE.md` and `docs/notes.md`.

## Decisions

**Framework.** The spec says "Nancy, ASP.NET API". Nancy is unmaintained and is awkward on .NET 10 with SignalR. We stay on ASP.NET Core minimal API, which the scaffold already uses, and record the deviation in the README. *(Confirmed.)*

**Quoting and quantity.** *(Confirmed.)*
- Pairs are quoted as quote-currency units per 1 USD (USD/EUR 0.8885, USD/GBP 0.7552, USD/CHF 0.8288), with USD as the base.
- Quantity is a USD notional, as in the prototype's "Quantity (USD)" label.
- Buy means buy USD and Sell means sell USD.
- Buy fills when the rate falls to the limit or lower. Sell fills when it rises to the limit or higher.
- A rate equal to the limit fills.
- Seed rates come from the 2 Oct 2026 close and live in `appsettings.json`: USD/EUR 0.8885, USD/GBP 0.7552, USD/CHF 0.8288. The README notes the source and date. We don't call a live FX feed.

**Fill price (simplification).** Orders always fill at their limit price, as in the prototype's toast. An order that is already marketable when placed is checked straight away, in the same operation, and also fills at its limit rather than the better market rate. This is a deliberate simplification and the README says so.

**Capital.** *(Confirmed.)* Placing an order does not reserve cash, and fills do not debit cash.
- Cash starts at 10,000 and changes only through realised P&L.
- Equity is cash plus unrealised P&L.
- Reject a new order if its notional plus the total notional of open positions plus the total notional of pending orders would exceed equity.
- Position notional is `|qty|` in USD, and order notional is its qty.

**Positions and P&L.** *(Netting confirmed.)*
- There is one net position per pair with a weighted-average entry. Shorts are allowed, and qty is signed.
- P&L is converted to USD at the current rate: `unrealised = qty × (1 − entry / rate)`.
- Closing or reducing a position realises P&L at the fill price.
- A flip closes the old side and opens the remainder at the fill price.
- A closed position disappears.
- The prototype's example P&L figures are illustrative and differ slightly from this formula.

**Orders.** Sequential ids start at 1001. Only pending orders can be cancelled. Status is Pending, Filled or Cancelled. Quantity and limit must be greater than zero.

**State.** Single anonymous user, in memory only, reset on restart or via `POST /api/reset`. No auth or persistence.

**Concurrency.** The simulation ticks on a background timer while HTTP requests place and cancel orders concurrently.
- `Portfolio` (Core) is pure and single-threaded. `SandboxEngine` (Simulation) is the only mutator, serialising place, cancel and tick under one lock. The operations are tiny and in-memory, so contention is negligible.
- This rules out double fills and cancel-vs-fill races, and each tick sees a consistent set of orders.
- Readers get immutable snapshots, and events are raised after the lock is released so subscribers can't deadlock.
- A channel-based actor was considered, and rejected as overkill here.

**State management.** The server is the source of truth, and the UI holds no business state.
- TanStack Query caches `['state']`, and SignalR pushes snapshots with `setQueryData`.
- Mutations update the cache from their response, and a reconnect refetches.
- Sparkline history comes from the snapshot's recent-rates window (about 60 ticks per pair), so there is no separate client store.
- Form state lives in react-hook-form with zod, and the theme lives in the existing provider.
- No Redux or Zustand.

## Verification

- Every slice: `dotnet build` and `dotnet test`. UI slices also run `npm run build`, `npm run lint` and `npm run format` from `src/fx-sandbox-ui`.
- Backend by hand: run the API with `dotnet run --project src/FxSandbox.Api --launch-profile http`, then use `FxSandbox.Api.http` or curl to place a marketable order and a far-off one, and watch `GET /api/state` tick over.
- End to end (slice 10 onward): start the API and `npm run dev`, then place buy and sell orders on each pair and check the checklist below.
  - Tickers move live.
  - Fills toast and move orders from Pending to Filled.
  - Positions net correctly, including a flip to short.
  - P&L and equity match the formula.
  - Cancel works and confirms first.
  - Over-limit orders are rejected.
  - A reconnect resyncs after restarting the API.
- Polish: check both themes at desktop and phone width, and follow the README instructions from a clean clone.

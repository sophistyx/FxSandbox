# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FX "sandbox" coding test: traders place limit orders (buy/sell) on simulated USD/EUR, USD/GBP and USD/CHF, starting with USD 10,000 capital. When the simulated spot rate reaches a limit price the order fills and a position is created/adjusted. The UI shows the order book, open positions and unrealised P&L against the latest rates.

Rate simulation: per-pair random walk, `newRate = oldRate × (1 + Δ)` with `Δ ∈ [-0.001, +0.001]`, seeded from the current exchange rate.

The full spec lives in `local/spec.md` (git-ignored, so it may be absent in a fresh clone). `reference/` holds the UI prototype and component-map images. `PLAN.md` tracks the slices and records the design decisions; `docs/` has working notes. `README.md` is still empty (written in the polish slice).

Status: the backend is complete (domain, simulation, REST API, SignalR hub). The UI has the header with a live connection badge, summary cards and rate tickers; the order form, positions, order book and fill toasts are still to come (see `PLAN.md`).

## Layout

.NET 10 solution (`FxSandbox.slnx`) plus a Vite UI:

- `src/FxSandbox.Api` – ASP.NET Core minimal API host. References Core and Simulation. `SandboxEndpoints.cs` maps `/api/state`, `/api/orders` (POST), `/api/orders/{id}` (DELETE) and `/api/reset`; `SandboxHub.cs` is the SignalR hub at `/hubs/sandbox` (pushes `snapshot` after each tick and `orderFilled` on fills) plus the publisher that forwards engine events to it; `Contracts.cs` holds the DTOs. `Program.cs` wires up string enums, ProblemDetails, CORS, OpenAPI and SignalR. Dev URLs are `http://localhost:5290` / `https://localhost:7249`.
- `src/FxSandbox.Core` – pure domain: `Pair`, `Side`, `OrderStatus`, `Order`, `Position`, `Rate`, `FillRule`, and the single-threaded `Portfolio` (place, cancel, fills, netting, mark-to-market) with immutable `Snapshot`s.
- `src/FxSandbox.Simulation` – `RateSimulator` (random walk, injectable `Random`), `SandboxEngine` (wraps `Portfolio` and the simulator under one lock, raises events after releasing it), `TickService` (`PeriodicTimer` background service), `SimulationOptions` (seed rates and tick interval from config) and `AddSandboxSimulation` for DI.
- `src/fx-sandbox-ui` – React 19 + TypeScript + Vite 8 + Tailwind 4 + shadcn/ui. `lib/api.ts` is the typed REST client and shared types; `hooks/use-sandbox-state.ts` is the `['state']` query; `hooks/use-sandbox-live.ts` is the SignalR connection that pushes snapshots into the query cache and exposes connection status; `components/` has `header`, `summary-cards`, `rate-tickers` and `theme-provider`, with shadcn primitives in `components/ui`. The Vite dev server proxies `/api` and `/hubs` to the API.
- `tests/FxSandbox.{Api,Core,Simulation}.Tests` – xUnit, one test project per source project (Api.Tests references the Api project and uses `WebApplicationFactory`).

## Commands

Backend (from repo root):

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"   # single test
dotnet run --project src/FxSandbox.Api --launch-profile http
```
When piping dotnet output (e.g. to grep/tail), add `--disable-build-servers`, e.g. `dotnet test --disable-build-servers 2>&1 | tail -20`. Without it, build-server processes keep the pipe open and the command hangs until timeout.

UI (from `src/fx-sandbox-ui`):

```
npm run dev
npm run build        # tsc -b && vite build
npm run lint
npm run typecheck
npm run format       # prettier, with tailwind class sorting
```

The UI has no test runner configured.

## UI conventions

- `@` is aliased to `src/fx-sandbox-ui/src`.
- shadcn/ui is configured with **Base UI, not Radix**. Check the generated component source in `src/components/ui` before using component APIs; Radix-style props (e.g. `asChild`) may not exist.
- Data flow: server state lives in TanStack Query (`useQuery(['state'])`, `useMutation` for writes), and SignalR (`hooks/use-sandbox-live.ts`) pushes snapshots into that cache with `setQueryData` and refetches on reconnect. The UI holds no business state of its own.

## Code conventions

- Domain data as immutable records; small value types as `readonly record struct`.
- Pure domain logic in static or plain classes (e.g. `FillRule.CanFill`), not interfaces + DI. Use interfaces only at real seams: I/O, time, randomness, publishing.
- `decimal` for all money and rates.

## Workflow

- Work from `PLAN.md`: implement one phase at a time, then stop and summarise. Tick the phase's checkbox when done.
- Before declaring a phase done: `dotnet build` and `dotnet test` pass; for UI changes, `npm run build`, `npm run lint` and `npm run format` pass.
- Only add dependencies (NuGet or npm) that the current phase needs.
- Write tests alongside the code, especially for order matching, fills, positions and P&L in Core.
- Don't commit; I review and commit each phase myself.
- Create and modify files only with the Write/Edit tools — never via shell commands (heredocs, python, sed, `cat >`, etc.). Read files with the Read tool.

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FX "sandbox" coding test: traders place limit orders (buy/sell) on simulated USD/EUR, USD/GBP and USD/CHF, starting with USD 10,000 capital. When the simulated spot rate reaches a limit price the order fills and a position is created/adjusted. The UI shows the order book, open positions and unrealised P&L against the latest rates.

Rate simulation: per-pair random walk, `newRate = oldRate × (1 + Δ)` with `Δ ∈ [-0.001, +0.001]`, seeded from the current exchange rate.

The full spec lives in `local/spec.md` (git-ignored, so it may be absent in a fresh clone). `reference/` holds the UI prototype and component-map images. `README.md` is currently empty.

## Layout

.NET 10 solution (`FxSandbox.slnx`) plus a Vite UI:

- `src/FxSandbox.Api` – ASP.NET Core minimal API (host). References Core and Simulation. `Program.cs` is still the template weather-forecast sample; dev URLs are `http://localhost:5290` / `https://localhost:7249`.
- `src/FxSandbox.Core` – domain (orders, positions, etc.). Currently only a `Class1.cs` placeholder.
- `src/FxSandbox.Simulation` – rate simulation. Currently only a placeholder.
- `src/fx-sandbox-ui` – React 19 + TypeScript + Vite 8 + Tailwind 4 + shadcn/ui.
- `tests/FxSandbox.{Api,Core,Simulation}.Tests` – xUnit, one test project per source project (Api.Tests references the Api project).

The codebase is an early scaffold; most of the above is placeholder code.

## Commands

Backend (from repo root):

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"   # single test
dotnet run --project src/FxSandbox.Api --launch-profile http
```

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
- Planned data flow (from `docs/notes.md`): server state via TanStack Query (`useQuery`/`useMutation`), with live updates via SignalR pushing into the query cache. Neither is installed yet; add them in the first slice that calls the API.

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

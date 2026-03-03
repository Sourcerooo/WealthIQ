# WealthIQ - C#/.NET Learning Plan (Mentor/Pair-Guide)

## Goal

Build a realistic capstone to learn C# and the .NET ecosystem through architecture-first practice:

- Import broker statements (start with Interactive Brokers XML)
- Normalize monetary events (trades, dividends, interest, fees, taxes)
- Aggregate over time ranges
- Distinguish open vs closed positions correctly
- Calculate realized P/L (tax/reporting relevant)

---

## Working Mode (Mentoring)

- You implement actively.
- I provide architecture guidance, focused tasks, review criteria, and pitfalls.
- No full code dump by default.
- Per phase we use:
  - Goal
  - Tasks
  - Review criteria
  - Definition of Done

---

## Recommended Learning Order

1. C# language delta for senior C++ developers
2. .NET runtime/tooling/project model
3. Capstone v1 as modular CLI app (domain-first)
4. API layer (ASP.NET Core)
5. UI layer (Avalonia)
6. Hardening (tests, observability, packaging, perf)

Recommended order for adapters: API first, then UI.

---

## Phase 0 - Setup and Ecosystem Foundation

### Goal

Own the tooling loop and a clean modular solution layout.

### Tasks

- Install current .NET LTS SDK.
- Create solution: `WealthIQ.sln`
- Create projects:
  - `WealthIQ.Domain`
  - `WealthIQ.Application`
  - `WealthIQ.Infrastructure.IBKR`
  - `WealthIQ.Cli`
  - `WealthIQ.Tests`
- Set references with dependency direction outer -> inner only.
- Ensure `dotnet build` and `dotnet test` are green.

### C++ -> C# focus

- `csproj` + NuGet workflow
- Nullable reference types
- `record`, `init`, and pattern matching

### Definition of Done

- Build green
- Tests green
- Phase note/ADR filled from template

---

## Phase 1 - C# Language Delta (Senior C++ Track)

### Focus

- Value vs reference semantics (`struct`, `class`, `record`)
- Immutability-first design
- Generics + constraints
- LINQ as a transformation language
- Pattern matching (`switch` expressions, property/type patterns)
- Async/await, cancellation, `IAsyncEnumerable`
- Performance tools when needed (`Span<T>`, pooling)

### Outcome

Idiomatic C# implementation style (not C++ transliteration).

### Definition of Done

- At least one implemented feature reviewed specifically for idiomatic C#
- Phase note/ADR created

---

## Phase 2 - Domain Model for Monetary Events

### Goal

Design a broker-agnostic core model and position realization logic.

### Initial model draft (to refine together later)

- `MonetaryEvent` family:
  - `TradeEvent`
  - `DividendEvent`
  - `InterestEvent`
  - `FeeEvent`
  - `TaxEvent`
- Value objects: `Money`, `InstrumentId`, `AccountId`
- `PositionLedger` / `LotMatcher`
- `RealizationResult`

### Key decisions

- Lot matching strategy: default FIFO (approved), extensible later
- Fee/tax treatment in P/L
- Multi-currency handling strategy

### Definition of Done

- Domain tests cover open/closed transitions and realized P/L
- Core invariants documented
- Phase note/ADR created

---

## Phase 3 - IBKR XML Ingestion

### Goal

Map IBKR XML into canonical domain events.

### Tasks

- Parser in `WealthIQ.Infrastructure.IBKR`
- Mapping to canonical events
- Validation + parse diagnostics
- Idempotency key strategy for duplicate imports
- Golden test files for realistic IBKR samples

### Definition of Done

- Parser robust to field/order variation
- Broker-specific logic isolated to infrastructure
- Phase note/ADR created

---

## Phase 4 - CLI v1

### Goal

Use the system through command-line workflows.

### Example commands

- `import ibkr --file <path>`
- `report pnl --from <date> --to <date>`
- `report positions --open`

### Definition of Done

- End-to-end flow works from import to report
- Structured, readable output and sensible exit codes
- Phase note/ADR created

---

## Phase 5 - API (ASP.NET Core)

### Goal

Expose the same application logic over HTTP without duplicating domain rules.

### Tasks

- Add `WealthIQ.Api`
- Endpoints for imports/reports/positions
- DTOs separate from domain entities
- OpenAPI enabled
- Integration tests via test host

### Definition of Done

- Contract stable and documented
- CLI/API share application use-cases
- Phase note/ADR created

---

## Phase 6 - UI (Avalonia)

### Goal

Desktop adapter with thin UI logic.

### Tasks

- Add `WealthIQ.Desktop`
- Views for import, open/closed positions, realized P/L range reports
- MVVM bindings and validation

### Definition of Done

- Usable import/report flow on desktop
- No domain rules embedded in views/viewmodels
- Phase note/ADR created

---

## Phase 7 - Hardening

### Scope

- Persistence (SQLite first)
- Test depth: unit, integration, property-based where useful
- Logging/configuration/DI standards
- Packaging and distribution

### Definition of Done

- Repeatable local and CI pipeline
- Documented operational defaults
- Phase note/ADR created

---

## Architecture Baseline

- `WealthIQ.Domain` - entities/value objects/domain services
- `WealthIQ.Application` - use-cases and ports
- `WealthIQ.Infrastructure.*` - broker/persistence adapters
- `WealthIQ.Cli` / `WealthIQ.Api` / `WealthIQ.Desktop` - delivery adapters
- `WealthIQ.Tests` - unit/integration tests

Rule: outer layers can depend on inner layers, never the other way around.

---

## Session 1 (next practical step)

1. Create solution and base projects
2. Define first domain types (`Money`, `TradeEvent`, `PositionLot`)
3. Implement simple FIFO lot matcher
4. Add 8-12 focused domain tests
5. Add a minimal CLI command for open positions report (in-memory input)

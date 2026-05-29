# Roadmap -- WealthIQ

## Purpose

This roadmap describes how WealthIQ should evolve from the current single-broker CLI tax-report slice into a broader personal wealth-management application.

It is intentionally broader than `docs/Todo.md`.
The roadmap answers which capabilities should come next and in which rough order.

## Planning Principles

- Deliver reliable tracking and tax value before advanced strategy automation.
- Prefer correctness and auditability over convenience shortcuts.
- Fail fast on missing, inconsistent, or unmapped data.
- Keep broker adapters, market-data adapters, and tax logic isolated behind clear boundaries.
- Preserve extensibility for new asset classes, brokers, and strategy modules.
- Build dashboard and PDF outputs on top of the same trusted calculation core.

## Product Direction

WealthIQ should evolve toward this broad product flow:

```text
Broker statements + market/reference data
  -> canonical portfolio ledger
  -> portfolio valuation and tax engine
  -> dashboard and PDF reporting
  -> later strategy evaluation and backtesting
  -> eventually trade recommendation and broker-assisted execution
```

## Milestones

### Milestone 1 -- Trusted Tracking And Tax Core

Goal: turn the current prototype slice into a reliable ledger and tax foundation.

- harden broker import around strict validation and explicit failure behavior
- make all unsupported or unmapped broker records visible and blocking
- stabilize canonical event and instrument models for multi-asset growth
- strengthen tax-calculation correctness and auditability
- produce a trustworthy CLI-based tax and tracking workflow before broader UI work

Exit criteria:

- the system can import supported broker statements without silent gaps
- every imported record is either mapped or explicitly rejected with a blocking error
- FIFO and current German tax rules in scope are reproducible and test-covered
- the output is trustworthy enough to act as the baseline for future dashboard and PDF layers

### Milestone 2 -- Multi-Broker Ingestion And Portfolio Ledger

Goal: expand import from one broker path to a reusable ingestion foundation.

- add Tastytrade CSV import
- add Trader's Place PDF import
- unify imports into one canonical portfolio ledger
- extend asset modeling for the near-term asset classes in scope
- ensure consistent EUR normalization across imported events and holdings

Exit criteria:

- multiple broker sources can be imported into one canonical timeline
- portfolio state can be derived from imported events across the supported brokers
- unmapped rows, assets, and currency issues fail loudly and visibly

### Milestone 3 -- Portfolio Valuation And Dashboard Foundation

Goal: make the portfolio visible beyond raw tax calculations.

- add market-data integration, starting with Yahoo Finance API
- compute current portfolio value and allocation in EUR
- support historical day-level portfolio valuation using closing prices
- calculate first portfolio and position KPIs for open and closed positions
- introduce the first dashboard for portfolio state, positions, and KPI views

Exit criteria:

- the current portfolio can be valued in EUR from imported holdings and market data
- a historical date can be selected to recalculate portfolio value for that day
- dashboard views consume the same calculation core as reporting workflows

### Milestone 4 -- Professional Reporting

Goal: make the outputs suitable for recurring real-world use.

- add professional PDF export for portfolio and tax reporting
- provide dashboard tax overviews and supporting drill-down views
- add audit trails from imported source records to report entries
- formalize report quality expectations for yearly tax submission support

Exit criteria:

- the system can generate a professional PDF tax report and portfolio report
- report totals can be traced back to underlying events and calculations
- the reporting layer is usable without relying on manual spreadsheet consolidation

### Milestone 5 -- Strategy Definition And Recommendation Engine

Goal: move from passive reporting to decision support.

- define a strategy model and strategy-allocation model
- support selectable strategies with target portfolio percentages
- evaluate current market data against strategy rules
- derive target positions and buy/sell recommendations
- start measuring deviation between target and actual positions

Exit criteria:

- at least one strategy can be defined, evaluated, and turned into target allocations
- the system can express actionable buy/sell recommendations with position sizing
- target-versus-actual position comparison is visible in the product

### Milestone 6 -- Backtesting And Historical Strategy Analysis

Goal: validate strategy ideas using historical data and consistent analytics.

- import or download historical market data
- execute strategies against historical data
- visualize portfolio-value curves and performance metrics
- report measures such as Sharpe ratio, win/loss statistics, and maximum drawdown

Exit criteria:

- at least one strategy can be backtested from historical inputs
- core backtest KPIs are visible in a dashboard
- the backtesting module uses a calculation model that is compatible with the live strategy layer where practical

### Milestone 7 -- Broker-Connected Execution Support

Goal: close the loop between analysis, recommendation, and execution.

- add direct broker integration starting with Interactive Brokers API
- allow order creation flows with manual confirmation where appropriate
- track divergence between intended and actually executed positions more precisely

Exit criteria:

- the system can prepare or submit trades through a broker integration
- actual execution results can be reconciled against intended target positions

## Near-Term Release Themes

### Release Theme A -- Make The Ledger Trustworthy

Focus:

- strict import validation
- explicit audit paths
- no silent data loss

### Release Theme B -- Make The Portfolio Visible

Focus:

- valuation in EUR
- position and allocation views
- first dashboard slice

### Release Theme C -- Make Reporting Replace Spreadsheets

Focus:

- tax-report confidence
- professional PDF output
- reduced manual consolidation effort

## Risks And Watchpoints

- PDF import quality may become a disproportionate complexity driver.
- Multi-currency valuation errors would undermine trust in the whole system.
- Silent fallback behavior would conflict with the fail-fast requirement.
- Tax logic scope can expand quickly once more asset classes are added.
- Combining tracking, tax, strategy, backtesting, and execution in one codebase requires strict boundaries to stay maintainable.

## Open Product Questions

- Which dashboard delivery technology should become the long-term UI host?
- How broad should the first "binding" tax-report claim be before professional tax review?
- Which asset classes should be in the first fully supported tax-and-tracking release beyond the currently implemented instruments?
- How much of trade execution should remain manual even after broker APIs are introduced?

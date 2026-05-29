# Vision -- WealthIQ

Working document for medium-term product direction, later decisions, and larger target capabilities.
This document intentionally does not replace `docs/Todo.md` as the source for the next concrete implementation steps.

## Product Vision

WealthIQ should become a personal wealth-management application for one private user.
Its main purpose is to consolidate portfolio tracking, tax reporting, portfolio analysis, and later strategy-driven decision support into one tool with minimal manual work.

The long-term target is a system that:

- tracks activities across multiple asset classes such as ETFs, leveraged ETFs, ETCs, stocks, crypto, bonds, and later additional assets
- imports broker data from multiple brokers and file formats
- values the current and historical portfolio in EUR using imported market data
- calculates portfolio KPIs, realized results, tax-relevant results, and portfolio allocation
- exposes the information in a modern dashboard and professional PDF reports
- later evaluates strategies, derives position recommendations, and eventually supports broker-assisted execution and backtesting

## Primary Product Priorities

The currently most important goals are:

1. reliable tracking of portfolio activity and open positions
2. reliable German tax reporting for a private person
3. reduction of manual work currently spread across Google Sheets and other tools

The topics `trade recommendations` and `backtesting` are explicitly long-term ambitions, not immediate priorities.

## User And Operating Model

- The target user is the repository owner only.
- The tool is for personal use, not a multi-user SaaS product.
- The user is a software developer and can work with technical tooling.
- Manual input should be minimized wherever import or API-based automation is possible.

## Data And Integration Vision

### Broker Data

The system should support multiple brokers over time.

Initial target broker inputs:

- IBKR XML reports
- Tastytrade CSV reports
- Trader's Place PDF reports

Longer term:

- direct broker integration, starting later with Interactive Brokers API
- eventual ability to create real trades that may still require manual confirmation

### Market Data

The system should load market data from one of the following sources:

- HTTP API endpoints
- databases
- files

The initial target is Yahoo Finance API.

### Valuation

- The current portfolio should be valued using current market data.
- The user should also be able to select a date in the past and get the historical portfolio value for that day.
- Day-level closing prices are sufficient for the initial historical valuation requirement.
- All values must be converted correctly into the base currency EUR.

## Reporting Vision

### Portfolio And KPI Reporting

WealthIQ should calculate and present:

- current portfolio value
- portfolio allocation across asset classes and holdings
- KPIs for open and closed positions
- historical and current performance summaries
- a dashboard for interactive inspection
- professional PDF reports for export

### Tax Reporting

WealthIQ should produce a German private-investor tax report that is intended to be usable as a binding submission basis for the tax office.

The target tax scope mentioned so far includes at least:

- FIFO lot matching
- `Vorabpauschale`
- tax-free holding-period handling where applicable, for example crypto and gold after one year
- explicit handling of every imported but unassignable event or asset through a visible error path and manual audit

Tax-report output should be available both:

- in the dashboard as an overview
- as a professional PDF document suitable for tax filing support

## Strategy And Decision-Support Vision

Later iterations should allow the user to define and select strategies such as:

- Ray Dalio All Weather Portfolio
- Dual Momentum Portfolio
- Moving Average Crossover systems

The target capability is:

- evaluate strategies on current market data
- derive buy and sell recommendations
- assign a percentage of total portfolio capital to each strategy
- compute resulting target position sizes

## Backtesting Vision

Later iterations should also support strategy backtesting using:

- downloaded historical data
- or historical data provided through files

Target outputs include at least:

- portfolio-value curve
- Sharpe ratio
- gross and net win/loss statistics
- maximum drawdown
- additional strategy diagnostics as they become useful

## Tracking-Error Vision

The system should later track the deviation between:

- the theoretical position derived from strategy logic and target sizing
- the actual executed position after trade execution

This area is explicitly a subject for later discussion about usefulness, feasibility, and scope.

## Product Constraints And Non-Negotiable Rules

- Errors and expectation deviations must fail fast instead of being silently tolerated.
- Missing input or missing market/reference data must be treated as an error.
- Imported but unmapped assets or events must be reported explicitly and must not be ignored.
- Currency handling must remain correct across assets traded in multiple currencies while using EUR as the base currency.
- The asset model must remain extensible because asset classes and strategies will evolve over time.

## Initial Asset Allocation Context

The current starting investment profile is:

- 50% unleveraged ETF
- 30% leveraged ETF
- 10% ETC (gold)
- 10% individual stocks

This is an initial practical context, not a fixed architectural limit.

## Candidate Future Features

The current product description suggests natural later extensions such as:

- portfolio drift and rebalance recommendations
- audit trails from imported statement line to report line
- data-quality dashboards for missing mappings and unresolved imports
- automated reconciliation between broker holdings, imported events, and calculated portfolio state

These are not committed roadmap items yet.

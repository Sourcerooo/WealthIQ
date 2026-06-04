# WealthIQ UI Redesign — "Midnight Ledger"

**Date:** 2026-06-04
**Status:** Approved (design), pending implementation plan
**Scope:** Frontend-only redesign of `src/WealthIQ.Web`. No changes to Domain, Application, or Infrastructure.

## Goal

Transform the current default-MudBlazor, light-only, flat-top-navbar UI into a modern, polished, visually distinctive single-user wealth/tax dashboard. The result should feel like a premium financial tool: an eye-catcher with tasteful motion, a left navigation pane, and a dark/light theme switch. Professional line-icons only — **no emoji**.

This is a **visual/UX redesign**, not a feature change. Every figure displayed continues to come from data the existing tax pipeline already produces. The v1 scope guardrails from the main spec still hold (no portfolio valuation, no PDF export, no additional brokers, single base currency).

## Hard constraint: no backend changes

All work stays within `WealthIQ.Web` (the composition root + UI). The redesign must not modify `WealthIQ.Domain`, `WealthIQ.Application`, or `WealthIQ.Infrastructure`, must not alter the database/schema/migrations, and must not change tax/ledger logic. If any desired UI behavior turns out to genuinely require a backend change, **stop and ask the user before proceeding**. Adding a Web-layer UI-state service (e.g. theme persistence) is in-scope; it touches no business logic, persistence, or schema.

## Design language — "Midnight Ledger"

Deep navy-slate base with an emerald accent. Dark-first and premium; light mode is a crisp paper-white. Numbers read as authoritative.

### Color tokens

**Dark mode (primary):**
- Background `#0F1420`; elevated surface `#161D2C`; sidebar `#0A0E17`; border `#232D40`
- Accent (primary) emerald `#10B981`, bright variant `#34D399` (gradients, active states, headline figures)
- Text primary `#E7ECF3`; text muted `#7D8AA3`; faint label `#5D6A82`
- Semantic: positive/gain emerald (`#34D399`), negative/loss `#F87171`, info `#60A5FA`, warning `#FBBF24`, secondary category `#A78BFA`

**Light mode:** background `#F7F9FC`; surface `#FFFFFF`; border `#E6EAF0`; text `#1E2733`; muted `#8893A4`; accent deepened to `#059669` for contrast on white. Same semantic hues, contrast-adjusted.

### Typography
- Swap Roboto → **Inter** (UI). Keep a system fallback stack.
- Enable `font-variant-numeric: tabular-nums` wherever numbers/currency appear so columns align.
- Figures: heavier weight, tight negative letter-spacing for the "authoritative number" feel.

### Motion (tasteful; honors `prefers-reduced-motion`)
- Card hover: subtle lift + border glow
- KPI and headline figures: count-up on load
- Composition donut: sweep-in on load
- Route/page change: smooth fade transition
- Active nav item: animated indicator
- Implemented with CSS + lightweight JS interop only; no heavy animation library.

## Layout shell & navigation

Replace the top `MudAppBar` with a **persistent left `MudDrawer`** (~240px; collapses to an icon-rail on narrow viewports). Drawer structure, top to bottom:

- **WealthIQ** wordmark + emerald mark
- Section **Bericht** → `Steuerreport` (`/`)
- Section **Daten erfassen** → `Import` (`/import`)
- Section **Stammdaten** → `Marktdaten` (`/data-admin`), `Instrumente` (`/data-admin/instruments`)
- Divider, pinned to the bottom — section **Diagnose** → `Diagnose` (`/diagnostics`), `Audit-Trail` (`/audit`). These are infrequent debugging/troubleshooting tools, deliberately out of the day-to-day path.
- Drawer footer: dark/light segmented toggle + active reporting-year indicator.

Each item gets a professional Material line-icon (suggested: `Receipt_Long` Steuerreport, `Upload_File` Import, `Insights`/`ShowChart` Marktdaten, `Inventory_2` Instrumente, `BugReport` Diagnose, `History` Audit-Trail). Active item shows an emerald pill + left indicator bar. Main content sits in a max-width container with generous padding.

**Information-architecture changes (labels/placement only — routes unchanged):**
- `Data-Admin` (`/data-admin`) is presented as **Marktdaten**.
- `Instrumente` (`/data-admin/instruments`) is promoted to a top-level nav item instead of being reached only from within Data-Admin. The route stays the same.
- German UI labels preserved throughout; English for all code/identifiers (per CLAUDE.md).

## Page-by-page redesign

All pages keep their existing functionality, bindings, and drill-down/source-provenance actions. Only presentation changes.

- **Steuerreport** (`/`, centerpiece): header with year selector → **hero row** (animated estimated-tax headline figure + tax-composition donut sourced from the existing per-category totals: sales / dividends / Vorabpauschale / interest / withholding) → KPI stat-card grid with the estimated-tax card emerald-highlighted → restyled drill-down expansion panels/tables retaining the existing drill-to-source buttons. Introduces reusable `StatCard` and `SectionCard`.
- **Import** (`/import`): centered upload card styled as a dropzone; per-file result cards with severity coloring; polished progress indicator; diagnostics table restyled.
- **Marktdaten** (`/data-admin`): the five expansion sections (Ledger, Historical Prices, FX Rates, Basiszins, Instruments link) restyled as distinct cards; clearer status/progress; themed confirm dialogs.
- **Instrumente** (`/data-admin/instruments`): restyled table (sticky header, hover, striping in-theme); inline edit panel reworked into a cleaner expanding/side editor; themed upload section (instruments.json / listings.json, merge/replace).
- **Diagnose** (`/diagnostics`) & **Audit-Trail** (`/audit`): restyled severity filter and tables; severity chips recolored to the new palette.
- **Error** / **NotFound**: on-brand, centered, minimal.

## Technical approach (within `WealthIQ.Web` only)

- **Theme:** a central `MudTheme` defined in a new `Theme/WealthIqTheme.cs` (PaletteLight + PaletteDark, typography, layout radii). `MudThemeProvider` bound with `@bind-IsDarkMode`.
- **Theme persistence:** a small **scoped UI service** + `localStorage` JS-interop to remember the user's dark/light choice across sessions. Pure Web-layer UI state; no DB, no business logic, no schema. Handle Blazor Server prerender (apply persisted value after first interactive render; default to dark, optionally seed from `prefers-color-scheme`).
- **Reusable components** under `Components/Shared/`: `StatCard`, `SectionCard`, `PageHeader`, and nav building blocks.
- **CSS:** custom rules in `wwwroot/app.css` (or a new `wwwroot/wealthiq.css`) for motion, gradients, tabular numerals, scrollbar styling — layered on top of MudBlazor, scoped to avoid fighting the component library.
- **Charts:** MudBlazor's built-in `MudChart` (donut) — no new package dependency.
- **Icons:** MudBlazor's bundled Material line-icons.

## Testing & verification

- `dotnet build WealthIQ.slnx` succeeds (Release too, per CI).
- `dotnet format WealthIQ.slnx --verify-no-changes` clean.
- Existing `dotnet test WealthIQ.slnx` suite stays green — this is UI-only and changes no tested logic.
- Add tests only if a non-trivial UI service is introduced worth covering (e.g. parsing/validating a persisted theme preference).
- Manual verification pass: every page in both dark and light mode; nav active states and routing; responsive drawer collapse; `prefers-reduced-motion` disables animation; theme choice persists across reloads.

## Out of scope (unchanged from main spec)

Portfolio valuation/charts beyond visualizing the existing tax result, PDF export, additional brokers, trading/backtesting, multi-base-currency, and any Domain/Application/Infrastructure change.

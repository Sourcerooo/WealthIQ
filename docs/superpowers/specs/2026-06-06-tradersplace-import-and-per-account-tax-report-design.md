# Trader's Place import + per-account tax report — Design

- **Date:** 2026-06-06
- **Branch:** `feat/brokerImport`
- **Status:** Approved (ready for implementation planning)
- **Spec author basis:** brainstorming session over the two sample exports in `data/sample/`

## 1. Purpose & scope

Add **Trader's Place** (a German broker) as a second statement source feeding the canonical
ledger, alongside the existing Interactive Brokers (IBKR) XML import. As a coupled requirement,
the German annual tax report (`Steuerreport`) must be shown **per account** rather than combined,
so data from different brokers/accounts is never mixed.

Tastytrade is explicitly **not** in this phase — it is the harder follow-up and gets its own
spec/plan once Trader's Place is done.

### In scope
- A `TradersPlaceStatementImporter` that ingests Trader's Place CSV exports into canonical entries.
- Plumbing to import the **two complementary CSV files** Trader's Place produces in one action.
- A dividend **alias→ISIN mapping** (the dividend export lacks ISINs and uses mangled names).
- Capturing broker-withheld **KESt** (German capital-gains tax) as a prepaid-tax figure.
- Making the **Steuerreport per account** (account dropdown, no cross-account mixing).
- Golden fixtures + tests for all of the above.

### Out of scope (this phase)
- Tastytrade import.
- Cash-position / account-balance tracking (cash movements are skipped, as today).
- **Xetra-Gold tax-free treatment** — see §11 (deferred future work, must be fixed later).
- Accrued `Stückzinsen` (bond accrued interest) handling — 0 in current data; deferred.
- Portfolio valuation/charts, PDF export, multi-base-currency (already out of v1 scope).

## 2. Background: the two Trader's Place exports

Trader's Place offers two different CSV exports, which together contain everything we need. They are
**not** to be physically merged (different column schemas); instead one importer ingests both and
**routes by transaction type**.

### 2.1 `Kontoumsätze` (account statement) — cash flows
Sample: `data/sample/Kontoumsätze_20260606_114351.csv`.
Columns: `Kontonummer; Kontoart; Buchungsdatum; Valutadatum; Transaktion; Währung; Betrag;
Kontotext / WP-Identifikation; Umsatz-ID (PK); Ausführungs-ID`.

`Transaktion` values observed:
- `Gutschrift` — inbound transfer (external → broker). Non-taxable cash movement.
- `Überweisung` — outbound transfer. Non-taxable cash movement.
- `Einzahlung` — inbound transfer (usually from a secondary broker account). Non-taxable.
- `Kontoabschluss` — interest/fees on cash. Positive = credit interest (taxable income);
  negative = debit interest / account fee.
- `Effekten` — **dividends** (taxable). **No ISIN**; `Kontotext` carries a mangled instrument
  name (e.g. `VANGUARD S+P 500U.ETF DLD`) that does not match the instrument's real name.
- `Kauf` / `Verkauf` — also present here, but **without quantity or price** (only total EUR).
  These are intentionally **ignored** from this file (taken from the trades export instead).

This file lacks share quantities and per-share prices for trades — insufficient on its own for
FIFO capital-gains tax.

### 2.2 `Depotumsätze` (securities statement) — trades
Sample: `data/sample/Depotumsätze_20260606_114607.csv`.
Columns: `Handelsdatum; Valutadatum; Transaktion; Instrumentenart; WP-Identifikationsart;
WP-Identifikation; WP-Name; Nominale / Stück; Kurs / Limit; Handelswährung; Zahlungswährung;
Kurswert in Zahlungswährung; Summe der eigenen Spesen in Zahlungswährung; Summe der fremden Spesen
in Zahlungswährung; aufgelaufene Stückzinsen in Zahlungswährung; bezahlte / erhaltene KESt in
Zahlungswährung; Endbetrag in Zahlungwährung; Währungskurs; Börse; Status; Orderart; Gültigkeit;
Lagerland`.

This is the Finanzamt-grade trade source: it has quantity (`Nominale / Stück`), price
(`Kurs / Limit`), separate own/foreign fees, accrued `Stückzinsen`, broker-withheld `KESt`, the net
`Endbetrag`, and an FX rate (`Währungskurs`). It does **not** contain dividends/interest.

### 2.3 Key subtlety: differing account numbers
The two files reference **different account numbers** for the same brokerage relationship: trades
reference the depot (`4415066001`), cash references the Verrechnungskonto (`4415066002`) and Tagesgeld
(`4415066010`). All must map to **one WealthIQ `AccountId`**, because the tax engine looks up held
lots by `AccountId` when offsetting dividends/Vorabpauschale (`GermanTaxCalculator.ProcessCash`,
`PerformYearEndClosing`). If trades and dividends landed in different accounts, that lookup would
silently find nothing — a correctness bug.

This unification also aligns with the per-account report: one broker relationship = one WealthIQ
account = one report section.

## 3. Encoding & parsing

- Files are **Windows-1252** (ANSI) with mojibake when read as UTF-8 (`W�hrung`, `�berweisung`,
  `St�ck`). Read with the Windows-1252 / Latin-1 code page so umlauts decode correctly. Register
  `CodePagesEncodingProvider` if needed on .NET.
- Separator is `;`; rows have a trailing empty field (line ends with `;`).
- Numbers use German format (decimal comma, e.g. `108,259000`, `14,59`, `-30,78`). Parse with
  `de-DE` culture (or invariant after replacing `,`→`.`, chosen consistently).
- Dates are `dd.MM.yyyy`.
- The final footer line (`Kontoumsätze;Depot: …` / `Depotumsätze;Depot: …`) is metadata — skip it.
- Validate at the boundary; a malformed required field becomes a structured `ImportDiagnostic`
  (Error), never a silent drop (project fail-fast rule).

## 4. Importer design: `TradersPlaceStatementImporter`

Location: `src/WealthIQ.Infrastructure/TradersPlace/Import/TradersPlaceStatementImporter.cs`,
implementing `WealthIQ.Application.Import.Interface.IStatementImporter`.

- `CanImport(source)` → `source.Broker == Broker.TradersPlace && source.Format == Format.CSV`.
- `ImportAsync` resolves all `*.csv` files at `source.FilePath` (a folder; see §5), classifies each
  by **header signature**:
  - header begins with `Handelsdatum;` → Depotumsätze (trades)
  - header begins with `Kontonummer;` → Kontoumsätze (cash)
  - unrecognized header → Fatal diagnostic (do not guess).
- All produced entries use the single `request.AccountId`.

### 4.1 Transaction routing

| Source file   | `Transaktion`                              | Result |
|---------------|--------------------------------------------|--------|
| Depotumsätze  | `Kauf`                                      | `TradeEntry` (Buy) |
| Depotumsätze  | `Verkauf`                                   | `TradeEntry` (Sell) + KESt (§7) |
| Kontoumsätze  | `Effekten`                                  | `CashEntry(Dividend)` via alias map (§6) |
| Kontoumsätze  | `Kontoabschluss`, amount > 0                | `CashEntry(Interest)` |
| Kontoumsätze  | `Kontoabschluss`, amount < 0                | skip + Info diagnostic |
| Kontoumsätze  | `Gutschrift` / `Überweisung` / `Einzahlung` | skip + Info diagnostic |
| Kontoumsätze  | `Kauf` / `Verkauf`                          | skip (trades come from Depotumsätze; prevents double-count) |
| either        | unknown `Transaktion`                       | Warning diagnostic + skip |

This routing is what makes "feed both files" equivalent to a clean merge without physically merging.

### 4.2 TradeEntry mapping (Depotumsätze)
- `Side` ← `Kauf`→Buy, `Verkauf`→Sell.
- `Quantity` ← `Nominale / Stück`.
- `UnitPrice` ← `Kurs / Limit`, currency = `Handelswährung`.
- `Fees` ← `Summe der eigenen Spesen` + `Summe der fremden Spesen` (in `Zahlungswährung`).
- `Taxes` ← `0` (KESt is **not** a transaction cost — see §7).
- `WithheldTax` ← `bezahlte / erhaltene KESt` (new field, §7).
- Instrument identified by `WP-Identifikation` (ISIN) + `WP-Name`; `Instrumentenart`
  (`Investmentfonds/ETFs`, `Zertifikat`, …) recorded for diagnostics. Asset-class gating mirrors
  IBKR (funds/ETFs/eligible securities accepted).

### 4.3 CashEntry mapping (Kontoumsätze)
- Dividend: gross amount ← `Betrag`, currency ← `Währung`, instrument resolved via alias map (§6).
- Interest: gross amount ← `Betrag` (positive only), currency ← `Währung`, cash instrument per currency.

### 4.4 FX policy
Amounts are stored in their **original currency** (`Handelswährung` for price, `Zahlungswährung`/
`Währung` for fees and cash). Trader's Place's `Währungskurs` column is **ignored** — replay converts
to EUR using WealthIQ's own FX lookup at the event's own date (architecture rule). All current sample
data is EUR (rate 1.0), so no FX is exercised yet; a missing required non-EUR rate fails loud.

### 4.5 Idempotency / dedup keys (`SourceProvenance`)
- `SourceSystem = "TradersPlace"`, `ImportFormat = "CSV"`, `SourceLocation` = stored CSV path.
- `SourceSection = "Kontoumsätze" | "Depotumsätze"`.
- `SourceRecordReference`:
  - Kontoumsätze: `Umsatz-ID (PK)` (e.g. `K463475`).
  - Depotumsätze (no ID column): stable composite hash of
    `(Handelsdatum, ISIN, Transaktion, Nominale, Kurs, Endbetrag)`, with an ordinal suffix to
    disambiguate genuinely identical rows in one file.
- Re-import remains idempotent via the existing `ILedgerStore` dedup on
  `(SourceSystem, SourceRecordReference)`.

## 5. Import plumbing for two files

The two CSVs must import as **one batch under one account** ("One import, both files" — chosen UX).

- `ImportSource.FilePath` stays a single path. For Trader's Place the Web page stages both uploaded
  temp files into **one temporary folder** and passes that folder as `FilePath`. The importer
  resolves `*.csv` within it (mirroring the IBKR importer's existing directory resolution).
- `StatementImportPipeline` / `IRawFileStore` must ingest **all files in the folder** into the audit
  store (today `IRawFileStore.Ingest` takes a single file). Add a directory-aware path (e.g.
  `Ingest` that, given a directory, archives each contained file and returns the stored locations),
  keeping IBKR's single-file behavior unchanged.
- One `ImportBatch` is produced for the combined import; `SourceLocation` per entry points at the
  specific archived CSV.
- Re-importing later with only one of the two files still works (idempotent dedup; missing data just
  isn't added).

### 5.1 Web import page
`Components/Pages/Import.razor` gains a **broker selector** (IBKR vs Trader's Place):
- IBKR: unchanged (XML, may select multiple files; each imported as its own batch as today).
- Trader's Place: accept the two CSVs, stage into one folder, run a single combined import command
  with `Format.CSV`. Account id derived via `DeterministicAccount.IdFor("TradersPlace", number)`.

## 6. Dividend alias→ISIN mapping

Dividends (`Effekten`) have no ISIN and a mangled `Kontotext` name. Chosen approach: an **explicit,
user-maintained alias→ISIN table**, seeded and editable, that fails loud when unmapped.

- New Application port `IDividendAliasMap` (e.g. `TryResolveIsin(string alias, out string isin)`).
- EF-backed implementation + table; seed from a committed reference file
  `data/reference/tradersplace_dividend_aliases.csv` (`Alias;Isin`), e.g.
  `VANGUARD S+P 500U.ETF DLD;IE00B3XXRP09`, `ISHSIV-DL T.BD20+YR DL D;IE00BSKRJZ44`.
- Matching normalizes whitespace/case; exact normalized match only (no fuzzy logic).
- An **unmapped** dividend alias → **Error** `ImportDiagnostic` → import aborts (no silent fallback),
  prompting the user to add the mapping.
- A small editable panel under **Stammdaten** (add/edit/delete rows, same pattern as the Basiszins
  table) backed by a refresh/CRUD service.
- New instrument ISINs introduced by Trader's Place still need an **instrument profile**
  (Teilfreistellung, `SubjectToVorabpauschale`) via the existing `instruments.json` / enricher; seed
  profiles for the sample instruments. A held instrument with no profile already fails loud at
  Vorabpauschale/sale.

## 7. KESt (broker-withheld German capital-gains tax) as prepaid tax

Depotumsätze reports KESt the broker already withheld at sale (e.g. `340,29` on the Vanguard sale).
Chosen treatment: **track as prepaid tax** and show it alongside the estimate (kept separate from
foreign withholding tax).

- `Domain.Model.Ledger.TradeEntry` gains an **optional** `Money WithheldTax` (default `0`).
  - Persisted (EF migration); IBKR importer passes `0` (no behavior change).
  - It is **not** part of proceeds/cost FIFO math (KESt is income tax on the gain, not a transaction
    cost). Explicitly excluded from `ConvertProceedsToEur`/`ConvertCostBasisToEur`.
- `Domain.Model.Tax.GermanTaxEntry` gains `WithheldKESt`. In `GermanTaxCalculator.ProcessTrade`, a
  sale's `WithheldTax` is allocated across FIFO consumption slices by matched-quantity ratio (same
  technique as fee allocation) and recorded on the resulting `Sell` entries.
- `TaxReportSummary` gains `WithheldKESt` (sum per account/year). The Steuerreport hero shows the
  estimate **and** "davon bereits einbehalten (KESt)" / the remaining figure.

## 8. Per-account tax report

The FIFO matcher and `GermanTaxCalculator` are **already account-scoped** internally (lots,
dividends, Vorabpauschale all filter by `AccountId`); only the reporting layer mixes accounts
(`AnnualTaxReportService` groups by year only). The fix is reporting/UI-level.

- `Domain.Model.Tax.GermanTaxEntry` gains `AccountId`, populated by `GermanTaxCalculator` from the
  originating trade/cash entry (and from the consumed lot's `AccountId` for sells).
- `AnnualTaxReportService.GenerateAsync` groups by **(AccountId, Year)** and returns per-account
  reports; account display names resolved from `PortfolioLedger.Accounts`. New result shape, e.g.
  `IReadOnlyList<AccountTaxReport>` where each carries `AccountId`, `AccountNumber`, and that
  account's `IReadOnlyList<AnnualTaxReport>`.
- `Components/Pages/Steuerreport.razor` gains a top **account dropdown** (preselect first account,
  same pattern as the Data Browser ledger dropdown), then the existing year dropdown scoped to the
  selected account. No cross-account aggregation.

## 9. Reference & test data

- `data/reference/tradersplace_dividend_aliases.csv` — committed seed (Alias;Isin).
- Instrument profiles for the sample ISINs added to `data/reference/instruments.json` (or the
  relevant seed), incl. `Type` and `SubjectToVorabpauschale`.
- `data/test/` — committed golden fixtures: the two Trader's Place sample CSVs plus the alias/profile
  configuration the regression test reads. **Must not be gitignored** (CI reads them).

## 10. Testing

Deterministic, no live network, fixed reference data (project rules). Names
`Method_Scenario_ExpectedResult`.

- **Parser unit tests:** Windows-1252 decoding (umlauts), German number/date parsing, header
  classification, transaction routing (incl. trades-ignored-from-Kontoumsätze → no double count),
  positive/negative `Kontoabschluss` handling, dedup-key derivation.
- **Alias map tests:** resolve known alias; unmapped alias → Error diagnostic / abort.
- **KESt tests:** `WithheldTax` allocated by ratio across FIFO slices; excluded from gain math;
  summed into `TaxReportSummary.WithheldKESt`.
- **Per-account report tests:** entries grouped by `(AccountId, Year)`; two accounts never mixed.
- **End-to-end regression test:** import both sample CSVs → assert exact parsed entry set and the
  resulting per-account tax figures, in the spirit of `GermanTaxRegressionTests`.

## 11. Known limitations / deferred work

- **Xetra-Gold tax-free treatment — MUST be fixed later, NOT in this phase/branch.** Xetra-Gold
  (`DE000A0S9GB0`, a physical-gold ETC) is tax-free in Germany when held > 1 year (§23-style
  treatment). The current engine has no concept of this and will tax its disposal gains as a normal
  capital gain per its instrument profile. For this phase it is imported and taxed conservatively
  (i.e. potentially **overstating** tax on gold). A dedicated future change is required to model the
  holding-period exemption. Tracked here explicitly.
- **Cash-position / balance tracking** is still not built; cash movements (`Gutschrift`/
  `Überweisung`/`Einzahlung`) are skipped. Needed later to reconcile account balances.
- **Accrued `Stückzinsen`** on bonds (0 in current data) is not yet handled.
- KESt is captured as a prepaid figure for reconciliation, not as a binding settlement; the
  EstimatedTax remains an orientation estimate (existing limitation).
- Existing per-year, single-pot limitation (no Verlustverrechnungstöpfe / loss carry-forward) is
  unchanged.

## 12. Touched components (summary)

- **Domain:** `TradeEntry` (+`WithheldTax`), `GermanTaxEntry` (+`AccountId`, +`WithheldKESt`).
- **Application:** `IDividendAliasMap` port; `GermanTaxCalculator` (KESt allocation, AccountId
  tagging, exclude KESt from gain math); `AnnualTaxReportService` (+ per-account grouping);
  `TaxReportSummary`/report result shape (+`WithheldKESt`, per-account); `IRawFileStore` directory
  ingest.
- **Infrastructure:** `TradersPlaceStatementImporter`; EF mapping/migration for `WithheldTax`;
  dividend-alias table + EF impl + seeder; instrument-profile seed additions.
- **Web:** `Import.razor` (broker selector + two-file Trader's Place flow); `Steuerreport.razor`
  (account dropdown); Stammdaten alias-mapping panel; DI wiring (composition root).
- **Tests + data:** fixtures under `data/test`, reference seeds under `data/reference`, unit +
  regression tests per §10.

Session 1 – Setup und erster Domain-Slice
- Ziel: Solution-Struktur steht, Domain-Grundtypen stehen, FIFO-Matching läuft für Kernfälle, Tests grün.
- Zeitbox: 60–90 Minuten.
- Wichtig: Alle Namen/Code/Docs in Englisch.
1) Projektstruktur anlegen
- Lege folgende Struktur an:
  - src/WealthIQ.Domain
  - src/WealthIQ.Application
  - src/WealthIQ.Infrastructure.IBKR
  - src/WealthIQ.Cli
  - tests/WealthIQ.Tests
- Erzeuge die Solution WealthIQ.sln.
- Erzeuge Projekttypen:
  - Domain/Application/Infrastructure.IBKR als Class Library
  - Cli als Console App
  - Tests als xUnit Testprojekt
2) Referenzen setzen (Dependency Direction)
- WealthIQ.Application -> WealthIQ.Domain
- WealthIQ.Infrastructure.IBKR -> WealthIQ.Application und WealthIQ.Domain
- WealthIQ.Cli -> WealthIQ.Application, WealthIQ.Domain, WealthIQ.Infrastructure.IBKR
- WealthIQ.Tests -> WealthIQ.Domain, WealthIQ.Application
3) Domain v1 modellieren (nur Kern)
- Value Objects:
  - Money(decimal Amount, string Currency)
  - AccountId, InstrumentId, LotId
- Events:
  - PortfolioEvent (base)
  - ExecutedTradeEvent
  - CashIncomeEvent
- Positions/Matching:
  - OpenLot
  - LotConsumption
  - TradeMatchResult
  - ILotMatcher
  - LotMatchingPolicy mit Fifo
4) FIFO-Matcher implementieren (v1-Regeln)
- Gegenrichtung schließen (Sell schließt Long, Buy schließt Short).
- Teilmengen schließen können.
- Restmenge als neues OpenLot anlegen.
- Realized-Datum: TradeDate.
- PnL pro Consumption:
  - Long: Gross = CloseNotional - OpenNotional
  - Short: Gross = OpenNotional - CloseNotional
  - Zusätzlich Net mit allokierten Fees/Taxes (pro-rata nach Quantity).
5) Tests (mind. 8)
- FullClose_Long_ProducesSingleConsumption
- PartialClose_Long_LeavesRemainingLot
- CloseAcrossMultipleLots_RespectsFifo
- OverClose_Long_OpensShortRemainder
- ShortCover_ProfitCase
- ShortCover_LossCase
- Allocation_ProRata_FeesTaxes
- Invariant_NoNegativeRemainingQuantity
6) CLI Minimalfunktion
- Implementiere zuerst nur:
  - report positions --open
- Für v1 mit In-Memory Seeddaten (kein XML, kein DB).
7) Phase-ADR nutzen
- Erzeuge z. B. docs/adr/phase-00-foundation.md aus PHASE_ADR_TEMPLATE.md.
- Fülle mindestens: Context, Decision, Consequences, Validation.
---
Wenn du fertig bist, schick mir bitte:
- die Projektstruktur (kurz als Baum),
- die Signaturen von OpenLot, LotConsumption, ILotMatcher,
- und 2–3 exemplarische Tests.
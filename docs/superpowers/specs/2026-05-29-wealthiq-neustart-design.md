# WealthIQ — Neustart-Design (v1)

- **Datum:** 2026-05-29
- **Status:** Akzeptiert (Design), bereit für Implementierungsplan
- **Kontext:** Neustart eines bestehenden Hobby-Projekts. Doku-Struktur und teils Architektur/Tech-Stack werden neu aufgesetzt. Die alte Dokumentation liegt als Diskussionsgrundlage unter `docs_old/` (nicht löschen). Das frühere „Sigmatic"-Projekt unter `data/old_project/` ist reine Referenz.

---

## 1. Produkt-Kontext

WealthIQ ist ein **persönliches Vermögensverwaltungs-Tool für genau einen Nutzer** (kein Multi-User-SaaS), lokal betrieben. Ziel ist, manuelle Arbeit aus Google Sheets & Co. zu ersetzen.

**Prioritäten (unverändert aus der Vision):**
1. Verlässliches Tracking von Aktivität und offenen Positionen
2. Verlässlicher deutscher Steuerreport für eine Privatperson (Finanzamt-tauglich als Grundlage)
3. Reduktion manueller Arbeit

**Ausdrücklich Langfrist (nicht v1):** Portfolio-Valuation/Dashboard-Charts, professionelle PDF-Reports, Strategie-Engine, Backtesting, Broker-API-Anbindung.

Dies ist **kein Lern-/Capstone-Projekt** mehr. Entscheidungen optimieren auf das beste Werkzeug und Korrektheit, nicht auf Lernpfad.

---

## 2. Ziele & Nicht-Ziele (v1)

**v1 liefert:** Import eines Broker-Statements (IBKR XML) → kanonischer Ledger in SQLite → deutscher Jahres-Steuerreport, sichtbar im lokalen Web-Dashboard, mit Drill-down zur Quelle (Audit).

**Nicht-Ziele v1:** Portfolio-Wert/Charts, PDF-Export, weitere Broker (Tastytrade CSV, Trader's Place PDF), Strategien, Backtesting, Multi-Basiswährung.

---

## 3. Entscheidungs-Log (festgezurrt)

| Thema | Entscheidung | Begründung |
|---|---|---|
| Neuanfang-Tiefe | Clean-Slate, Tech-Stack offen evaluiert | Nutzer steht früh, will bewusst neu entscheiden |
| Backend-Sprache/Plattform | **C# / .NET** (ASP.NET Core) | Statisch typisiert, performant, echte Concurrency (kein GIL) — wichtig für späteres Backtesting/Strategie-Compute; vorhandene korrekte Steuerlogik wiederverwendbar |
| Oberfläche | **Lokales Web-Dashboard: Blazor Server + MudBlazor** | Eine Sprache durchgängig, geteilte Typen, direkte Wiederverwendung der Domänen-/Steuerlogik, kein API-Vertrag; Blazor Server auf localhost faktisch latenzfrei. Charts bei Bedarf via JS-Interop (ECharts/ApexCharts) |
| Persistenz | **SQLite via EF Core**; Roh-Broker-Dateien zusätzlich als Audit-Quelle auf Disk | Abfragbar fürs Dashboard, Single-File, kein Server; Rohdateien bleiben unveränderliche Wahrheit |
| v1-Umfang | **Steuerreport zuerst** | Löst den schmerzhaften jährlichen Aufwand; baut auf vorhandener Steuerlogik |
| Re-Import | **Dedup über Transaktions-ID** (Provenance) | Überlappende/doppelte Importe gefahrlos; schützt Korrektheit |
| Referenzdaten | **In SQLite seeden** (aus mitgelieferten Dateien), später UI-editierbar | Eine Quelle, ein Backup-Artefakt |
| FX | **Speichern in Ursprungswährung; Umrechnung nur bei Replay/Akkumulation mit Ereigniszeit-Kurs** | Korrektheit von Reporting/Steuer; siehe §7 |

---

## 4. Architektur & Projektstruktur

Layered / Clean Architecture mit strikter Abhängigkeit nach innen. Die Domäne kennt weder DB noch Broker noch UI.

```
WealthIQ.Web            (Blazor Server + MudBlazor; Delivery-Host & Composition Root)
   ↓ nutzt
WealthIQ.Application    (Use-Cases & Ports: Import-Pipeline, Ledger-Replay, FIFO-Matcher,
   ↓ nutzt              German-Tax-Calculator, FX-Konvertierung)
WealthIQ.Domain         (kanonischer Ledger, Value Objects, Lots, Tax-Ergebnistypen, Invarianten)
   ↑ implementiert Ports
WealthIQ.Infrastructure (EF Core + SQLite, Broker-Importer, Referenz-/FX-/Preis-Adapter)
```

**Projekte:**
- `WealthIQ.Domain` (Class Library) — reiner Kern, keine IO/EF.
- `WealthIQ.Application` (Class Library) — hängt nur von Domain ab; definiert Ports (Interfaces).
- `WealthIQ.Infrastructure` (Class Library) — implementiert die Ports.
- `WealthIQ.Web` (Blazor Server App) — Composition Root, verdrahtet alles per DI.
- `WealthIQ.Tests` (xUnit).
- (Optional später: `WealthIQ.Cli` für skriptbare Importe.)

**Abhängigkeitsrichtung:** `Domain ← Application ← Infrastructure`; `Web → Application, Domain, Infrastructure`. **Nur** `Web` referenziert `Infrastructure` (Composition Root) — Persistenz leakt nicht in Application/Domain.

**Ports/Adapter-Prinzip:** Application definiert Interfaces (Persistenz, Broker-Import, Markt-/Referenzdaten); Infrastructure implementiert sie.

**Umgang mit vorhandenem Code:**
- **Portieren & verfeinern:** Domain + Application (kanonischer Ledger, FIFO, GermanTaxCalculator, FX) sind bereits korrekt → übernehmen, nicht neu schreiben.
- **Neu bauen:** Infrastructure (SQLite-Persistenz) + Web (Dashboard).
- **Ablösen:** CLI als Haupt-Host entfällt.

---

## 5. Domänenkern: kanonischer Ledger & Modell

**Leitprinzip:** Der Ledger speichert Quellwährungs-Fakten als unveränderliche Wahrheit. Positionen, Realisierungen und Steuer werden per **Replay** daraus rekonstruiert — nie umgekehrt.

**Value Objects:** `Money` (decimal Amount + Currency, erzwingt gleiche-Währung-Arithmetik), `Quantity`, `ISIN`, `AccountId`, `InstrumentId`, `EntryId`. Bewusste Trennung `DateTimeOffset` (`OccurredAt`) vs. `DateOnly` (`EffectiveDate`). `decimal` für Geld/Mengen, nie `double`.

**Kanonischer Ledger:** `PortfolioLedger` = geordnete, unveränderliche `PortfolioEntry`-Menge + Instrument-Katalog + Accounts + Diagnostics.

**`PortfolioEntry`** (gemeinsam: `EntryId, AccountId, OccurredAt, EffectiveDate, SourceProvenance, Category`), vier Familien:
- **TradeEntry** — InstrumentId, Side, Quantity, UnitPrice, Fees, Taxes
- **CashEntry** — CashFlowType (Dividend/Interest/WithholdingTax/…), GrossAmount, Fees, Taxes, optional RelatedInstrument
- **PositionAdjustmentEntry** — Splits/Merger/Korrektur (QuantityDelta, AmountDelta?)
- **AssetTransferEntry** — Transfer ohne Disposal (verhindert falschen PnL/Steuerfall)

Beträge bleiben in **Quellwährung** — keine eingebettete EUR-Umrechnung im Entry.

**Realisierung:** `OpenLot` (RemainingQuantity, RemainingCost, AccumulatedVorabpauschale, Provenance), `LotConsumption` (CostBasis, Proceeds, RealizedPnL), `TradeMatchResult` (Consumptions + aktualisierte Lots + Rest-Lot).

**Instrument:** stabile Identität (ISIN) + mutable Enrichment (AssetClass *erweiterbar*, Symbol, TradingCurrency, Teilfreistellungsquote, Marktdaten-Key).

**Steuer-Ergebnis:** `GermanTaxEntry` (Sell/Dividend/Interest/WithholdingTax/Vorabpauschale).

**Provenance:** Broker, Format, Datei, Transaktions-Referenz, Zeile/Record → volle Audit-Rückverfolgbarkeit (Dashboard-Drill-down).

> Konkrete Typen dürfen sich bei der Implementierung verschieben, wenn sich Funktionalität einfacher/besser abbilden lässt. Verbindlich ist das Leitprinzip (Quellwährung speichern, Replay) und die FX-Regel (§7).

---

## 6. Datenfluss v1 (Steuerreport) & Persistenz

**Pipeline:**
1. **Ingest** — Datei im Dashboard wählen/ablegen → Rohdatei wird in den Daten-Ordner kopiert (unveränderliche Audit-Quelle).
2. **Import** — Broker-Importer (IBKR XML) parst → kanonische `PortfolioEntry`s + Diagnostics + Provenance. Fail-fast (§8).
3. **Persist** — Entries + Instruments + Accounts + Diagnostics → SQLite (EF Core). Idempotent über Provenance-Transaktions-ID (kein Doppel-Import). Transaktional pro Batch.
4. **Replay & Compute** — Ledger aus DB laden → FIFO-Matching → German-Tax-Calc mit FX (Ereigniszeit) + Referenzdaten (Basiszins, Jahresendpreise, Teilfreistellung) → jährliche `GermanTaxEntry`s.
5. **Present** — Blazor-Dashboard: Jahres-Steuerreport + Drill-down zur Quelle. (PDF später.)

**Persistenz-Modell:**
- **Disk:** Roh-Broker-Dateien (Audit-Quelle, via Provenance referenziert).
- **SQLite:** Imports/Batches · PortfolioEntries (Ledger = Wahrheit in DB) · Instruments · Accounts · Diagnostics · Referenzdaten-Tabellen.
- **Steuerergebnisse:** on-demand aus dem Ledger neu berechnet (kein verfrühtes Caching).
- **Referenzdaten:** beim ersten Start aus mitgelieferten Dateien in SQLite geseedet, später per UI editierbar. Die vorhandenen Python-Download-Skripte bleiben reine Daten-Vorbereitung.

---

## 7. FX-Regel (verbindlich)

- Alles wird in **Ursprungswährung** gespeichert.
- Umrechnung in die Basiswährung (EUR) passiert **ausschließlich beim Replay/Akkumulieren**.
- Es wird der Kurs **zum Zeitpunkt des Ereignisses** verwendet (Trade-/Buchungs-/Anschaffungsdatum) — **nicht** der Kurs zum Akkumulationszeitpunkt.
- Mark-to-Market einer offenen Position an Stichtag D nutzt Preis **und** FX von Tag D (der „Ereigniszeitpunkt" der Bewertung selbst).
- **Fehlender benötigter Kurs = blockierender Fehler**, kein stiller Fallback. (Datumshandling: `ExactDate`, optional `NextAvailableOnOrAfter` für gesetzliche Stichtage an Nicht-Handelstagen.)

---

## 8. Fehlerbehandlung / Fail-Fast

Nicht-verhandelbar: Fehler und fehlende Daten scheitern laut, nichts wird still toleriert.

- **Import-Grenze:** Jeder Quell-Datensatz wird entweder zu einem kanonischen Entry (alle Pflichtfelder) **oder** erzeugt eine **blockierende** Diagnostic. Keine stillen Drops. Nicht-blockierende Fälle (z. B. außer-Scope Asset-Klasse) → Warning, Import läuft weiter. **Alle** Diagnostics werden gesammelt (kein Abbruch beim ersten Fehler); gibt es danach ≥1 blockierende → Import wird abgebrochen. **Transaktional pro Batch** → bei Abbruch nichts persistiert (Rollback).
- **Berechnung:** Fehlt ein benötigter Referenz-/FX-/Preiswert → Fehler an den Nutzer, kein stilles 0/Fallback.
- **Domänen-Invarianten:** Spezifische Exceptions (Währungs-Mismatch, negative Rest-Menge …), Guard-Clauses bei Konstruktion der Value Objects.
- **Results vs. Exceptions:** Erwartbare fachliche Ergebnisse (Import mit Diagnostics) als strukturierte Result-Objekte; Invarianten-Verletzungen werfen. Keine verschluckten Exceptions; Meldungen mit Bezeichner/Kontext.
- **Web-Schicht:** Stellt Fehler klar dar (Datei/Datensatz/fehlender Kurs).
- **Diagnostics-Modell:** strukturiert — Severity (Info/Warning/Error/Fatal), Code, Message, Provenance/Kontext; im Dashboard mit Drill-down.

---

## 9. Dashboard / UI (v1)

Bewusst schlank, MudBlazor-Komponenten. Das Design wird in späteren Iterationen ansprechender gestaltet und besser ins Gesamttool integriert.

**Seite „Steuerreport"** (Hauptseite): Jahres-Auswahl; Jahres-Zusammenfassung (Verkäufe netto, Dividenden, Zinsen, Quellensteuer, Vorabpauschale, geschätzte Steuer — in EUR); aufklappbare Sektionen mit DataGrids (Verkäufe/FIFO realisierter PnL, Vorabpauschale, Dividenden, Zinsen, Quellensteuer); Zeile → Drill-down zur Quelle (Provenance).

**Seite „Import":** Broker (IBKR) + Account wählen, Datei per Drag&Drop/Auswahl, Import starten → Zusammenfassung (Entries nach Typ) + Diagnostics-Liste.

**Seite „Diagnostics / Audit":** Liste aller Diagnostics, Filter nach Severity/Import, je Eintrag Kontext + Provenance.

---

## 10. Testing-Strategie

TDD für neue Logik. Tests deterministisch (kein Live-Netzwerk, keine Echtzeit, feste Referenzdaten).

- **Domain (Unit):** Money/Quantity-Invarianten, Lot-Consumption (pro-rata Kosten/Vorabpauschale), FIFO-Reihenfolge, Teil-/Über-Schließung (Rest-Lot Gegenrichtung), Long-/Short-PnL.
- **Application (Unit):** German-Tax-Calc (Dividenden, Zinsen, Quellensteuer, Vorabpauschale inkl. Abzug zuvor versteuerter Vorabpauschale beim Verkauf, Teilfreistellung); FX-Konvertierung (ExactDate/NextAvailableOnOrAfter, Ereigniszeit-Regel); Replay-Korrektheit.
- **Infrastructure / Integration:** IBKR-Importer gegen echte Sample-XML (Golden-Files aus `data/old_project`); Idempotenz/Dedup über Transaktions-ID; EF-Core-Round-Trip + Transaktions-Rollback bei blockierendem Fehler; Referenzdaten-Seeding.
- **End-to-End-Regression:** Sample → Import → Persist → Steuerreport; Jahres-Summen gegen bekannte Soll-Werte (Golden-Baseline aus bestehendem Regressionstest).
- **Web/UI:** UI dünn (Logik in Application); v1 manuelle Verifikation; bUnit für kritische Komponenten optional später.

---

## 11. Offene Punkte / später

- Weitere Broker: Tastytrade CSV, Trader's Place PDF (Import-Architektur ist dafür über Ports/Adapter vorbereitet).
- Portfolio-Valuation + Charts im Dashboard (Marktdaten, Yahoo Finance), professionelle PDF-Reports.
- Strategie-Engine & Backtesting (CPU-Compute — Grund für die .NET-Wahl).
- Multi-Basiswährung.
- Genauer Umfang „verbindlicher" Steuerregeln vor fachlicher Steuer-Review.
- `CLAUDE.md` wird nach diesem Design erstellt (ersetzt `AGENTS.md`).

---

## 12. Referenzen

- Alte Doku (Diskussionsgrundlage, nicht löschen): `docs_old/`
- Referenz-Altprojekt: `data/old_project/` (Sigmatic)
- Daten-Vorbereitungs-Skripte: `scripts/`

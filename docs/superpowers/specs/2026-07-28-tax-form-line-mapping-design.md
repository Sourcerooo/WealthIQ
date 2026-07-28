# Steuerformular-Zuordnung im Steuerreport (Anlage KAP / KAP-INV)

**Datum:** 2026-07-28
**Status:** Design, freigegeben
**Betrifft:** `WealthIQ.Domain`, `WealthIQ.Application`, `WealthIQ.Infrastructure`, `WealthIQ.Web`

## 1. Problem

Der Steuerreport aggregiert zu stark, um daraus die Anlage KAP / KAP-INV auszufüllen. Drei
Defekte, alle im Aggregationsblock von `TaxReportPrint.razor`:

1. **Falsche Formularziele.** Verkaufsergebnisse und Dividenden aus ETFs stehen unter
   „Anlage KAP Zeile 19". ETFs sind Investmentfonds im Sinne des InvStG; ihre Erträge gehören in
   die **Anlage KAP-INV**, aufgeteilt nach Fondsart. Anlage KAP Zeile 19 bleibt den Nicht-Fonds
   vorbehalten (Einzelaktien, ETCs, Anleihen) sowie den Zinsen.
2. **Falsche Bemessungsbasis.** Der Report zeigt Beträge *nach* Teilfreistellung. Die Anlage
   KAP-INV verlangt durchgängig Beträge *vor* Teilfreistellung — ELSTER rechnet die
   Teilfreistellung selbst. Wer den gekürzten Betrag einträgt, wird doppelt gekürzt.
3. **Fehlende Aufgliederung.** Die Vorabpauschale steht als eine Zahl unter „KAP-INV Zeile 9",
   muss aber je nach Fondsart auf die Zeilen 9–13 verteilt werden.

Die Rohdaten liegen intern vollständig vor (`GermanTaxEntry.RawAmount`, `.UsedVorabpauschale`).
Es fehlt allein die **Fondsart-Klassifikation** je Instrument und die Aufbereitung im Report.

### Geprüfte Zeilennummern (Formularstand VZ 2025)

| Report bisher | Bewertung |
|---|---|
| KAP Z19 „Veräußerung von Wertpapieren (nach TF)" | falsch für Fonds → KAP-INV Z14/17/20/23/26, vor TF |
| KAP Z19 „Dividenden (nach TF)" | falsch für Fonds → KAP-INV Z4–8, vor TF |
| KAP Z19 „Zinserträge" | richtig (Z19 = Summenzeile ausländischer Kapitalerträge) |
| KAP Z41 „anrechenbare ausländische Quellensteuer" | richtig |
| KAP Z37 „einbehaltene deutsche KESt" | richtig (Z37–42 = Steuerabzugsbeträge) |
| KAP-INV Z9 „Vorabpauschalen (nach TF)" | doppelt falsch: Aufteilung Z9–13 fehlt, Betrag muss brutto sein |

Nicht abgebildet und zu ergänzen: KAP Z20 (darin Aktienveräußerungsgewinne), Z22/Z23
(Verlusttöpfe), KAP-INV Z15/16 (bestandsgeschützte Alt-Anteile, fiktive Veräußerung), Z29
(Zwischengewinne).

## 2. Domain: `TaxAssetClass`

Neues Enum `src/WealthIQ.Domain/Enumeration/TaxAssetClass.cs`. Es bildet genau die Assetklassen
ab, nach denen die Formulare trennen (KAP-INV Zeile 48 nennt sie „Art des Investmentfonds
(Assetklasse)").

| Wert | JSON-Schlüssel | Bedeutung | TF | Formularziel |
|---|---|---|---|---|
| `Share` | `share` | Einzelaktie (§ 20 Abs. 2 Satz 1 Nr. 1 EStG) | 0 % | KAP Z19 **+ Z20** |
| `OtherSecurity` | `other_security` | ETC, Anleihe, Zertifikat — kein Fonds | 0 % | KAP Z19 |
| `EquityFund` | `equity_fund` | Aktienfonds | 30 % | KAP-INV Z4 / Z9 / Z14 |
| `MixedFund` | `mixed_fund` | Mischfonds | 15 % | Z5 / Z10 / Z17 |
| `RealEstateFund` | `real_estate_fund` | Immobilienfonds | 60 % | Z6 / Z11 / Z20 |
| `ForeignRealEstateFund` | `foreign_real_estate_fund` | Auslands-Immobilienfonds | 80 % | Z7 / Z12 / Z23 |
| `OtherFund` | `other_fund` | sonstiger Investmentfonds | 0 % | Z8 / Z13 / Z26 |

`Instrument` erhält `TaxAssetClass? TaxAssetClass { get; init; }` — init-only wie `Type` und
`SubjectToVorabpauschale`, `null` = noch nicht angereichert.

`GermanTaxEntry` erhält additiv `TaxAssetClass? TaxAssetClass = null` und
`string InstrumentName = ""` (Formular Z47 verlangt die Fondsbezeichnung). Beides sind reine
Zuordnungs-/Anzeigefelder nach dem Vorbild von `Origin`, `Fees` und `OpenedOn`; **die
Steuermathematik in `GermanTaxCalculator` bleibt unangetastet**. Befüllt werden sie dort, wo die
Entries entstehen (Sell, Dividend, Vorabpauschale, WithholdingTax).

Die Teilfreistellungsquote wird **nicht** aus der Assetklasse abgeleitet. `Teilfreistellungsquote`
bleibt das steuerwirksame Feld, `TaxAssetClass` ist ausschließlich Formularzuordnung. Die Tabelle
oben nennt die typische Quote nur zur Orientierung.

**Fail-fast:** Trifft der Formular-Builder auf einen Sell-, Dividend- oder
Vorabpauschale-Eintrag mit `TaxAssetClass is null`, wirft er eine `InvalidOperationException`
mit ISIN und Handlungsanweisung — dieselbe Logik wie beim fehlenden `SubjectToVorabpauschale`.

## 3. Application: `TaxFormReport`

Neu unter `src/WealthIQ.Application/Tax/Report/Forms/`:

```
TaxFormLine(string Form, string Line, string Caption, decimal Amount, string Nachweis, bool Muted)
TaxFormSection(string Title, string? Note, IReadOnlyList<TaxFormLine> Lines)
TaxFormReport(int Year, bool DomesticWithholding, IReadOnlyList<TaxFormSection> Sections)
TaxFormReportBuilder   // AnnualTaxReport -> TaxFormReport
```

`Muted` markiert Zeilen, die strukturell zum Formular gehören, bei denen WealthIQ aber immer 0
liefert; sie werden grau mit Fußnote gerendert, damit beim Abtippen kein Feld übersehen wird.

Die Zeilennummern liegen als Konstanten in `TaxFormLines` (Formularstand VZ 2025). Über jedem
Block steht sichtbar: *„Formularstand VZ 2025 — Zeilennummern älterer Jahrgänge weichen ab."*
Die Beträge sind jahrgangsunabhängig korrekt, nur die Beschriftung ist auf 2025 geeicht.

### 3.1 Zuordnungsregeln

Alle KAP-INV-Beträge sind **`RawAmount`**, also vor Teilfreistellung.

**KAP-INV — Erträge aus Investmentanteilen (Z4–8)**
Σ `RawAmount` der Dividend-Entries je Fondsklasse.

**KAP-INV — Vorabpauschalen (Z9–13)**
Σ `RawAmount` der Vorabpauschale-Entries je Fondsklasse.

**KAP-INV — Erträge aus dem Verkauf (Z14/17/20/23/26)**
Σ `RawAmount` der Sell-Entries je Fondsklasse. `RawAmount` hat die nach § 19 InvStG verrechnete
Vorabpauschale bereits abgezogen — genau das verlangt das Formular.

- **Z15/18/21/24/27** (Gewinne aus bestandsgeschützten Alt-Anteilen): berechnet aus
  `OpenedOn.Year < 2009`. Normalerweise 0 und grau. Ist der Wert ≠ 0, erscheint ein Warnhinweis,
  dass WealthIQ die 100.000-€-Freibetragsregel für bestandsgeschützte Alt-Anteile nicht
  modelliert und der Wert manuell zu prüfen ist.
- **Z16/19/22/25/28** (fiktive Veräußerung zum 31.12.2017): 0, grau, Fußnote „nicht modelliert".
- **Z29** (Zwischengewinne nach InvStG 2004): 0, grau, Fußnote „nicht modelliert".

**Anlage KAP — Nicht-Fonds und Zinsen**

| Zeile | Inhalt |
|---|---|
| Z19 | Σ Zinsen + Σ `RawAmount` der Sell-/Dividend-Entries mit `Share` oder `OtherSecurity` |
| Z20 | darin enthaltene positive Veräußerungsgewinne aus `Share` |
| Z22 | darin enthaltene Verluste ohne Aktienveräußerungen (Topf 2), als positiver Betrag |
| Z23 | darin enthaltene Verluste aus Aktienveräußerungen (Topf 1), als positiver Betrag |
| Z37 | einbehaltene deutsche Kapitalertragsteuer (`WithheldKESt`) |
| Z41 | anrechenbare, noch nicht angerechnete ausländische Steuern (`ForeignWithholdingTax`) |
| Z42 | fiktive ausländische Steuern — 0, grau |

### 3.2 Konten mit inländischem Steuerabzug

Gilt ein Konto-Jahr als „inländischer Steuerabzug" (`Summary.WithheldKESt > 0`, praktisch
Trader's Place), entfällt der KAP-INV-Block. Stattdessen erscheint:

| Zeile | Inhalt |
|---|---|
| Z7 | Kapitalerträge, die dem inländischen Steuerabzug unterlegen haben (Bemessungsgrundlage nach TF) |
| Z8 | darin enthaltene Gewinne aus Aktienveräußerungen |
| Z37–39 | Kapitalertragsteuer / Solidaritätszuschlag / Kirchensteuer |

mit dem Hinweis: *„Maßgeblich ist die Steuerbescheinigung des Brokers. Die folgenden Zahlen
dienen der Kontrolle."* Soli und Kirchensteuer werden heute nicht erfasst und bleiben grau.

Die Heuristik ist bewusst einfach. Ein deutsches Konto ohne steuerpflichtige Erträge im Jahr
hätte `WithheldKESt = 0` und würde fälschlich als Auslandskonto behandelt; der Block ist dann
aber leer und richtet keinen Schaden an. Ein explizites Konto-Flag ist die Fallback-Option, falls
sich das in der Praxis als störend erweist.

## 4. Einzelnachweise werden zur Ermittlung

Die Nachweise A, B und D bekommen eine **Fondsart-Spalte** und **Zwischensummen je Fondsart**.
Damit sind sie zugleich die Ermittlungsseiten des Formulars:

- **Nachweis A (Veräußerungen)** → „zugleich Ermittlung KAP-INV Z46–56". Die Spalten
  Stück / Kauf gesamt / Verkauf gesamt / Kosten / Roh-G/V / darin Vorabp. entsprechen Z49–55.
- **Nachweis B (Dividenden)** → Ermittlung zu Z4–8.
- **Nachweis D (Vorabpauschalen)** → „zugleich Ermittlung KAP-INV Z30–45". Kurs 01.01. /
  Basiszins / Ausschüttung je Anteil / Monatsanteil sind exakt die Rechenschritte des Formulars.

**Geänderte Konvention.** Bisher galt: in Nachweis A nur die steuerpflichtige Spalte summieren,
weil eine Roh-G/V-Summe über verschiedene Teilfreistellungsquoten hinweg als Gleichung nicht
aufgeht. Das gilt weiterhin für die **Gesamtsumme**. Innerhalb einer Fondsart ist die Quote
jedoch einheitlich — dort ist die Roh-G/V-Zwischensumme korrekt und genau der Wert, der ins
Formular gehört. Also: Zwischensummen je Fondsart über Roh-G/V *und* steuerpflichtig,
Gesamtsumme weiterhin nur steuerpflichtig. `CLAUDE.md` wird entsprechend angepasst.

## 5. Web

`Components/Shared/TaxFormBlock.razor` rendert einen `TaxFormReport`. Bewusst schlichtes
semantisches HTML ohne MudBlazor, weil `PrintLayout` keine MudBlazor-Chrome lädt. CSS-Klassen
`.wiq-form-*` einmal in `wwwroot/wealthiq.css` (Bildschirm) und einmal in
`wwwroot/steuerreport-print.css` (Papier).

Eingebunden an zwei Stellen:

- `Components/Pages/TaxReportPrint.razor` — ersetzt den heutigen Aggregationsblock.
- `Components/Pages/Steuerreport.razor` — neuer Abschnitt „Eingabehilfe Steuerformulare",
  unterhalb der KPI-Karten.

Der Ergebnisblock („Ermittlung der geschätzten Steuer") bleibt unverändert erhalten; er ist
Orientierung, kein Formularabbild.

## 6. Stammdaten, Migration und die ETC-Korrektur

### 6.1 Referenzdaten

`tax_asset_class` wird ergänzt in `data/reference/instruments.json`,
`data/test/configuration/instruments.json` und der Trader's-Place-Fixture-Konfiguration.
`InstrumentProfileRow`, `InstrumentAdminModels`, `ReferenceDataSeeder`,
`DbInstrumentReferenceAdmin`, `DbInstrumentProfileEnricher` und `JsonInstrumentProfileEnricher`
werden um das Feld erweitert. `InstrumentsAdmin.razor` bekommt ein `MudSelect` für die Fondsart.

### 6.2 Migration `AddTaxAssetClass`

Der `ReferenceDataSeeder` befüllt `InstrumentProfiles` nur, wenn die Tabelle leer ist
(`ReferenceDataSeeder.cs:32`). Eine Änderung an `instruments.json` erreicht bestehende
Datenbanken also nicht — die Korrektur muss die Migration tragen.

Backfill aus dem vorhandenen `Type`:

| `Type` | `TaxAssetClass` | `SubjectToVorabpauschale` |
|---|---|---|
| `ETF_EQUITY` | `equity_fund` | unverändert |
| `ETF_BOND`, `ETF_MONEY_MARKET` | `other_fund` | unverändert |
| `STOCK` | `share` | unverändert |
| `ETC` | `other_security` | **auf `false` gesetzt** |
| sonst | `NULL` (in der UI zu pflegen) | unverändert |

Das ist eine einmalige Datenmigration, keine Laufzeit-Inferenz; die Fail-fast-Regel „explizites
Profil, keine Ableitung" bleibt zur Laufzeit bestehen.

### 6.3 Warum ETCs auf `SubjectToVorabpauschale = false` gehören

Ein ETC ist eine besicherte Schuldverschreibung mit beschränktem Rückgriffsrecht, kein
Investmentanteil. Das InvStG greift nicht: **keine Vorabpauschale, keine Teilfreistellung.**
BlackRock stellt das für die iShares-Physical-Reihe ausdrücklich klar.

Betroffen ist `IE00B4ND3602` (iShares Physical Gold ETC), bisher fälschlich
`subject_to_vorabpauschale: true`. `DE000A0S9GB0` (Xetra-Gold) steht bereits korrekt auf `false`.

Die Veräußerungsbesteuerung ändert sich für **keines** der beiden Papiere:

- `IE00B4ND3602` gewährt Privatanlegern keinen Anspruch auf physische Auslieferung. Gewinne sind
  daher nach § 20 Abs. 2 EStG unabhängig von der Haltedauer voll abgeltungsteuerpflichtig — die
  heutige Behandlung als gewöhnlicher Gewinn ist bereits richtig.
- `DE000A0S9GB0` gewährt einen Lieferanspruch und fällt damit unter § 23 EStG (nach 12 Monaten
  steuerfrei). Das bleibt die in `CLAUDE.md` dokumentierte, bewusst nicht modellierte
  Einschränkung.

### 6.4 Erwartete Auswirkung auf die Steuerzahlen

`IE00B4ND3602` in den Golden-Fixtures: Kauf 512 (2021-07-01), 400 (2021-12-09), 416 (2022-02-17),
Verkauf 498 (2022-03-22), Restverkauf 830 (2024-06-28).

- **2021 / 2022** — Basiszins negativ (−0,45 % / −0,05 %), es entstand ohnehin keine
  Vorabpauschale. Keine Änderung.
- **2023 → gebucht 01.01.2024** — Basiszins 2,55 %, 830 Stück über den Jahreswechsel gehalten.
  Diese Vorabpauschale **entfällt**.
- **Verkauf 28.06.2024** — `UsedVorabpauschale` fällt auf 0, der Roh-G/V steigt um denselben
  Betrag.
- **2025** — Position geschlossen, Basiszins negativ. Keine Änderung.

Die Teilfreistellungsquote des ETC ist 0 %, beide Effekte wirken also 1:1 gegeneinander und
fallen in dasselbe Jahr. Die **Bemessungsgrundlage 2024 bleibt daher voraussichtlich unverändert**;
es verschiebt sich nur die Aufteilung zwischen „Vorabpauschale" und „Verkäufe". Das ist zu
verifizieren, nicht anzunehmen — der Regressionstest liefert die tatsächlichen Werte.

## 7. Tests

- `TaxFormReportBuilderTests` — Kategorie-Routing je Entry-Typ, Bruttobeträge (nicht `TaxableAmount`),
  Verlustaufteilung Z22/Z23, Z20 nur für `Share`, fehlende Klassifikation → wirft,
  Route für Konten mit inländischem Steuerabzug, Alt-Anteil-Warnung bei `OpenedOn.Year < 2009`.
- `AnnualTaxReportServiceTests` — erweitert um die neuen Felder.
- Formular-Assertion auf dem 2024-IBKR-Golden-Fixture.
- `ReferenceDataSeederTests`, `DbInstrumentReferenceAdminTests` — neues Feld.
- Migrationstest: Backfill setzt ETC auf `other_security` + `SubjectToVorabpauschale = false`.

**`GermanTaxRegressionTests` benötigt ein bewusstes Baseline-Update.** Der Test prüft die
IGLN-Einträge (= `IE00B4ND3602`) namentlich, sowohl die drei FIFO-Konsumptionen des Verkaufs vom
Juni 2024 als auch die Vorabpauschale-Einträge. Die neuen Erwartungswerte werden aus dem
tatsächlichen Lauf übernommen und im Test-Kommentar mit Verweis auf Abschnitt 6.3 begründet.
`TradersPlaceRegressionTests` ist nicht betroffen (keine ETCs in den Fixtures).

## 8. Nicht in diesem Umfang

- Verlustverrechnungstöpfe und Verlustvortrag über Jahresgrenzen (bestehende, dokumentierte
  Einschränkung). Z22/Z23 weisen die Verluste nur *aus*, verrechnen sie nicht.
- Solidaritätszuschlag und Kirchensteuer als eigene erfasste Größen (KAP Z38/Z39).
- Bestandsgeschützte Alt-Anteile inklusive 100.000-€-Freibetrag.
- Fiktive Veräußerung zum 31.12.2017 und Zwischengewinne nach InvStG 2004.
- § 23-EStG-Behandlung von Xetra-Gold.
- Zeilenschemata für Veranlagungszeiträume vor 2025.

## Quellen

- Haufe, *Einkünfte aus Kapitalvermögen 12.2.8 — Anlage KAP Zeilen 37–42*
- Haufe, *Anlage KAP 2025, 17 — Ermittlung der Vorabpauschalen, KAP-INV Zeilen 30–45*
- Haufe, *Einkünfte aus Kapitalvermögen 9.2.8 — Anlage KAP-INV*
- KAPitan, *Anlage KAP / KAP-INV 2025, Zeilen erklärt*
- BlackRock / iShares, Produktinformation *iShares Physical Gold ETC*
- extraETF, *Besteuerung von Gold-ETCs*
- Screenshots der Eingabemaske des Steuertools, `data/sample/` (2026-07-28)

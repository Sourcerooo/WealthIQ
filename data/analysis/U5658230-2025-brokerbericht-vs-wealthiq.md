# Analyse 2025: Informeller Broker-Steuerbericht vs. WealthIQ

Stand: 19. Juli 2026

## 1. Zweck und Status

Dieses Dokument dokumentiert die vollständige Abstimmung zwischen:

- dem informellen Interactive-Brokers-/PwC-Steuerbericht für das Konto `U5658230` und das Steuerjahr 2025,
- den zugrunde liegenden IBKR-Flex-Query-Rohdaten für 2021 bis 2025,
- den mit dem aktuellen lokalen Datenbestand von WealthIQ berechneten Steuerwerten,
- sowie der anschließenden Recherche zur deutschen FIFO-Regel und zur Behandlung von Ausschüttungen bei der Vorabpauschale.

Der Brokerbericht ist ausdrücklich kein offizielles Steuerdokument. Er weist selbst darauf hin, dass Drittanbieterdaten und Standardbehandlungen verwendet werden und die Angaben individuell geprüft werden müssen. Auch WealthIQ ersetzt keine steuerliche Beratung. Ziel ist deshalb nicht, eine Quelle ungeprüft als richtig zu übernehmen, sondern die Abweichungen auf Einzelebene zu erklären und anhand möglichst primärer Rechtsquellen zu bewerten.

Personenbezogene Angaben aus dem PDF sind in diesem Dokument nicht wiedergegeben.

## 2. Untersuchte Quellen

### 2.1 Lokale Primärdaten

- Brokerbericht: [`data/U5658230.2025.PWC_DE.pdf`](../U5658230.2025.PWC_DE.pdf)
- Vollständiger IBKR-Export 2025: [`data/input/TaxAlpha_Raw_Data_2025_complete.xml`](../input/TaxAlpha_Raw_Data_2025_complete.xml)
- IBKR-Exporte der Vorjahre:
  - [`data/input/TaxAlpha_Raw_Data_2021.xml`](../input/TaxAlpha_Raw_Data_2021.xml)
  - [`data/input/TaxAlpha_Raw_Data_2022.xml`](../input/TaxAlpha_Raw_Data_2022.xml)
  - [`data/input/TaxAlpha_Raw_Data_2023.xml`](../input/TaxAlpha_Raw_Data_2023.xml)
  - [`data/input/TaxAlpha_Raw_Data_2024.xml`](../input/TaxAlpha_Raw_Data_2024.xml)
- WealthIQ-Instrumentprofile: [`data/reference/instruments.json`](../reference/instruments.json)
- WealthIQ-Listings: [`data/reference/listings.json`](../reference/listings.json)
- Lokale WealthIQ-Datenbank zum Analysezeitpunkt: `data/app/wealthiq.db`

### 2.2 Relevante WealthIQ-Implementierung

- Steuerberechnung und Vorabpauschale: [`src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`](../../src/WealthIQ.Application/Tax/GermanTaxCalculator.cs)
- FIFO-Matcher: [`src/WealthIQ.Application/Matcher/FiFoMatcher.cs`](../../src/WealthIQ.Application/Matcher/FiFoMatcher.cs)
- IBKR-Instrumentidentität: [`src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs`](../../src/WealthIQ.Infrastructure/Ibkr/Import/IbkrStatementImporter.cs)
- Jahresaggregation und Steuerschätzung: [`src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs`](../../src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs)
- Währungsumrechnung: [`src/WealthIQ.Application/Currency/FxConverter.cs`](../../src/WealthIQ.Application/Currency/FxConverter.cs)

### 2.3 Externe Rechts- und Fachquellen

Die vollständigen Links und ihre Bedeutung sind in Abschnitt 12 aufgeführt. Die rechtliche Kernaussage stützt sich vor allem auf:

- § 20 Abs. 4 EStG,
- § 18 und § 19 InvStG,
- das BMF-Schreiben vom 14. Mai 2025 zu Einzelfragen der Abgeltungsteuer, insbesondere Randnummern 97 und 98,
- das BMF-Anwendungsschreiben zum Investmentsteuergesetz,
- sowie die IBKR-Dokumentation zur Bedeutung von CONID, Handelswährung und Lagerstelle.

## 3. Zusammenfassung der Ergebnisse

Die großen Unterschiede haben im Wesentlichen drei Ursachen:

1. **Unterschiedliche FIFO-Pools:** WealthIQ führt Anteile derselben ISIN innerhalb eines Kontos gemeinsam nach FIFO. Der Brokerbericht trennt Positionen offenbar nach IBKR-Symbol, CONID, Handelswährung oder Lagerstelle. Das erklärt nahezu die gesamte Abweichung bei den Verkäufen.
2. **Nicht berücksichtigte VUSA-Ausschüttungen:** Der Broker setzt bei der Vorabpauschale für VUSA die Ausschüttungen des Jahres 2024 mit `0,00 EUR` an, obwohl die IBKR-Rohdaten Ausschüttungen auf den betreffenden Bestand nachweisen. Dadurch ist die Broker-Vorabpauschale für VUSA wahrscheinlich deutlich zu hoch.
3. **Unterschiedliche Fondsklassifizierung:** Der Broker ordnet alle Fonds als "Other funds" ein und verwendet keine Teilfreistellung. WealthIQ verwendet für hinterlegte Aktienfonds grundsätzlich 30 Prozent Teilfreistellung. Deshalb dürfen Broker-Rohwerte nicht unmittelbar mit den steuerpflichtigen WealthIQ-Summen verglichen werden.

Die anschließende Rechtsrecherche spricht deutlich für die WealthIQ-Grundannahme zur FIFO-Zuordnung:

> Bei identischen vertretbaren Wertpapieren gilt FIFO nach der Verwaltungsauffassung pro Depot. Eine andere Börse, Handelswährung, IBKR-CONID oder technische Lagerstelle begründet für sich genommen kein eigenes Depot. Nur ein tatsächliches Depot oder nummeriertes Unterdepot bildet einen separaten FIFO-Pool.

Diese Schlussfolgerung gilt unter der Voraussetzung, dass die Positionen tatsächlich im selben rechtlichen Depot `U5658230` lagen und keine eigenständigen nummerierten Unterdepots bestanden.

## 4. Vergleichsbasis: Rohbetrag oder steuerpflichtiger Betrag

Der Brokerbericht weist die Beträge in den KAP-INV-Zeilen ausdrücklich **vor Teilfreistellung** aus. Die direkt vergleichbare WealthIQ-Größe ist daher `RawAmount`, nicht `TaxableAmount` und nicht die aggregierte Steuerschätzung des Dashboards.

Mit dem lokalen WealthIQ-Datenbestand ergaben sich für 2025:

| Kategorie | WealthIQ roh | WealthIQ steuerpflichtig | Brokerbericht | Direkte Rohdifferenz |
|---|---:|---:|---:|---:|
| Vorabpauschale aus 2024 | 1.810,70 EUR | 1.267,49 EUR | 2.163,53 EUR | -352,83 EUR |
| Verkäufe | -4.138,40 EUR | -6.517,84 EUR | -727,13 EUR | -3.411,27 EUR |
| Ausschüttungen | 2.794,88 EUR | 2.716,17 EUR | 2.868,96 EUR | -74,08 EUR |
| Zinsen | 147,64 EUR | 147,64 EUR | 147,64 EUR | 0,00 EUR |
| Einbehaltene ausländische Steuer | 28,56 EUR | nicht Teil der Bemessungsgrundlage | 28,56 EUR | 0,00 EUR |

Die WealthIQ-Steuerschätzung ergab aus den steuerpflichtigen Beträgen eine negative Bemessungsgrundlage von `-2.386,54 EUR` und damit eine geschätzte Steuer von `0,00 EUR`. Diese Schätzung ist jedoch nicht mit einer vollständigen deutschen Steuerveranlagung gleichzusetzen, weil WealthIQ derzeit insbesondere keine Verlustvorträge und keine getrennten Verlustverrechnungstöpfe modelliert.

## 5. Vorabpauschale

### 5.1 Zeitliche Zuordnung

Der Bericht für 2025 enthält die Vorabpauschale, die für das Kalenderjahr 2024 ermittelt wurde und Anfang 2025 als zugeflossen gilt. Dass im Jahr 2025 neu erworbene Fonds nicht in dieser Tabelle erscheinen, ist daher grundsätzlich korrekt.

Broker und WealthIQ verwenden für 2024 denselben Basiszins von 2,29 Prozent. Der relevante Faktor beträgt:

```text
2,29 % x 70 % = 1,603 %
```

### 5.2 Vergleich je Fonds

| Fonds | ISIN | WealthIQ roh | Broker | Differenz |
|---|---|---:|---:|---:|
| iShares Core S&P 500 | IE00B5BMR087 | 348,38 EUR | 348,14 EUR | +0,24 EUR |
| iShares NASDAQ 100 USD Acc | IE00B53SZB19 | 1.096,73 EUR | 1.108,04 EUR | -11,31 EUR |
| Vanguard S&P 500 USD Distributing | IE00B3XXRP09 | 79,53 EUR | 418,57 EUR | -339,04 EUR |
| Xtrackers S&P 500 2x Leveraged Daily Swap | LU0411078552 | 286,07 EUR | 288,78 EUR | -2,71 EUR |
| **Gesamt** | | **1.810,70 EUR** | **2.163,53 EUR** | **-352,83 EUR** |

Fast die gesamte Abweichung entfällt auf VUSA.

### 5.3 Kleine Kursdifferenzen bei CSPX, CNDX und XS2L

| Fonds | WealthIQ-Jahresanfangswert | Broker-Jahresanfangswert |
|---|---:|---:|
| CSPX | ca. 454,346985 EUR | 454,04 EUR |
| CNDX | ca. 862,400024 EUR | 871,30 EUR |
| XS2L | ca. 145,679993 EUR | 147,06 EUR |

WealthIQ verwendet den ersten verfügbaren Marktpreis des Jahres beziehungsweise den letzten verfügbaren Marktpreis zum Jahresende und rechnet jeden Wert mit dem für das Ereignisdatum hinterlegten FX-Kurs in Euro um. Der Broker verwendet eigene, teilweise von Drittanbietern bezogene Rücknahme-, Markt- und FX-Daten. Die daraus resultierenden Abweichungen sind klein und erklären nicht das Hauptproblem.

### 5.4 VUSA: Broker setzt Ausschüttungen fälschlich auf null

Der Brokerbericht berechnet für VUSA:

```text
Jahresanfangspreis                 81,60 EUR
Faktor                             1,603 %
Basisertrag je Anteil              1,30802 EUR
Ausschüttungen 2024                0,00 EUR
Anzahl Anteile                   320
Vorabpauschale                   418,57 EUR
```

Diese Nullsetzung widerspricht den Rohdaten. Im IBKR-Export 2024 sind für VUSA unter anderem folgende Ausschüttungen enthalten:

| Datum | Betrag | Bemerkung |
|---|---:|---|
| 27.03.2024 | 118,55 USD | `392 x 0,302416 USD` |
| 27.03.2024 | 96,77 USD | `320 x 0,302416 USD` |
| 26.06.2024 | 82,96 USD | Ausschüttung nach dem Verkauf des 392er-Bestands |
| 25.09.2024 | 91,03 USD | Ausschüttung auf den verbleibenden Bestand |
| 27.12.2024 | 97,97 USD | Ausschüttung auf den verbleibenden Bestand |

Die Zahlung von `96,77 USD` entspricht bereits für sich genommen exakt der Ausschüttung auf 320 Anteile. Die Daten stehen in `data/input/TaxAlpha_Raw_Data_2024.xml`, insbesondere bei den Cash-Transaktionen um die Zeilen 31 bis 38.

WealthIQ berücksichtigt für den relevanten VUSA-Bestand Ausschüttungen von ungefähr `1,069659 EUR` je Anteil. Vereinfacht ergibt sich:

```text
Jahresanfangswert                  ca. 82,232096 EUR
x 1,603 %                          ca.  1,318 EUR Basisertrag je Anteil
- Ausschüttungen                   ca.  1,070 EUR je Anteil
= Vorabpauschale                   ca.  0,248 EUR je Anteil
x 320                              ca. 79,53 EUR
```

Nach § 18 Abs. 1 InvStG sind Ausschüttungen sowohl bei der Wertsteigerungsbegrenzung als auch beim anschließenden Vergleich mit dem Basisertrag zu berücksichtigen. Eine andere Handelswährung oder Börsennotierung beseitigt die tatsächlich gutgeschriebene Ausschüttung nicht.

**Bewertung:** Die WealthIQ-Größenordnung ist deutlich besser durch die Rohdaten und § 18 InvStG gestützt. Die Broker-Vorabpauschale von `418,57 EUR` ist mit hoher Wahrscheinlichkeit überhöht.

### 5.5 WealthIQ-Formel

Die relevante Berechnung steht in `GermanTaxCalculator.cs`, insbesondere um die Zeilen 291 bis 349:

```text
basisErtrag = yearStartValue x basisRate x 70 %
cap = max(0, yearEndValue - yearStartValue + distributionsPerShare)
cappedBasisErtrag = min(basisErtrag, cap)
vorabFull = max(0, cappedBasisErtrag - distributionsPerShare)
vorabPerShare = vorabFull x acquisitionMonthFactor
```

Die rohe Vorabpauschale wird auf dem Lot angesammelt und bei einem späteren Verkauf nach § 19 Abs. 1 InvStG vollständig vom Veräußerungsgewinn abgezogen. Die Teilfreistellung wird erst auf den steuerpflichtigen Betrag angewandt.

## 6. Verkäufe

### 6.1 Vergleich je Position

| Position | WealthIQ roh | Broker | Differenz |
|---|---:|---:|---:|
| CSPX | 516,70 EUR | 516,93 EUR | -0,23 EUR |
| CNDX | -5.435,96 EUR | -5.447,29 EUR | +11,33 EUR |
| XS2L | 1.427,57 EUR | 1.424,84 EUR | +2,73 EUR |
| XEON | 541,63 EUR | 541,62 EUR | +0,01 EUR |
| IE00BSKRJZ44, Verkauf im Mai | -446,46 EUR | -115,20 EUR | -331,26 EUR |
| IE00BSKRJZ44, Verkäufe im Oktober | -11.623,41 EUR | -11.436,15 EUR | -187,26 EUR |
| VUSA | 10.881,54 EUR | 13.788,12 EUR | -2.906,58 EUR |
| **Gesamt** | **-4.138,40 EUR** | **-727,13 EUR** | **-3.411,27 EUR** |

CSPX, CNDX, XS2L und XEON stimmen auf Rohbasis praktisch überein. Die gesamte materielle Abweichung konzentriert sich auf VUSA und den unter IDTL beziehungsweise IS04 gehandelten Treasury-ETF.

### 6.2 WealthIQ-Verkaufsformel

WealthIQ berechnet je verbrauchtem FIFO-Lot:

```text
rawProfit = EUR-Verkaufserlös
          - EUR-Anschaffungskosten
          - während der Besitzzeit angesetzte rohe Vorabpauschalen

taxableProfit = rawProfit x (1 - Teilfreistellungsquote)
```

Die Implementierung steht in `GermanTaxCalculator.cs` um die Zeilen 97 bis 105. Anschaffungskosten und Verkaufserlöse werden zu den jeweiligen Anschaffungs- beziehungsweise Verkaufsdaten in Euro umgerechnet.

### 6.3 VUSA: ein ISIN-Pool in WealthIQ, getrennte Brokerpositionen

Für `IE00B3XXRP09` sind unter anderem folgende Vorgänge relevant:

- Kauf von 320 Anteilen in EUR im April 2021,
- weitere Käufe derselben ISIN in GBP in den Jahren 2021 und 2022,
- spätere Verkäufe in GBP,
- Verkauf von 320 Anteilen in EUR am 31.12.2025.

Der Brokerbericht ordnet den Verkauf vom 31.12.2025 dem ursprünglichen EUR-Kauf von 320 Anteilen zu:

```text
Verkaufserlös                              35.528,79 EUR
Anschaffungskosten inkl. Vorabpauschalen   21.740,67 EUR
Veräußerungsgewinn                         13.788,12 EUR
```

WealthIQ identifiziert ein Instrument bei vorhandener ISIN ausschließlich über die normalisierte ISIN. Siehe `IbkrStatementImporter.cs` um die Zeilen 306 bis 327. Der FIFO-Matcher grenzt anschließend nach `AccountId`, `InstrumentId` und Positionsrichtung ab, nicht nach Symbol, Listing oder Handelswährung. Siehe `FiFoMatcher.cs` um die Zeilen 21 bis 35.

Dadurch haben frühere GBP-Verkäufe zuerst den älteren EUR-Lot verbraucht. Für den Verkauf 2025 verwendet WealthIQ verbliebene GBP-Lots, insbesondere Teile der Käufe mit den IBKR-Transaktionsreferenzen `757965141` und `939200738`. Nach Umrechnung der historischen GBP-Anschaffungskosten und Abzug der den Lots zugeordneten Vorabpauschalen entsteht ein Rohgewinn von `10.881,54 EUR`.

Der Unterschied von `2.906,58 EUR` ist damit überwiegend kein Rechen- oder Rundungsfehler, sondern die Folge unterschiedlicher FIFO-Pooldefinitionen.

### 6.4 Treasury-ETF: IDTL und IS04

`IDTL` und `IS04` haben beide die ISIN `IE00BSKRJZ44`:

- `IDTL` wurde in USD gehandelt,
- `IS04` wurde in EUR gehandelt,
- wirtschaftlich und anhand der ISIN handelt es sich um dieselbe Anteilsklasse.

Beim Verkauf der 286,0518 IS04-Anteile im Mai 2025 ordnet der Broker die Veräußerung dem EUR-Kauf vom 28.06.2024 zu und ermittelt ungefähr `-115,20 EUR`.

WealthIQ führt alle Anteile derselben ISIN im gemeinsamen FIFO-Pool. Der EUR-Verkauf verbraucht deshalb ältere USD-Lots aus 2021. Dadurch:

- fällt der Verlust im Mai um ungefähr `331 EUR` höher aus,
- verschiebt sich die Lot-Zuordnung der USD-Verkäufe im Oktober,
- entsteht dort eine weitere Differenz von ungefähr `187 EUR`.

Die gesamte Abweichung für diese ISIN beträgt damit ungefähr `519 EUR`.

### 6.5 Angesetzte Vorabpauschalen beim Verkauf

WealthIQ hat bei den Verkäufen 2025 insgesamt ungefähr `1.875,31 EUR` zuvor angesetzte rohe Vorabpauschalen verbraucht:

| Fonds | Verrechnete Vorabpauschale |
|---|---:|
| CSPX | 348,38 EUR |
| CNDX | 1.096,73 EUR |
| XS2L | 286,07 EUR |
| VUSA | 144,14 EUR |

Auch der Broker erhöht seine Anschaffungskosten um angesetzte Vorabpauschalen. Die verbleibenden Unterschiede folgen deshalb überwiegend aus den bereits beschriebenen abweichenden Vorabpauschalen und FIFO-Lots.

## 7. Ausschüttungen, Zinsen und Quellensteuer

### 7.1 Ausschüttungen 2025

Der Broker weist `2.868,96 EUR` aus; WealthIQ berechnet mit den importierten Rohdaten `2.794,88 EUR`. Die Differenz beträgt `74,08 EUR`.

Der Broker enthält zusätzlich eine VUSA-Ausschüttung vom 31.12.2025:

```text
95,71 USD = 81,48 EUR laut Brokerbericht
```

Diese Buchung fehlt im vollständigen XML-Export `TaxAlpha_Raw_Data_2025_complete.xml`. Das erklärt den größten Teil der Differenz. Der verbleibende Unterschied stammt aus abweichenden FX-Kursen:

- der Broker verwendet eigene Umrechnungskurse,
- WealthIQ verwendet die im eigenen FX-Bestand hinterlegten Kurse am jeweiligen Ereignisdatum,
- das XML-Feld `fxRateToBase` wird von WealthIQ nicht als steuerliche Umrechnung übernommen.

Vor der endgültigen Steuererklärung muss die fehlende Ausschüttung ergänzt oder über einen neueren vollständigen Flex-Query-Export importiert werden.

### 7.2 Zinsen

Die Zinsen stimmen exakt überein:

```text
Brokerbericht: 147,64 EUR
WealthIQ:       147,64 EUR
```

Damit ist der Import der acht relevanten Zinsbuchungen 2025 plausibilisiert.

### 7.3 Einbehaltene ausländische Steuer

Beide Systeme erfassen `28,56 EUR` einbehaltene Steuer. Der Broker weist davon nach DBA-Betrachtung nur `21,42 EUR` als anrechenbar aus. WealthIQ zieht in seiner groben Steuerschätzung derzeit die vollständigen `28,56 EUR` ab. Für 2025 wirkt sich das wegen der negativen WealthIQ-Bemessungsgrundlage nicht aus, die Logik ist aber für andere Jahre nicht ohne Weiteres als Veranlagungswert verwendbar.

## 8. Teilfreistellung und Fondsklassifizierung

Der Broker ordnet sämtliche Fonds als "Other funds" ein und weist die Beträge in den KAP-INV-Zeilen 8, 13 und 26 aus. In seinen Erläuterungen erklärt er, dass bei unzureichenden Daten standardmäßig keine Teilfreistellung angewandt wird.

WealthIQ verwendet zum Analysezeitpunkt unter anderem folgende Profile:

| ISIN | Fonds | WealthIQ-Klassifizierung | Teilfreistellung |
|---|---|---|---:|
| IE00B3XXRP09 | Vanguard S&P 500 UCITS ETF | Aktien-ETF | 30 % |
| IE00B53SZB19 | iShares NASDAQ 100 UCITS ETF | Aktien-ETF | 30 % |
| IE00B5BMR087 | iShares Core S&P 500 | Aktien-ETF | 30 % |
| LU0411078552 | Xtrackers S&P 500 2x Leveraged | Aktien-ETF im lokalen Profil | 30 % |
| IE00BSKRJZ44 | iShares USD Treasury Bond 20+yr | Anleihen-ETF | 0 % |

Für klassische physisch oder hinreichend aktienorientiert investierende Aktienfonds ist die 30-prozentige Teilfreistellung grundsätzlich plausibel. Bei synthetischen beziehungsweise gehebelten Fonds wie XS2L sollte die Erfüllung der gesetzlichen Aktienfondsquote anhand der Anlagebedingungen oder steuerlichen Fondsdaten belegt werden. Dass der Broker alle Fonds unterschiedslos als "Other funds" behandelt, deutet eher auf fehlende oder unzureichende Stammdaten als auf eine fundierte Einzelfallklassifizierung hin.

Ein zusätzlicher WealthIQ-Risikopunkt ist, dass importierte ISIN-Instrumente ohne vollständiges Profil initial mit einer 30-prozentigen Quote angelegt werden können. Vor einer Steuerverwendung müssen deshalb insbesondere XEON und alle nicht ausdrücklich gepflegten Instrumente kontrolliert werden.

## 9. Steuerrechtliche Recherche zur FIFO-Frage

### 9.1 Gesetzliche Grundlage

§ 20 Abs. 4 Satz 7 EStG bestimmt:

> "Bei vertretbaren Wertpapieren, die einem Verwahrer zur Sammelverwahrung im Sinne des § 5 des Depotgesetzes [...] anvertraut worden sind, ist zu unterstellen, dass die zuerst angeschafften Wertpapiere zuerst veräußert wurden."

Quelle: [§ 20 EStG](https://www.gesetze-im-internet.de/estg/__20.html)

§ 20 Abs. 4 Satz 1 EStG regelt getrennt davon die Fremdwährungsumrechnung:

> "[...] bei nicht in Euro getätigten Geschäften sind die Einnahmen im Zeitpunkt der Veräußerung und die Anschaffungskosten im Zeitpunkt der Anschaffung in Euro umzurechnen."

Die Handelswährung ist damit ein Bewertungs- und Umrechnungsmerkmal, aber kein gesetzlich genanntes Merkmal zur Bildung eines eigenen FIFO-Pools.

§ 19 Abs. 1 InvStG überträgt diese Gewinnermittlung auf Investmentanteile:

> "Für die Ermittlung des Gewinns aus der Veräußerung von Investmentanteilen, die nicht zu einem Betriebsvermögen gehören, ist § 20 Absatz 4 des Einkommensteuergesetzes entsprechend anzuwenden."

Quelle: [§ 19 InvStG](https://www.gesetze-im-internet.de/invstg_2018/__19.html)

### 9.2 BMF-Schreiben vom 14. Mai 2025

Die klarste amtliche Aussage enthält das aktuelle BMF-Schreiben "Einzelfragen zur Abgeltungsteuer" vom 14.05.2025, Aktenzeichen `IV C 1 - S 2252/00075/016/070`.

Randnummer 97 lautet:

> "Gemäß § 20 Absatz 4 Satz 7 EStG ist bei Wertpapieren bei der Veräußerung aus der Girosammelverwahrung (§§ 5 ff. DepotG) zu unterstellen, dass die zuerst angeschafften Wertpapiere zuerst veräußert werden (Fifo-Methode). Die Anwendung der Fifo-Methode im Sinne des § 20 Absatz 4 Satz 7 EStG ist auf das einzelne Depot bezogen anzuwenden. Konkrete Einzelweisungen des Kunden, welches Wertpapier veräußert werden soll, sind insoweit einkommensteuerrechtlich unbeachtlich."

Randnummer 98 lautet:

> "Als Depot im Sinne dieser Regelung ist auch ein Unterdepot anzusehen. Bei einem Unterdepot handelt es sich um eine eigenständige Untergliederung eines Depots mit einer laufenden Unterdepot-Nummer. Der Kunde kann hierbei die Zuordnung der einzelnen Wertpapiere zum jeweiligen Depot bestimmen."

Randnummer 99 bestätigt die FIFO-Methode außerdem für die Streifbandverwahrung.

Quellen:

- [BMF-Landingpage](https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Abgeltungsteuer/2025-05-14-einzelfragen-zur-abgeltungsteuer.html)
- [Offizielles BMF-PDF](https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Abgeltungsteuer/2025-05-14-einzelfragen-zur-abgeltungsteuer.pdf?__blob=publicationFile&v=6)

### 9.3 Konsequenz für Listing, Währung, CONID und Lagerstelle

Aus Gesetz und BMF-Schreiben ergibt sich folgende Bewertung:

| Unterschied | Eigener steuerlicher FIFO-Pool? | Begründung |
|---|---|---|
| Anderer Ausführungsplatz/Börse | Nein | Der Ausführungsplatz ändert das erworbene vertretbare Wertpapier nicht. |
| Andere Handels- oder Notierungswährung | Nein | Die Währung beeinflusst nach § 20 Abs. 4 Satz 1 EStG die EUR-Umrechnung. |
| Anderes IBKR-Symbol | Nein | Brokerinternes Identifikationsmerkmal, kein Depot. |
| Andere IBKR-CONID | Nicht allein deshalb | Brokerinterner Contract für Listing/Währung/Lagerstelle, keine Unterdepotnummer im Sinne des BMF. |
| Andere technische Lagerstelle | Nicht allein deshalb | Verwahrinfrastruktur ist nicht automatisch ein eigenes Kundendepot. |
| Eigenständiges Depot/Konto | Ja | FIFO gilt pro einzelnem Depot. |
| Nummeriertes Unterdepot | Ja | Vom BMF in Randnummer 98 ausdrücklich als eigenes Depot anerkannt. |
| Bloßes Währungsunterkonto | Nein | Kein eigenständiges Wertpapierdepot. |

Ein verbleibender Grenzfall wäre eine Konstellation, in der unterschiedliche Lagerstellen tatsächlich unterschiedliche, nicht fungible rechtliche Verwahransprüche oder eigenständige Kundendepots begründen. Hierzu wurde keine unmittelbar passende BFH-Entscheidung speziell für IBKR-CONIDs gefunden. Entscheidend wären dann Depotvertrag, Depotnummern, Übertragbarkeit und rechtliche Verwahrform, nicht die Darstellung im informellen Steuerbericht.

### 9.4 IBKR-Dokumentation

IBKR erläutert selbst:

> "Stocks can be multi-listed in different currencies at different depositories."

und:

> "At our firm, the currencies of multi-listed stocks are differentiated by their conID, e.g., the same ISIN, a different conID."

Quelle: [IBKR Client Portal Guide - Depository Switch](https://www.ibkrguides.com/clientportal/transferandpay/depositoryswitch.htm)

IBKR ermöglicht einen "Depository / Trading Currency Change" zwischen solchen Positionen. Das zeigt, dass CONID, Handelswährung und Lagerstelle für IBKR operative Positionsmerkmale sind. Die Dokumentation sagt jedoch nicht, dass dadurch ein eigenständiges Kundendepot oder Unterdepot im Sinne des deutschen Steuerrechts entsteht.

### 9.5 Bewertung für WealthIQ

Unter der Voraussetzung eines einzigen rechtlichen Depots ohne nummerierte Unterdepots ist die fachlich defensible Standardregel:

```text
FIFO-Pool = Depot/Konto + vertretbares Wertpapier, praktisch identifiziert über die ISIN
```

Das entspricht für die untersuchten Fälle der aktuellen WealthIQ-Logik `AccountId + InstrumentId`, weil `InstrumentId` bei vorhandener ISIN stabil aus der ISIN gebildet wird.

Die im Brokerbericht erkennbare Trennung nach Symbol, CONID, Handelswährung oder Lagerstelle ist ohne Nachweis eines eigenständigen Unterdepots wahrscheinlich nicht mit der depotbezogenen BMF-Regel vereinbar.

## 10. Steuerrechtliche Recherche zur Vorabpauschale

### 10.1 Gesetzliche Formel

§ 18 Abs. 1 InvStG lautet:

> "Die Vorabpauschale ist der Betrag, um den die Ausschüttungen eines Investmentfonds innerhalb eines Kalenderjahres den Basisertrag für dieses Kalenderjahr unterschreiten."

Weiter heißt es:

> "Der Basisertrag ist auf den Mehrbetrag begrenzt, der sich zwischen dem ersten und dem letzten im Kalenderjahr festgesetzten Rücknahmepreis zuzüglich der Ausschüttungen innerhalb des Kalenderjahres ergibt."

Quelle: [§ 18 InvStG](https://www.gesetze-im-internet.de/invstg_2018/__18.html)

Die Formel lautet damit sinngemäß:

```text
Basisertrag = min(
    Jahresanfangspreis x 70 % x Basiszins,
    Jahresendpreis - Jahresanfangspreis + Ausschüttungen
)

Vorabpauschale = max(0, Basisertrag - Ausschüttungen)
```

§ 2 Abs. 11 InvStG definiert Ausschüttungen als die dem Anleger gezahlten oder gutgeschriebenen Beträge einschließlich des Steuerabzugs. Quelle: [§ 2 InvStG](https://www.gesetze-im-internet.de/invstg_2018/__2.html).

### 10.2 BMF-Anwendungsschreiben zum Investmentsteuergesetz

Das BMF-Anwendungsschreiben vom 21. Mai 2019, BStBl I 2019 S. 527, in der fortgeschriebenen Fassung, wiederholt die Berechnung nach § 18 InvStG. In seinem Beispiel wird die Ausschüttung pro Anteil vom begrenzten Basisertrag abgezogen. Für fremdwährungsnotierte Investmentanteile verlangt es die Umrechnung der Werte zu den jeweiligen Stichtagen beziehungsweise Ausschüttungsterminen.

Quellen:

- [BZSt-Landingpage zum BMF-Anwendungsschreiben](https://www.bzst.de/SharedDocs/BMF/DE/Downloads/bmf_schreiben_20190521_InvStG_18_anwendungsfragen.html)
- [Offizielles BZSt/BMF-PDF](https://www.bzst.de/SharedDocs/BMF/DE/Downloads/bmf_schreiben_20190521_InvStG_18_anwendungsfragen.pdf?__blob=publicationFile&v=1)
- [BMF-Änderungsschreiben vom 24.11.2025](https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Investmentsteuer/2025-11-24-anwendungsfragen-InvStG.html)

Eine andere Handelswährung führt danach zu einer anderen EUR-Umrechnung, nicht zu einer Nichtberücksichtigung der Ausschüttung.

### 10.3 Anteilsklasse und Handelswährung

§ 96 Abs. 1 KAGB erlaubt unterschiedliche Anteilklassen mit verschiedenen Ausgestaltungsmerkmalen, beispielsweise Ertragsverwendung, Währung des Anteilwerts oder Verwaltungsvergütung. Anteile derselben Anteilklasse haben jedoch gleiche Ausgestaltungsmerkmale.

Quelle: [§ 96 KAGB](https://www.gesetze-im-internet.de/kagb/__96.html)

Davon zu unterscheiden ist die reine Handelswährung eines Börsenlistings. Dieselbe Anteilklasse kann an verschiedenen Börsen in unterschiedlichen Währungen gehandelt werden, ohne dadurch eine neue Anteilklasse zu werden. Die identische ISIN ist ein starkes Indiz für dasselbe Finanzinstrument beziehungsweise dieselbe Anteilklasse.

### 10.4 Konsequenz für VUSA

Wenn die 320 VUSA-Anteile dieselbe ISIN und Anteilsklasse hatten und die Ausschüttungen tatsächlich auf diese Anteile gutgeschrieben wurden, darf die Vorabpauschalenberechnung die Ausschüttungen nicht allein wegen eines anderen Listings, einer anderen Handelswährung oder CONID auf null setzen.

Der Broker müsste für eine abweichende Behandlung einen anderen Sachverhalt nachweisen, zum Beispiel:

- tatsächlich unterschiedliche Anteilsklassen trotz fehlerhafter ISIN-Zuordnung,
- fehlende Besitzzeit am relevanten Ausschüttungstermin,
- einen Depotübertrag ohne vollständige Steuerdaten,
- oder ein formal eigenständiges Unterdepot.

Die vorhandenen Rohdaten sprechen gegen diese Erklärungen und für einen Datenzuordnungsfehler des Brokerberichts.

## 11. Gesamtbewertung und Sicherheit

### 11.1 Feststellungen mit hoher Sicherheit

- Der Brokerbericht und WealthIQ stimmen bei Zinsen, Quellensteuer sowie den Rohgewinnen von CSPX, CNDX, XS2L und XEON weitgehend überein.
- Die großen Verkaufsabweichungen entstehen durch unterschiedliche FIFO-Pooldefinitionen für dieselbe ISIN.
- Das aktuelle BMF-Schreiben verlangt eine depotbezogene FIFO-Anwendung und erkennt nur echte nummerierte Unterdepots als separate Pools an.
- Eine andere Handelswährung oder IBKR-CONID ist für sich genommen kein Unterdepot.
- Der Brokerbericht setzt bei VUSA Ausschüttungen 2024 mit null an, obwohl die Rohdaten eine Ausschüttung auf den 320er-Bestand nachweisen.
- Nach § 18 InvStG müssen diese Ausschüttungen die Vorabpauschale mindern.

### 11.2 Wahrscheinlich richtige Schlussfolgerungen

- Unter der Annahme eines einzigen Depots `U5658230` ist WealthIQs gemeinsames FIFO je Konto und ISIN wahrscheinlich steuerlich richtiger als die listingbezogene Brokerberechnung.
- Die WealthIQ-Vorabpauschale für VUSA von ungefähr `79,53 EUR` ist wahrscheinlich wesentlich näher am korrekten Wert als die `418,57 EUR` des Brokers.
- Die pauschale Brokerklassifizierung aller Fonds als "Other funds" ist wahrscheinlich durch unvollständige Drittanbieter-Stammdaten verursacht.

### 11.3 Offene oder gesondert zu prüfende Punkte

- IBKR sollte bestätigen, ob hinter `U5658230` echte nummerierte Unterdepots oder lediglich unterschiedliche CONIDs/Lagerstellen geführt wurden.
- Die Teilfreistellung von XS2L sowie die Klassifizierung von XEON sollten anhand belastbarer Fondsdokumente geprüft werden.
- Die VUSA-Ausschüttung vom 31.12.2025 fehlt im importierten XML und muss ergänzt werden.
- Die exakten gesetzlichen Jahresanfangs- und Jahresendpreise beziehungsweise Rücknahmepreise sollten für eine endgültige Vorabpauschale dokumentiert werden.
- WealthIQ modelliert noch keine steuerlichen Verlustvorträge und keine getrennten Verlustverrechnungstöpfe. Seine Steuerschätzung ist daher nicht automatisch der in der Erklärung anzusetzende Steuerbetrag.
- Eine speziell den IBKR-Grenzfall "gleiche ISIN, unterschiedliche CONID/Lagerstelle in einem angezeigten Konto" entscheidende BFH-Rechtsprechung wurde nicht gefunden.

## 12. Referenzen und Quellenverzeichnis

### 12.1 Primäre Rechtsquellen

1. **§ 20 EStG - Einkünfte aus Kapitalvermögen**  
   FIFO in Absatz 4 Satz 7; Fremdwährungsumrechnung in Absatz 4 Satz 1.  
   <https://www.gesetze-im-internet.de/estg/__20.html>

2. **§ 18 InvStG - Vorabpauschale**  
   Gesetzliche Berücksichtigung von Ausschüttungen, Wertsteigerungsbegrenzung, Erwerbsmonatskürzung und Zuflusszeitpunkt.  
   <https://www.gesetze-im-internet.de/invstg_2018/__18.html>

3. **§ 19 InvStG - Gewinne aus der Veräußerung von Investmentanteilen**  
   Entsprechende Anwendung von § 20 Abs. 4 EStG und vollständiger Abzug angesetzter Vorabpauschalen.  
   <https://www.gesetze-im-internet.de/invstg_2018/__19.html>

4. **§ 2 InvStG - Begriffsbestimmungen**  
   Definition von Investmentanteil und Ausschüttung.  
   <https://www.gesetze-im-internet.de/invstg_2018/__2.html>

5. **§ 96 KAGB - Anteilklassen und Teilinvestmentvermögen**  
   Rechtliche Merkmale unterschiedlicher Anteilklassen.  
   <https://www.gesetze-im-internet.de/kagb/__96.html>

6. **§§ 5 und 6 DepotG - Sammelverwahrung und Sammelbestand**  
   Grundlagen der Sammelverwahrung vertretbarer Wertpapiere.  
   <https://www.gesetze-im-internet.de/depotg/__5.html>  
   <https://www.gesetze-im-internet.de/depotg/__6.html>

### 12.2 Verwaltungsanweisungen

7. **BMF-Schreiben vom 14.05.2025 - Einzelfragen zur Abgeltungsteuer**  
   Aktenzeichen `IV C 1 - S 2252/00075/016/070`; besonders Randnummern 97 bis 99 zur depotbezogenen FIFO-Methode und zu Unterdepots.  
   Landingpage: <https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Abgeltungsteuer/2025-05-14-einzelfragen-zur-abgeltungsteuer.html>  
   PDF: <https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Abgeltungsteuer/2025-05-14-einzelfragen-zur-abgeltungsteuer.pdf?__blob=publicationFile&v=6>

8. **BMF-Schreiben vom 21.05.2019 - Anwendungsfragen zum Investmentsteuergesetz**  
   BStBl I 2019 S. 527, fortgeschriebene Fassung; Berechnung der Vorabpauschale, Ausschüttung je Anteil und Fremdwährungsumrechnung.  
   Landingpage: <https://www.bzst.de/SharedDocs/BMF/DE/Downloads/bmf_schreiben_20190521_InvStG_18_anwendungsfragen.html>  
   PDF: <https://www.bzst.de/SharedDocs/BMF/DE/Downloads/bmf_schreiben_20190521_InvStG_18_anwendungsfragen.pdf?__blob=publicationFile&v=1>

9. **BMF-Änderungsschreiben vom 24.11.2025 zum InvStG-Anwendungsschreiben**  
   <https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Investmentsteuer/2025-11-24-anwendungsfragen-InvStG.html>

### 12.3 Broker- und Marktinfrastruktur

10. **Interactive Brokers - Depository Switch**  
    Erläutert, dass mehrfach gelistete Wertpapiere mit derselben ISIN bei IBKR je Handelswährung/Lagerstelle unterschiedliche CONIDs haben können.  
    <https://www.ibkrguides.com/clientportal/transferandpay/depositoryswitch.htm>

11. **Interactive Brokers - Flex Query Trades Fields**  
    Dokumentiert die getrennten Felder für Symbol, CONID, Listing Exchange, Currency und ISIN.  
    <https://www.ibkrguides.com/reportingreference/reportguide/tradesfq.htm>

12. **WM Gruppe - WKN/ISIN als eindeutige Finanzinstrumentidentifikation**  
    Sekundäre Marktinfrastrukturquelle zur Identifikations- und Fungibilitätsfunktion der ISIN.  
    <https://www.wmgruppe.de/de/news/70-jahre-wkn-eine-erfolgsgeschichte-made-in-germany/>

### 12.4 Ergänzende Fachquellen

13. **BVI - FAQ Vorabpauschale**  
    Verständliche Darstellung `Vorabpauschale = Basisertrag - Ausschüttung des Vorjahres`.  
    <https://www.bvi.de/faq/faq-vorabpauschale/>

14. **Haufe - FIFO bei vertretbaren Wertpapieren**  
    Sekundärkommentar mit Bestätigung, dass auf das einzelne Depot abzustellen ist und ein Unterdepot als eigenes Depot gilt.  
    <https://www.haufe.de/id/kommentar/littmannbitzpust-das-einkommensteuerrecht-estg-20-7-vertretbare-wertpapiere-in-girosammelverwahrung-20-abs4-s7-estg-HI14678365.html>

15. **Haufe - Vorabpauschalen gemäß § 18 InvStG**  
    Sekundärkommentar zur Berücksichtigung von Ausschüttungen und zur zeitanteiligen Kürzung im Erwerbsjahr.  
    <https://www.haufe.de/id/kommentar/frotschergeurts-estg-43-kapitalertraege-mit-steuerabzug-533-vorabpauschalen-gem-18-invstg-HI10125765.html>

## 13. Empfohlenes weiteres Vorgehen

1. Bei IBKR schriftlich klären, ob für VUSA sowie IDTL/IS04 echte nummerierte Unterdepots bestanden oder ob die Trennung lediglich auf Symbol, CONID, Handelswährung oder Lagerstelle beruhte.
2. Wenn keine echten Unterdepots bestanden, für die Steuerberechnung grundsätzlich dem gemeinsamen WealthIQ-FIFO je Konto und ISIN folgen.
3. Die fehlende VUSA-Ausschüttung vom 31.12.2025 in den WealthIQ-Daten ergänzen.
4. Die Fondsprofile und Teilfreistellungen, insbesondere XS2L und XEON, anhand offizieller Fondsunterlagen dokumentieren.
5. Für die endgültige Erklärung die KAP-INV-Rohbeträge verwenden und die Teilfreistellung korrekt den jeweiligen Fondskategorien zuordnen; nicht lediglich die aggregierte WealthIQ-Steuerschätzung übernehmen.
6. Die Berechnung und die hier aufgeführten Quellen als Nachweisunterlage aufbewahren, weil die erklärten Werte vom informellen Brokerbericht abweichen.

Vorgeschlagene Anfrage an IBKR:

> Wurden VUSA (`IE00B3XXRP09`) beziehungsweise IDTL/IS04 (`IE00BSKRJZ44`) im Konto `U5658230` in rechtlich eigenständigen Unterdepots mit jeweils eigener laufender Unterdepotnummer geführt, oder beruht die Trennung im German Tax Report lediglich auf unterschiedlichen CONIDs, Symbolen, Handelswährungen oder Lagerstellen? Bitte erläutern Sie außerdem, weshalb bei der Ermittlung der VUSA-Vorabpauschale für 2024 Ausschüttungen von `0,00 EUR` angesetzt wurden, obwohl dem Konto im Jahr 2024 Ausschüttungen auf den Bestand von 320 Anteilen gutgeschrieben wurden.

## 14. Abschließendes Urteil

Unter den derzeit bekannten Tatsachen ist WealthIQ bei den beiden materiellen Streitpunkten wahrscheinlich näher an der deutschen steuerlichen Behandlung als der informelle Brokerbericht:

- Das depot- und ISIN-bezogene FIFO ist durch § 20 Abs. 4 EStG und die Randnummern 97 und 98 des BMF-Schreibens besser gestützt als eine Trennung nach IBKR-CONID, Symbol oder Handelswährung.
- Die Berücksichtigung der tatsächlich gutgeschriebenen VUSA-Ausschüttungen entspricht § 18 InvStG; die Nullsetzung des Brokers ist durch die Rohdaten widerlegt.

Dieses Ergebnis rechtfertigt jedoch kein blindes Vertrauen in jede WealthIQ-Gesamtsumme. Datenvollständigkeit, Fondsklassifizierung, Teilfreistellung, Verlustverrechnung und exakte Kurs-/FX-Nachweise bleiben eigenständige Prüfpunkte.

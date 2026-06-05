# Phase 3 — Data Visualization & Insights Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make imported/reference data inspectable via a new Data Browser, make the German tax report verifiable down to source, fix the donut tooltip, and add an editable Basiszins table.

**Architecture:** New read-only Blazor pages in `WealthIQ.Web` query `WealthIqDbContext`/`ILedgerStore` directly (matching the existing `DataAdmin`/`Audit` precedent). Charts use TradingView **Lightweight Charts v4** (vendored JS, Apache-2.0) wrapped by one reusable `LightweightChart.razor` component. The only Domain/Application changes are three optional fields on `GermanTaxEntry`, a pure adjusted-OHLC helper, and a Basiszins delete method — all behind existing layering rules.

**Tech Stack:** C#/.NET 10, Blazor Server, MudBlazor 9.5, EF Core + SQLite, TradingView Lightweight Charts 4.2, xUnit.

---

## File Structure

**Create:**
- `src/WealthIQ.Application/MarketData/AdjustedPriceCalculator.cs` — pure adjusted-OHLC derivation.
- `src/WealthIQ.Web/wwwroot/lib/lightweight-charts/lightweight-charts.standalone.production.js` — vendored library (v4.2.0).
- `src/WealthIQ.Web/wwwroot/wiq-charts.js` — JS interop wrapper around the library.
- `src/WealthIQ.Web/Components/Shared/LightweightChart.razor` — reusable chart component.
- `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor` — ledger tables.
- `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor` — candlestick page.
- `src/WealthIQ.Web/Components/Pages/Browse/FxChart.razor` — FX line page.
- `tests/WealthIQ.Tests/Application/MarketData/AdjustedPriceCalculatorTests.cs`
- `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs`
- `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbBasisInterestRateStoreTests.cs`

**Modify:**
- `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs` — add `OpenedOn`, `Fees`, `Origin`.
- `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` — populate the new fields.
- `src/WealthIQ.Application/Tax/BasisInterestRateRefreshModels.cs` (`IBasisInterestRateStore`) — add `Delete`.
- `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateStore.cs` — implement `Delete`.
- `src/WealthIQ.Application/Tax/BasisInterestRateRefreshService.cs` — add `DeleteAsync`.
- `src/WealthIQ.Web/Components/Layout/MainLayout.razor` — new nav group.
- `src/WealthIQ.Web/Components/App.razor` — reference the two JS files.
- `src/WealthIQ.Web/wwwroot/wealthiq.js` — add `scrollToAnchor`.
- `src/WealthIQ.Web/Components/Pages/Steuerreport.razor` — donut fix + detail panel + Zinsen/Quellensteuer columns.
- `src/WealthIQ.Web/Components/Pages/DataAdmin.razor` — editable Basiszins table.

---

## Task 1: Fix the donut tooltip decimals (Steuerreport)

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor:130-145`

- [ ] **Step 1: Round each donut segment to 2 decimals**

In `Steuerreport.razor`, replace the `Data` initializer inside `CompositionSeries` (currently `(double)Math.Max(0, ...)` for the four categories) with rounded values:

```csharp
Data = new[]
{
    Math.Round((double)Math.Max(0, Current.Summary.NetRealizedGainsTaxable), 2),
    Math.Round((double)Math.Max(0, Current.Summary.DividendsTaxable), 2),
    Math.Round((double)Math.Max(0, Current.Summary.VorabpauschaleTaxable), 2),
    Math.Round((double)Math.Max(0, Current.Summary.InterestTaxable), 2),
},
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web` then open the Steuerreport, hover a donut segment.
Expected: tooltip shows a euro value with at most two decimals (no long float tail).

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "Fix: round Steuerreport donut segments to 2 decimals"
```

---

## Task 2: Extend `GermanTaxEntry` with `OpenedOn`, `Fees`, `Origin`

**Files:**
- Modify: `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`

New optional fields go at the **end** of the positional record so existing positional/named construction (and the golden regression test) is unaffected.

- [ ] **Step 1: Add the fields**

Replace the record body with:

```csharp
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Domain.Model.Tax;

public readonly record struct GermanTaxEntry(
    int Year,
    DateOnly Date,
    GermanTaxEntryType Type,
    string Symbol,
    string Isin,
    decimal RawAmount,
    decimal TaxableAmount,
    decimal UsedVorabpauschale = 0m,
    decimal ForeignWithholdingTax = 0m,
    decimal QuantitySold = 0m,
    decimal SaleProceeds = 0m,
    decimal AcquisitionCosts = 0m,
    DateOnly OpenedOn = default,
    decimal Fees = 0m,
    string Origin = "");
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors (defaults keep all existing call sites valid).

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs
git commit -m "Feat: add OpenedOn/Fees/Origin to GermanTaxEntry"
```

---

## Task 3: Populate `OpenedOn` + `Fees` on Sell entries

**Files:**
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs`
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:107-118`

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs`. It reuses the golden `data/test` fixtures (same wiring as `GermanTaxRegressionTests`) and asserts that 2024 Sell entries carry the acquisition date and non-negative fees:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxEntryDetailTests
{
    [Fact]
    public async Task Calculate_SellEntries_CarryOpenedOnAndFees()
    {
        var repoRoot = FindRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "test", "statements");
        var configurationPath = Path.Combine(repoRoot, "data", "test", "configuration");

        var importer = new IbkrStatementImporter();
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
        }, CancellationToken.None);

        var instrumentCatalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(configurationPath, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(configurationPath, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(configurationPath, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(configurationPath, "fx_rates.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog);

        var sells = result.Entries.Where(x => x.Year == 2024 && x.Type == GermanTaxEntryType.Sell).ToList();

        Assert.NotEmpty(sells);
        Assert.All(sells, s => Assert.True(s.OpenedOn != default(DateOnly), $"{s.Symbol} sell missing OpenedOn"));
        Assert.All(sells, s => Assert.True(s.OpenedOn <= s.Date, $"{s.Symbol} opened after close"));
        Assert.All(sells, s => Assert.True(s.Fees >= 0m, $"{s.Symbol} negative fees"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WealthIQ.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root could not be located.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryDetailTests"`
Expected: FAIL — `OpenedOn` is `default` (the calculator does not set it yet).

- [ ] **Step 3: Populate the fields in the Sell branch**

In `GermanTaxCalculator.cs`, the Sell `ledger.Add(new GermanTaxEntry(...))` call (inside `foreach (var consumption in matchResult.Consumptions)`) currently ends with the `AcquisitionCosts:` named argument. Add fee conversion just above the `ledger.Add` and pass the two new named arguments. Replace the block from `var taxableProfit = ...` through the `ledger.Add(...)` statement with:

```csharp
            var taxableProfit = rawProfit * (1m - instrument.Teilfreistellungsquote);

            // Fees attributable to this slice, in EUR. Open-side fees convert at the open date and
            // close-side fees at the close date — the same rates already required for cost/proceeds,
            // so this adds no new FX-rate dependency.
            var feesEur =
                _fxConverter.Convert(consumption.AllocatedOpenFees, consumption.OpenTradeDate).Amount +
                _fxConverter.Convert(consumption.AllocatedCloseFees, consumption.CloseTradeDate).Amount;

            ledger.Add(new GermanTaxEntry(
                tradeEntry.OccurredAt.Year,
                DateOnly.FromDateTime(tradeEntry.OccurredAt.UtcDateTime),
                GermanTaxEntryType.Sell,
                instrument.Symbol,
                instrument.ISIN,
                rawProfit,
                taxableProfit,
                usedVorabpauschale,
                QuantitySold: consumption.MatchedQuantity.Value,
                SaleProceeds: saleProceeds.Amount,
                AcquisitionCosts: acquisitionCosts.Amount,
                OpenedOn: consumption.OpenTradeDate,
                Fees: feesEur));
```

- [ ] **Step 4: Run the new test + the golden regression test**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxEntryDetailTests|FullyQualifiedName~GermanTaxRegressionTests"`
Expected: BOTH PASS (regression unchanged — it projects only Symbol/RawAmount/UsedVorabpauschale/TaxableAmount).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs
git commit -m "Feat: populate OpenedOn and Fees on Sell tax entries"
```

---

## Task 4: Populate `Origin` on Withholding-tax entries

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs:184-197`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs` (add a method)

- [ ] **Step 1: Write the failing test**

Append this method to `GermanTaxEntryDetailTests` (reuse the same setup; factor the calculator wiring into a local helper or copy the arrange block — copy is acceptable here):

```csharp
    [Fact]
    public async Task Calculate_WithholdingEntries_CarryOrigin()
    {
        var repoRoot = FindRepositoryRoot();
        var inputPath = Path.Combine(repoRoot, "data", "test", "statements");
        var configurationPath = Path.Combine(repoRoot, "data", "test", "configuration");

        var importer = new IbkrStatementImporter();
        var importResult = await importer.ImportAsync(new ImportRequest
        {
            AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
        }, CancellationToken.None);

        var instrumentCatalog = new InstrumentCatalogBuilder(
            new JsonInstrumentProfileEnricher(Path.Combine(configurationPath, "instruments.json")))
            .Build(importResult.Instruments);

        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(configurationPath, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(configurationPath, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(configurationPath, "fx_rates.csv")));

        var result = calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog);

        var withholdings = result.Entries
            .Where(x => x.Type == GermanTaxEntryType.WithholdingTax)
            .ToList();

        Assert.NotEmpty(withholdings);
        Assert.All(withholdings, w => Assert.False(string.IsNullOrWhiteSpace(w.Origin), "withholding missing Origin"));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "DisplayName~CarryOrigin"`
Expected: FAIL — `Origin` is empty.

- [ ] **Step 3: Populate `Origin` in the WithholdingTax branch**

In `GermanTaxCalculator.cs`, replace the `case CashFlowType.WithholdingTax:` block (the `ledger.Add(new GermanTaxEntry(...))` ending with `ForeignWithholdingTax: Math.Abs(withholdingTaxAmount))`) with:

```csharp
            case CashFlowType.WithholdingTax:
                var withholdingInstrumentId = cashEntry.RelatedInstrumentId ?? cashEntry.CashInstrumentId;
                var withholdingInstrument = GetInstrument(instrumentById, withholdingInstrumentId);
                var withholdingTaxAmount = _fxConverter.Convert(cashEntry.GrossAmount, date).Amount;

                // Origin: a security if the withholding references a related instrument with an ISIN;
                // otherwise it stems from an interest payment (no security) → label "Zinsen".
                var withholdingOrigin = cashEntry.RelatedInstrumentId.HasValue
                    && !string.IsNullOrWhiteSpace(withholdingInstrument.ISIN)
                        ? withholdingInstrument.Symbol
                        : "Zinsen";

                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.WithholdingTax,
                    withholdingInstrument.Symbol,
                    withholdingInstrument.ISIN,
                    withholdingTaxAmount,
                    0m,
                    ForeignWithholdingTax: Math.Abs(withholdingTaxAmount),
                    Origin: withholdingOrigin));
                break;
```

- [ ] **Step 4: Run the test**

Run: `dotnet test WealthIQ.slnx --filter "DisplayName~CarryOrigin"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/Tax/GermanTaxCalculator.cs tests/WealthIQ.Tests/Application/Tax/GermanTaxEntryDetailTests.cs
git commit -m "Feat: populate Origin on withholding-tax entries"
```

---

## Task 5: Steuerreport — detail panel, Zinsen/Quellensteuer columns, anchor linking

**Files:**
- Modify: `src/WealthIQ.Web/wwwroot/wealthiq.js`
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

- [ ] **Step 1: Add a scroll helper to `wealthiq.js`**

Inside the `window.wealthiq = { ... }` object (after `runCountUps`), add:

```javascript
    ,
    // Smooth-scroll to an element by id and briefly highlight it.
    scrollToAnchor: function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.add('wiq-flash');
        setTimeout(function () { el.classList.remove('wiq-flash'); }, 1200);
    }
```

- [ ] **Step 2: Make `EntryTable` ISIN column optional and the Sell "Anzeigen" scroll to detail**

In `Steuerreport.razor`, replace the `EntryTable` render fragment (the whole `private RenderFragment EntryTable(...)` method) with a version that takes a `showIsin` flag and, when rendering the Sells table, points "Anzeigen" at the detail anchor. Replace `DrillToSource` usage accordingly:

```csharp
    private RenderFragment EntryTable(IReadOnlyList<GermanTaxEntry> entries, bool showIsin = true, bool linkToDetail = false) => __builder =>
    {
        // Materialize once so we can call IndexOf (IReadOnlyList has none) and bind the same list.
        var rowList = entries as List<GermanTaxEntry> ?? entries.ToList();
        if (rowList.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="rowList" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Datum</MudTh>
                    <MudTh>Symbol</MudTh>
                    @if (showIsin)
                    {
                        <MudTh>ISIN</MudTh>
                    }
                    <MudTh Style="text-align:right">Brutto (€)</MudTh>
                    <MudTh Style="text-align:right">Steuerpflichtig (€)</MudTh>
                    <MudTh Style="text-align:right">Verrechn. Vorabpausch. (€)</MudTh>
                    <MudTh>Quelle</MudTh>
                </HeaderContent>
                <RowTemplate Context="row">
                    <MudTd DataLabel="Datum">@row.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Symbol">@row.Symbol</MudTd>
                    @if (showIsin)
                    {
                        <MudTd DataLabel="ISIN">@row.Isin</MudTd>
                    }
                    <MudTd DataLabel="Brutto" Style="text-align:right">@row.RawAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Steuerpflichtig" Style="text-align:right">@row.TaxableAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Vorabpauschale" Style="text-align:right">@row.UsedVorabpauschale.ToString("N2")</MudTd>
                    <MudTd DataLabel="Quelle">
                        @if (linkToDetail)
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                       OnClick="() => ScrollToDetail(rowList.IndexOf(row))">Anzeigen</MudButton>
                        }
                        else
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                       OnClick="() => DrillToSource(row.Isin)">Anzeigen</MudButton>
                        }
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    };

    private async Task ScrollToDetail(int index)
        => await JS.InvokeVoidAsync("wealthiq.scrollToAnchor", $"sell-detail-{index}");
```

- [ ] **Step 3: Wire the panels — Sells link to detail, Zinsen hide ISIN, add the detail + Herkunft panels**

In `Steuerreport.razor`, replace the `<MudExpansionPanels MultiExpansion="true" Elevation="0">` block (inside `wiq-rise-3`) with:

```razor
        <MudExpansionPanels MultiExpansion="true" Elevation="0">
            <MudExpansionPanel Text="@($"Verkäufe (realisierter PnL) ({Current.Sells.Count})")">
                @EntryTable(Current.Sells, showIsin: true, linkToDetail: true)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Verkäufe — Details ({Current.Sells.Count})")">
                @SellDetailTable(Current.Sells)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Vorabpauschale ({Current.Vorabpauschale.Count})")">
                @EntryTable(Current.Vorabpauschale)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Dividenden ({Current.Dividends.Count})")">
                @EntryTable(Current.Dividends)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Zinsen ({Current.Interest.Count})")">
                @EntryTable(Current.Interest, showIsin: false)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Quellensteuer ({Current.WithholdingTaxes.Count})")">
                @WithholdingTable(Current.WithholdingTaxes)
            </MudExpansionPanel>
        </MudExpansionPanels>
```

- [ ] **Step 4: Add the `SellDetailTable` and `WithholdingTable` fragments**

In the `@code` block of `Steuerreport.razor`, add these two render fragments (after `EntryTable`):

```csharp
    private RenderFragment SellDetailTable(IReadOnlyList<GermanTaxEntry> sells) => __builder =>
    {
        // Same materialized list used for Items and for the anchor index (must align with the
        // summary table's rowList so "Anzeigen" scrolls to the matching detail row).
        var rowList = sells as List<GermanTaxEntry> ?? sells.ToList();
        if (rowList.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Verkäufe.</MudText>
        }
        else
        {
            <MudTable Items="rowList" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Symbol</MudTh>
                    <MudTh>Eröffnet</MudTh>
                    <MudTh>Geschlossen</MudTh>
                    <MudTh Style="text-align:right">Stück</MudTh>
                    <MudTh Style="text-align:right">Kaufpreis (€)</MudTh>
                    <MudTh Style="text-align:right">Verkaufspreis (€)</MudTh>
                    <MudTh Style="text-align:right">Kosten (€)</MudTh>
                    <MudTh Style="text-align:right">Roh-PnL (€)</MudTh>
                    <MudTh Style="text-align:right">Verr. Vorab. (€)</MudTh>
                    <MudTh Style="text-align:right">Steuerpfl. (€)</MudTh>
                    <MudTh>Quelle</MudTh>
                </HeaderContent>
                <RowTemplate Context="row">
                    <MudTd DataLabel="Symbol"><span id="@($"sell-detail-{rowList.IndexOf(row)}")">@row.Symbol</span></MudTd>
                    <MudTd DataLabel="Eröffnet">@row.OpenedOn.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Geschlossen">@row.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Stück" Style="text-align:right">@row.QuantitySold.ToString("0.####")</MudTd>
                    <MudTd DataLabel="Kaufpreis" Style="text-align:right">@PerShare(row.AcquisitionCosts, row.QuantitySold)</MudTd>
                    <MudTd DataLabel="Verkaufspreis" Style="text-align:right">@PerShare(row.SaleProceeds, row.QuantitySold)</MudTd>
                    <MudTd DataLabel="Kosten" Style="text-align:right">@row.Fees.ToString("N2")</MudTd>
                    <MudTd DataLabel="Roh-PnL" Style="text-align:right">@row.RawAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Verr. Vorab." Style="text-align:right">@row.UsedVorabpauschale.ToString("N2")</MudTd>
                    <MudTd DataLabel="Steuerpfl." Style="text-align:right">@row.TaxableAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Quelle">
                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                   OnClick="() => DrillToSource(row.Isin)">Import</MudButton>
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    };

    private RenderFragment WithholdingTable(IReadOnlyList<GermanTaxEntry> entries) => __builder =>
    {
        if (entries.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="entries" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Datum</MudTh>
                    <MudTh>Herkunft</MudTh>
                    <MudTh>ISIN</MudTh>
                    <MudTh Style="text-align:right">Quellensteuer (€)</MudTh>
                    <MudTh>Quelle</MudTh>
                </HeaderContent>
                <RowTemplate Context="row">
                    <MudTd DataLabel="Datum">@row.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Herkunft">@row.Origin</MudTd>
                    <MudTd DataLabel="ISIN">@row.Isin</MudTd>
                    <MudTd DataLabel="Quellensteuer" Style="text-align:right">@row.ForeignWithholdingTax.ToString("N2")</MudTd>
                    <MudTd DataLabel="Quelle">
                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                   OnClick="() => DrillToSource(row.Isin)">Anzeigen</MudButton>
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    };

    private static string PerShare(decimal total, decimal qty)
        => qty == 0m ? "—" : (total / qty).ToString("N2");
```

- [ ] **Step 5: Add a subtle flash style for the scroll target**

Append to `src/WealthIQ.Web/wwwroot/wealthiq.css`:

```css
.wiq-flash { animation: wiq-flash 1.2s ease-out; }
@keyframes wiq-flash {
    0% { background-color: var(--mud-palette-primary); }
    100% { background-color: transparent; }
}
@media (prefers-reduced-motion: reduce) { .wiq-flash { animation: none; } }
```

- [ ] **Step 6: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`, open Steuerreport:
- Verkäufe "Anzeigen" scrolls to the matching row in "Verkäufe — Details" (which flashes).
- The detail row shows Eröffnet/Geschlossen/Stück/Kaufpreis/Verkaufspreis/Kosten and an "Import" link that opens `/audit` filtered by ISIN.
- The Zinsen table has no ISIN column.
- The Quellensteuer table shows a "Herkunft" column (instrument symbol or "Zinsen").

- [ ] **Step 8: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor src/WealthIQ.Web/wwwroot/wealthiq.js src/WealthIQ.Web/wwwroot/wealthiq.css
git commit -m "Feat: Steuerreport sell-detail drill-down, Zinsen/Quellensteuer column fixes"
```

---

## Task 6: Basiszins store + service `Delete`

**Files:**
- Modify: `src/WealthIQ.Application/Tax/BasisInterestRateRefreshModels.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateStore.cs`
- Modify: `src/WealthIQ.Application/Tax/BasisInterestRateRefreshService.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbBasisInterestRateStoreTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbBasisInterestRateStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbBasisInterestRateStoreTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Delete_RemovesExistingYear()
    {
        using var db = NewDb();
        var store = new DbBasisInterestRateStore(db);
        store.Upsert(2023, 0.0255m);
        store.Upsert(2024, 0.0253m);
        await store.SaveChangesAsync(CancellationToken.None);

        store.Delete(2023);
        await store.SaveChangesAsync(CancellationToken.None);

        Assert.Null(await db.BasisInterestRates.FindAsync(2023));
        Assert.NotNull(await db.BasisInterestRates.FindAsync(2024));
    }

    [Fact]
    public async Task Delete_MissingYear_NoOp()
    {
        using var db = NewDb();
        var store = new DbBasisInterestRateStore(db);

        store.Delete(1999);
        await store.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0, await db.BasisInterestRates.CountAsync());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbBasisInterestRateStoreTests"`
Expected: FAIL — `IBasisInterestRateStore` has no `Delete` (compile error).

- [ ] **Step 3: Add `Delete` to the interface**

In `src/WealthIQ.Application/Tax/BasisInterestRateRefreshModels.cs`:

```csharp
namespace WealthIQ.Application.Tax;

public interface IBasisInterestRateStore
{
    void Upsert(int year, decimal rate);
    void Delete(int year);
    Task SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implement `Delete` in the store**

In `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateStore.cs`, add after `Upsert`:

```csharp
    public void Delete(int year)
    {
        var existing = db.BasisInterestRates.Find(year);
        if (existing is not null)
        {
            db.BasisInterestRates.Remove(existing);
        }
    }
```

- [ ] **Step 5: Add `DeleteAsync` to the refresh service**

In `src/WealthIQ.Application/Tax/BasisInterestRateRefreshService.cs`, add after `SetManualAsync`:

```csharp
    public async Task DeleteAsync(int year, CancellationToken ct)
    {
        store.Delete(year);
        await store.SaveChangesAsync(ct);
    }
```

- [ ] **Step 6: Run the test**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbBasisInterestRateStoreTests"`
Expected: PASS (both cases).

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Application/Tax/BasisInterestRateRefreshModels.cs src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateStore.cs src/WealthIQ.Application/Tax/BasisInterestRateRefreshService.cs tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbBasisInterestRateStoreTests.cs
git commit -m "Feat: Basiszins store/service Delete"
```

---

## Task 7: Editable Basiszins table on the Marktdaten page

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`

- [ ] **Step 1: Add the editable table to the Basiszins panel**

In `DataAdmin.razor`, inside the Basiszins `<MudExpansionPanel ...>`, immediately **before** the final `<MudDivider Class="my-3" />` (the one above the Löschen/Reseed buttons), insert:

```razor
        <MudDivider Class="my-3" />
        <MudText Typo="Typo.subtitle2" Class="mb-2">Gespeicherte Werte</MudText>
        @if (_basiszinsRows.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="_basiszinsRows" Dense="true" Hover="true" Elevation="0"
                      CanCancelEdit="true" RowEditCommit="CommitBasiszinsEdit" T="BasiszinsRow"
                      Class="mb-2" Style="max-width:480px;">
                <HeaderContent>
                    <MudTh>Jahr</MudTh>
                    <MudTh Style="text-align:right">Zinssatz</MudTh>
                    <MudTh></MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="Jahr">@context.Year</MudTd>
                    <MudTd DataLabel="Zinssatz" Style="text-align:right">@context.Rate.ToString("F4")</MudTd>
                    <MudTd Style="text-align:right">
                        <MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error"
                                       Disabled="_busy" OnClick="() => DeleteBasiszins(context.Year)" aria-label="Löschen" />
                    </MudTd>
                </RowTemplate>
                <RowEditingTemplate>
                    <MudTd DataLabel="Jahr">@context.Year</MudTd>
                    <MudTd DataLabel="Zinssatz">
                        <MudNumericField @bind-Value="context.Rate" Format="F4" Variant="Variant.Text" Style="max-width:160px;" />
                    </MudTd>
                    <MudTd></MudTd>
                </RowEditingTemplate>
            </MudTable>
            <MudText Typo="Typo.caption" Style="color:var(--mud-palette-text-secondary);">
                Zeile anklicken zum Bearbeiten des Zinssatzes. Neue Jahre über „Manuell erfassen" oder „Von BMF abrufen".
            </MudText>
        }
```

- [ ] **Step 2: Add the row type, load, edit-commit, and delete handlers**

In the `@code` block of `DataAdmin.razor`, add the field and methods (place the field near the other Basiszins fields, and the methods near `SaveManualBasiszins`):

```csharp
    public sealed class BasiszinsRow
    {
        public int Year { get; set; }
        public decimal Rate { get; set; }
    }

    private List<BasiszinsRow> _basiszinsRows = new();

    private async Task CommitBasiszinsEdit(object element)
    {
        var row = (BasiszinsRow)element;
        _busy = true;
        try
        {
            await BasisRefresh.SetManualAsync(row.Year, row.Rate, default);
            await RefreshLog.RecordAsync("Basiszins", Clock.GetUtcNow(), $"Bearbeitet: Jahr {row.Year} = {row.Rate:F4}");
            ShowSuccess($"Basiszins {row.Year} = {row.Rate:F4} gespeichert.");
            await LoadStatusAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }

    private async Task DeleteBasiszins(int year)
    {
        _busy = true;
        try
        {
            await BasisRefresh.DeleteAsync(year, default);
            await RefreshLog.RecordAsync("Basiszins", Clock.GetUtcNow(), $"Gelöscht: Jahr {year}");
            ShowSuccess($"Basiszins {year} gelöscht.");
            await LoadStatusAsync();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _busy = false; }
    }
```

- [ ] **Step 3: Load the rows in `LoadStatusAsync`**

In `DataAdmin.razor`, inside `LoadStatusAsync`, immediately after the existing Basiszins min/max block (after the `_basiszinsMax = null;` else-branch closes), add:

```csharp
        _basiszinsRows = await Db.BasisInterestRates
            .OrderByDescending(x => x.Year)
            .Select(x => new BasiszinsRow { Year = x.Year, Rate = x.Rate })
            .ToListAsync();
```

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`, open Stammdaten → Marktdaten → Basiszins:
- The "Gespeicherte Werte" table lists all stored year→rate rows.
- Clicking a row lets you edit the rate; committing persists and the table re-renders.
- The delete icon removes a row.
- Adding a new year via "Manuell erfassen" still works and the new row appears.

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/DataAdmin.razor
git commit -m "Feat: editable Basiszins table on Marktdaten page"
```

---

## Task 8: Adjusted-OHLC helper

**Files:**
- Create: `src/WealthIQ.Application/MarketData/AdjustedPriceCalculator.cs`
- Test: `tests/WealthIQ.Tests/Application/MarketData/AdjustedPriceCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Application/MarketData/AdjustedPriceCalculatorTests.cs`:

```csharp
using WealthIQ.Application.MarketData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.MarketData;

public sealed class AdjustedPriceCalculatorTests
{
    private static PriceBar Bar(decimal o, decimal h, decimal l, decimal c, decimal adj)
        => new(new DateOnly(2024, 1, 2), "TEST", CurrencyCode.EUR, o, h, l, c, adj, 0);

    [Fact]
    public void ToAdjusted_ScalesOhlcByAdjustmentFactor()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 100m, 50m) });

        var bar = Assert.Single(result);
        // factor = 50/100 = 0.5
        Assert.Equal(50m, bar.Open);
        Assert.Equal(55m, bar.High);
        Assert.Equal(45m, bar.Low);
        Assert.Equal(50m, bar.Close);
    }

    [Fact]
    public void ToAdjusted_CloseZero_LeavesBarUnscaled()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 0m, 0m) });

        var bar = Assert.Single(result);
        Assert.Equal(100m, bar.Open);
        Assert.Equal(110m, bar.High);
        Assert.Equal(90m, bar.Low);
        Assert.Equal(0m, bar.Close);
    }

    [Fact]
    public void ToAdjusted_NoAdjustment_ReturnsSameValues()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 100m, 100m) });

        var bar = Assert.Single(result);
        Assert.Equal(100m, bar.Open);
        Assert.Equal(110m, bar.High);
        Assert.Equal(90m, bar.Low);
        Assert.Equal(100m, bar.Close);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~AdjustedPriceCalculatorTests"`
Expected: FAIL — `AdjustedPriceCalculator` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

Create `src/WealthIQ.Application/MarketData/AdjustedPriceCalculator.cs`:

```csharp
namespace WealthIQ.Application.MarketData;

/// <summary>
/// Derives split/dividend-adjusted OHLC candles from raw bars that carry a single
/// <see cref="PriceBar.AdjustedClose"/>. For each bar the factor <c>AdjustedClose / Close</c>
/// scales Open/High/Low; Close becomes AdjustedClose. Bars with a non-positive Close are passed
/// through unscaled (no factor can be derived). This is for visual inspection only — the tax
/// engine continues to use raw <c>Close</c>.
/// </summary>
public static class AdjustedPriceCalculator
{
    public static IReadOnlyList<PriceBar> ToAdjusted(IReadOnlyList<PriceBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var result = new List<PriceBar>(bars.Count);
        foreach (var bar in bars)
        {
            if (bar.Close <= 0m)
            {
                result.Add(bar);
                continue;
            }

            var factor = bar.AdjustedClose / bar.Close;
            result.Add(bar with
            {
                Open = bar.Open * factor,
                High = bar.High * factor,
                Low = bar.Low * factor,
                Close = bar.AdjustedClose,
            });
        }

        return result;
    }
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~AdjustedPriceCalculatorTests"`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/MarketData/AdjustedPriceCalculator.cs tests/WealthIQ.Tests/Application/MarketData/AdjustedPriceCalculatorTests.cs
git commit -m "Feat: adjusted-OHLC derivation helper"
```

---

## Task 9: Vendor Lightweight Charts + JS interop + register scripts

**Files:**
- Create: `src/WealthIQ.Web/wwwroot/lib/lightweight-charts/lightweight-charts.standalone.production.js`
- Create: `src/WealthIQ.Web/wwwroot/wiq-charts.js`
- Modify: `src/WealthIQ.Web/Components/App.razor`

- [ ] **Step 1: Vendor the library (v4.2.0)**

Run (downloads the committed offline copy):

```bash
mkdir -p "src/WealthIQ.Web/wwwroot/lib/lightweight-charts"
curl -L -o "src/WealthIQ.Web/wwwroot/lib/lightweight-charts/lightweight-charts.standalone.production.js" \
  https://unpkg.com/lightweight-charts@4.2.0/dist/lightweight-charts.standalone.production.js
```

Verify it is non-empty and exposes the global:

Run: `grep -c "LightweightCharts" "src/WealthIQ.Web/wwwroot/lib/lightweight-charts/lightweight-charts.standalone.production.js"`
Expected: a number ≥ 1.

- [ ] **Step 2: Write the interop wrapper**

Create `src/WealthIQ.Web/wwwroot/wiq-charts.js`:

```javascript
// Thin wrapper around TradingView Lightweight Charts (v4) for Blazor interop.
// Charts are keyed by a string id supplied from C#. The library is loaded as a
// classic script in App.razor, exposing the global `LightweightCharts`.
window.wiqCharts = {
    _charts: {},

    create: function (id, kind, theme) {
        var container = document.getElementById(id);
        if (!container || !window.LightweightCharts) return;
        this.dispose(id);

        var chart = LightweightCharts.createChart(container, {
            autoSize: true,
            layout: { background: { color: 'transparent' }, textColor: theme.textColor },
            grid: { vertLines: { color: theme.gridColor }, horzLines: { color: theme.gridColor } },
            rightPriceScale: { borderColor: theme.gridColor },
            timeScale: { borderColor: theme.gridColor, timeVisible: false },
            crosshair: { mode: 0 }
        });

        var series = kind === 'line'
            ? chart.addLineSeries({ color: theme.lineColor, lineWidth: 2 })
            : chart.addCandlestickSeries({
                upColor: theme.upColor, downColor: theme.downColor,
                borderUpColor: theme.upColor, borderDownColor: theme.downColor,
                wickUpColor: theme.upColor, wickDownColor: theme.downColor
            });

        this._charts[id] = { chart: chart, series: series };
    },

    setData: function (id, data) {
        var entry = this._charts[id];
        if (!entry) return;
        entry.series.setData(data || []);
        entry.chart.timeScale().fitContent();
    },

    applyTheme: function (id, theme) {
        var entry = this._charts[id];
        if (!entry) return;
        entry.chart.applyOptions({
            layout: { textColor: theme.textColor },
            grid: { vertLines: { color: theme.gridColor }, horzLines: { color: theme.gridColor } },
            rightPriceScale: { borderColor: theme.gridColor },
            timeScale: { borderColor: theme.gridColor }
        });
        if (theme.lineColor) entry.series.applyOptions({ color: theme.lineColor });
    },

    dispose: function (id) {
        var entry = this._charts[id];
        if (entry) { try { entry.chart.remove(); } catch (e) { } delete this._charts[id]; }
    }
};
```

- [ ] **Step 3: Reference both scripts in `App.razor`**

In `src/WealthIQ.Web/Components/App.razor`, after the `wealthiq.js` script tag (line ~25), add:

```razor
    <script src="lib/lightweight-charts/lightweight-charts.standalone.production.js"></script>
    <script src="@Assets["wiq-charts.js"]"></script>
```

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Web/wwwroot/lib/lightweight-charts/lightweight-charts.standalone.production.js src/WealthIQ.Web/wwwroot/wiq-charts.js src/WealthIQ.Web/Components/App.razor
git commit -m "Feat: vendor Lightweight Charts and JS interop wrapper"
```

---

## Task 10: `LightweightChart.razor` reusable component

**Files:**
- Create: `src/WealthIQ.Web/Components/Shared/LightweightChart.razor`

- [ ] **Step 1: Create the component**

Create `src/WealthIQ.Web/Components/Shared/LightweightChart.razor`:

```razor
@implements IAsyncDisposable
@inject IJSRuntime JS

<div id="@_id" style="width:100%;height:@Height;"></div>

@code {
    /// <summary>"candlestick" (default) or "line".</summary>
    [Parameter] public string Kind { get; set; } = "candlestick";

    /// <summary>Candlestick points: Time "yyyy-MM-dd", Open/High/Low/Close. Ignored in line mode.</summary>
    [Parameter] public IReadOnlyList<Candle>? Candles { get; set; }

    /// <summary>Line points: Time "yyyy-MM-dd", Value. Ignored in candlestick mode.</summary>
    [Parameter] public IReadOnlyList<LinePoint>? Line { get; set; }

    [Parameter] public bool Dark { get; set; } = true;

    [Parameter] public string Height { get; set; } = "460px";

    public sealed record Candle(string Time, decimal Open, decimal High, decimal Low, decimal Close);
    public sealed record LinePoint(string Time, decimal Value);

    private readonly string _id = $"wiq-chart-{Guid.NewGuid():N}";
    private bool _created;

    private object Theme => Dark
        ? new { textColor = "#7D8AA3", gridColor = "#232D40", upColor = "#34D399", downColor = "#F87171", lineColor = "#34D399" }
        : new { textColor = "#5A6678", gridColor = "#E6EAF0", upColor = "#059669", downColor = "#DC2626", lineColor = "#059669" };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("wiqCharts.create", _id, Kind, Theme);
            _created = true;
            await PushDataAsync();
        }
        else if (_created)
        {
            await JS.InvokeVoidAsync("wiqCharts.applyTheme", _id, Theme);
            await PushDataAsync();
        }
    }

    private async Task PushDataAsync()
    {
        if (Kind == "line")
        {
            var data = (Line ?? Array.Empty<LinePoint>())
                .Select(p => new { time = p.Time, value = p.Value });
            await JS.InvokeVoidAsync("wiqCharts.setData", _id, data);
        }
        else
        {
            var data = (Candles ?? Array.Empty<Candle>())
                .Select(c => new { time = c.Time, open = c.Open, high = c.High, low = c.Low, close = c.Close });
            await JS.InvokeVoidAsync("wiqCharts.setData", _id, data);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("wiqCharts.dispose", _id); }
        catch (JSDisconnectedException) { /* circuit gone — nothing to clean up */ }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Shared/LightweightChart.razor
git commit -m "Feat: reusable LightweightChart Blazor component"
```

---

## Task 11: Navigation group "Daten ansehen"

**Files:**
- Modify: `src/WealthIQ.Web/Components/Layout/MainLayout.razor:26-32`

- [ ] **Step 1: Add the nav group**

In `MainLayout.razor`, between the "Daten erfassen" block (the Import link) and the "Stammdaten" label, insert:

```razor
                <div class="wiq-nav-label">Daten ansehen</div>
                <MudNavLink Href="/browse/ledger" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.TableRows">Ledger</MudNavLink>
                <MudNavLink Href="/browse/prices" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.CandlestickChart">Kurschart</MudNavLink>
                <MudNavLink Href="/browse/fx" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Outlined.Timeline">Wechselkurse</MudNavLink>
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors (routes resolve once the pages in Tasks 12–14 exist; until then the links 404, which is fine).

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Layout/MainLayout.razor
git commit -m "Feat: Data Browser navigation group"
```

---

## Task 12: Ledger Data page

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor`:

```razor
@page "/browse/ledger"
@using WealthIQ.Application.Persistence.Interface
@using WealthIQ.Domain.Enumeration
@using WealthIQ.Domain.Model.General
@using WealthIQ.Domain.Model.Ledger
@inject ILedgerStore LedgerStore
@inject NavigationManager Navigation

<PageTitle>WealthIQ — Ledger</PageTitle>

<PageHeader Title="Ledger" Subtitle="Importierte Buchungen (Originalwährung)" />

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

@if (_loading)
{
    <div style="display:flex;justify-content:center;padding:64px;">
        <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
    </div>
}
else
{
    <div class="wiq-rise">
        <MudExpansionPanels MultiExpansion="true" Elevation="0">
            <MudExpansionPanel Text="@($"Trades ({_trades.Count})")">
                @if (_trades.Count == 0) { <MudText Typo="Typo.body2">Keine Einträge.</MudText> }
                else
                {
                    <MudTable Items="_trades" Dense="true" Hover="true" Elevation="0" Breakpoint="Breakpoint.Sm">
                        <HeaderContent>
                            <MudTh>Datum</MudTh><MudTh>Seite</MudTh><MudTh>Symbol</MudTh><MudTh>ISIN</MudTh>
                            <MudTh Style="text-align:right">Menge</MudTh><MudTh Style="text-align:right">Kurs</MudTh>
                            <MudTh Style="text-align:right">Gebühren</MudTh><MudTh Style="text-align:right">Steuern</MudTh>
                            <MudTh>Whg.</MudTh><MudTh></MudTh>
                        </HeaderContent>
                        <RowTemplate>
                            <MudTd DataLabel="Datum">@context.Date.ToString("yyyy-MM-dd")</MudTd>
                            <MudTd DataLabel="Seite">@context.Side</MudTd>
                            <MudTd DataLabel="Symbol">@context.Symbol</MudTd>
                            <MudTd DataLabel="ISIN">@context.Isin</MudTd>
                            <MudTd DataLabel="Menge" Style="text-align:right">@context.Quantity.ToString("0.####")</MudTd>
                            <MudTd DataLabel="Kurs" Style="text-align:right">@context.UnitPrice.ToString("N4")</MudTd>
                            <MudTd DataLabel="Gebühren" Style="text-align:right">@context.Fees.ToString("N2")</MudTd>
                            <MudTd DataLabel="Steuern" Style="text-align:right">@context.Taxes.ToString("N2")</MudTd>
                            <MudTd DataLabel="Whg.">@context.Currency</MudTd>
                            <MudTd>@SourceLink(context.Isin)</MudTd>
                        </RowTemplate>
                    </MudTable>
                }
            </MudExpansionPanel>

            <MudExpansionPanel Text="@($"Dividenden ({_dividends.Count})")">
                @CashTable(_dividends, showIsin: true)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Zinsen ({_interest.Count})")">
                @CashTable(_interest, showIsin: false)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Quellensteuer ({_withholding.Count})")">
                @CashTable(_withholding, showIsin: true)
            </MudExpansionPanel>
            <MudExpansionPanel Text="@($"Sonstige Buchungen ({_other.Count})")">
                @CashTable(_other, showIsin: true)
            </MudExpansionPanel>
        </MudExpansionPanels>
    </div>
}

@code {
    private bool _loading = true;
    private string? _error;

    private sealed record TradeView(DateOnly Date, TradeSide Side, string Symbol, string Isin,
        decimal Quantity, decimal UnitPrice, decimal Fees, decimal Taxes, Currency Currency);
    private sealed record CashView(DateOnly Date, string Symbol, string Isin, CashFlowType Type,
        decimal GrossAmount, decimal Fees, decimal Taxes, Currency Currency);

    private List<TradeView> _trades = new();
    private List<CashView> _dividends = new();
    private List<CashView> _interest = new();
    private List<CashView> _withholding = new();
    private List<CashView> _other = new();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var ledger = await LedgerStore.LoadLedgerAsync();
            var byId = ledger.Instruments.ToDictionary(i => i.InstrumentId);

            foreach (var entry in ledger.Entries)
            {
                switch (entry)
                {
                    case TradeEntry t:
                        var (ts, ti) = Resolve(t.InstrumentId, byId);
                        _trades.Add(new TradeView(t.EffectiveDate, t.Side, ts, ti,
                            t.Quantity.Value, t.UnitPrice.Amount, t.Fees.Amount, t.Taxes.Amount, t.UnitPrice.Currency));
                        break;
                    case CashEntry c:
                        var srcId = c.RelatedInstrumentId ?? c.CashInstrumentId;
                        var (cs, ci) = Resolve(srcId, byId);
                        var view = new CashView(c.EffectiveDate, cs, ci, c.CashFlowType,
                            c.GrossAmount.Amount, c.Fees.Amount, c.Taxes.Amount, c.GrossAmount.Currency);
                        switch (c.CashFlowType)
                        {
                            case CashFlowType.Dividend: _dividends.Add(view); break;
                            case CashFlowType.Interest: _interest.Add(view); break;
                            case CashFlowType.WithholdingTax: _withholding.Add(view); break;
                            default: _other.Add(view); break;
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _error = $"Ledger konnte nicht geladen werden: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private static (string Symbol, string Isin) Resolve(InstrumentId id, IReadOnlyDictionary<InstrumentId, Instrument> byId)
        => byId.TryGetValue(id, out var i) ? (i.Symbol, i.ISIN) : ("", "");

    private RenderFragment SourceLink(string isin) => __builder =>
    {
        if (!string.IsNullOrWhiteSpace(isin))
        {
            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                       OnClick="() => DrillToSource(isin)">Anzeigen</MudButton>
        }
    };

    private void DrillToSource(string isin)
        => Navigation.NavigateTo($"/audit?isin={Uri.EscapeDataString(isin)}");

    private RenderFragment CashTable(IReadOnlyList<CashView> items, bool showIsin) => __builder =>
    {
        if (items.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="items" Dense="true" Hover="true" Elevation="0" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Datum</MudTh><MudTh>Symbol</MudTh>
                    @if (showIsin) { <MudTh>ISIN</MudTh> }
                    <MudTh Style="text-align:right">Betrag</MudTh>
                    <MudTh Style="text-align:right">Gebühren</MudTh><MudTh Style="text-align:right">Steuern</MudTh>
                    <MudTh>Whg.</MudTh><MudTh></MudTh>
                </HeaderContent>
                <RowTemplate Context="row">
                    <MudTd DataLabel="Datum">@row.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Symbol">@row.Symbol</MudTd>
                    @if (showIsin) { <MudTd DataLabel="ISIN">@row.Isin</MudTd> }
                    <MudTd DataLabel="Betrag" Style="text-align:right">@row.GrossAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Gebühren" Style="text-align:right">@row.Fees.ToString("N2")</MudTd>
                    <MudTd DataLabel="Steuern" Style="text-align:right">@row.Taxes.ToString("N2")</MudTd>
                    <MudTd DataLabel="Whg.">@row.Currency</MudTd>
                    <MudTd>@SourceLink(row.Isin)</MudTd>
                </RowTemplate>
            </MudTable>
        }
    };
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`, open Daten ansehen → Ledger.
Expected: Trades/Dividenden/Zinsen/Quellensteuer/Sonstige panels populate (after an import); Zinsen has no ISIN column; "Anzeigen" opens `/audit` filtered by ISIN.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Browse/LedgerBrowser.razor
git commit -m "Feat: Ledger Data browser page"
```

---

## Task 13: Candlestick price chart page

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor`:

```razor
@page "/browse/prices"
@using Microsoft.EntityFrameworkCore
@using WealthIQ.Application.MarketData
@using WealthIQ.Infrastructure.Persistence
@using WealthIQ.Web.Services
@inject WealthIqDbContext Db
@inject ThemePreferenceService ThemePreference
@using CurrencyCode = WealthIQ.Domain.Enumeration.Currency

<PageTitle>WealthIQ — Kurschart</PageTitle>

<PageHeader Title="Kurschart" Subtitle="Bereinigte Tageskerzen (OHLC)">
    <Actions>
        <MudAutocomplete T="ListingOption" Value="_selected" ValueChanged="OnSymbolChanged"
                         SearchFunc="SearchListings" ToStringFunc="o => o is null ? string.Empty : o.Label"
                         Label="Instrument" Variant="Variant.Outlined" Dense="true"
                         Clearable="true" Style="min-width:320px;" />
    </Actions>
</PageHeader>

<div class="wiq-rise">
    <SectionCard>
        <ChildContent>
            @if (_selected is null)
            {
                <MudText Typo="Typo.body2">Instrument oben auswählen.</MudText>
            }
            else if (_candles.Count == 0)
            {
                <MudText Typo="Typo.body2">Keine Kursdaten für @_selected.Label. Auf der Marktdaten-Seite herunterladen.</MudText>
            }
            else
            {
                <LightweightChart Kind="candlestick" Candles="_candles" Dark="_dark" />
            }
        </ChildContent>
    </SectionCard>
</div>

@code {
    public sealed record ListingOption(string ProviderSymbol, string Isin, string Currency)
    {
        public string Label => $"{ProviderSymbol} — {Isin} ({Currency})";
    }

    private List<ListingOption> _listings = new();
    private ListingOption? _selected;
    private List<LightweightChart.Candle> _candles = new();
    private bool _dark = true;

    protected override async Task OnInitializedAsync()
    {
        _listings = await Db.InstrumentListings
            .Select(x => new ListingOption(x.ProviderSymbol, x.Isin, x.Currency))
            .Where(x => x.ProviderSymbol != "")
            .OrderBy(x => x.ProviderSymbol)
            .ToListAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dark = await ThemePreference.LoadIsDarkAsync();
            StateHasChanged();
        }
    }

    private Task<IEnumerable<ListingOption>> SearchListings(string value, CancellationToken ct)
    {
        IEnumerable<ListingOption> result = string.IsNullOrWhiteSpace(value)
            ? _listings
            : _listings.Where(o =>
                o.ProviderSymbol.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                o.Isin.Contains(value, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(result);
    }

    private async Task OnSymbolChanged(ListingOption? option)
    {
        _selected = option;
        _candles = new();
        if (option is not null)
        {
            var currency = Enum.TryParse<CurrencyCode>(option.Currency, out var c) ? c : CurrencyCode.EUR;
            var rows = await Db.HistoricalPrices
                .Where(x => x.ProviderSymbol == option.ProviderSymbol)
                .OrderBy(x => x.Date)
                .Select(x => new PriceBar(x.Date, x.ProviderSymbol, currency, x.Open, x.High, x.Low, x.Close, x.AdjustedClose, x.Volume))
                .ToListAsync();

            _candles = AdjustedPriceCalculator.ToAdjusted(rows)
                .Select(b => new LightweightChart.Candle(
                    b.Date.ToString("yyyy-MM-dd"),
                    decimal.Round(b.Open, 4), decimal.Round(b.High, 4),
                    decimal.Round(b.Low, 4), decimal.Round(b.Close, 4)))
                .ToList();
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`, open Daten ansehen → Kurschart.
Expected: the autocomplete searches symbol/ISIN; selecting one with downloaded prices renders adjusted daily candles that zoom (mouse wheel) and scroll (drag); theme matches; empty state appears for a listing without bars.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Browse/PriceChart.razor
git commit -m "Feat: candlestick price chart browser page"
```

---

## Task 14: FX line chart page

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Browse/FxChart.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Browse/FxChart.razor`:

```razor
@page "/browse/fx"
@using Microsoft.EntityFrameworkCore
@using WealthIQ.Infrastructure.Persistence
@using WealthIQ.Web.Services
@inject WealthIqDbContext Db
@inject ThemePreferenceService ThemePreference

<PageTitle>WealthIQ — Wechselkurse</PageTitle>

<PageHeader Title="Wechselkurse" Subtitle="Tageskurse (X / EUR)">
    <Actions>
        @if (_currencies.Count > 0)
        {
            <MudSelect T="string" Value="_selected" ValueChanged="OnCurrencyChanged" Label="Währung"
                       Variant="Variant.Outlined" Dense="true" Style="min-width:200px;">
                @foreach (var ccy in _currencies)
                {
                    <MudSelectItem T="string" Value="@ccy">@($"{ccy} / EUR")</MudSelectItem>
                }
            </MudSelect>
        }
    </Actions>
</PageHeader>

<div class="wiq-rise">
    <SectionCard>
        <ChildContent>
            @if (_currencies.Count == 0)
            {
                <MudText Typo="Typo.body2">Keine Wechselkurse vorhanden. Auf der Marktdaten-Seite aktualisieren.</MudText>
            }
            else if (_points.Count == 0)
            {
                <MudText Typo="Typo.body2">Keine Daten für @_selected.</MudText>
            }
            else
            {
                <LightweightChart Kind="line" Line="_points" Dark="_dark" />
            }
        </ChildContent>
    </SectionCard>
</div>

@code {
    private List<string> _currencies = new();
    private string _selected = "";
    private List<LightweightChart.LinePoint> _points = new();
    private bool _dark = true;

    protected override async Task OnInitializedAsync()
    {
        _currencies = await Db.FxRates.Select(x => x.Currency).Distinct().OrderBy(x => x).ToListAsync();
        if (_currencies.Count > 0)
        {
            _selected = _currencies[0];
            await LoadSeriesAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dark = await ThemePreference.LoadIsDarkAsync();
            StateHasChanged();
        }
    }

    private async Task OnCurrencyChanged(string ccy)
    {
        _selected = ccy;
        await LoadSeriesAsync();
    }

    private async Task LoadSeriesAsync()
    {
        _points = await Db.FxRates
            .Where(x => x.Currency == _selected)
            .OrderBy(x => x.Date)
            .Select(x => new LightweightChart.LinePoint(x.Date.ToString("yyyy-MM-dd"), decimal.Round(x.RateToEur, 6)))
            .ToListAsync();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project src/WealthIQ.Web`, open Daten ansehen → Wechselkurse.
Expected: the currency dropdown shows "X / EUR" labels; selecting one renders a daily line that zooms/drags; theme matches; empty states behave.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Browse/FxChart.razor
git commit -m "Feat: FX line chart browser page"
```

---

## Task 15: Full verification & wrap-up

**Files:** none (verification only)

- [ ] **Step 1: Format**

Run: `dotnet format WealthIQ.slnx`
Expected: completes; re-run with `--verify-no-changes` to confirm clean.

- [ ] **Step 2: Full build (Release, as CI does)**

Run: `dotnet build WealthIQ.slnx --configuration Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Full test suite**

Run: `dotnet test WealthIQ.slnx --configuration Release --no-build`
Expected: all tests PASS — including the unchanged `GermanTaxRegressionTests`, plus the new `GermanTaxEntryDetailTests`, `AdjustedPriceCalculatorTests`, and `DbBasisInterestRateStoreTests`.

- [ ] **Step 4: End-to-end manual smoke (all touched pages)**

Run: `dotnet run --project src/WealthIQ.Web` and verify, in one session:
- Steuerreport: donut tooltip rounded; sell-detail drill-down + audit link; Zinsen no ISIN; Quellensteuer Herkunft.
- Daten ansehen → Ledger / Kurschart / Wechselkurse all render and interact.
- Marktdaten → Basiszins editable table edits + deletes.
- Toggle dark/light and confirm charts re-theme on navigation back to a chart page.

- [ ] **Step 5: Update CLAUDE.md**

Add to the "Web UI" section a short note that the dashboard now has a **Daten ansehen** nav group (`/browse/ledger`, `/browse/prices`, `/browse/fx`); charts use vendored **TradingView Lightweight Charts** (`wwwroot/lib/lightweight-charts/`, interop in `wwwroot/wiq-charts.js`, wrapped by `Components/Shared/LightweightChart.razor`); and the Marktdaten Basiszins section has an inline-editable table. Note `GermanTaxEntry` now carries `OpenedOn`/`Fees`/`Origin` for the report drill-down.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md
git commit -m "Docs: note Phase 3 Data Browser, charts, and Basiszins table in CLAUDE.md"
```

---

## Notes for the implementer

- **Layering:** the only `Domain`/`Application` edits are the three `GermanTaxEntry` fields, the `AdjustedPriceCalculator`, and the Basiszins `Delete`. Browser pages live in `Web` and read `WealthIqDbContext`/`ILedgerStore` directly — matching `DataAdmin.razor`/`Audit.razor`. Do not introduce EUR conversion in the Ledger browser; show original currency.
- **Lightweight Charts API is v4** (`chart.addCandlestickSeries` / `chart.addLineSeries`). If you bump the vendored file to v5 the series API changes (`chart.addSeries(CandlestickSeries, …)`) — keep v4 unless you also update `wiq-charts.js`.
- **CI requirement:** the vendored JS file and all `data/test` fixtures must be committed (CI clones clean and runs Release tests with `--no-build` after a Release build).
- **Blazor verify-by-running:** build + xUnit do not catch `.razor` render errors — every UI task includes a manual `dotnet run` step; do not skip it.

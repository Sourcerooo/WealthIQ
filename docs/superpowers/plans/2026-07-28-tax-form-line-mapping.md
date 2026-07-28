# Steuerformular-Zuordnung (Anlage KAP / KAP-INV) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Der Steuerreport zeigt die Werte so aufgegliedert und beschriftet, dass sie 1:1 in die Eingabemaske für Anlage KAP / KAP-INV übertragen werden können.

**Architecture:** Eine neue Instrument-Klassifikation `TaxAssetClass` trägt die Fondsart durch Domain, Persistenz und Referenzdaten. Ein reiner Application-Baustein `TaxFormReportBuilder` übersetzt einen `AnnualTaxReport` in eine Liste beschrifteter Formularzeilen (`TaxFormReport`). Eine gemeinsame Razor-Komponente rendert dieses Modell sowohl auf der Bildschirmseite als auch im PDF-Report. Die Steuermathematik in `GermanTaxCalculator` wird nicht angefasst — mit einer Ausnahme, die über Referenzdaten läuft: ETCs sind keine Investmentfonds und bekommen `SubjectToVorabpauschale = false`.

**Tech Stack:** C# / .NET 10 (`net10.0`), EF Core + SQLite, Blazor Server + MudBlazor, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-28-tax-form-line-mapping-design.md`

## Global Constraints

- Nullable reference types sind aktiviert. Keine `!`-Null-Forgiving-Operatoren; Guard Clauses bevorzugen.
- `decimal` für Geld und Stückzahlen, niemals `double`.
- Englisch für Identifier, Kommentare und Commit-Messages. Formularbeschriftungen und UI-Texte sind Deutsch (es sind Zitate aus dem amtlichen Formular).
- Abhängigkeitsrichtung strikt: `Application → Domain`, `Infrastructure → Application, Domain`, `Web → alle`. Nur `Web` referenziert `Infrastructure`.
- Alle KAP-INV-Beträge sind **`RawAmount`** (vor Teilfreistellung), niemals `TaxableAmount`.
- Zeilennummern gelten für **Formularstand VZ 2025**.
- `record` / `readonly record struct` für unveränderliche wertorientierte Modelle; `IReadOnlyList<T>` über Grenzen hinweg.
- `Components/Shared/`-Komponenten haben keinen `Class`/`Style`-Parameter; äußerer Abstand kommt über ein umgebendes `div`.
- Icon-only `MudIconButton` nutzt `aria-label`, nicht `Title` (MUD0002-Analyzer).
- Nach jedem Task: `dotnet build WealthIQ.slnx` muss fehlerfrei sein, `dotnet format WealthIQ.slnx --verify-no-changes` muss sauber sein.
- Commits nur lokal; **kein `git push`** ohne ausdrückliche Aufforderung.

## Dateiübersicht

**Neu:**
- `src/WealthIQ.Domain/Enumeration/TaxAssetClass.cs` — das Enum
- `src/WealthIQ.Application/Tax/Report/Forms/TaxFormReport.cs` — Modell (`TaxFormReport`, `TaxFormSection`, `TaxFormLine`)
- `src/WealthIQ.Application/Tax/Report/Forms/KapInvRows.cs` — Zeilenschema VZ 2025
- `src/WealthIQ.Application/Tax/Report/Forms/TaxFormReportBuilder.cs` — die Übersetzung
- `src/WealthIQ.Infrastructure/ReferenceData/TaxAssetClassCode.cs` — JSON-/DB-Code ↔ Enum
- `src/WealthIQ.Infrastructure/Persistence/Migrations/*_TaxAssetClass.cs` — Schema + Backfill
- `src/WealthIQ.Web/Components/Shared/TaxFormBlock.razor` — gemeinsamer Renderer
- Tests: `TaxFormReportBuilderTests`, `TaxAssetClassCodeTests`, `TaxAssetClassMigrationTests`, `GermanTaxCalculatorAssetClassTests`

**Geändert:**
- `src/WealthIQ.Domain/Model/General/Instrument.cs` — `AssetClass`
- `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs` — `AssetClass`, `InstrumentName`
- `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` — beide Felder befüllen
- `src/WealthIQ.Application/ReferenceData/InstrumentAdminModels.cs` — `AssetClass` im DTO
- `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`
- `src/WealthIQ.Infrastructure/ReferenceData/{ReferenceDataSeeder,DbInstrumentProfileEnricher,DbInstrumentReferenceAdmin}.cs`
- `src/WealthIQ.Infrastructure/Ibkr/Tax/JsonInstrumentProfileEnricher.cs`
- `src/WealthIQ.Web/Components/Pages/{TaxReportPrint,Steuerreport,InstrumentsAdmin}.razor`
- `src/WealthIQ.Web/wwwroot/{wealthiq.css,steuerreport-print.css}`
- `data/reference/instruments.json`, `data/test/configuration/instruments.json`, `data/test/tradersplace/configuration/instruments.json`, `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json`
- `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs` — Baseline
- `CLAUDE.md`

---

### Task 1: `TaxAssetClass` in Domain und Steuer-Entries

**Files:**
- Create: `src/WealthIQ.Domain/Enumeration/TaxAssetClass.cs`
- Modify: `src/WealthIQ.Domain/Model/General/Instrument.cs`
- Modify: `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs`
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorAssetClassTests.cs`

**Interfaces:**
- Produces: `WealthIQ.Domain.Enumeration.TaxAssetClass` (Werte `Share`, `OtherSecurity`, `EquityFund`, `MixedFund`, `RealEstateFund`, `ForeignRealEstateFund`, `OtherFund`); `Instrument.AssetClass` vom Typ `TaxAssetClass?`; `GermanTaxEntry.AssetClass` vom Typ `TaxAssetClass?` und `GermanTaxEntry.InstrumentName` vom Typ `string`.

**Wichtig zur Benennung:** Das Enum heißt `TaxAssetClass`, das Property überall `AssetClass`. Ein Property mit demselben Namen wie sein Typ ist zwar legal, macht aber Typverweise innerhalb des Records mehrdeutig — deshalb der abweichende Membername.

- [ ] **Step 1: Schreibe den fehlschlagenden Test**

Neue Datei `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorAssetClassTests.cs`. Das Szenario ist bewusst dasselbe wie in `GermanTaxCalculatorTests.Calculate_BuyDividendVorabAndSell_ProducesExpectedTaxEntries` — Kauf, Dividende, Vorabpauschale zum Jahreswechsel, Verkauf — damit alle drei Entry-Typen in einem Lauf entstehen. Die vier Stub-Klassen sind in `GermanTaxCalculatorTests` `private` und daher nicht wiederverwendbar; sie werden hier wörtlich kopiert (Quelle: `GermanTaxCalculatorTests.cs:204-246`).

```csharp
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Tax;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

public sealed class GermanTaxCalculatorAssetClassTests
{
    [Fact]
    public void Calculate_ClassifiedFund_CopiesAssetClassAndNameOntoEveryEntry()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "Test Equity Fund", 0.30m)
            {
                SubjectToVorabpauschale = true,
                AssetClass = TaxAssetClass.EquityFund
            }
        };

        var result = Run(instruments, instrumentId);

        // Dividend, Vorabpauschale and Sell — every entry the fund produces.
        Assert.Equal(3, result.Entries.Count);
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(TaxAssetClass.EquityFund, entry.AssetClass);
            Assert.Equal("Test Equity Fund", entry.InstrumentName);
        });
    }

    [Fact]
    public void Calculate_UnclassifiedInstrument_LeavesAssetClassNull()
    {
        var instrumentId = InstrumentId.NewId();
        var instruments = new[]
        {
            new Instrument(instrumentId, "IE00B6R52259", "ACWI", "Unclassified Fund", 0.30m)
            {
                SubjectToVorabpauschale = true
            }
        };

        var result = Run(instruments, instrumentId);

        // The calculator must not invent a classification.
        Assert.All(result.Entries, entry => Assert.Null(entry.AssetClass));
    }

    private static GermanTaxCalculationResult Run(IReadOnlyList<Instrument> instruments, InstrumentId instrumentId)
    {
        var accountId = AccountId.NewId();

        var calculator = new GermanTaxCalculator(
            new StubInterestRateProvider((2024, 0.025m)),
            new StubYearStartAndEndPriceProvider(("IE00B6R52259", 2024, 100m, 120m)),
            new StubFxRateLookup());

        return calculator.Calculate(new PortfolioLedger([
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 1, 15),
                CreateSourceProvenance("BUY-1"),
                instrumentId,
                TradeSide.Buy,
                new Quantity(10m),
                new Money(100m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR)),
            new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero),
                new DateOnly(2024, 6, 10),
                CreateSourceProvenance("DIV-1"),
                InstrumentId.NewId(),
                CashFlowType.Dividend,
                new Money(5m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                instrumentId),
            new TradeEntry(
                PortfolioEntryId.NewId(),
                accountId,
                new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero),
                new DateOnly(2025, 2, 1),
                CreateSourceProvenance("SELL-1"),
                instrumentId,
                TradeSide.Sell,
                new Quantity(10m),
                new Money(130m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR),
                new Money(0m, CurrencyCode.EUR))
        ]), instruments);
    }

    private sealed class StubInterestRateProvider(params (int Year, decimal Rate)[] rates) : IBasisInterestRateProvider
    {
        private readonly Dictionary<int, decimal> _rates = rates.ToDictionary(x => x.Year, x => x.Rate);

        public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
    }

    private sealed class StubYearStartAndEndPriceProvider(params (string Isin, int Year, decimal Start, decimal End)[] prices) : IInstrumentPriceProvider
    {
        public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
        {
            var entry = prices.FirstOrDefault(p => p.Isin == isin && p.Year == pricingDate.Year);
            if (entry == default) return null;
            var price = handling == PriceQuoteHandling.EarliestOnOrAfter ? entry.Start : entry.End;
            return new InstrumentQuote(price, CurrencyCode.EUR, pricingDate);
        }
    }

    private sealed class StubFxRateLookup : IFxRateLookup
    {
        public decimal GetRate(DateOnly conversionDate, CurrencyCode sourceCurrency, CurrencyCode targetCurrency, FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
            => sourceCurrency == targetCurrency && targetCurrency == CurrencyCode.EUR ? 1m : throw new InvalidOperationException("Unexpected FX lookup in unit test.");
    }

    private static SourceProvenance CreateSourceProvenance(string sourceReference)
        => new()
        {
            SourceSystem = "IBKR",
            ImportFormat = "TEST",
            SourceLocation = "unit-test",
            SourceRecordReference = sourceReference
        };
}
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxCalculatorAssetClassTests"
```

Erwartet: Compile-Fehler „'GermanTaxEntry' does not contain a definition for 'AssetClass'".

- [ ] **Step 3: Enum anlegen**

`src/WealthIQ.Domain/Enumeration/TaxAssetClass.cs`:

```csharp
namespace WealthIQ.Domain.Enumeration;

/// <summary>
/// The asset class a German tax return distinguishes. Anlage KAP-INV calls it
/// "Art des Investmentfonds (Assetklasse)" and derives the Teilfreistellung rate from it;
/// non-fund securities are declared on Anlage KAP instead.
///
/// This drives ONLY the form-line mapping in the report. The tax-effective rate stays
/// <see cref="WealthIQ.Domain.Model.General.Instrument.Teilfreistellungsquote"/> — the
/// typical rate noted per member is orientation, not a source of truth.
/// </summary>
public enum TaxAssetClass
{
    /// <summary>Single share, § 20 Abs. 2 Satz 1 Nr. 1 EStG. Anlage KAP Zeile 19 and 20.</summary>
    Share,

    /// <summary>ETC, bond, certificate — not an investment fund. Anlage KAP Zeile 19.</summary>
    OtherSecurity,

    /// <summary>Aktienfonds, typically 30 % Teilfreistellung. KAP-INV Zeilen 4 / 9 / 14.</summary>
    EquityFund,

    /// <summary>Mischfonds, typically 15 %. KAP-INV Zeilen 5 / 10 / 17.</summary>
    MixedFund,

    /// <summary>Immobilienfonds, typically 60 %. KAP-INV Zeilen 6 / 11 / 20.</summary>
    RealEstateFund,

    /// <summary>Auslands-Immobilienfonds, typically 80 %. KAP-INV Zeilen 7 / 12 / 23.</summary>
    ForeignRealEstateFund,

    /// <summary>Sonstiger Investmentfonds, typically 0 %. KAP-INV Zeilen 8 / 13 / 26.</summary>
    OtherFund
}
```

- [ ] **Step 4: `Instrument` erweitern**

In `src/WealthIQ.Domain/Model/General/Instrument.cs` innerhalb des `Instrument`-Records ergänzen (nach `SubjectToVorabpauschale`), und `using WealthIQ.Domain.Enumeration;` oben hinzufügen:

```csharp
    /// <summary>Which asset class the German tax forms put this instrument in. Set explicitly by the
    /// profile; there is no inference. <c>null</c> = not yet enriched / no profile on file.
    /// Drives only the report's form-line mapping, never the tax math.</summary>
    public TaxAssetClass? AssetClass { get; init; }
```

- [ ] **Step 5: `GermanTaxEntry` erweitern**

In `src/WealthIQ.Domain/Model/Tax/GermanTaxEntry.cs` die beiden Parameter **ganz am Ende** der Parameterliste ergänzen (nach `decimal WithheldKESt = 0m`), damit alle bestehenden Aufrufe mit Positionsargumenten unverändert bleiben:

```csharp
    AccountId AccountId = default,
    decimal WithheldKESt = 0m,
    // --- Form-line mapping (display/aggregation only, never tax math) ---
    // Anlage KAP-INV needs the fund category per line and the fund name in the Ermittlung.
    TaxAssetClass? AssetClass = null,
    string InstrumentName = "");
```

- [ ] **Step 6: Kalkulator befüllt die Felder**

In `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs` bei **jedem** der fünf `ledger.Add(new GermanTaxEntry(...))`-Aufrufe die beiden benannten Argumente ergänzen. Konkret:

- Sell (ca. Zeile 119): nach `WithheldKESt: kestSlice` → `, AssetClass: instrument.AssetClass, InstrumentName: instrument.Name`
- Dividend (ca. Zeile 163): nach `AccountId: cashEntry.AccountId` → `, AssetClass: dividendInstrument.AssetClass, InstrumentName: dividendInstrument.Name`
- Interest (ca. Zeile 198): nach `AccountId: cashEntry.AccountId` → `, AssetClass: interestInstrument.AssetClass, InstrumentName: interestInstrument.Name`
- WithholdingTax (ca. Zeile 225): nach `AccountId: cashEntry.AccountId` → `, AssetClass: withholdingInstrument.AssetClass, InstrumentName: withholdingInstrument.Name`
- Vorabpauschale (ca. Zeile 335): nach `AccountId: lot.AccountId` → `, AssetClass: instrument.AssetClass, InstrumentName: instrument.Name`

Keine andere Zeile in dieser Datei ändern.

- [ ] **Step 7: Tests laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxCalculatorAssetClassTests"
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~Tax"
```

Erwartet: alles PASS. Die bestehenden Steuer-Tests dürfen sich nicht verändern — die neuen Felder sind rein additiv.

- [ ] **Step 8: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Domain src/WealthIQ.Application tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorAssetClassTests.cs
git commit -m "feat: carry TaxAssetClass and instrument name on tax entries"
```

---

### Task 2: Persistenz-Spalte und Migration mit ETC-Korrektur

**Files:**
- Create: `src/WealthIQ.Infrastructure/ReferenceData/TaxAssetClassCode.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Migrations/<timestamp>_TaxAssetClass.cs` (per `dotnet ef`)
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/TaxAssetClassCodeTests.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/TaxAssetClassMigrationTests.cs`

**Interfaces:**
- Consumes: `TaxAssetClass` aus Task 1.
- Produces: `TaxAssetClassCode.Parse(string?) → TaxAssetClass?` und `TaxAssetClassCode.ToCode(TaxAssetClass?) → string?`; `InstrumentProfileRow.TaxAssetClass` vom Typ `string?` (der Code, nicht das Enum).

- [ ] **Step 1: Schreibe den fehlschlagenden Parser-Test**

`tests/WealthIQ.Tests/Infrastructure/ReferenceData/TaxAssetClassCodeTests.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class TaxAssetClassCodeTests
{
    [Theory]
    [InlineData("share", TaxAssetClass.Share)]
    [InlineData("other_security", TaxAssetClass.OtherSecurity)]
    [InlineData("equity_fund", TaxAssetClass.EquityFund)]
    [InlineData("mixed_fund", TaxAssetClass.MixedFund)]
    [InlineData("real_estate_fund", TaxAssetClass.RealEstateFund)]
    [InlineData("foreign_real_estate_fund", TaxAssetClass.ForeignRealEstateFund)]
    [InlineData("other_fund", TaxAssetClass.OtherFund)]
    public void Parse_KnownCode_ReturnsMatchingMember(string code, TaxAssetClass expected)
        => Assert.Equal(expected, TaxAssetClassCode.Parse(code));

    [Theory]
    [InlineData("EQUITY_FUND")]
    [InlineData("  equity_fund  ")]
    public void Parse_CodeWithDifferentCasingOrPadding_StillResolves(string code)
        => Assert.Equal(TaxAssetClass.EquityFund, TaxAssetClassCode.Parse(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingCode_ReturnsNull(string? code)
        => Assert.Null(TaxAssetClassCode.Parse(code));

    [Fact]
    public void Parse_UnknownCode_ThrowsNamingTheCode()
    {
        var ex = Assert.Throws<ArgumentException>(() => TaxAssetClassCode.Parse("hedge_fund"));
        Assert.Contains("hedge_fund", ex.Message);
    }

    [Fact]
    public void ToCode_EveryMember_RoundTripsThroughParse()
    {
        foreach (var member in Enum.GetValues<TaxAssetClass>())
        {
            Assert.Equal(member, TaxAssetClassCode.Parse(TaxAssetClassCode.ToCode(member)));
        }
    }

    [Fact]
    public void ToCode_Null_ReturnsNull() => Assert.Null(TaxAssetClassCode.ToCode(null));
}
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxAssetClassCodeTests"
```

Erwartet: Compile-Fehler „The name 'TaxAssetClassCode' does not exist".

- [ ] **Step 3: Parser implementieren**

`src/WealthIQ.Infrastructure/ReferenceData/TaxAssetClassCode.cs`:

```csharp
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Translates between <see cref="TaxAssetClass"/> and the snake_case code stored in
/// instruments.json and in the InstrumentProfiles table. A stable code keeps reference files
/// readable and survives renaming the enum members.
/// </summary>
public static class TaxAssetClassCode
{
    private static readonly Dictionary<string, TaxAssetClass> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["share"] = TaxAssetClass.Share,
        ["other_security"] = TaxAssetClass.OtherSecurity,
        ["equity_fund"] = TaxAssetClass.EquityFund,
        ["mixed_fund"] = TaxAssetClass.MixedFund,
        ["real_estate_fund"] = TaxAssetClass.RealEstateFund,
        ["foreign_real_estate_fund"] = TaxAssetClass.ForeignRealEstateFund,
        ["other_fund"] = TaxAssetClass.OtherFund
    };

    private static readonly Dictionary<TaxAssetClass, string> ToCodeMap =
        ByCode.ToDictionary(x => x.Value, x => x.Key);

    /// <summary>Empty/absent input means "not classified" and stays <c>null</c>; an unknown code
    /// is a data error and fails loudly rather than defaulting to a category.</summary>
    public static TaxAssetClass? Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (ByCode.TryGetValue(code.Trim(), out var value))
        {
            return value;
        }

        throw new ArgumentException(
            $"Unknown tax asset class code '{code}'. Expected one of: {string.Join(", ", ByCode.Keys)}.",
            nameof(code));
    }

    public static string? ToCode(TaxAssetClass? value)
        => value is null ? null : ToCodeMap[value.Value];
}
```

- [ ] **Step 4: Parser-Test laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxAssetClassCodeTests"
```

Erwartet: PASS.

- [ ] **Step 5: Spalte auf der Row ergänzen**

`src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentProfileRow
{
    public string Isin { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
    public bool SubjectToVorabpauschale { get; set; }

    /// <summary>Snake_case code of <see cref="WealthIQ.Domain.Enumeration.TaxAssetClass"/>;
    /// <c>null</c> when the profile has not been classified yet. See <c>TaxAssetClassCode</c>.</summary>
    public string? TaxAssetClass { get; set; }
}
```

- [ ] **Step 6: Migration erzeugen**

```bash
dotnet ef migrations add TaxAssetClass --project src/WealthIQ.Infrastructure
```

- [ ] **Step 7: Backfill in die Migration schreiben**

Die generierte Migration enthält nur `AddColumn`. Ergänze im `Up` **nach** dem `AddColumn` folgende SQL-Blöcke, und im `Down` nichts außer dem generierten `DropColumn` (die `SubjectToVorabpauschale`-Korrektur wird bewusst nicht zurückgenommen — sie ist eine Datenkorrektur, kein Schema-Detail; vermerke das als Kommentar):

```csharp
            // Backfill the new classification from the free-text Type column. This is a one-time
            // data migration, not runtime inference: the fail-fast rule "explicit profile, no
            // derivation" still holds at report time for anything left NULL here.
            migrationBuilder.Sql(@"
                UPDATE InstrumentProfiles SET TaxAssetClass = 'equity_fund'   WHERE Type = 'ETF_EQUITY';
                UPDATE InstrumentProfiles SET TaxAssetClass = 'other_fund'    WHERE Type IN ('ETF_BOND', 'ETF_MONEY_MARKET');
                UPDATE InstrumentProfiles SET TaxAssetClass = 'share'         WHERE Type = 'STOCK';
            ");

            // An ETC is a secured debt security with limited recourse, not an investment fund.
            // The InvStG does not apply: no Vorabpauschale, no Teilfreistellung, and its gains are
            // declared on Anlage KAP Zeile 19 rather than KAP-INV. Profiles that claimed otherwise
            // (IE00B4ND3602) are corrected here. Deliberately NOT reverted in Down(): this is a
            // data correction, and re-introducing the wrong flag on a rollback would be worse than
            // leaving it right.
            migrationBuilder.Sql(@"
                UPDATE InstrumentProfiles
                   SET TaxAssetClass = 'other_security',
                       SubjectToVorabpauschale = 0
                 WHERE Type = 'ETC';
            ");
```

- [ ] **Step 8: Schreibe den fehlschlagenden Migrationstest**

`tests/WealthIQ.Tests/Infrastructure/Persistence/TaxAssetClassMigrationTests.cs`. Der Test migriert eine frische SQLite-Datei zunächst auf die **vorherige** Migration, schreibt Zeilen im alten Schema und migriert dann weiter.

Der Name der Vormigration ist `DividendAliases` (siehe `src/WealthIQ.Infrastructure/Persistence/Migrations/20260606194726_DividendAliases.cs`). Wenn zwischenzeitlich eine weitere Migration dazugekommen ist, nimm die jeweils letzte vor `TaxAssetClass`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class TaxAssetClassMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wiq-migration-{Guid.NewGuid():N}.db");

    private WealthIqDbContext CreateContext()
        => new(new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options);

    [Theory]
    [InlineData("ETF_EQUITY", true, "equity_fund", true)]
    [InlineData("ETF_BOND", true, "other_fund", true)]
    [InlineData("ETF_MONEY_MARKET", true, "other_fund", true)]
    [InlineData("STOCK", false, "share", false)]
    [InlineData("ETC", true, "other_security", false)]
    [InlineData("ETC", false, "other_security", false)]
    [InlineData("SOMETHING_ELSE", true, null, true)]
    public async Task Migrate_BackfillsTaxAssetClassFromTypeAndClearsVorabpauschaleForEtcs(
        string type, bool subjectBefore, string? expectedClass, bool expectedSubjectAfter)
    {
        await using (var db = CreateContext())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("DividendAliases");

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO InstrumentProfiles (Isin, Name, Type, Teilfreistellungsquote, SubjectToVorabpauschale) " +
                "VALUES ('TEST0000001', 'Probe', {0}, 0.0, {1});",
                type, subjectBefore ? 1 : 0);
        }

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();

            var row = await db.InstrumentProfiles.SingleAsync(x => x.Isin == "TEST0000001");
            Assert.Equal(expectedClass, row.TaxAssetClass);
            Assert.Equal(expectedSubjectAfter, row.SubjectToVorabpauschale);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

Falls `IMigrator`/`GetService` nicht auflösbar ist, fehlt `using Microsoft.EntityFrameworkCore.Infrastructure;` — ergänze es.

- [ ] **Step 9: Migrationstest laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxAssetClassMigrationTests"
```

Erwartet: PASS. Falls der Backfill nicht greift, prüfe zuerst, ob die SQL-Statements tatsächlich im `Up` **nach** dem `AddColumn` stehen.

- [ ] **Step 10: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Infrastructure tests/WealthIQ.Tests/Infrastructure
git commit -m "feat: persist TaxAssetClass and correct ETC profiles in migration"
```

---

### Task 3: Referenzdaten und Admin-Pfad tragen die Klassifikation

**Files:**
- Modify: `src/WealthIQ.Application/ReferenceData/InstrumentAdminModels.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Tax/JsonInstrumentProfileEnricher.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentReferenceAdmin.cs`
- Modify: `data/reference/instruments.json`
- Modify: `data/test/configuration/instruments.json`
- Modify: `data/test/tradersplace/configuration/instruments.json`
- Modify: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json`
- Test: `tests/WealthIQ.Tests/Infrastructure/Tax/JsonInstrumentProfileEnricherTests.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs`

**Interfaces:**
- Consumes: `TaxAssetClassCode` und `InstrumentProfileRow.TaxAssetClass` aus Task 2, `Instrument.AssetClass` aus Task 1.
- Produces: JSON-Schlüssel `tax_asset_class`; `InstrumentAdminDto` bekommt einen neuen Parameter `TaxAssetClass? AssetClass` **vor** `IReadOnlyList<InstrumentListingDto> Listings`.

**Abgrenzung zu Task 4:** In diesem Task bleibt `subject_to_vorabpauschale` in **allen** JSON-Dateien unverändert. `IE00B4ND3602` bekommt hier `"tax_asset_class": "other_security"`, behält aber vorerst `"subject_to_vorabpauschale": true`. Das ist ein bewusst zweistufiges Vorgehen: die Datenkorrektur samt Baseline-Update ist Task 4 und soll getrennt review-bar sein. `GermanTaxRegressionTests` muss nach diesem Task noch grün sein.

- [ ] **Step 1: Schreibe den fehlschlagenden Enricher-Test**

Öffne `tests/WealthIQ.Tests/Infrastructure/Tax/JsonInstrumentProfileEnricherTests.cs` und lies, wie dort Profile-JSON bereitgestellt wird (temporäre Datei oder Fixture). Ergänze im selben Stil:

```csharp
    [Fact]
    public void Enrich_ProfileWithTaxAssetClass_AppliesItToTheInstrument()
    {
        // instruments JSON containing:
        //   "IE00B3XXRP09": { "name": "...", "type": "ETF_EQUITY", "tfs_quote": 0.30,
        //                     "subject_to_vorabpauschale": true, "tax_asset_class": "equity_fund" }
        var enriched = enricher.Enrich(instrumentWithIsin("IE00B3XXRP09"));

        Assert.Equal(TaxAssetClass.EquityFund, enriched.AssetClass);
    }

    [Fact]
    public void Enrich_ProfileWithoutTaxAssetClass_LeavesAssetClassNull()
    {
        // Same profile, but with the "tax_asset_class" key absent.
        var enriched = enricher.Enrich(instrumentWithIsin("IE00B3XXRP09"));

        Assert.Null(enriched.AssetClass);
    }
```

Ergänze die gleichen zwei Fälle in `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs`, dort über eine `InstrumentProfileRow` mit gesetztem bzw. leerem `TaxAssetClass`.

Ergänze außerdem je einen Rundlauf-Test für Seeder und Admin. Lies zuerst `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs` und `DbInstrumentReferenceAdminTests.cs` und folge dem dortigen Aufbau (In-Memory- bzw. temporäre SQLite-DB, Fixture-Pfade):

```csharp
    // In ReferenceDataSeederTests
    [Fact]
    public async Task SeedAsync_ProfileWithTaxAssetClass_StoresTheRawCode()
    {
        // Seed from the Fixtures/instruments.json, which carries "tax_asset_class" after this task.
        var row = await db.InstrumentProfiles.SingleAsync(x => x.Isin == "IE00B3XXRP09");

        Assert.Equal("equity_fund", row.TaxAssetClass);
    }
```

```csharp
    // In DbInstrumentReferenceAdminTests
    [Fact]
    public async Task SaveAsync_ThenListAsync_RoundTripsTheAssetClass()
    {
        await admin.SaveAsync(new InstrumentAdminDto(
            "TESTISIN0001", "Probe", "ETF_EQUITY", 0.30m, true, TaxAssetClass.EquityFund, []));

        var listed = (await admin.ListAsync()).Single(x => x.Isin == "TESTISIN0001");

        Assert.Equal(TaxAssetClass.EquityFund, listed.AssetClass);
    }

    [Fact]
    public async Task SaveAsync_WithoutAssetClass_RoundTripsAsNull()
    {
        await admin.SaveAsync(new InstrumentAdminDto(
            "TESTISIN0002", "Probe", "SOMETHING", 0m, false, null, []));

        var listed = (await admin.ListAsync()).Single(x => x.Isin == "TESTISIN0002");

        Assert.Null(listed.AssetClass);
    }
```

Diese Tests brechen zunächst am Compiler (das DTO hat den Parameter noch nicht) — das ist der gewünschte Fehlschlag.

**Achtung:** Die bestehenden Aufrufe von `new InstrumentAdminDto(...)` in `DbInstrumentReferenceAdminTests.cs` und `ClearServiceTests.cs` bekommen durch den neuen Parameter einen Compile-Fehler. Ergänze dort jeweils `null` an der richtigen Position (vor der Listings-Liste); ändere keine Assertion.

- [ ] **Step 2: Tests laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~InstrumentProfileEnricherTests"
```

Erwartet: Compile-Fehler „'Instrument' does not contain a definition for 'AssetClass'" existiert **nicht** mehr (Task 1), stattdessen FAIL, weil `AssetClass` null bleibt.

- [ ] **Step 3: `JsonInstrumentProfileEnricher` erweitern**

Drei Änderungen in `src/WealthIQ.Infrastructure/Ibkr/Tax/JsonInstrumentProfileEnricher.cs`:

DTO um den Schlüssel erweitern:

```csharp
        [JsonPropertyName("tax_asset_class")]
        public string? TaxAssetClass { get; init; }
```

Internes Record erweitern:

```csharp
    private sealed record InstrumentProfile(
        string Name, string Type, decimal Teilfreistellungsquote,
        bool SubjectToVorabpauschale, TaxAssetClass? AssetClass)
    {
        public string SymbolFallback => "Unknown";
    }
```

`Load` und `Enrich` anpassen:

```csharp
            _profiles[isin] = new InstrumentProfile(
                profile.Name, profile.Type, teilfreistellungsquote, profile.SubjectToVorabpauschale,
                WealthIQ.Infrastructure.ReferenceData.TaxAssetClassCode.Parse(profile.TaxAssetClass));
```

```csharp
                SubjectToVorabpauschale = profile.SubjectToVorabpauschale,
                AssetClass = profile.AssetClass,
```

Ergänze `using WealthIQ.Domain.Enumeration;` oben.

- [ ] **Step 4: `DbInstrumentProfileEnricher` erweitern**

Das Tupel im Dictionary um ein Feld erweitern und beim `with` durchreichen:

```csharp
    private readonly Dictionary<string, (string Name, string Type, decimal Teilfreistellungsquote, bool SubjectToVorabpauschale, TaxAssetClass? AssetClass)> _profiles;

    public DbInstrumentProfileEnricher(WealthIqDbContext db)
    {
        _profiles = db.InstrumentProfiles.ToDictionary(
            x => x.Isin,
            x => (x.Name, x.Type, x.Teilfreistellungsquote, x.SubjectToVorabpauschale,
                  TaxAssetClassCode.Parse(x.TaxAssetClass)),
            StringComparer.OrdinalIgnoreCase);
    }
```

und im `Enrich`-`with`-Block nach `SubjectToVorabpauschale = profile.SubjectToVorabpauschale,` ergänzen:

```csharp
                AssetClass = profile.AssetClass,
```

Ergänze `using WealthIQ.Domain.Enumeration;`.

- [ ] **Step 5: Seeder und Admin erweitern**

`ReferenceDataSeeder.cs` — im DTO (ca. Zeile 236) ergänzen:

```csharp
        [JsonPropertyName("tax_asset_class")]
        public string? TaxAssetClass { get; init; }
```

und in `ReadInstrumentProfiles` (ca. Zeile 140) die Row-Erzeugung ergänzen:

```csharp
                SubjectToVorabpauschale = dto.SubjectToVorabpauschale,
                TaxAssetClass = dto.TaxAssetClass
```

Die Codes werden hier bewusst **nicht** geparst, sondern roh gespeichert — die Validierung passiert beim Lesen im Enricher. Wenn ein unbekannter Code in der Seed-Datei steht, schlägt der erste Report laut fehl, was gewollt ist.

`InstrumentAdminModels.cs` — DTO erweitern:

```csharp
public sealed record InstrumentAdminDto(
    string Isin,
    string Name,
    string Type,
    decimal Teilfreistellungsquote,
    bool SubjectToVorabpauschale,
    TaxAssetClass? AssetClass,
    IReadOnlyList<InstrumentListingDto> Listings);
```

mit `using WealthIQ.Domain.Enumeration;` oben.

`DbInstrumentReferenceAdmin.cs` — vier Stellen:

1. `ListAsync` (ca. Zeile 22):
```csharp
        return profiles.Select(p => new InstrumentAdminDto(
            p.Isin, p.Name, p.Type, p.Teilfreistellungsquote, p.SubjectToVorabpauschale,
            TaxAssetClassCode.Parse(p.TaxAssetClass),
            listingsByIsin.TryGetValue(p.Isin, out var lst)
                ? lst.Select(MapListing).ToList()
                : []))
            .ToList();
```

2. `SaveAsync` — beim `Add` `TaxAssetClass = TaxAssetClassCode.ToCode(dto.AssetClass)` ergänzen und beim `else`-Zweig `existing.TaxAssetClass = TaxAssetClassCode.ToCode(dto.AssetClass);`.

3. `UploadAsync` — im privaten `InstrumentProfileDto` (ca. Zeile 185) `[JsonPropertyName("tax_asset_class")] public string? TaxAssetClass { get; init; }` ergänzen; beim `Add` `TaxAssetClass = dto.TaxAssetClass` und im `else`-Zweig `existing.TaxAssetClass = dto.TaxAssetClass;`.

- [ ] **Step 6: JSON-Referenzdateien ergänzen**

In **allen vier** Dateien bekommt jedes Profil einen `tax_asset_class`-Schlüssel nach dieser Regel:

| `type` | `tax_asset_class` |
|---|---|
| `ETF_EQUITY` | `equity_fund` |
| `ETF_BOND`, `ETF_MONEY_MARKET` | `other_fund` |
| `ETC` | `other_security` |
| `STOCK` | `share` |

Beispiel für `data/reference/instruments.json`:

```json
  "IE00B3XXRP09": {
    "name": "Vanguard S&P 500 UCITS ETF",
    "type": "ETF_EQUITY",
    "tfs_quote": 0.30,
    "subject_to_vorabpauschale": true,
    "tax_asset_class": "equity_fund"
  },
  "IE00B4ND3602": {
    "name": "iShares Physical Gold ETC",
    "type": "ETC",
    "tfs_quote": 0.00,
    "subject_to_vorabpauschale": true,
    "tax_asset_class": "other_security"
  },
```

`subject_to_vorabpauschale` bleibt in diesem Task überall unverändert — auch bei den ETCs. Prüfe die Trader's-Place-Fixture und die Test-Fixture unter `tests/.../Fixtures/instruments.json` und ergänze dort ebenso; wenn ein `type` dort nicht in der Tabelle steht, lass `tax_asset_class` weg und notiere es im Commit.

- [ ] **Step 7: Tests laufen lassen**

```
dotnet test WealthIQ.slnx
```

Erwartet: alles PASS, **einschließlich** `GermanTaxRegressionTests` und `TradersPlaceRegressionTests`. Wenn die Regressionstests hier rot werden, wurde versehentlich ein `subject_to_vorabpauschale` verändert — rückgängig machen.

- [ ] **Step 8: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src data tests
git commit -m "feat: carry tax_asset_class through reference data and admin"
```

---

### Task 4: ETC-Datenkorrektur und Golden-Baseline

**Files:**
- Modify: `data/reference/instruments.json`
- Modify: `data/test/configuration/instruments.json`
- Modify: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json`
- Modify: `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs`

**Zur Test-Fixture:** `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json` enthält `IE00B4ND3602` und wird von `ReferenceDataSeederTests`, `DbInstrumentReferenceAdminTests` und `ClearServiceTests` gelesen. Setze `subject_to_vorabpauschale` auch dort auf `false` und prüfe, ob eine Assertion in diesen Tests den alten Wert erwartet — wenn ja, ziehe sie mit nach und begründe es im Commit.

**Interfaces:**
- Consumes: die JSON-Struktur aus Task 3.
- Produces: nichts für spätere Tasks.

**Hintergrund:** Ein ETC ist keine Investmentanteil, das InvStG greift nicht — also keine Vorabpauschale. In den Fixtures ist `IE00B4ND3602` (Symbol `IGLN`) betroffen: gekauft 512 (2021-07-01), 400 (2021-12-09), 416 (2022-02-17), verkauft 498 (2022-03-22) und 830 (2024-06-28). Nur das Jahresende 2023 erzeugt eine Vorabpauschale (Basiszins 2,55 %); 2021 und 2022 hatten negativen Basiszins, Ende 2024 war die Position geschlossen.

- [ ] **Step 1: `subject_to_vorabpauschale` auf `false` setzen**

In `data/reference/instruments.json` und `data/test/configuration/instruments.json` für **jedes** Profil mit `"type": "ETC"`:

```json
  "IE00B4ND3602": {
    "name": "iShares Physical Gold ETC",
    "type": "ETC",
    "tfs_quote": 0.00,
    "subject_to_vorabpauschale": false,
    "tax_asset_class": "other_security"
  },
```

`DE000A0S9GB0` (Xetra-Gold) steht bereits auf `false` und bleibt unverändert.

- [ ] **Step 2: Regressionstest laufen lassen und die neuen Werte ablesen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxRegressionTests"
```

Erwartet: FAIL. Notiere die tatsächlichen Werte aus der Assertion-Ausgabe.

**Vorhergesagte Werte — gegen diese prüfen, bevor du irgendetwas einträgst:**

Die drei IGLN-Verkaufszeilen verlieren ihre `UsedVorabpauschale`; der Betrag wandert 1:1 in `RawAmount` (§ 19 InvStG zieht die bereits versteuerte Vorabpauschale beim Verkauf ab — fällt sie weg, steigt der Gewinn):

| bisher | neu |
|---|---|
| `("IGLN", 177.19m, 8.13m, 177.19m)` | `("IGLN", 185.32m, 0m, 185.32m)` |
| `("IGLN", 3838.59m, 241.54m, 3838.59m)` | `("IGLN", 4080.13m, 0m, 4080.13m)` |
| `("IGLN", 4439.52m, 232.25m, 4439.52m)` | `("IGLN", 4671.77m, 0m, 4671.77m)` |

Summe steuerpflichtige Verkäufe: `10882.06m` → `11363.98m`
Die drei IGLN-Zeilen in `expectedVorabEntries` entfallen ersatzlos; die sechs VUSA-Zeilen bleiben unverändert.
Summe steuerpflichtige Vorabpauschale: `541.23m` → `59.31m`

Die Bemessungsgrundlage 2024 bleibt in Summe gleich (11363.98 + 59.31 = 10882.06 + 541.23 = 11423.29). Genau das ist die Probe darauf, dass nur umgebucht und nichts verloren wurde.

**Weicht ein tatsächlicher Wert um mehr als 0,02 von der Vorhersage ab, halte an und untersuche die Ursache, statt die Ausgabe blind zu übernehmen.** Eine größere Abweichung heißt, dass mehr passiert ist als die reine Umbuchung.

- [ ] **Step 3: Baseline und Kommentare aktualisieren**

Trage die neuen Werte in `expectedSellEntries`, `expectedVorabEntries` und die beiden Summen-Assertions ein. Passe außerdem die erklärenden Kommentare an, damit sie nicht weiter eine Vorabpauschale für IGLN behaupten:

- Im Klassen-XML-Kommentar den Block „IGLN.L (USD, no distributions): …" ersetzen durch:
```
/// IGLN.L is an ETC (a secured debt security, not an investment fund): the InvStG does not apply,
/// so it carries no Vorabpauschale at all. Its 2024 sells therefore have UsedVorabpauschale = 0 and
/// RawAmount is the plain FIFO gain. See docs/superpowers/specs/2026-07-28-tax-form-line-mapping-design.md §6.3.
```
- Den Inline-Kommentar bei den IGLN-Sell-Zeilen (ca. Zeile 93-95) entsprechend auf „no Vorabpauschale — ETC, outside the InvStG" kürzen.
- Im Kommentar über `expectedVorabEntries` die IGLN-Zeilen streichen.

- [ ] **Step 4: Volle Testsuite laufen lassen**

```
dotnet test WealthIQ.slnx
```

Erwartet: alles PASS. Insbesondere `TradersPlaceRegressionTests` darf sich nicht verändert haben.

- [ ] **Step 5: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add data tests
git commit -m "fix: ETCs are not investment funds, so they carry no Vorabpauschale

An ETC is a secured debt security with limited recourse; the InvStG does
not apply. IE00B4ND3602 was profiled as subject to Vorabpauschale, which
produced a 2023 year-end Vorabpauschale that was then deducted again from
the June 2024 sale under 19 InvStG. Both entries disappear; the 2024 tax
base is unchanged, only its split between Vorabpauschale and Veraeusserung
is now correct."
```

---

### Task 5: `TaxFormReport`-Modell und Zeilenschema

**Files:**
- Create: `src/WealthIQ.Application/Tax/Report/Forms/TaxFormReport.cs`
- Create: `src/WealthIQ.Application/Tax/Report/Forms/KapInvRows.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/Forms/KapInvRowsTests.cs`

**Interfaces:**
- Consumes: `TaxAssetClass` aus Task 1.
- Produces:
  - `TaxFormLine(string Line, string Caption, decimal Amount, string Nachweis, bool Muted)`
  - `TaxFormSection(string Form, string Title, string? Note, IReadOnlyList<TaxFormLine> Lines)`
  - `TaxFormReport(int Year, bool DomesticWithholding, IReadOnlyList<TaxFormSection> Sections)`
  - `KapInvRows.All` vom Typ `IReadOnlyList<KapInvRows.FundRow>`
  - `TaxAssetClassFormExtensions.IsFund(this TaxAssetClass)` → `bool`

- [ ] **Step 1: Schreibe den fehlschlagenden Test**

`tests/WealthIQ.Tests/Application/Tax/Forms/KapInvRowsTests.cs`:

```csharp
using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Tests.Application.Tax.Forms;

public sealed class KapInvRowsTests
{
    [Fact]
    public void All_CoversEveryFundClassExactlyOnce()
    {
        var covered = KapInvRows.All.Select(x => x.Class).ToList();

        var expected = Enum.GetValues<TaxAssetClass>().Where(x => x.IsFund()).ToList();

        Assert.Equal(expected.Count, covered.Count);
        Assert.Equal(expected.OrderBy(x => x), covered.OrderBy(x => x));
    }

    [Fact]
    public void All_UsesTheVz2025LineNumbers()
    {
        var equity = KapInvRows.All.Single(x => x.Class == TaxAssetClass.EquityFund);

        Assert.Equal("4", equity.DistributionLine);
        Assert.Equal("9", equity.VorabLine);
        Assert.Equal("14", equity.SaleLine);
        Assert.Equal("15", equity.AltLine);
        Assert.Equal("16", equity.FiktivLine);

        var other = KapInvRows.All.Single(x => x.Class == TaxAssetClass.OtherFund);

        Assert.Equal("8", other.DistributionLine);
        Assert.Equal("13", other.VorabLine);
        Assert.Equal("26", other.SaleLine);
    }

    [Fact]
    public void All_AssignsEveryLineNumberOnlyOnce()
    {
        var lines = KapInvRows.All
            .SelectMany(x => new[] { x.DistributionLine, x.VorabLine, x.SaleLine, x.AltLine, x.FiktivLine })
            .ToList();

        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Theory]
    [InlineData(TaxAssetClass.Share, false)]
    [InlineData(TaxAssetClass.OtherSecurity, false)]
    [InlineData(TaxAssetClass.EquityFund, true)]
    [InlineData(TaxAssetClass.MixedFund, true)]
    [InlineData(TaxAssetClass.RealEstateFund, true)]
    [InlineData(TaxAssetClass.ForeignRealEstateFund, true)]
    [InlineData(TaxAssetClass.OtherFund, true)]
    public void IsFund_SeparatesFundsFromPlainSecurities(TaxAssetClass value, bool expected)
        => Assert.Equal(expected, value.IsFund());
}
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~KapInvRowsTests"
```

Erwartet: Compile-Fehler „The name 'KapInvRows' does not exist".

- [ ] **Step 3: Modell anlegen**

`src/WealthIQ.Application/Tax/Report/Forms/TaxFormReport.cs`:

```csharp
namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>One line of a German tax form, ready to be typed into the tax software.</summary>
/// <param name="Line">The form's line number as printed on it, e.g. "14". Empty for memo rows.</param>
/// <param name="Caption">The form's own wording for that line.</param>
/// <param name="Amount">EUR. For Anlage KAP-INV always BEFORE Teilfreistellung — the tax office
/// applies the quota itself, so entering a reduced amount would cut it twice.</param>
/// <param name="Nachweis">Cross-reference to the Einzelnachweis backing this figure, e.g. "A".</param>
/// <param name="Muted">The line belongs to the form but WealthIQ always reports 0 for it — rendered
/// greyed out so it is visibly "checked and zero" rather than forgotten.</param>
public sealed record TaxFormLine(
    string Line,
    string Caption,
    decimal Amount,
    string Nachweis = "",
    bool Muted = false);

/// <summary>A block of lines belonging to one form section.</summary>
public sealed record TaxFormSection(
    string Form,
    string Title,
    string? Note,
    IReadOnlyList<TaxFormLine> Lines);

/// <summary>One account-year rendered as the form lines it maps to (spec §3).</summary>
/// <param name="DomesticWithholding">The broker already withheld German KESt, so the income is
/// declared on Anlage KAP Zeile 7 from the Steuerbescheinigung instead of on KAP-INV.</param>
public sealed record TaxFormReport(
    int Year,
    bool DomesticWithholding,
    IReadOnlyList<TaxFormSection> Sections)
{
    /// <summary>Line numbers shift between assessment years; this report is calibrated on VZ 2025.</summary>
    public const string Vintage =
        "Formularstand VZ 2025 — Zeilennummern älterer Jahrgänge können abweichen. Die Beträge sind jahresunabhängig.";
}
```

- [ ] **Step 4: Zeilenschema anlegen**

`src/WealthIQ.Application/Tax/Report/Forms/KapInvRows.cs`. Die Beschriftungen sind wörtliche Zitate aus dem Formular — nicht umformulieren, auch nicht die uneinheitliche Benennung („ausländischen Immobilienfonds" in Zeile 7/12 gegenüber „Auslands-Immobilienfonds" in Zeile 23):

```csharp
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>Which Anlage KAP-INV line each fund class maps to, formular vintage VZ 2025.
/// Captions are verbatim quotes from the form.</summary>
public static class KapInvRows
{
    public sealed record FundRow(
        TaxAssetClass Class,
        string DistributionLine, string DistributionCaption,
        string VorabLine, string VorabCaption,
        string SaleLine, string SaleCaption,
        string AltLine, string AltCaption,
        string FiktivLine, string FiktivCaption);

    public static IReadOnlyList<FundRow> All { get; } =
    [
        new(TaxAssetClass.EquityFund,
            "4", "Ausschüttungen aus Aktienfonds vor Teilfreistellung",
            "9", "Vorabpauschalen aus Aktienfonds vor Teilfreistellung",
            "14", "Einkünfte aus Verkäufen von Anteilen an Aktienfonds vor Teilfreistellung",
            "15", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "16", "Einkünfte aus fiktiven Verkäufen von Anteilen an Aktienfonds"),

        new(TaxAssetClass.MixedFund,
            "5", "Ausschüttungen aus Mischfonds vor Teilfreistellung",
            "10", "Vorabpauschalen aus Mischfonds vor Teilfreistellung",
            "17", "Einkünfte aus Verkäufen von Anteilen an Mischfonds vor Teilfreistellung",
            "18", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "19", "Einkünfte aus fiktiven Verkäufen von Anteilen an Mischfonds"),

        new(TaxAssetClass.RealEstateFund,
            "6", "Ausschüttungen aus Immobilienfonds vor Teilfreistellung",
            "11", "Vorabpauschalen aus Immobilienfonds vor Teilfreistellung",
            "20", "Einkünfte aus Verkäufen von Anteilen an Immobilienfonds vor Teilfreistellung",
            "21", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "22", "Einkünfte aus fiktiven Verkäufen von Anteilen an Immobilienfonds"),

        new(TaxAssetClass.ForeignRealEstateFund,
            "7", "Ausschüttungen aus ausländischen Immobilienfonds vor Teilfreistellung",
            "12", "Vorabpauschalen aus ausländischen Immobilienfonds vor Teilfreistellung",
            "23", "Einkünfte aus Verkäufen von Anteilen an Auslands-Immobilienfonds vor Teilfreistellung",
            "24", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "25", "Einkünfte aus fiktiven Verkäufen von Anteilen an Auslands-Immobilienfonds"),

        new(TaxAssetClass.OtherFund,
            "8", "Ausschüttungen aus sonstigen Investmentfonds",
            "13", "Vorabpauschalen aus sonstigen Investmentfonds vor Teilfreistellung",
            "26", "Einkünfte aus Verkäufen von Anteilen an sonstigen Fonds vor Teilfreistellung",
            "27", "Davon Gewinne aus Verkäufen von bestandsgeschützten Alt-Anteilen vor Teilfreistellung",
            "28", "Einkünfte aus fiktiven Verkäufen von Anteilen an sonstigen Fonds")
    ];
}

public static class TaxAssetClassFormExtensions
{
    /// <summary>Investment funds are declared on Anlage KAP-INV; everything else on Anlage KAP.</summary>
    public static bool IsFund(this TaxAssetClass value) => value is not (TaxAssetClass.Share or TaxAssetClass.OtherSecurity);
}
```

- [ ] **Step 5: Test laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~KapInvRowsTests"
```

Erwartet: PASS.

- [ ] **Step 6: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Application/Tax/Report/Forms tests/WealthIQ.Tests/Application/Tax/Forms
git commit -m "feat: add tax form line model and KAP-INV line schema"
```

---

### Task 6: `TaxFormReportBuilder` — KAP-INV-Abschnitte

**Files:**
- Create: `src/WealthIQ.Application/Tax/Report/Forms/TaxFormReportBuilder.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportBuilderKapInvTests.cs`

**Interfaces:**
- Consumes: `TaxFormReport`, `TaxFormSection`, `TaxFormLine`, `KapInvRows`, `IsFund()` aus Task 5; `AnnualTaxReport` und `TaxReportSummary` aus `WealthIQ.Application.Tax.Report`; `GermanTaxEntry.AssetClass` aus Task 1.
- Produces: `TaxFormReportBuilder.Build(AnnualTaxReport report)` → `TaxFormReport` (statische Methode, kein DI nötig).

- [ ] **Step 1: Schreibe den fehlschlagenden Test**

`tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportBuilderKapInvTests.cs`:

```csharp
using WealthIQ.Application.Tax.Report;
using WealthIQ.Application.Tax.Report.Forms;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Tests.Application.Tax.Forms;

public sealed class TaxFormReportBuilderKapInvTests
{
    private static GermanTaxEntry Entry(
        GermanTaxEntryType type, TaxAssetClass? assetClass,
        decimal raw, decimal taxable, DateOnly? openedOn = null)
        => new(
            Year: 2025,
            Date: new DateOnly(2025, 6, 1),
            Type: type,
            Symbol: "SYM",
            Isin: "TESTISIN0001",
            RawAmount: raw,
            TaxableAmount: taxable,
            OpenedOn: openedOn ?? new DateOnly(2020, 1, 1),
            AssetClass: assetClass,
            InstrumentName: "Testfonds");

    private static AnnualTaxReport Report(
        IReadOnlyList<GermanTaxEntry>? sells = null,
        IReadOnlyList<GermanTaxEntry>? dividends = null,
        IReadOnlyList<GermanTaxEntry>? interest = null,
        IReadOnlyList<GermanTaxEntry>? withholding = null,
        IReadOnlyList<GermanTaxEntry>? vorab = null,
        decimal withheldKest = 0m)
        => new(
            2025,
            new TaxReportSummary(0m, 0m, 0m, 0m, 0m, 0m, withheldKest),
            sells ?? [], dividends ?? [], interest ?? [], withholding ?? [], vorab ?? []);

    private static TaxFormLine Line(TaxFormReport report, string form, string line)
        => report.Sections.Where(s => s.Form == form).SelectMany(s => s.Lines).Single(l => l.Line == line);

    [Fact]
    public void Build_EquityFundDividend_LandsOnKapInvLine4AtGrossAmount()
    {
        var report = Report(dividends:
        [
            Entry(GermanTaxEntryType.Dividend, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        // 1000, not 700: KAP-INV wants the amount before Teilfreistellung.
        Assert.Equal(1000m, Line(form, "KAP-INV", "4").Amount);
    }

    [Fact]
    public void Build_MixedAndOtherFundVorabpauschale_SplitsAcrossLines10And13()
    {
        var report = Report(vorab:
        [
            Entry(GermanTaxEntryType.Vorabpauschale, TaxAssetClass.MixedFund, raw: 30m, taxable: 25.5m),
            Entry(GermanTaxEntryType.Vorabpauschale, TaxAssetClass.OtherFund, raw: 12m, taxable: 12m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(30m, Line(form, "KAP-INV", "10").Amount);
        Assert.Equal(12m, Line(form, "KAP-INV", "13").Amount);
        Assert.Equal(0m, Line(form, "KAP-INV", "9").Amount);
    }

    [Fact]
    public void Build_EquityFundSales_SumIntoLine14BeforeTeilfreistellung()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: -200m, taxable: -140m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(600m, Line(form, "KAP-INV", "14").Amount);
    }

    [Fact]
    public void Build_NoAltAnteile_MarksLine15MutedAndZero()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m,
                  openedOn: new DateOnly(2019, 3, 1))
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var line15 = Line(form, "KAP-INV", "15");
        Assert.Equal(0m, line15.Amount);
        Assert.True(line15.Muted);
    }

    [Fact]
    public void Build_GainOnPre2009Lot_ReportsItOnLine15AndUnmutesIt()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 800m, taxable: 560m,
                  openedOn: new DateOnly(2007, 5, 4))
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var line15 = Line(form, "KAP-INV", "15");
        Assert.Equal(800m, line15.Amount);
        Assert.False(line15.Muted);
    }

    [Fact]
    public void Build_FiktiveVeraeusserungAndZwischengewinne_AreAlwaysZeroAndMuted()
    {
        var form = TaxFormReportBuilder.Build(Report());

        Assert.True(Line(form, "KAP-INV", "16").Muted);
        Assert.Equal(0m, Line(form, "KAP-INV", "16").Amount);
        Assert.True(Line(form, "KAP-INV", "29").Muted);
        Assert.Equal(0m, Line(form, "KAP-INV", "29").Amount);
    }

    [Fact]
    public void Build_EntryWithoutAssetClass_ThrowsNamingTheIsin()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, assetClass: null, raw: 100m, taxable: 100m)
        ]);

        var ex = Assert.Throws<InvalidOperationException>(() => TaxFormReportBuilder.Build(report));

        Assert.Contains("TESTISIN0001", ex.Message);
    }

    [Fact]
    public void Build_NonFundSell_DoesNotAppearOnAnyKapInvLine()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 500m, taxable: 500m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        var kapInvSaleLines = KapInvRows.All.Select(r => r.SaleLine);
        foreach (var line in kapInvSaleLines)
        {
            Assert.Equal(0m, Line(form, "KAP-INV", line).Amount);
        }
    }
}
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxFormReportBuilderKapInvTests"
```

Erwartet: Compile-Fehler „The name 'TaxFormReportBuilder' does not exist".

- [ ] **Step 3: Builder implementieren (nur KAP-INV)**

`src/WealthIQ.Application/Tax/Report/Forms/TaxFormReportBuilder.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>
/// Translates one account-year into the lines of Anlage KAP / KAP-INV (spec §3). Pure: it only
/// regroups and relabels what <see cref="AnnualTaxReportService"/> already computed, and never
/// touches tax math.
/// </summary>
public static class TaxFormReportBuilder
{
    /// <summary>Bestandsgeschützte Alt-Anteile are units acquired before this date.</summary>
    private static readonly DateOnly AltAnteilCutoff = new(2009, 1, 1);

    public static TaxFormReport Build(AnnualTaxReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sections = new List<TaxFormSection>
        {
            BuildDistributions(report),
            BuildVorabpauschalen(report),
            BuildSales(report)
        };

        return new TaxFormReport(report.Year, DomesticWithholding: false, sections);
    }

    private static TaxFormSection BuildDistributions(AnnualTaxReport report) =>
        new("KAP-INV",
            "Anlage KAP-INV: Erträge aus Investmentanteilen (Zeilen 4 bis 8)",
            "Alle Beträge vor Teilfreistellung — das Finanzamt kürzt selbst.",
            KapInvRows.All
                .Select(row => new TaxFormLine(
                    row.DistributionLine,
                    row.DistributionCaption,
                    SumRaw(report.Dividends, row.Class),
                    Nachweis: "B"))
                .ToList());

    private static TaxFormSection BuildVorabpauschalen(AnnualTaxReport report) =>
        new("KAP-INV",
            "Anlage KAP-INV: Vorabpauschalen (Zeilen 9 bis 13)",
            "Ermittlung je Fonds siehe Nachweis D (entspricht Zeilen 30 bis 45).",
            KapInvRows.All
                .Select(row => new TaxFormLine(
                    row.VorabLine,
                    row.VorabCaption,
                    SumRaw(report.Vorabpauschale, row.Class),
                    Nachweis: "D"))
                .ToList());

    private static TaxFormSection BuildSales(AnnualTaxReport report)
    {
        var lines = new List<TaxFormLine>();

        foreach (var row in KapInvRows.All)
        {
            lines.Add(new TaxFormLine(
                row.SaleLine, row.SaleCaption, SumRaw(report.Sells, row.Class), Nachweis: "A"));

            // Gains on units bought before 2009 are only taxable above a 100.000 EUR allowance,
            // which WealthIQ does not model. Normally zero; when it is not, the line un-mutes so
            // the figure is visible instead of quietly wrong.
            var altAnteile = report.Sells
                .Where(e => ClassOf(e) == row.Class && e.OpenedOn < AltAnteilCutoff && e.RawAmount > 0m)
                .Sum(e => e.RawAmount);

            lines.Add(new TaxFormLine(
                row.AltLine, row.AltCaption, altAnteile, Nachweis: "A", Muted: altAnteile == 0m));

            // Deemed disposal as of 31.12.2017 is not modelled: every lot in the ledger was
            // acquired after that date.
            lines.Add(new TaxFormLine(row.FiktivLine, row.FiktivCaption, 0m, Muted: true));
        }

        lines.Add(new TaxFormLine(
            "29", "Zwischengewinne aus fiktiven Verkäufen zum 31.12.2017", 0m, Muted: true));

        return new TaxFormSection(
            "KAP-INV",
            "Anlage KAP-INV: Erträge aus dem Verkauf (Zeilen 14 bis 29)",
            "Ermittlung je Fonds siehe Nachweis A (entspricht Zeilen 46 bis 56). Die bereits "
                + "versteuerte Vorabpauschale ist nach § 19 InvStG bereits abgezogen.",
            lines);
    }

    private static decimal SumRaw(IReadOnlyList<GermanTaxEntry> entries, TaxAssetClass assetClass)
        => entries.Where(e => ClassOf(e) == assetClass).Sum(e => e.RawAmount);

    /// <summary>An entry whose instrument was never classified cannot be placed on a form line.
    /// Fail loudly rather than dropping it into a default bucket (CLAUDE.md: fail-fast everywhere).</summary>
    private static TaxAssetClass ClassOf(GermanTaxEntry entry)
        => entry.AssetClass ?? throw new InvalidOperationException(
            $"Instrument '{entry.Isin}' has no tax asset class, so its {entry.Type} entry cannot be "
            + "mapped to a form line. Set the Assetklasse under Stammdaten → Instrumente.");
}
```

- [ ] **Step 4: Test laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxFormReportBuilderKapInvTests"
```

Erwartet: PASS.

- [ ] **Step 5: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Application/Tax/Report/Forms tests/WealthIQ.Tests/Application/Tax/Forms
git commit -m "feat: map fund income onto Anlage KAP-INV lines"
```

---

### Task 7: `TaxFormReportBuilder` — Anlage-KAP-Abschnitt und Steuerabzugs-Route

**Files:**
- Modify: `src/WealthIQ.Application/Tax/Report/Forms/TaxFormReportBuilder.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportBuilderKapTests.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportGoldenTests.cs`

**Interfaces:**
- Consumes: alles aus Task 6.
- Produces: keine neuen Signaturen; `Build` liefert zusätzlich Sections mit `Form == "KAP"`, und bei `WithheldKESt > 0` ist `DomesticWithholding == true` und es gibt **keine** `KAP-INV`-Section.

- [ ] **Step 1: Schreibe den fehlschlagenden Test**

`tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportBuilderKapTests.cs`. Übernimm die privaten Helfer `Entry`, `Report` und `Line` wörtlich aus `TaxFormReportBuilderKapInvTests` (sie sind privat und damit nicht wiederverwendbar; das Duplikat ist gewollt, damit jede Testklasse für sich lesbar bleibt).

```csharp
    [Fact]
    public void Build_InterestAndNonFundIncome_SumIntoKapLine19()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 500m, taxable: 500m)],
            dividends: [Entry(GermanTaxEntryType.Dividend, TaxAssetClass.Share, raw: 120m, taxable: 120m)],
            interest: [Entry(GermanTaxEntryType.Interest, TaxAssetClass.OtherSecurity, raw: 30m, taxable: 30m)]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(650m, Line(form, "KAP", "19").Amount);
    }

    [Fact]
    public void Build_FundIncome_IsExcludedFromKapLine19()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 5000m, taxable: 3500m)],
            interest: [Entry(GermanTaxEntryType.Interest, TaxAssetClass.OtherSecurity, raw: 30m, taxable: 30m)]);

        var form = TaxFormReportBuilder.Build(report);

        // Fund income belongs on KAP-INV; Zeile 19 must not double-count it.
        Assert.Equal(30m, Line(form, "KAP", "19").Amount);
    }

    [Fact]
    public void Build_ShareGains_AlsoAppearOnKapLine20()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.Share, raw: 400m, taxable: 400m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: 100m, taxable: 100m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(400m, Line(form, "KAP", "20").Amount);
    }

    [Fact]
    public void Build_Losses_AreReportedPositivelyAndSplitByPot()
    {
        var report = Report(sells:
        [
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.Share, raw: -250m, taxable: -250m),
            Entry(GermanTaxEntryType.Sell, TaxAssetClass.OtherSecurity, raw: -80m, taxable: -80m)
        ]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(80m, Line(form, "KAP", "22").Amount);   // Topf 2, ohne Aktien
        Assert.Equal(250m, Line(form, "KAP", "23").Amount);  // Topf 1, Aktien
    }

    [Fact]
    public void Build_ForeignWithholding_LandsOnKapLine41()
    {
        // No withheld KESt, so this stays on the foreign route.
        var report = Report(
            withholding: [Entry(GermanTaxEntryType.WithholdingTax, TaxAssetClass.Share, raw: 45m, taxable: 0m) with { ForeignWithholdingTax = 45m }]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.Equal(45m, Line(form, "KAP", "41").Amount);
        // Nothing was withheld domestically, so Zeile 37 is present but greyed out at zero.
        Assert.Equal(0m, Line(form, "KAP", "37").Amount);
        Assert.True(Line(form, "KAP", "37").Muted);
    }

    [Fact]
    public void Build_AccountWithWithheldKest_SwitchesToTheDomesticRoute()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)],
            withheldKest: 40m);

        var form = TaxFormReportBuilder.Build(report);

        Assert.True(form.DomesticWithholding);
        Assert.DoesNotContain(form.Sections, s => s.Form == "KAP-INV");
        Assert.Equal(700m, Line(form, "KAP", "7").Amount);   // nach Teilfreistellung, lt. Bescheinigung
    }

    [Fact]
    public void Build_AccountWithoutWithheldKest_KeepsTheKapInvRoute()
    {
        var report = Report(
            sells: [Entry(GermanTaxEntryType.Sell, TaxAssetClass.EquityFund, raw: 1000m, taxable: 700m)]);

        var form = TaxFormReportBuilder.Build(report);

        Assert.False(form.DomesticWithholding);
        Assert.Contains(form.Sections, s => s.Form == "KAP-INV");
    }
```

- [ ] **Step 2: Test laufen lassen, Fehlschlag bestätigen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxFormReportBuilderKapTests"
```

Erwartet: FAIL — es gibt noch keine Section mit `Form == "KAP"`.

- [ ] **Step 3: `Build` um die Verzweigung erweitern**

In `TaxFormReportBuilder.cs` die `Build`-Methode ersetzen:

```csharp
    public static TaxFormReport Build(AnnualTaxReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // A German broker already withheld KESt, so the income is certified on a Steuerbescheinigung
        // and declared on Anlage KAP Zeile 7 — Anlage KAP-INV is for income WITHOUT domestic
        // withholding only.
        var domestic = report.Summary.WithheldKESt > 0m;

        var sections = new List<TaxFormSection>();

        if (domestic)
        {
            sections.Add(BuildDomestic(report));
        }
        else
        {
            sections.Add(BuildDistributions(report));
            sections.Add(BuildVorabpauschalen(report));
            sections.Add(BuildSales(report));
            sections.Add(BuildKap(report));
        }

        return new TaxFormReport(report.Year, domestic, sections);
    }
```

- [ ] **Step 4: Die beiden neuen Sections implementieren**

Am Ende von `TaxFormReportBuilder` ergänzen:

```csharp
    private static TaxFormSection BuildKap(AnnualTaxReport report)
    {
        // Zeile 19 is a NET sum of everything that is not an investment fund; funds are declared on
        // KAP-INV instead and must not be counted here as well.
        var nonFundSells = report.Sells.Where(e => !ClassOf(e).IsFund()).ToList();
        var nonFundDividends = report.Dividends.Where(e => !ClassOf(e).IsFund()).ToList();

        var total = report.Interest.Sum(e => e.RawAmount)
            + nonFundSells.Sum(e => e.RawAmount)
            + nonFundDividends.Sum(e => e.RawAmount);

        var shareGains = nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.RawAmount > 0m)
            .Sum(e => e.RawAmount);

        // Zeilen 22 and 23 restate the losses ALREADY contained in Zeile 19, as positive figures.
        var shareLosses = -nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.RawAmount < 0m)
            .Sum(e => e.RawAmount);

        var otherLosses = -nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.OtherSecurity && e.RawAmount < 0m)
            .Sum(e => e.RawAmount);

        return new TaxFormSection(
            "KAP",
            "Anlage KAP: Kapitalerträge ohne inländischen Steuerabzug",
            "Ohne Investmenterträge — die stehen in der Anlage KAP-INV.",
            [
                new TaxFormLine("19", "Ausländische Kapitalerträge", total, Nachweis: "A · B · C"),
                new TaxFormLine("20", "Darin enthaltene Gewinne aus Aktienveräußerungen", shareGains, Nachweis: "A",
                    Muted: shareGains == 0m),
                new TaxFormLine("22", "Darin enthaltene Verluste ohne Aktienveräußerungen", otherLosses, Nachweis: "A",
                    Muted: otherLosses == 0m),
                new TaxFormLine("23", "Darin enthaltene Verluste aus Aktienveräußerungen", shareLosses, Nachweis: "A",
                    Muted: shareLosses == 0m),
                new TaxFormLine("37", "Einbehaltene deutsche Kapitalertragsteuer", report.Summary.WithheldKESt,
                    Muted: report.Summary.WithheldKESt == 0m),
                new TaxFormLine("41", "Anrechenbare, noch nicht angerechnete ausländische Steuern",
                    report.WithholdingTaxes.Sum(e => e.ForeignWithholdingTax), Nachweis: "E"),
                new TaxFormLine("42", "Fiktive ausländische Steuern", 0m, Muted: true)
            ]);
    }

    private static TaxFormSection BuildDomestic(AnnualTaxReport report)
    {
        // Certified figures are already net of Teilfreistellung, so this route uses TaxableAmount.
        var certified = report.Sells.Sum(e => e.TaxableAmount)
            + report.Dividends.Sum(e => e.TaxableAmount)
            + report.Interest.Sum(e => e.TaxableAmount)
            + report.Vorabpauschale.Sum(e => e.TaxableAmount);

        var shareGains = report.Sells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.TaxableAmount > 0m)
            .Sum(e => e.TaxableAmount);

        return new TaxFormSection(
            "KAP",
            "Anlage KAP: Kapitalerträge mit inländischem Steuerabzug",
            "Maßgeblich ist die Steuerbescheinigung des Brokers. Die folgenden Zahlen dienen der "
                + "Kontrolle. Solidaritätszuschlag und Kirchensteuer erfasst WealthIQ nicht.",
            [
                new TaxFormLine("7", "Kapitalerträge, die dem inländischen Steuerabzug unterlegen haben",
                    certified, Nachweis: "A · B · C · D"),
                new TaxFormLine("8", "Darin enthaltene Gewinne aus Aktienveräußerungen", shareGains, Nachweis: "A",
                    Muted: shareGains == 0m),
                new TaxFormLine("37", "Kapitalertragsteuer", report.Summary.WithheldKESt),
                new TaxFormLine("38", "Solidaritätszuschlag", 0m, Muted: true),
                new TaxFormLine("39", "Kirchensteuer zur Kapitalertragsteuer", 0m, Muted: true),
                new TaxFormLine("41", "Anrechenbare, noch nicht angerechnete ausländische Steuern",
                    report.WithholdingTaxes.Sum(e => e.ForeignWithholdingTax), Nachweis: "E")
            ]);
    }
```

- [ ] **Step 5: Golden-Test gegen das echte Fixture**

Die Unit-Tests arbeiten mit synthetischen Einträgen. Dieser Test beweist, dass die Zuordnung auch auf den echten IBKR-Daten greift — insbesondere, dass der Gold-ETC **nicht** in der Anlage KAP-INV landet.

Neue Datei `tests/WealthIQ.Tests/Application/Tax/Forms/TaxFormReportGoldenTests.cs`. Der Fixture-Aufbau (Import, Katalog, Provider) ist wörtlich aus `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs:50-76` zu übernehmen, ebenso die private Methode `FindRepositoryRoot` (`:175-189`).

```csharp
    [Fact]
    public async Task Build_2024Fixture_KeepsFundIncomeOnKapInvAndTheGoldEtcOnKap()
    {
        // ... fixture setup copied from GermanTaxRegressionTests ...
        var result = calculator.Calculate(importResult.PortfolioLedger, instrumentCatalog);

        var entries = result.Entries.Where(x => x.Year == 2024).ToList();
        var annual = new AnnualTaxReport(
            2024,
            new TaxReportSummary(0m, 0m, 0m, 0m, 0m, 0m, 0m),
            entries.Where(x => x.Type == GermanTaxEntryType.Sell).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Dividend).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Interest).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.WithholdingTax).ToList(),
            entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale).ToList());

        var form = TaxFormReportBuilder.Build(annual);

        decimal Amount(string formName, string line) => form.Sections
            .Where(s => s.Form == formName).SelectMany(s => s.Lines).Single(x => x.Line == line).Amount;

        static decimal R(decimal value) => decimal.Round(value, 2);

        // VUSA is ETF_EQUITY -> Aktienfonds.
        Assert.Equal(8314.70m, R(Amount("KAP-INV", "14")));
        Assert.Equal(84.73m, R(Amount("KAP-INV", "9")));

        // IDTL is ETF_BOND -> sonstiger Investmentfonds; 2024 was a loss year and it had no
        // Vorabpauschale (the bonds depreciated in 2023, so the cap was 0).
        Assert.Equal(-3393.55m, R(Amount("KAP-INV", "26")));
        Assert.Equal(0m, R(Amount("KAP-INV", "13")));

        // IGLN is an ETC, not an investment fund: its 8937.22 gain must appear on Anlage KAP
        // Zeile 19 and on no KAP-INV sale line at all.
        var kapInvSales = KapInvRows.All.Sum(row => Amount("KAP-INV", row.SaleLine));
        Assert.Equal(4921.15m, R(kapInvSales));

        var interest = annual.Interest.Sum(x => x.RawAmount);
        Assert.Equal(8937.22m, R(Amount("KAP", "19") - interest));
    }
```

Die Zahlen leiten sich aus der in Task 4 aktualisierten Baseline ab: VUSA-Verkäufe 8314,70; IDTL-Verkäufe −3393,55 (Summe 4921,15); VUSA-Vorabpauschale 84,73; IGLN-Verkäufe 8937,22. Weicht ein Wert um genau 0,01 ab, ist das Rundung (die Baseline summiert gerundete Einzelwerte, hier wird die ungerundete Summe gerundet) — übernimm dann den tatsächlichen Wert. Größere Abweichungen sind ein echter Fehler; halte an und untersuche.

- [ ] **Step 6: Tests laufen lassen**

```
dotnet test WealthIQ.slnx --filter "FullyQualifiedName~TaxFormReport"
```

Erwartet: alle drei Testklassen PASS.

- [ ] **Step 7: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Application/Tax/Report/Forms tests/WealthIQ.Tests/Application/Tax/Forms
git commit -m "feat: map non-fund income onto Anlage KAP and route certified accounts"
```

---

### Task 8: Gemeinsame Razor-Komponente und Styles

**Files:**
- Create: `src/WealthIQ.Web/Components/Shared/TaxFormBlock.razor`
- Modify: `src/WealthIQ.Web/wwwroot/steuerreport-print.css`
- Modify: `src/WealthIQ.Web/wwwroot/wealthiq.css`

**Interfaces:**
- Consumes: `TaxFormReport` aus Task 5.
- Produces: `<TaxFormBlock Report="@formReport" />`.

**Warum eigene Klassennamen:** `wealthiq.css` wird global geladen, auch auf der Druckseite. Eine ungeschützte `.wiq-form`-Regel dort würde in die paginierten Seiten hineinwirken. Deshalb: im Druck-Stylesheet plain `.wiq-form*`, im Bildschirm-Stylesheet unter `.wiq-page` geschachtelt — `.wiq-page` ist der Wrapper aus `MainLayout.razor:66` und existiert im `PrintLayout` nicht.

- [ ] **Step 1: Komponente anlegen**

`src/WealthIQ.Web/Components/Shared/TaxFormBlock.razor`:

```razor
@* Renders a TaxFormReport as the lines you type into Anlage KAP / KAP-INV. Deliberately plain
   HTML: PrintLayout loads no MudBlazor chrome, so the same markup has to work on paper and on
   screen. Screen styling lives in wealthiq.css scoped under .wiq-page, paper styling in
   steuerreport-print.css. *@
@using WealthIQ.Application.Tax.Report.Forms
@using static WealthIQ.Web.Services.TaxReportPrintFormat

@foreach (var section in Report.Sections)
{
    <table class="wiq-form">
        <tbody>
            <tr class="wiq-form__head">
                <td colspan="4">@section.Title</td>
            </tr>
            @if (!string.IsNullOrWhiteSpace(section.Note))
            {
                <tr class="wiq-form__note">
                    <td colspan="4">@section.Note</td>
                </tr>
            }
            @foreach (var line in section.Lines)
            {
                <tr class="@(line.Muted ? "wiq-form--muted" : null)">
                    <td class="wiq-form__line">Zeile @line.Line</td>
                    <td class="wiq-form__caption">@line.Caption</td>
                    <td class="wiq-form__amount @(line.Amount < 0m ? "wiq-form__neg" : null)">@Num(line.Amount)</td>
                    <td class="wiq-form__ref">@(string.IsNullOrEmpty(line.Nachweis) ? "" : $"→ Nachweis {line.Nachweis}")</td>
                </tr>
            }
        </tbody>
    </table>
}

<p class="wiq-form__vintage">@TaxFormReport.Vintage</p>

@code {
    [Parameter, EditorRequired] public TaxFormReport Report { get; set; } = default!;
}
```

- [ ] **Step 2: Druck-Styles ergänzen**

An `src/WealthIQ.Web/wwwroot/steuerreport-print.css` anhängen (die vorhandenen `.wiq-p-form*`-Regeln bleiben unverändert — der Ergebnisblock nutzt sie weiter):

```css
/* ---- Formularzeilen-Block (TaxFormBlock.razor) ---- */
.wiq-form {
    width: 100%;
    border-collapse: collapse;
    margin-bottom: 4mm;
}

.wiq-form__head td {
    background: #1f2933;
    color: #fff;
    font-weight: 600;
    font-size: 8pt;
    padding: 1.6mm 2mm;
    letter-spacing: 0.04em;
}

.wiq-form td {
    padding: 1.1mm 2mm;
    border-bottom: 0.25pt solid #cbd2d9;
    vertical-align: baseline;
}

.wiq-form__note td {
    font-size: 6.8pt;
    font-style: italic;
    color: #52606d;
    border-bottom: 0.25pt solid #cbd2d9;
}

.wiq-form__line {
    width: 18mm;
    color: #7b8794;
    font-size: 7pt;
    white-space: nowrap;
}

.wiq-form__amount {
    width: 32mm;
    font-variant-numeric: tabular-nums;
    text-align: right;
    font-weight: 600;
}

.wiq-form__ref {
    width: 26mm;
    font-size: 6.8pt;
    color: #7b8794;
    text-align: right;
    white-space: nowrap;
}

.wiq-form__neg {
    color: #b42318;
}

.wiq-form--muted td {
    color: #9aa5b1;
    font-weight: 400;
}

.wiq-form__vintage {
    font-size: 6.8pt;
    color: #7b8794;
    margin: 0 0 4mm;
}
```

- [ ] **Step 3: Bildschirm-Styles ergänzen**

An `src/WealthIQ.Web/wwwroot/wealthiq.css` anhängen. **Jede** Regel unter `.wiq-page` schachteln, sonst greift sie auch in die Druckseite:

```css
/* ---- Formularzeilen-Block (TaxFormBlock.razor), Bildschirm ----
   Scoped under .wiq-page (MainLayout) so these rules never reach the print page, which uses
   PrintLayout and its own stylesheet. */
.wiq-page .wiq-form {
    width: 100%;
    border-collapse: collapse;
    margin-bottom: 24px;
    font-size: 0.875rem;
}

.wiq-page .wiq-form__head td {
    background: var(--mud-palette-background-grey);
    color: var(--mud-palette-text-primary);
    font-weight: 600;
    padding: 10px 12px;
    letter-spacing: 0.03em;
}

.wiq-page .wiq-form td {
    padding: 8px 12px;
    border-bottom: 1px solid var(--mud-palette-lines-default);
    vertical-align: baseline;
}

.wiq-page .wiq-form__note td {
    font-size: 0.78rem;
    font-style: italic;
    color: var(--mud-palette-text-secondary);
}

.wiq-page .wiq-form__line {
    width: 90px;
    color: var(--mud-palette-text-secondary);
    font-size: 0.78rem;
    white-space: nowrap;
}

.wiq-page .wiq-form__amount {
    width: 160px;
    font-variant-numeric: tabular-nums;
    text-align: right;
    font-weight: 600;
}

.wiq-page .wiq-form__ref {
    width: 140px;
    font-size: 0.78rem;
    color: var(--mud-palette-text-secondary);
    text-align: right;
    white-space: nowrap;
}

.wiq-page .wiq-form__neg {
    color: var(--mud-palette-error);
}

.wiq-page .wiq-form--muted td {
    color: var(--mud-palette-text-disabled);
    font-weight: 400;
}

.wiq-page .wiq-form__vintage {
    font-size: 0.78rem;
    color: var(--mud-palette-text-secondary);
    margin: 0 0 24px;
}
```

- [ ] **Step 4: Build prüfen**

```
dotnet build WealthIQ.slnx
```

Erwartet: fehlerfrei. Es gibt für diesen Task keinen Unit-Test — die Komponente ist reines Markup ohne Logik; sie wird in Task 9 im laufenden Browser verifiziert.

- [ ] **Step 5: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Web
git commit -m "feat: add shared tax form block component and its styles"
```

---

### Task 9: Formularblock in beide Seiten einbinden

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/TaxReportPrint.razor`
- Modify: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

**Interfaces:**
- Consumes: `TaxFormBlock` aus Task 8, `TaxFormReportBuilder.Build` aus Task 7.

- [ ] **Step 1: PDF-Report umstellen**

In `src/WealthIQ.Web/Components/Pages/TaxReportPrint.razor`:

`@using WealthIQ.Application.Tax.Report.Forms` oben ergänzen.

Die beiden Tabellenblöcke unter den Kommentaren `@* ---- Anlage KAP ---- *@` (Zeilen 69–106) und `@* ---- Anlage KAP-INV ---- *@` (Zeilen 108–133) **vollständig** ersetzen durch:

```razor
            @* ---- Anlage KAP / KAP-INV ---- *@
            <TaxFormBlock Report="Forms" />
```

Der Block `@* ---- Ergebnis ---- *@` (ab Zeile 135) bleibt unverändert.

Im `@code`-Block nach der `TaxBase`-Property ergänzen:

```csharp
    private TaxFormReport Forms => TaxFormReportBuilder.Build(Current!);
```

- [ ] **Step 2: Bildschirmseite ergänzen**

In `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`:

`@using WealthIQ.Application.Tax.Report.Forms` oben ergänzen.

Zwischen dem KPI-`MudGrid` (endet Zeile 106) und dem Drill-down-`<div class="wiq-rise-3">` (Zeile 109) einfügen:

```razor
    @if (Forms is not null)
    {
        <div class="wiq-rise-3 mb-4">
            <SectionCard Title="Eingabehilfe Steuerformulare">
                <ChildContent>
                    <TaxFormBlock Report="Forms" />
                </ChildContent>
            </SectionCard>
        </div>
    }
```

Im `@code`-Block nach `private AnnualTaxReport? Current => ...` ergänzen:

```csharp
    // Rebuilt on every render; a pure regrouping of Current and cheap enough not to cache.
    private TaxFormReport? Forms => Current is null ? null : TaxFormReportBuilder.Build(Current);
```

Anders als in `TaxReportPrint.razor` wird hier **kein** `Current!` verwendet: diese Datei nutzt den Null-Forgiving-Operator nirgends, und die Konvention lautet, ihn zu vermeiden.

- [ ] **Step 3: Build und volle Testsuite**

```
dotnet build WealthIQ.slnx
dotnet test WealthIQ.slnx
```

Erwartet: beides grün.

- [ ] **Step 4: Im Browser verifizieren**

Build und Test fangen Render-Fehler in Blazor nicht ab — diese Seiten müssen laufen gesehen werden.

```
dotnet run --project src/WealthIQ.Web
```

Prüfe im Browser:
1. `/steuerreport` — der Abschnitt „Eingabehilfe Steuerformulare" erscheint, die Zeilennummern stehen links, die Beträge rechtsbündig mit Tabellenziffern, graue Zeilen sind erkennbar abgesetzt.
2. Jahr und Konto umschalten — die Werte ändern sich mit.
3. „PDF-Export" öffnen — der Formularblock ersetzt die alten Aggregationstabellen, das Layout bricht nicht, der Ergebnisblock steht weiterhin darunter.
4. Falls ein Konto mit einbehaltener KESt existiert (Trader's Place): dort erscheint der KAP-Z7-Block statt KAP-INV.

Behebe gefundene Fehler, bevor du fortfährst.

- [ ] **Step 5: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Web
git commit -m "feat: show the form-line block on the tax report and its PDF export"
```

---

### Task 10: Nachweise A, B und D werden zur Ermittlung

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/TaxReportPrint.razor`

**Interfaces:**
- Consumes: `GermanTaxEntry.AssetClass` aus Task 1, `KapInvRows` aus Task 5.

**Ziel:** Die Einzelnachweise bekommen eine Fondsart-Spalte und Zwischensummen je Fondsart, damit sie zugleich als Ermittlungsseiten (KAP-INV Zeilen 30–45 bzw. 46–56) taugen.

- [ ] **Step 1: Anzeigename für die Fondsart bereitstellen**

In `src/WealthIQ.Web/Services/TaxReportPrintFormat.cs` ergänzen (und `using WealthIQ.Domain.Enumeration;` oben):

```csharp
    /// <summary>The asset class as Anlage KAP-INV Zeile 48 names it.</summary>
    public static string AssetClassLabel(TaxAssetClass? value) => value switch
    {
        TaxAssetClass.EquityFund => "Aktienfonds",
        TaxAssetClass.MixedFund => "Mischfonds",
        TaxAssetClass.RealEstateFund => "Immobilienfonds",
        TaxAssetClass.ForeignRealEstateFund => "Auslands-Immobilienfonds",
        TaxAssetClass.OtherFund => "sonstiger Fonds",
        TaxAssetClass.Share => "Aktie",
        TaxAssetClass.OtherSecurity => "sonstiges Wertpapier",
        _ => "—"
    };
```

- [ ] **Step 2: Nachweis A umbauen**

In `NachweisSells()`:

Der bisherige `<tbody>` hat 11 Spalten. Mit der neuen Fondsart-Spalte werden es 12 — alle `colspan`-Werte in dieser Tabelle steigen entsprechend um 1.

Ersetze `<thead>` und `<tbody>` durch:

```razor
            <thead>
                <tr>
                    <th>Beleg</th>
                    <th>Symbol / ISIN</th>
                    <th>Fondsart</th>
                    <th>Eröffnet</th>
                    <th>Verkauft</th>
                    <th class="wiq-p-num">Stück</th>
                    <th class="wiq-p-num">Kauf ges.</th>
                    <th class="wiq-p-num">Verkauf ges.</th>
                    <th class="wiq-p-num">Kosten</th>
                    <th class="wiq-p-num">Roh-G/V</th>
                    @* "darin", not "abzgl.": the amount is already deducted inside Roh-G/V. *@
                    <th class="wiq-p-num">darin Vorabp.</th>
                    <th class="wiq-p-num">Steuerpfl.</th>
                </tr>
            </thead>
            <tbody>
                @{ var belegIndex = 0; }
                @foreach (var group in rows.GroupBy(r => r.AssetClass))
                {
                    foreach (var r in group)
                    {
                        var beleg = Beleg("A", belegIndex++);
                        <tr>
                            <td class="wiq-p-belegnr">@beleg</td>
                            <td>@InstrumentCell(r)</td>
                            <td>@AssetClassLabel(r.AssetClass)</td>
                            <td>@Date(r.OpenedOn)</td>
                            <td>@Date(r.Date)</td>
                            <td class="wiq-p-num">@Qty(r.QuantitySold)</td>
                            <td class="wiq-p-num">@Num(r.AcquisitionCosts)</td>
                            <td class="wiq-p-num">@Num(r.SaleProceeds)</td>
                            <td class="wiq-p-num">@Num(r.Fees)</td>
                            <td class="wiq-p-num @NegClass(r.RawAmount)">@Num(r.RawAmount)</td>
                            <td class="wiq-p-num">@Num(r.UsedVorabpauschale)</td>
                            <td class="wiq-p-num @NegClass(r.TaxableAmount)"><b>@Num(r.TaxableAmount)</b></td>
                        </tr>
                        <tr class="wiq-p-srcline">
                            <td></td>
                            <td colspan="11">
                                Kauf-Ref. @r.SourceReference · Verkauf-Ref. @r.CloseReference · Datei @SourceFileName(r.SourceFile)
                            </td>
                        </tr>
                    }
                    @* A Roh-G/V subtotal DOES hold inside one fund class — the Teilfreistellung
                       quota is uniform there — and it is exactly the figure the form wants. *@
                    <tr class="wiq-p-memo">
                        <td colspan="9">Summe @AssetClassLabel(group.Key) → Anlage KAP-INV Zeile @SaleLineFor(group.Key)</td>
                        <td class="wiq-p-num @NegClass(group.Sum(r => r.RawAmount))">@Num(group.Sum(r => r.RawAmount))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.UsedVorabpauschale))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.TaxableAmount))</td>
                    </tr>
                }
            </tbody>
```

Im `<tfoot>` bleibt die Gesamtsumme **nur** auf der steuerpflichtigen Spalte — über Fondsarten hinweg ginge eine Roh-G/V-Summe als Gleichung nicht auf. Beide `colspan="10"` werden zu `colspan="11"`.

Ändere außerdem den Untertitel auf:

```razor
        @SectionTitle("A", "Veräußerungen",
            $"{rows.Count} Positionen · FIFO-Zuordnung · Beträge in EUR · zugleich Ermittlung Anlage KAP-INV Zeilen 46 bis 56")
```

Helfer im `@code`-Block ergänzen:

```csharp
    private static string SaleLineFor(TaxAssetClass? assetClass)
        => KapInvRows.All.FirstOrDefault(r => r.Class == assetClass)?.SaleLine ?? "—";

    private static string VorabLineFor(TaxAssetClass? assetClass)
        => KapInvRows.All.FirstOrDefault(r => r.Class == assetClass)?.VorabLine ?? "—";

    private static string DistributionLineFor(TaxAssetClass? assetClass)
        => KapInvRows.All.FirstOrDefault(r => r.Class == assetClass)?.DistributionLine ?? "—";
```

  mit `@using WealthIQ.Application.Tax.Report.Forms` und `@using WealthIQ.Domain.Enumeration` oben.

- [ ] **Step 3: Nachweis B umbauen**

`NachweisCash` wird von B (Dividenden) **und** C (Zinsen) genutzt. Erweitere die Signatur um einen Schalter, damit nur B gruppiert:

```csharp
    private RenderFragment NachweisCash(string letter, string title, IReadOnlyList<GermanTaxEntry> rows,
        bool showIsin, bool groupByAssetClass = false) => __builder =>
```

Aufrufe anpassen: `@NachweisCash("B", "Dividenden", Current.Dividends, showIsin: true, groupByAssetClass: true)` und `@NachweisCash("C", "Zinsen", Current.Interest, showIsin: false)`.

Wenn `groupByAssetClass` gesetzt ist: eine `<th>Fondsart</th>`-Spalte nach der Symbol-Spalte ergänzen, nach Fondsart gruppieren, den Belegzähler wie in Nachweis A über alle Gruppen fortlaufend führen und je Gruppe eine Zwischensumme ausgeben. Untertitel um „· zugleich Ermittlung zu Zeilen 4 bis 8" ergänzen. Bei `groupByAssetClass == false` bleibt alles unverändert — Zinsen haben keine Fondsart.

Die Tabelle hat bisher 7 Spalten, mit der Fondsart 8. Die Gruppen-Zwischensumme:

```razor
                    <tr class="wiq-p-memo">
                        <td colspan="4">Summe @AssetClassLabel(group.Key) → Anlage KAP-INV Zeile @DistributionLineFor(group.Key)</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.RawAmount))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.RawAmount - r.TaxableAmount))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.TaxableAmount))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.ForeignWithholdingTax))</td>
                    </tr>
```

Der `colspan` der Belegzeile (`<tr class="wiq-p-srcline">`) und der `<tfoot>`-Summenzeile steigt im gruppierten Fall jeweils um 1.

- [ ] **Step 4: Nachweis D umbauen**

In `NachweisVorabpauschale()`:

- Untertitel: `$"{rows.Count} Positionen · Beträge in EUR · zugleich Ermittlung Anlage KAP-INV Zeilen 30 bis 45"`.
- `<th>Fondsart</th>` nach `<th>Symbol / ISIN</th>`, Zelle `<td>@AssetClassLabel(r.AssetClass)</td>` entsprechend. Die Tabelle wächst damit von 11 auf 12 Spalten.
- Nach Fondsart gruppieren, Belegzähler wie in Nachweis A über alle Gruppen fortlaufend (`@{ var belegIndex = 0; }` vor der Gruppenschleife, `Beleg("D", belegIndex++)` in der inneren Schleife).
- Je Gruppe eine Zwischensumme:

```razor
                    <tr class="wiq-p-memo">
                        <td colspan="10">Summe @AssetClassLabel(group.Key) → Anlage KAP-INV Zeile @VorabLineFor(group.Key)</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.RawAmount))</td>
                        <td class="wiq-p-num">@Num(group.Sum(r => r.TaxableAmount))</td>
                    </tr>
```

- Im `<tfoot>` steigt `colspan="9"` auf `colspan="10"`.

- [ ] **Step 5: Build und Browser-Verifikation**

```
dotnet build WealthIQ.slnx
dotnet run --project src/WealthIQ.Web
```

Öffne `/steuerreport/print?...`. Prüfe: Fondsart-Spalte gefüllt, Zwischensummen je Gruppe vorhanden, Spaltenausrichtung stimmt (kein verschobenes `colspan`), Belegnummern lückenlos aufsteigend über Gruppen hinweg, Seitenumbruch weiterhin sauber.

**Probe auf Richtigkeit:** Die Roh-G/V-Zwischensumme je Fondsart in Nachweis A muss exakt dem Betrag in der zugehörigen KAP-INV-Zeile des Formularblocks entsprechen. Stimmt das nicht, liegt ein Fehler in der Gruppierung vor.

- [ ] **Step 6: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Web
git commit -m "feat: group the Einzelnachweise by fund class as the KAP-INV Ermittlung"
```

---

### Task 11: Assetklasse in der Stammdaten-UI pflegen

**Files:**
- Modify: `src/WealthIQ.Web/Components/Pages/InstrumentsAdmin.razor`

**Interfaces:**
- Consumes: `InstrumentAdminDto.AssetClass` aus Task 3.

- [ ] **Step 1: Editiermodell und Speichern erweitern**

In `InstrumentsAdmin.razor`:

`@using WealthIQ.Domain.Enumeration` oben ergänzen.

In `InstrumentEditModel` ergänzen:

```csharp
        public TaxAssetClass? AssetClass { get; set; }
```

In `StartEdit` nach `SubjectToVorabpauschale = dto.SubjectToVorabpauschale,` ergänzen:

```csharp
            AssetClass = dto.AssetClass,
```

In `SaveEdit` das DTO um den neuen Parameter erweitern — er steht **vor** der Listings-Liste:

```csharp
            var dto = new InstrumentAdminDto(
                _editModel.Isin,
                _editModel.Name,
                _editModel.Type,
                _editModel.Teilfreistellungsquote,
                _editModel.SubjectToVorabpauschale,
                _editModel.AssetClass,
                _editModel.Listings.Select(l => new InstrumentListingDto(
```

- [ ] **Step 2: Auswahlfeld und Tabellenspalte ergänzen**

Im Editor-Panel, in dem `div` mit Teilfreistellungsquote und Vorabpauschale-Checkbox (ca. Zeile 92), **vor** der Checkbox einfügen:

```razor
                    <MudSelect T="TaxAssetClass?" @bind-Value="_editModel.AssetClass" Label="Assetklasse (Steuerformular)"
                               Variant="Variant.Outlined" Clearable="true" Style="min-width: 260px;">
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.EquityFund)">Aktienfonds</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.MixedFund)">Mischfonds</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.RealEstateFund)">Immobilienfonds</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.ForeignRealEstateFund)">Auslands-Immobilienfonds</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.OtherFund)">sonstiger Investmentfonds</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.Share)">Aktie (kein Fonds)</MudSelectItem>
                        <MudSelectItem T="TaxAssetClass?" Value="@(TaxAssetClass.OtherSecurity)">sonstiges Wertpapier (kein Fonds)</MudSelectItem>
                    </MudSelect>
```

In der Übersichtstabelle nach `<MudTh>Typ</MudTh>` ein `<MudTh>Assetklasse</MudTh>` und in `RowTemplate` nach der Typ-Zelle:

```razor
                        <MudTd DataLabel="Assetklasse">@(context.AssetClass?.ToString() ?? "—")</MudTd>
```

- [ ] **Step 3: Build, Tests und Browser-Verifikation**

```
dotnet build WealthIQ.slnx
dotnet test WealthIQ.slnx
dotnet run --project src/WealthIQ.Web
```

Öffne `/data-admin/instruments`: Spalte gefüllt, Bearbeiten öffnet das Auswahlfeld mit dem gespeicherten Wert, Speichern und erneutes Laden behalten die Auswahl, Leeren des Feldes speichert `null`.

- [ ] **Step 4: Format + Commit**

```bash
dotnet format WealthIQ.slnx
git add src/WealthIQ.Web
git commit -m "feat: edit the tax asset class in the instruments admin page"
```

---

### Task 12: Dokumentation nachziehen

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: `CLAUDE.md` aktualisieren**

Vier Stellen:

1. Im Abschnitt zum Steuerreport / `GermanTaxEntry`: ergänzen, dass der Entry zusätzlich `AssetClass` und `InstrumentName` trägt (rein für die Formularzuordnung, nie Steuermathematik) und dass `Application/Tax/Report/Forms/TaxFormReportBuilder` daraus die Anlage-KAP-/KAP-INV-Zeilen baut, gerendert von `Components/Shared/TaxFormBlock.razor` auf `/steuerreport` und im PDF. Zeilenschema = Formularstand VZ 2025.

2. Im Abschnitt „Printable tax report / PDF export", bei den Report-Konventionen: die Regel „Nachweis A sums **only** the taxable column" präzisieren — sie gilt weiterhin für die Gesamtsumme, aber je Fondsart ist die Roh-G/V-Zwischensumme korrekt und wird ausgewiesen, weil die Teilfreistellungsquote innerhalb einer Fondsart einheitlich ist.

3. Im Abschnitt „Data layout (`data/`)": `instruments.json` trägt zusätzlich `tax_asset_class`; die Spalte `TaxAssetClass` gehört zu `InstrumentProfile`.

4. In den „Tax-pipeline guardrails", bei der bekannten Einschränkung zu Xetra-Gold: ergänzen, dass ETCs generell keine Investmentfonds sind und deshalb `subject_to_vorabpauschale=false` tragen (per Migration korrigiert). Für `DE000A0S9GB0` bleibt die §-23-EStG-Behandlung (steuerfrei nach 12 Monaten) unmodelliert; `IE00B4ND3602` hat keinen Lieferanspruch und ist als gewöhnlicher §-20-Gewinn korrekt erfasst.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document the tax form line mapping and the ETC correction"
```

---

### Task 13: Abschlussverifikation

- [ ] **Step 1: Sauberer Rebuild**

```bash
dotnet clean WealthIQ.slnx && dotnet build WealthIQ.slnx --configuration Release
```

Erwartet: fehlerfrei. Release, weil CI genau so baut.

- [ ] **Step 2: Volle Testsuite wie in CI**

```bash
dotnet test WealthIQ.slnx --configuration Release --no-build
```

Erwartet: alles PASS.

- [ ] **Step 3: Formatprüfung**

```bash
dotnet format WealthIQ.slnx --verify-no-changes
```

Erwartet: keine Ausgabe.

- [ ] **Step 4: Prüfen, dass keine Testdaten gitignored sind**

CI klont sauber; alles, was Tests lesen, muss eingecheckt sein.

```bash
git status --short data tests
git check-ignore -v data/test/configuration/instruments.json data/reference/instruments.json
```

Erwartet: `git status` zeigt keine ungetrackten Dateien unter `data/test` oder `tests`; `git check-ignore` gibt nichts aus (Exit-Code 1).

- [ ] **Step 5: Abschließende Browser-Verifikation**

```bash
dotnet run --project src/WealthIQ.Web
```

Durchgehen: `/` (Dashboard unbeschädigt), `/steuerreport` (Formularblock, Konto- und Jahreswechsel), `/steuerreport/print` (Formularblock, gruppierte Nachweise, Seitenumbruch), `/data-admin/instruments` (Assetklasse pflegen).

Beim ersten Start läuft die Migration gegen die lokale DB. Prüfe unter `/data-admin/instruments`, dass die ETC-Profile jetzt Assetklasse „OtherSecurity" und **kein** Vorabpauschale-Häkchen haben.

- [ ] **Step 6: Bericht an den Nutzer**

Fasse zusammen: was umgesetzt wurde, welche Zahlen sich in der Golden-Baseline verschoben haben und warum, und was bewusst offen blieb (Verlustverrechnungstöpfe, Soli/Kirchensteuer, Alt-Anteile, § 23 EStG bei Xetra-Gold, Zeilenschemata vor VZ 2025).

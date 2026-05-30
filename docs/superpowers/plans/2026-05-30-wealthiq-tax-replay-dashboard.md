# WealthIQ Tax Replay & Blazor Dashboard Implementation Plan (Plan 3 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete WealthIQ v1 — replay the persisted ledger out of SQLite through the existing German tax engine using DB-backed reference data, expose the yearly tax report, import, and audit/diagnostics in a local Blazor Server + MudBlazor dashboard, and wire it all up (EF Core migrations + DI + startup seeding) so the spec §6 pipeline (Ingest → Import → Persist → **Replay & Compute → Present**) runs end-to-end.

**Architecture:** Four new Infrastructure adapters read the Plan 2 reference tables and implement the Application reference ports that `GermanTaxCalculator` already depends on (`IBasisInterestRateProvider`, `IYearEndPriceProvider`, `IInstrumentProfileEnricher`, `IFxRateLookup`) — the `DbFxRateLookup` reproducing the `NextAvailableOnOrAfter` roll-forward of `CsvFxRateLookup`. A new Application use-case `AnnualTaxReportService` loads the ledger via `ILedgerStore`, enriches the instrument catalog, runs the tax calculator, and aggregates per-year totals (spec §9). A new `IImportAuditStore` exposes persisted batches + diagnostics for the Audit page. EF Core migrations + a design-time factory replace `EnsureCreated` for the production DB. The new `WealthIQ.Web` project is the composition root (the only project referencing Infrastructure): it registers everything per spec §4, applies migrations and seeds reference data on startup, and renders three thin MudBlazor pages (Steuerreport, Import, Diagnostics/Audit).

**Tech Stack:** C# / .NET 10, ASP.NET Core Blazor Server, MudBlazor, EF Core 10 (Microsoft.EntityFrameworkCore.Sqlite + .Design), xUnit.

**Spec:** `docs/superpowers/specs/2026-05-29-wealthiq-neustart-design.md` (§4 architecture/DI, §6 replay & present, §7 FX rule, §9 dashboard, §10 testing). **Predecessors:** `docs/superpowers/plans/2026-05-29-wealthiq-foundation-persistence.md` (Plan 1 — done), `docs/superpowers/plans/2026-05-30-wealthiq-import-persist-reference-data.md` (Plan 2 — done).

---

## Context for the implementer

Plans 1 & 2 are complete. The solution (`WealthIQ.slnx`) has four projects, all targeting `net10.0` with `Nullable` and `ImplicitUsings` enabled. **Only `Infrastructure` references EF Core today; after this plan `WealthIQ.Web` also references Infrastructure as the composition root.**

- `src/WealthIQ.Domain` — pure domain (ledger, value objects, typed ids, tax result types).
- `src/WealthIQ.Application` — ports + use-cases (import pipeline, FIFO, **`GermanTaxCalculator`**, FX, persistence ports, reference-data seeding contracts).
- `src/WealthIQ.Infrastructure` — IBKR importer, EF Core + SQLite persistence, reference-data rows + seeder, CSV/JSON reference adapters.
- `tests/WealthIQ.Tests` — xUnit; `tests/.../Infrastructure/Persistence/InMemorySqlite.cs` gives an in-memory SQLite `WealthIqDbContext` via `EnsureCreated()` (creates every registered table). The test csproj already copies any `**/Fixtures/**` to the output directory.

### Existing types you will wire together (do NOT redefine — reference them)

```csharp
// --- Application reference ports already consumed by GermanTaxCalculator ---
public interface IBasisInterestRateProvider { decimal GetRate(int year); }                  // WealthIQ.Application.Tax.Interface
public interface IYearEndPriceProvider { decimal? GetPrice(string isin, int year); }         // WealthIQ.Application.Tax.Interface
public interface IInstrumentProfileEnricher { Instrument Enrich(Instrument instrument); }     // WealthIQ.Application.Tax.Interface
public enum FxRateLookupDateHandling { ExactDate, NextAvailableOnOrAfter }                    // WealthIQ.Application.Currency.Interface
public interface IFxRateLookup                                                                // WealthIQ.Application.Currency.Interface
{
    decimal GetRate(DateOnly conversionDate, Currency sourceCurrency, Currency targetCurrency,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate);
}

// --- Application use-cases / builders (concrete, ctor-injected) ---
public sealed class InstrumentCatalogBuilder(IInstrumentProfileEnricher profileEnricher)      // WealthIQ.Application.Tax
{ public IReadOnlyList<Instrument> Build(IReadOnlyList<Instrument> importedInstruments); }
public sealed class GermanTaxCalculator(                                                       // WealthIQ.Application.Tax
    IBasisInterestRateProvider interestRateProvider, IYearEndPriceProvider yearEndPriceProvider, IFxRateLookup fxRateLookup)
{ public GermanTaxCalculationResult Calculate(PortfolioLedger portfolioLedger, IReadOnlyList<Instrument> instruments); }
public sealed class StatementImportPipeline(                                                   // WealthIQ.Application.Import
    IStatementImporter importer, IRawFileStore rawFileStore, IImportStore importStore, TimeProvider timeProvider)
{ public Task<ImportPipelineResult> RunAsync(ImportStatementCommand command, CancellationToken ct = default); }

// --- Domain tax result ---
public readonly record struct GermanTaxEntry(int Year, DateOnly Date, GermanTaxEntryType Type, string Symbol, string Isin,
    decimal RawAmount, decimal TaxableAmount, decimal UsedVorabpauschale = 0m, decimal ForeignWithholdingTax = 0m,
    decimal QuantitySold = 0m, decimal SaleProceeds = 0m, decimal AcquisitionCosts = 0m);   // WealthIQ.Domain.Model.Tax
public enum GermanTaxEntryType { Sell = 1, Dividend = 2, Interest = 3, Vorabpauschale = 4, WithholdingTax = 5 } // WealthIQ.Domain.Enumeration
public sealed record GermanTaxCalculationResult(IReadOnlyList<GermanTaxEntry> Entries, IReadOnlyList<OpenLot> OpenLots);

// --- Persistence (Plan 1/2) ---
public interface ILedgerStore { Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger l, CancellationToken ct = default);
                                Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default); }
public sealed class SqliteLedgerStore(WealthIqDbContext db) : ILedgerStore;
public sealed class SqliteImportStore(WealthIqDbContext db, ILedgerStore ledgerStore) : IImportStore;
public sealed class WealthIqDbContext(DbContextOptions<WealthIqDbContext> options) : DbContext  // WealthIQ.Infrastructure.Persistence
{ public DbSet<PortfolioEntryRow> PortfolioEntries; public DbSet<InstrumentRow> Instruments; public DbSet<AccountRow> Accounts;
  public DbSet<ImportBatchRow> ImportBatches; public DbSet<ImportDiagnosticRow> ImportDiagnostics;
  public DbSet<BasisInterestRateRow> BasisInterestRates; public DbSet<YearEndPriceRow> YearEndPrices;
  public DbSet<InstrumentProfileRow> InstrumentProfiles; public DbSet<FxRateRow> FxRates; }

// --- Reference rows (Plan 2) ---
public sealed class BasisInterestRateRow { public int Year; public decimal Rate; }
public sealed class YearEndPriceRow { public int Year; public string Isin; public decimal PriceEur; }
public sealed class InstrumentProfileRow { public string Isin; public string Name; public decimal Teilfreistellungsquote; }
public sealed class FxRateRow { public DateOnly Date; public string Currency; public decimal RateToEur; }
public sealed class ImportBatchRow { public Guid BatchId; public string Broker; public string Format; public Guid AccountId;
    public string RawFilePath; public DateTimeOffset ImportedAt; public int InsertedEntries; public int SkippedDuplicateEntries; }
public sealed class ImportDiagnosticRow { public Guid Id; public Guid BatchId; public string Severity; public string Code;
    public string Message; public string? Section; public string? SourceReference; public string? Field; }

// --- Domain primitives ---
public sealed record Instrument(InstrumentId InstrumentId, string ISIN, string Symbol, string Name, decimal Teilfreistellungsquote);
public sealed record Account(AccountId AccountId, string AccountNumber);
public enum Currency { USD, EUR, CHF, GBP, JPY, AUD, CAD, NZD, SEK, NOK, DKK, ZAR, HKD, SGD, CNY, INR }
public sealed record SourceProvenance { public required string SourceSystem; public required string ImportFormat;
    public required string SourceLocation; public required string SourceRecordReference; public string? SourceSection; public string? SourceLineReference; }

// --- Import pipeline command / result (Plan 2) ---
public sealed record ImportStatementCommand(ImportRequest Request, Account Account);
public sealed record ImportRequest { public required ImportSource Source { get; init; } public required AccountId AccountId { get; init; } }
public sealed record class ImportSource(Broker Broker, Format Format, string FilePath);
public sealed record ImportPipelineResult(ImportStatus Status, Guid BatchId, int InsertedEntries, int SkippedDuplicateEntries, IReadOnlyList<ImportDiagnostic> Diagnostics);
public enum ImportStatus { Committed, Aborted }
public sealed class IbkrStatementImporter : IStatementImporter;       // WealthIQ.Infrastructure.Ibkr.Import; parameterless ctor
public sealed class FileSystemRawFileStore(string rootFolder) : IRawFileStore;  // WealthIQ.Infrastructure.Ingest
public sealed class ReferenceDataSeeder(WealthIqDbContext db) : IReferenceDataSeeder; // WealthIQ.Infrastructure.ReferenceData
public sealed record ReferenceDataSources(string BasisInterestRateCsvPath, string YearEndPriceCsvPath, string InstrumentProfileJsonPath, string FxRateCsvPath);
```

### Reference adapter behaviour you must reproduce

The DB adapters in Part A must behave **identically** to the existing CSV/JSON adapters (`src/WealthIQ.Infrastructure/Ibkr/Tax/Csv*Provider.cs`, `JsonInstrumentProfileEnricher.cs`, `Ibkr/Currency/CsvFxRateLookup.cs`), differing only in the data source (SQLite rows instead of files). Critical:

- `IFxRateLookup`: same currency → `1`; target ≠ EUR → throw; exact-date hit → that rate; `NextAvailableOnOrAfter` → first stored date `>= conversionDate` for that currency, else throw; `ExactDate` miss → throw. **No silent fallback** (spec §7).
- `IInstrumentProfileEnricher`: known ISIN → apply profile Name + Teilfreistellungsquote, keep existing Symbol or fall back to `"Unknown"` when empty; unknown ISIN **with** an ISIN → default `0.30` Teilfreistellung and `"Auto-Generated"` name when name empty; **no** ISIN → never invent a 30 % default.
- `IBasisInterestRateProvider`: unknown year → `0` (no Vorabpauschale). `IYearEndPriceProvider`: unknown (isin, year) → `null`.

### Verification commands (run from repo root `E:\05 Projects\CSharp\WealthIQ`)

- Build: `dotnet build "WealthIQ.slnx"`
- All tests: `dotnet test "WealthIQ.slnx"`
- Single test class: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~<Namespace.Class>"`
- Run the web app: `dotnet run --project "src/WealthIQ.Web/WealthIQ.Web.csproj"`

---

## File Structure (created/modified in this plan)

```
src/WealthIQ.Infrastructure/
  ReferenceData/DbBasisInterestRateProvider.cs            (new) — IBasisInterestRateProvider on BasisInterestRates
  ReferenceData/DbYearEndPriceProvider.cs                 (new) — IYearEndPriceProvider on YearEndPrices
  ReferenceData/DbInstrumentProfileEnricher.cs            (new) — IInstrumentProfileEnricher on InstrumentProfiles
  ReferenceData/DbFxRateLookup.cs                         (new) — IFxRateLookup on FxRates (NextAvailableOnOrAfter)
  Persistence/SqliteImportAuditStore.cs                   (new) — IImportAuditStore impl
  Persistence/WealthIqDbContextFactory.cs                 (new) — design-time factory for `dotnet ef`
  Persistence/Migrations/*                                (new, generated) — InitialCreate migration
  WealthIQ.Infrastructure.csproj                          (modify) — add EF Core Design package

src/WealthIQ.Application/
  Tax/Report/TaxReportSummary.cs                          (new) — yearly EUR totals
  Tax/Report/AnnualTaxReport.cs                           (new) — one year's report
  Tax/Report/AnnualTaxReportService.cs                    (new) — load → enrich → calc → aggregate
  Audit/ImportBatchView.cs                                (new) — batch DTO
  Audit/ImportDiagnosticView.cs                           (new) — diagnostic DTO
  Audit/Interface/IImportAuditStore.cs                    (new) — audit query port

src/WealthIQ.Web/                                         (new project — Blazor Server composition root)
  WealthIQ.Web.csproj
  Program.cs                                              — DI wiring + startup migrate + seed
  Composition/DeterministicAccount.cs                    — stable AccountId per (broker, account number)
  Components/_Imports.razor                               (modify) — MudBlazor + app usings
  Components/App.razor                                    (modify) — MudBlazor assets
  Components/Routes.razor                                 (scaffolded)
  Components/Layout/MainLayout.razor                      (modify) — MudBlazor shell + nav
  Components/Pages/Steuerreport.razor                    (new) — "/" main page
  Components/Pages/Import.razor                           (new) — "/import"
  Components/Pages/Audit.razor                            (new) — "/audit"

tests/WealthIQ.Tests/
  Infrastructure/ReferenceData/DbReferenceProviderTests.cs    (new)
  Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs (new)
  Infrastructure/ReferenceData/DbFxRateLookupTests.cs         (new)
  Infrastructure/Persistence/SqliteImportAuditStoreTests.cs   (new)
  Application/Tax/AnnualTaxReportServiceTests.cs              (new)

WealthIQ.slnx                                            (modify) — add WealthIQ.Web
```

---

# Part A — DB-backed reference adapters

## Task 1: DbBasisInterestRateProvider + DbYearEndPriceProvider (TDD)

Both load their (small) tables eagerly in the constructor into a dictionary — same shape as the CSV providers, reading rows instead of file lines.

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbReferenceProviderTests.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateProvider.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbYearEndPriceProvider.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbReferenceProviderTests.cs`:

```csharp
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbReferenceProviderTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.BasisInterestRates.AddRange(
            new BasisInterestRateRow { Year = 2023, Rate = 0.0255m },
            new BasisInterestRateRow { Year = 2024, Rate = 0.0229m });
        ctx.YearEndPrices.AddRange(
            new YearEndPriceRow { Year = 2024, Isin = "IE00B3XXRP09", PriceEur = 106.47m },
            new YearEndPriceRow { Year = 2024, Isin = "IE00B4ND3602", PriceEur = 48.77m });
        ctx.SaveChanges();
        return db;
    }

    [Fact]
    public void BasisInterestRate_ReturnsRate_AndZeroForUnknownYear()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var provider = new DbBasisInterestRateProvider(ctx);

        Assert.Equal(0.0255m, provider.GetRate(2023));
        Assert.Equal(0.0229m, provider.GetRate(2024));
        Assert.Equal(0m, provider.GetRate(1999)); // unknown year → 0 (no Vorabpauschale)
    }

    [Fact]
    public void YearEndPrice_ReturnsPrice_AndNullForUnknown()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var provider = new DbYearEndPriceProvider(ctx);

        Assert.Equal(106.47m, provider.GetPrice("IE00B3XXRP09", 2024));
        Assert.Equal(48.77m, provider.GetPrice("IE00B4ND3602", 2024));
        Assert.Null(provider.GetPrice("IE00B3XXRP09", 2023)); // right ISIN, wrong year
        Assert.Null(provider.GetPrice("UNKNOWN", 2024));        // unknown ISIN
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbReferenceProviderTests"`
Expected: FAIL — `DbBasisInterestRateProvider` / `DbYearEndPriceProvider` do not exist (compile error).

- [ ] **Step 3: Implement DbBasisInterestRateProvider**

Create `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateProvider.cs`:

```csharp
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Basis interest rates from the seeded <c>BasisInterestRates</c> table. Loaded once on construction.</summary>
public sealed class DbBasisInterestRateProvider : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates;

    public DbBasisInterestRateProvider(WealthIqDbContext db)
    {
        _rates = db.BasisInterestRates.ToDictionary(x => x.Year, x => x.Rate);
    }

    public decimal GetRate(int year) => _rates.GetValueOrDefault(year);
}
```

- [ ] **Step 4: Implement DbYearEndPriceProvider**

Create `src/WealthIQ.Infrastructure/ReferenceData/DbYearEndPriceProvider.cs`:

```csharp
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Year-end prices from the seeded <c>YearEndPrices</c> table. Loaded once on construction.</summary>
public sealed class DbYearEndPriceProvider : IYearEndPriceProvider
{
    private readonly Dictionary<(int Year, string Isin), decimal> _prices;

    public DbYearEndPriceProvider(WealthIqDbContext db)
    {
        _prices = db.YearEndPrices.ToDictionary(x => (x.Year, x.Isin), x => x.PriceEur);
    }

    public decimal? GetPrice(string isin, int year)
        => _prices.TryGetValue((year, isin), out var price) ? price : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbReferenceProviderTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateProvider.cs src/WealthIQ.Infrastructure/ReferenceData/DbYearEndPriceProvider.cs tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbReferenceProviderTests.cs
git commit -m "feat: add DB-backed basis-interest and year-end-price providers"
```

---

## Task 2: DbInstrumentProfileEnricher (TDD)

Mirrors `JsonInstrumentProfileEnricher` exactly, reading `InstrumentProfiles` rows instead of JSON.

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs`:

```csharp
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbInstrumentProfileEnricherTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.InstrumentProfiles.AddRange(
            new InstrumentProfileRow { Isin = "IE00B3XXRP09", Name = "Vanguard S&P 500", Teilfreistellungsquote = 0.30m },
            new InstrumentProfileRow { Isin = "IE00B4ND3602", Name = "iShares Physical Gold", Teilfreistellungsquote = 0m });
        ctx.SaveChanges();
        return db;
    }

    private static Instrument Raw(string isin, string symbol, string name = "raw", decimal tfs = 0m)
        => new(InstrumentId.NewId(), isin, symbol, name, tfs);

    [Fact]
    public void Enrich_KnownIsin_AppliesProfileNameAndTeilfreistellung_KeepsSymbol()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B3XXRP09", "VUSA"));

        Assert.Equal("Vanguard S&P 500", enriched.Name);
        Assert.Equal(0.30m, enriched.Teilfreistellungsquote);
        Assert.Equal("VUSA", enriched.Symbol);
    }

    [Fact]
    public void Enrich_KnownIsin_ZeroTeilfreistellung_IsRespected()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B4ND3602", "SGLN"));
        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }

    [Fact]
    public void Enrich_KnownIsin_EmptySymbol_UsesUnknownFallback()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B3XXRP09", ""));
        Assert.Equal("Unknown", enriched.Symbol);
    }

    [Fact]
    public void Enrich_UnknownIsin_WithIsin_DefaultsToThirtyPercentAndAutoName()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("XX0000000000", "ABC", name: ""));
        Assert.Equal(0.30m, enriched.Teilfreistellungsquote);
        Assert.Equal("Auto-Generated", enriched.Name);
    }

    [Fact]
    public void Enrich_NoIsin_DoesNotInventTeilfreistellung()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("", "EUR", name: ""));
        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbInstrumentProfileEnricherTests"`
Expected: FAIL — `DbInstrumentProfileEnricher` does not exist (compile error).

- [ ] **Step 3: Implement the enricher**

Create `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs`:

```csharp
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Enriches instruments from the seeded <c>InstrumentProfiles</c> table. Behaviour matches
/// <see cref="WealthIQ.Infrastructure.Ibkr.Tax.JsonInstrumentProfileEnricher"/>: known ISIN applies the
/// stored profile (symbol falls back to "Unknown" when empty); an unknown but ISIN-bearing fund defaults
/// to 30 % Teilfreistellung and an "Auto-Generated" name; an instrument without an ISIN is never defaulted.
/// </summary>
public sealed class DbInstrumentProfileEnricher : IInstrumentProfileEnricher
{
    private readonly Dictionary<string, (string Name, decimal Teilfreistellungsquote)> _profiles;

    public DbInstrumentProfileEnricher(WealthIqDbContext db)
    {
        _profiles = db.InstrumentProfiles.ToDictionary(
            x => x.Isin,
            x => (x.Name, x.Teilfreistellungsquote),
            StringComparer.OrdinalIgnoreCase);
    }

    public Instrument Enrich(Instrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(instrument.ISIN)
            && _profiles.TryGetValue(instrument.ISIN, out var profile))
        {
            return instrument with
            {
                Name = profile.Name,
                Teilfreistellungsquote = profile.Teilfreistellungsquote,
                Symbol = string.IsNullOrWhiteSpace(instrument.Symbol) ? "Unknown" : instrument.Symbol
            };
        }

        return instrument with
        {
            Name = string.IsNullOrWhiteSpace(instrument.Name) ? "Auto-Generated" : instrument.Name,
            Teilfreistellungsquote = instrument.Teilfreistellungsquote == 0m && !string.IsNullOrWhiteSpace(instrument.ISIN)
                ? 0.30m
                : instrument.Teilfreistellungsquote
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbInstrumentProfileEnricherTests"`
Expected: PASS (all five tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentProfileEnricherTests.cs
git commit -m "feat: add DB-backed instrument profile enricher"
```

---

## Task 3: DbFxRateLookup (TDD — reproduces NextAvailableOnOrAfter)

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbFxRateLookupTests.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbFxRateLookup.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbFxRateLookupTests.cs`:

```csharp
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbFxRateLookupTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.FxRates.AddRange(
            new FxRateRow { Date = new DateOnly(2021, 3, 26), Currency = "USD", RateToEur = 0.8487523341m },
            new FxRateRow { Date = new DateOnly(2021, 3, 29), Currency = "USD", RateToEur = 0.8501000000m },
            new FxRateRow { Date = new DateOnly(2021, 3, 26), Currency = "GBP", RateToEur = 1.1695496064m });
        ctx.SaveChanges();
        return db;
    }

    private static DbFxRateLookup Lookup(InMemorySqlite db) => new(db.NewContext());

    [Fact]
    public void GetRate_SameCurrency_ReturnsOne()
    {
        using var db = SeededDb();
        Assert.Equal(1m, Lookup(db).GetRate(new DateOnly(2099, 1, 1), Currency.EUR, Currency.EUR));
    }

    [Fact]
    public void GetRate_ExactDate_ReturnsConfiguredRate()
    {
        using var db = SeededDb();
        Assert.Equal(0.8487523341m, Lookup(db).GetRate(new DateOnly(2021, 3, 26), Currency.USD, Currency.EUR));
    }

    [Fact]
    public void GetRate_MissingDate_ExactHandling_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 27), Currency.USD, Currency.EUR, FxRateLookupDateHandling.ExactDate));
    }

    [Fact]
    public void GetRate_MissingDate_NextAvailableOnOrAfter_RollsForwardToNextTradingDay()
    {
        using var db = SeededDb();
        var rate = Lookup(db).GetRate(new DateOnly(2021, 3, 27), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8501000000m, rate);
    }

    [Fact]
    public void GetRate_ExactDatePresent_NextAvailableHandling_ReturnsExactNotRolled()
    {
        using var db = SeededDb();
        var rate = Lookup(db).GetRate(new DateOnly(2021, 3, 26), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter);
        Assert.Equal(0.8487523341m, rate);
    }

    [Fact]
    public void GetRate_AfterLastAvailableDate_NextAvailableHandling_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 4, 1), Currency.USD, Currency.EUR, FxRateLookupDateHandling.NextAvailableOnOrAfter));
    }

    [Fact]
    public void GetRate_TargetCurrencyNotEur_Throws()
    {
        using var db = SeededDb();
        var lookup = Lookup(db);
        Assert.Throws<InvalidOperationException>(() =>
            lookup.GetRate(new DateOnly(2021, 3, 26), Currency.GBP, Currency.USD));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbFxRateLookupTests"`
Expected: FAIL — `DbFxRateLookup` does not exist (compile error).

- [ ] **Step 3: Implement DbFxRateLookup**

Create `src/WealthIQ.Infrastructure/ReferenceData/DbFxRateLookup.cs`:

```csharp
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// FX rates from the seeded <c>FxRates</c> table. Loaded once on construction. Reproduces
/// <see cref="WealthIQ.Infrastructure.Ibkr.Currency.CsvFxRateLookup"/>: same currency → 1; target ≠ EUR
/// throws; exact-date hit wins; <see cref="FxRateLookupDateHandling.NextAvailableOnOrAfter"/> rolls forward
/// to the first stored date on or after the requested one; otherwise a missing rate is a blocking error (spec §7).
/// Rows whose currency text is not a known <see cref="CurrencyCode"/> or whose rate is ≤ 0 are ignored.
/// </summary>
public sealed class DbFxRateLookup : IFxRateLookup
{
    private readonly Dictionary<(DateOnly Date, CurrencyCode Currency), decimal> _rates = new();
    private readonly Dictionary<CurrencyCode, List<DateOnly>> _datesByCurrency = new();

    public DbFxRateLookup(WealthIqDbContext db)
    {
        foreach (var row in db.FxRates)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency) || row.RateToEur <= 0m)
            {
                continue;
            }

            _rates[(row.Date, currency)] = row.RateToEur;

            if (!_datesByCurrency.TryGetValue(currency, out var dates))
            {
                dates = [];
                _datesByCurrency[currency] = dates;
            }

            dates.Add(row.Date);
        }

        foreach (var currency in _datesByCurrency.Keys.ToList())
        {
            _datesByCurrency[currency] = _datesByCurrency[currency].Distinct().OrderBy(x => x).ToList();
        }
    }

    public decimal GetRate(
        DateOnly conversionDate,
        CurrencyCode sourceCurrency,
        CurrencyCode targetCurrency,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
    {
        if (sourceCurrency == targetCurrency)
        {
            return 1m;
        }

        if (targetCurrency != CurrencyCode.EUR)
        {
            throw new InvalidOperationException($"Target currency '{targetCurrency}' is not supported by the DB FX lookup.");
        }

        if (_rates.TryGetValue((conversionDate, sourceCurrency), out var exactRate))
        {
            return exactRate;
        }

        if (dateHandling == FxRateLookupDateHandling.NextAvailableOnOrAfter
            && _datesByCurrency.TryGetValue(sourceCurrency, out var availableDates))
        {
            var nextDate = availableDates.FirstOrDefault(x => x >= conversionDate);
            if (nextDate != default && _rates.TryGetValue((nextDate, sourceCurrency), out var nextRate))
            {
                return nextRate;
            }
        }

        throw new InvalidOperationException(
            $"FX rate missing for {sourceCurrency}->{targetCurrency} on '{conversionDate:yyyy-MM-dd}' with handling '{dateHandling}'.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~DbFxRateLookupTests"`
Expected: PASS (all seven tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/ReferenceData/DbFxRateLookup.cs tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbFxRateLookupTests.cs
git commit -m "feat: add DB-backed FX rate lookup with NextAvailableOnOrAfter roll-forward"
```

---

# Part B — Tax-report query & audit query

## Task 4: AnnualTaxReportService + report DTOs (TDD)

Loads the ledger, enriches the catalog, runs `GermanTaxCalculator`, and aggregates one `AnnualTaxReport` per year present in the result (spec §9 summary fields, all EUR).

**Files:**
- Create: `src/WealthIQ.Application/Tax/Report/TaxReportSummary.cs`
- Create: `src/WealthIQ.Application/Tax/Report/AnnualTaxReport.cs`
- Create: `src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs`

- [ ] **Step 1: Create the summary record**

Create `src/WealthIQ.Application/Tax/Report/TaxReportSummary.cs`:

```csharp
namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// One year's tax totals, all in EUR. <see cref="EstimatedTax"/> is a rough Abgeltungsteuer estimate
/// (25 % + 5.5 % Solidaritätszuschlag = 26.375 %) on the positive taxable base, less foreign withholding
/// tax already paid. It is an estimate for orientation, not a Finanzamt-binding figure (spec §1, §9).
/// </summary>
public sealed record TaxReportSummary(
    decimal NetRealizedGainsTaxable,
    decimal DividendsTaxable,
    decimal InterestTaxable,
    decimal VorabpauschaleTaxable,
    decimal ForeignWithholdingTax,
    decimal EstimatedTax);
```

- [ ] **Step 2: Create the annual-report record**

Create `src/WealthIQ.Application/Tax/Report/AnnualTaxReport.cs`:

```csharp
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>A single tax year: headline summary plus the underlying tax entries grouped by kind (for the drill-down grids).</summary>
public sealed record AnnualTaxReport(
    int Year,
    TaxReportSummary Summary,
    IReadOnlyList<GermanTaxEntry> Sells,
    IReadOnlyList<GermanTaxEntry> Dividends,
    IReadOnlyList<GermanTaxEntry> Interest,
    IReadOnlyList<GermanTaxEntry> WithholdingTaxes,
    IReadOnlyList<GermanTaxEntry> Vorabpauschale);
```

- [ ] **Step 3: Write the failing service test**

Create `tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs`:

```csharp
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class AnnualTaxReportServiceTests
{
    private sealed class FixedLedgerStore(PortfolioLedger ledger) : ILedgerStore
    {
        public Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger l, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default) => Task.FromResult(ledger);
    }

    private sealed class IdentityProfileEnricher : IInstrumentProfileEnricher
    {
        public Instrument Enrich(Instrument instrument) => instrument;
    }

    [Fact]
    public async Task Generate_BuySellAndDividendSameYear_ProducesYearSummaryAndSections()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0001", "AAA", "Alpha", 0m); // 0 % Teilfreistellung → taxable == raw

        // EUR-only so no FX rate is needed. Buy 10@100 (2024-01-10), sell 10@120 (2024-06-10) → gain 200.
        var buy = TaxEntries.Trade(accountId, instrumentId, TradeSide.Buy, 10m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "BUY-1");
        var sell = TaxEntries.Trade(accountId, instrumentId, TradeSide.Sell, 10m, 120m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "SELL-1");
        var dividend = TaxEntries.Dividend(accountId, instrumentId, instrumentId, 50m,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero), "DIV-1");

        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { buy, sell, dividend },
            new[] { instrument },
            new[] { new Account(accountId, "U1") });

        var service = new AnnualTaxReportService(
            new FixedLedgerStore(ledger),
            new InstrumentCatalogBuilder(new IdentityProfileEnricher()),
            new GermanTaxCalculator(
                new FakeBasisInterestRateProvider((2024, 0m)),   // rate 0 → no Vorabpauschale
                new FakeYearEndPriceProvider(),
                new FakeFxRateLookup()));

        var reports = await service.GenerateAsync();

        var report = Assert.Single(reports);
        Assert.Equal(2024, report.Year);
        Assert.Single(report.Sells);
        Assert.Single(report.Dividends);
        Assert.Empty(report.Vorabpauschale);

        Assert.Equal(200m, report.Summary.NetRealizedGainsTaxable);
        Assert.Equal(50m, report.Summary.DividendsTaxable);
        Assert.Equal(0m, report.Summary.InterestTaxable);
        Assert.Equal(0m, report.Summary.VorabpauschaleTaxable);
        Assert.Equal(0m, report.Summary.ForeignWithholdingTax);
        // (200 + 50) * 0.26375 = 65.9375
        Assert.Equal(65.9375m, report.Summary.EstimatedTax);
    }
}
```

> `TaxEntries`, `FakeBasisInterestRateProvider`, `FakeYearEndPriceProvider`, and `FakeFxRateLookup` already exist in `tests/WealthIQ.Tests/Application/Tax/TaxTestDoubles.cs` (same namespace) — reuse them.

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~AnnualTaxReportServiceTests"`
Expected: FAIL — `AnnualTaxReportService` does not exist (compile error).

- [ ] **Step 5: Implement the service**

Create `src/WealthIQ.Application/Tax/Report/AnnualTaxReportService.cs`:

```csharp
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// Builds the yearly German tax report (spec §6 "Replay & Compute", §9). Loads the persisted ledger,
/// enriches the instrument catalog, runs <see cref="GermanTaxCalculator"/>, and aggregates per year.
/// A missing FX/reference value surfaces as the calculator's exception (fail-fast, spec §7/§8) — callers display it.
/// </summary>
public sealed class AnnualTaxReportService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    GermanTaxCalculator calculator)
{
    private const decimal AbgeltungsteuerWithSoli = 0.26375m; // 25 % + 5.5 % Soli

    public async Task<IReadOnlyList<AnnualTaxReport>> GenerateAsync(CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var result = calculator.Calculate(ledger, catalog);

        return result.Entries
            .GroupBy(e => e.Year)
            .OrderBy(g => g.Key)
            .Select(BuildAnnualReport)
            .ToList();
    }

    private static AnnualTaxReport BuildAnnualReport(IGrouping<int, GermanTaxEntry> yearEntries)
    {
        var sells = yearEntries.Where(e => e.Type == GermanTaxEntryType.Sell).ToList();
        var dividends = yearEntries.Where(e => e.Type == GermanTaxEntryType.Dividend).ToList();
        var interest = yearEntries.Where(e => e.Type == GermanTaxEntryType.Interest).ToList();
        var withholding = yearEntries.Where(e => e.Type == GermanTaxEntryType.WithholdingTax).ToList();
        var vorab = yearEntries.Where(e => e.Type == GermanTaxEntryType.Vorabpauschale).ToList();

        var netSells = sells.Sum(e => e.TaxableAmount);
        var dividendTaxable = dividends.Sum(e => e.TaxableAmount);
        var interestTaxable = interest.Sum(e => e.TaxableAmount);
        var vorabTaxable = vorab.Sum(e => e.TaxableAmount);
        var foreignWithholding = withholding.Sum(e => e.ForeignWithholdingTax);

        var taxableBase = netSells + dividendTaxable + interestTaxable + vorabTaxable;
        var grossTax = Math.Max(0m, taxableBase) * AbgeltungsteuerWithSoli;
        var estimatedTax = Math.Max(0m, grossTax - foreignWithholding);

        var summary = new TaxReportSummary(netSells, dividendTaxable, interestTaxable, vorabTaxable, foreignWithholding, estimatedTax);
        return new AnnualTaxReport(yearEntries.Key, summary, sells, dividends, interest, withholding, vorab);
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~AnnualTaxReportServiceTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Application/Tax/Report tests/WealthIQ.Tests/Application/Tax/AnnualTaxReportServiceTests.cs
git commit -m "feat: add AnnualTaxReportService and report DTOs (yearly tax replay)"
```

---

## Task 5: IImportAuditStore + SqliteImportAuditStore (TDD)

Read-only query over the persisted `ImportBatches` and `ImportDiagnostics` for the Audit page (spec §9 "Diagnostics / Audit").

**Files:**
- Create: `src/WealthIQ.Application/Audit/ImportBatchView.cs`
- Create: `src/WealthIQ.Application/Audit/ImportDiagnosticView.cs`
- Create: `src/WealthIQ.Application/Audit/Interface/IImportAuditStore.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportAuditStoreTests.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs`

- [ ] **Step 1: Create the view DTOs**

Create `src/WealthIQ.Application/Audit/ImportBatchView.cs`:

```csharp
namespace WealthIQ.Application.Audit;

/// <summary>One persisted import run, for the Audit page.</summary>
public sealed record ImportBatchView(
    Guid BatchId,
    string Broker,
    string Format,
    Guid AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt,
    int InsertedEntries,
    int SkippedDuplicateEntries);
```

Create `src/WealthIQ.Application/Audit/ImportDiagnosticView.cs`:

```csharp
namespace WealthIQ.Application.Audit;

/// <summary>One persisted diagnostic, linked to its batch, for the Audit page.</summary>
public sealed record ImportDiagnosticView(
    Guid Id,
    Guid BatchId,
    string Severity,
    string Code,
    string Message,
    string? Section,
    string? SourceReference,
    string? Field);
```

- [ ] **Step 2: Create the port**

Create `src/WealthIQ.Application/Audit/Interface/IImportAuditStore.cs`:

```csharp
namespace WealthIQ.Application.Audit.Interface;

/// <summary>Read-only access to persisted import batches and diagnostics (spec §9 Diagnostics/Audit).</summary>
public interface IImportAuditStore
{
    Task<IReadOnlyList<ImportBatchView>> GetBatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ImportDiagnosticView>> GetDiagnosticsAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportAuditStoreTests.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class SqliteImportAuditStoreTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/file.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(AccountId account, InstrumentId instrument, string reference) =>
        new(PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1), Provenance(reference), instrument,
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    [Fact]
    public async Task GetBatchesAndDiagnostics_ReturnPersistedRows()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);
        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1") },
            new[] { instrument }, new[] { account });
        var batchId = Guid.NewGuid();
        var batch = new ImportBatch(batchId, Broker.InteractiveBrokers, Format.XML, account.AccountId,
            "audit/file.xml", new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));
        var diagnostics = new[]
        {
            new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped one", Section: "Trades")
        };

        await using (var ctx = db.NewContext())
        {
            await new SqliteImportStore(ctx, new SqliteLedgerStore(ctx)).PersistImportAsync(batch, ledger, diagnostics);
        }

        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportAuditStore(ctx);

            var batchView = Assert.Single(await store.GetBatchesAsync());
            Assert.Equal(batchId, batchView.BatchId);
            Assert.Equal("InteractiveBrokers", batchView.Broker);
            Assert.Equal(1, batchView.InsertedEntries);

            var diagView = Assert.Single(await store.GetDiagnosticsAsync());
            Assert.Equal(batchId, diagView.BatchId);
            Assert.Equal("Warning", diagView.Severity);
            Assert.Equal("Trades", diagView.Section);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteImportAuditStoreTests"`
Expected: FAIL — `SqliteImportAuditStore` does not exist (compile error).

- [ ] **Step 5: Implement the audit store**

Create `src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Audit;
using WealthIQ.Application.Audit.Interface;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>Reads persisted import batches and diagnostics for the Audit page. Newest batches first.</summary>
public sealed class SqliteImportAuditStore(WealthIqDbContext db) : IImportAuditStore
{
    public async Task<IReadOnlyList<ImportBatchView>> GetBatchesAsync(CancellationToken ct = default)
    {
        var rows = await db.ImportBatches.AsNoTracking()
            .OrderByDescending(x => x.ImportedAt)
            .ToListAsync(ct);

        return rows.Select(x => new ImportBatchView(
            x.BatchId, x.Broker, x.Format, x.AccountId, x.RawFilePath, x.ImportedAt,
            x.InsertedEntries, x.SkippedDuplicateEntries)).ToList();
    }

    public async Task<IReadOnlyList<ImportDiagnosticView>> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var rows = await db.ImportDiagnostics.AsNoTracking().ToListAsync(ct);

        return rows.Select(x => new ImportDiagnosticView(
            x.Id, x.BatchId, x.Severity, x.Code, x.Message, x.Section, x.SourceReference, x.Field)).ToList();
    }
}
```

- [ ] **Step 6: Run the test, then the full suite**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteImportAuditStoreTests"`
Expected: PASS.

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Application/Audit src/WealthIQ.Infrastructure/Persistence/SqliteImportAuditStore.cs tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportAuditStoreTests.cs
git commit -m "feat: add import-audit query store (batches + diagnostics)"
```

---

# Part C — EF Core migrations

## Task 6: Design-time factory + InitialCreate migration

Replaces `EnsureCreated` for the production DB. Tests keep using `InMemorySqlite` (`EnsureCreated`), unaffected.

**Files:**
- Modify: `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj`
- Create: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContextFactory.cs`
- Create (generated): `src/WealthIQ.Infrastructure/Persistence/Migrations/*`

- [ ] **Step 1: Add the EF Core Design package**

Run: `dotnet add "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj" package Microsoft.EntityFrameworkCore.Design --version 10.0.0`
Expected: package added; `WealthIQ.Infrastructure.csproj` now lists `Microsoft.EntityFrameworkCore.Design`.

- [ ] **Step 2: Ensure the `dotnet ef` tool is available**

Run: `dotnet tool install --global dotnet-ef --version 10.0.0 || dotnet tool update --global dotnet-ef --version 10.0.0`
Then verify: `dotnet ef --version`
Expected: prints an Entity Framework Core .NET Command-line Tools `10.x` version. (If `dotnet ef` is not found, ensure `~/.dotnet/tools` is on PATH for this shell.)

- [ ] **Step 3: Create the design-time factory**

Create `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` build the context without the Web host. The connection string here is only used by the
/// EF tooling at design time; the running app supplies its own SQLite path via DI (see WealthIQ.Web/Program.cs).
/// </summary>
public sealed class WealthIqDbContextFactory : IDesignTimeDbContextFactory<WealthIqDbContext>
{
    public WealthIqDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("Data Source=wealthiq-design.db")
            .Options;
        return new WealthIqDbContext(options);
    }
}
```

- [ ] **Step 4: Generate the InitialCreate migration**

Run from the repo root:

```bash
dotnet ef migrations add InitialCreate \
  --project "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj" \
  --output-dir "Persistence/Migrations"
```

Expected: "Build started... Done." and three generated files under `src/WealthIQ.Infrastructure/Persistence/Migrations/`: `<timestamp>_InitialCreate.cs`, `<timestamp>_InitialCreate.Designer.cs`, and `WealthIqDbContextModelSnapshot.cs`.

- [ ] **Step 5: Sanity-check the migration covers every table**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

Open the generated `<timestamp>_InitialCreate.cs` and confirm its `Up` creates all nine tables: `PortfolioEntries`, `Instruments`, `Accounts`, `ImportBatches`, `ImportDiagnostics`, `BasisInterestRates`, `YearEndPrices`, `InstrumentProfiles`, `FxRates`. (No code edit — this is a verification read. If any table is missing, a DbSet/`OnModelCreating` entry was lost; fix the DbContext, delete the `Persistence/Migrations` folder, and re-run Step 4.)

- [ ] **Step 6: Remove the stray design-time database file if one was created**

Run: `rm -f wealthiq-design.db`
(The factory's connection string can cause the EF tooling to touch a file in the repo root; it is not needed and must not be committed.)

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj src/WealthIQ.Infrastructure/Persistence/WealthIqDbContextFactory.cs src/WealthIQ.Infrastructure/Persistence/Migrations
git commit -m "feat: add EF Core design-time factory and InitialCreate migration"
```

---

# Part D — Blazor Server dashboard (composition root)

> The Blazor UI is deliberately thin (spec §9) and verified manually for v1 (spec §10 — bUnit optional later). Each task ends by building and, where noted, running the app and observing behaviour. Logic lives in Application; pages only orchestrate and render.

## Task 7: Create the WealthIQ.Web project and add MudBlazor

**Files:**
- Create: `src/WealthIQ.Web/` (Blazor Server scaffold)
- Modify: `WealthIQ.slnx`

- [ ] **Step 1: Scaffold a Blazor Server project**

Run from the repo root:

```bash
dotnet new blazor -o "src/WealthIQ.Web" --interactivity Server --empty
```

Expected: a new project at `src/WealthIQ.Web` with `Program.cs`, `WealthIQ.Web.csproj`, and a `Components/` folder (`App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, `Pages/Home.razor`).

> If your SDK rejects `--empty`, omit it and run `dotnet new blazor -o "src/WealthIQ.Web" --interactivity Server`, then delete the sample pages it adds (`Components/Pages/Counter.razor`, `Components/Pages/Weather.razor`) in Step 4.

- [ ] **Step 2: Reference the solution projects**

Run from the repo root:

```bash
dotnet add "src/WealthIQ.Web/WealthIQ.Web.csproj" reference \
  "src/WealthIQ.Application/WealthIQ.Application.csproj" \
  "src/WealthIQ.Domain/WealthIQ.Domain.csproj" \
  "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj"
```

- [ ] **Step 3: Add MudBlazor**

Run: `dotnet add "src/WealthIQ.Web/WealthIQ.Web.csproj" package MudBlazor`
Expected: latest stable MudBlazor (8.x) added. Pin whatever version resolves by leaving the `<PackageReference>` as written in the csproj.

- [ ] **Step 4: Add WealthIQ.Web to the solution and remove sample pages**

Edit `WealthIQ.slnx` — add the Web project inside the `/src/` folder, after the Infrastructure line:

```xml
    <Project Path="src/WealthIQ.Web/WealthIQ.Web.csproj" />
```

Then remove any sample pages so they do not collide with our routes:

```bash
rm -f "src/WealthIQ.Web/Components/Pages/Home.razor" \
      "src/WealthIQ.Web/Components/Pages/Counter.razor" \
      "src/WealthIQ.Web/Components/Pages/Weather.razor"
```

- [ ] **Step 5: Build**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded (the Web project builds; it has no pages with routes yet, which is fine).

- [ ] **Step 6: Commit**

```bash
git add WealthIQ.slnx src/WealthIQ.Web
git commit -m "chore: scaffold WealthIQ.Web (Blazor Server) and add MudBlazor"
```

---

## Task 8: Composition root — DI wiring, MudBlazor, startup migrate + seed

**Files:**
- Create: `src/WealthIQ.Web/Composition/DeterministicAccount.cs`
- Replace: `src/WealthIQ.Web/Program.cs`
- Modify: `src/WealthIQ.Web/Components/_Imports.razor`
- Modify: `src/WealthIQ.Web/Components/App.razor`
- Modify: `src/WealthIQ.Web/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Create the deterministic account helper**

Create `src/WealthIQ.Web/Composition/DeterministicAccount.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Web.Composition;

/// <summary>
/// Derives a stable <see cref="AccountId"/> from (broker, account number) so that re-importing the same
/// account upserts one account row instead of creating duplicates (dedup itself is by source reference).
/// </summary>
public static class DeterministicAccount
{
    public static AccountId IdFor(string broker, string accountNumber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{broker}:{accountNumber}"));
        return new AccountId(new Guid(hash.AsSpan(0, 16)));
    }
}
```

- [ ] **Step 2: Replace Program.cs with the composition root**

Replace the entire contents of `src/WealthIQ.Web/Program.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WealthIQ.Application.Audit.Interface;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ingest;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// --- Local data layout (dev convenience; a configurable path is a later concern) ---
// ContentRootPath = src/WealthIQ.Web → repo root is two levels up.
var repoData = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
var referenceDir = Path.Combine(repoData, "reference");
var appDataDir = Path.Combine(repoData, "app");
var auditDir = Path.Combine(appDataDir, "audit");
var dbPath = Path.Combine(appDataDir, "wealthiq.db");
Directory.CreateDirectory(auditDir);

// --- Blazor + MudBlazor ---
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

// --- Persistence (scoped DbContext; single-user local app → operations are sequential) ---
builder.Services.AddDbContext<WealthIqDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<ILedgerStore, SqliteLedgerStore>();
builder.Services.AddScoped<IImportStore, SqliteImportStore>();
builder.Services.AddScoped<IImportAuditStore, SqliteImportAuditStore>();
builder.Services.AddSingleton<IRawFileStore>(_ => new FileSystemRawFileStore(auditDir));

// --- Import ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IStatementImporter, IbkrStatementImporter>();
builder.Services.AddScoped<StatementImportPipeline>();

// --- Reference data (seeder + DB-backed adapters the tax engine consumes) ---
builder.Services.AddScoped<IReferenceDataSeeder, ReferenceDataSeeder>();
builder.Services.AddScoped<IBasisInterestRateProvider, DbBasisInterestRateProvider>();
builder.Services.AddScoped<IYearEndPriceProvider, DbYearEndPriceProvider>();
builder.Services.AddScoped<IInstrumentProfileEnricher, DbInstrumentProfileEnricher>();
builder.Services.AddScoped<IFxRateLookup, DbFxRateLookup>();

// --- Tax replay ---
builder.Services.AddScoped<InstrumentCatalogBuilder>();
builder.Services.AddScoped<GermanTaxCalculator>();
builder.Services.AddScoped<AnnualTaxReportService>();

var app = builder.Build();

// --- Startup: apply migrations, then seed reference data from data/reference (seed-if-empty) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WealthIqDbContext>();
    db.Database.Migrate();

    var seeder = scope.ServiceProvider.GetRequiredService<IReferenceDataSeeder>();
    var sources = new ReferenceDataSources(
        Path.Combine(referenceDir, "basiszins.csv"),
        Path.Combine(referenceDir, "prices.csv"),
        Path.Combine(referenceDir, "instruments.json"),
        Path.Combine(referenceDir, "fx_rates.csv"));
    await seeder.SeedIfEmptyAsync(sources);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
```

> The `DbFxRateLookup`, `DbBasisInterestRateProvider`, `DbYearEndPriceProvider`, and `DbInstrumentProfileEnricher` load their (already-seeded) reference rows in their constructors. Because they are scoped, each Blazor circuit resolves them once after seeding has completed — reference data does not change during a session.

- [ ] **Step 3: Wire MudBlazor usings in _Imports.razor**

Append these lines to `src/WealthIQ.Web/Components/_Imports.razor`:

```razor
@using MudBlazor
@using WealthIQ.Web.Components
@using WealthIQ.Web.Composition
```

- [ ] **Step 4: Add MudBlazor's CSS/JS to App.razor**

Edit `src/WealthIQ.Web/Components/App.razor`. Inside `<head>`, add the MudBlazor stylesheet and the Roboto font link (after the existing `<link>`/`HeadOutlet` lines):

```html
    <link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
```

And just before the closing `</body>`, after the existing `<script src="_framework/blazor.web.js"></script>`, add:

```html
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

- [ ] **Step 5: Replace MainLayout with a MudBlazor shell + nav**

Replace the entire contents of `src/WealthIQ.Web/Components/Layout/MainLayout.razor` with:

```razor
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudText Typo="Typo.h6">WealthIQ</MudText>
        <MudSpacer />
        <MudButton Href="/" Color="Color.Inherit">Steuerreport</MudButton>
        <MudButton Href="/import" Color="Color.Inherit">Import</MudButton>
        <MudButton Href="/audit" Color="Color.Inherit">Diagnostics</MudButton>
    </MudAppBar>
    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Large" Class="my-6">
            @Body
        </MudContainer>
    </MudMainContent>
</MudLayout>
```

> If the scaffold generated `Components/Layout/NavMenu.razor`, it is no longer referenced; leave it or delete it — it does not affect routing.

- [ ] **Step 6: Build**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Web
git commit -m "feat: wire WealthIQ.Web composition root (DI, MudBlazor, startup migrate + seed)"
```

---

## Task 9: Steuerreport page (main, "/")

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Steuerreport.razor`:

```razor
@page "/"
@rendermode InteractiveServer
@using WealthIQ.Application.Tax.Report
@using WealthIQ.Domain.Model.Tax
@inject AnnualTaxReportService ReportService
@inject NavigationManager Navigation

<PageTitle>WealthIQ — Steuerreport</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Steuerreport</MudText>

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

@if (_loading)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_reports.Count == 0)
{
    <MudAlert Severity="Severity.Info">Noch keine Daten. Importiere zuerst ein Broker-Statement auf der Import-Seite.</MudAlert>
}
else
{
    <MudPaper Class="pa-4 mb-4">
        <MudSelect T="int" Value="_selectedYear" ValueChanged="OnYearChanged" Label="Jahr" Variant="Variant.Outlined" Dense="true" Style="max-width: 200px;">
            @foreach (var report in _reports)
            {
                <MudSelectItem T="int" Value="report.Year">@report.Year</MudSelectItem>
            }
        </MudSelect>
    </MudPaper>

    @if (Current is not null)
    {
        <MudGrid Class="mb-4">
            @SummaryCard("Verkäufe (steuerpflichtig)", Current.Summary.NetRealizedGainsTaxable)
            @SummaryCard("Dividenden (steuerpflichtig)", Current.Summary.DividendsTaxable)
            @SummaryCard("Zinsen (steuerpflichtig)", Current.Summary.InterestTaxable)
            @SummaryCard("Vorabpauschale (steuerpflichtig)", Current.Summary.VorabpauschaleTaxable)
            @SummaryCard("Anrechenbare Quellensteuer", Current.Summary.ForeignWithholdingTax)
            @SummaryCard("Geschätzte Steuer", Current.Summary.EstimatedTax)
        </MudGrid>

        <MudExpansionPanels MultiExpansion="true">
            @Section("Verkäufe (realisierter PnL)", Current.Sells)
            @Section("Vorabpauschale", Current.Vorabpauschale)
            @Section("Dividenden", Current.Dividends)
            @Section("Zinsen", Current.Interest)
            @Section("Quellensteuer", Current.WithholdingTaxes)
        </MudExpansionPanels>
    }
}

@code {
    private bool _loading = true;
    private string? _error;
    private IReadOnlyList<AnnualTaxReport> _reports = Array.Empty<AnnualTaxReport>();
    private int _selectedYear;

    private AnnualTaxReport? Current => _reports.FirstOrDefault(r => r.Year == _selectedYear);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _reports = await ReportService.GenerateAsync();
            _selectedYear = _reports.Count > 0 ? _reports[^1].Year : 0;
        }
        catch (Exception ex)
        {
            _error = $"Berechnung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnYearChanged(int year) => _selectedYear = year;

    private static string Eur(decimal amount) => amount.ToString("N2") + " €";

    private RenderFragment SummaryCard(string label, decimal amount) => @<MudItem xs="12" sm="6" md="4">
        <MudPaper Class="pa-4" Elevation="2">
            <MudText Typo="Typo.caption">@label</MudText>
            <MudText Typo="Typo.h6">@Eur(amount)</MudText>
        </MudPaper>
    </MudItem>;

    private RenderFragment Section(string title, IReadOnlyList<GermanTaxEntry> entries) => @<MudExpansionPanel Text="@($"{title} ({entries.Count})")">
        @if (entries.Count == 0)
        {
            <MudText Typo="Typo.body2">Keine Einträge.</MudText>
        }
        else
        {
            <MudTable Items="entries" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
                <HeaderContent>
                    <MudTh>Datum</MudTh>
                    <MudTh>Symbol</MudTh>
                    <MudTh>ISIN</MudTh>
                    <MudTh Style="text-align:right">Brutto (€)</MudTh>
                    <MudTh Style="text-align:right">Steuerpflichtig (€)</MudTh>
                    <MudTh Style="text-align:right">Verrechn. Vorabpausch. (€)</MudTh>
                    <MudTh>Quelle</MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="Datum">@context.Date.ToString("yyyy-MM-dd")</MudTd>
                    <MudTd DataLabel="Symbol">@context.Symbol</MudTd>
                    <MudTd DataLabel="ISIN">@context.Isin</MudTd>
                    <MudTd DataLabel="Brutto" Style="text-align:right">@context.RawAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Steuerpflichtig" Style="text-align:right">@context.TaxableAmount.ToString("N2")</MudTd>
                    <MudTd DataLabel="Vorabpauschale" Style="text-align:right">@context.UsedVorabpauschale.ToString("N2")</MudTd>
                    <MudTd DataLabel="Quelle">
                        <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Primary"
                                   OnClick="() => DrillToSource(context.Isin)">Anzeigen</MudButton>
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    </MudExpansionPanel>;

    private void DrillToSource(string isin)
        => Navigation.NavigateTo($"/audit?isin={Uri.EscapeDataString(isin)}");
}
```

> The "Quelle / Anzeigen" link is the spec §9 drill-down: it opens the Audit page filtered to the instrument's ISIN, where the persisted source entries and their provenance are shown (Task 11).

- [ ] **Step 2: Build**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Steuerreport.razor
git commit -m "feat: add Steuerreport page (yearly summary + drill-down sections)"
```

---

## Task 10: Import page ("/import")

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Import.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Import.razor`:

```razor
@page "/import"
@rendermode InteractiveServer
@using WealthIQ.Application.Import
@using WealthIQ.Application.Import.Diagnostic
@using WealthIQ.Application.Import.Enumeration
@using WealthIQ.Domain.Model.General
@inject StatementImportPipeline Pipeline

<PageTitle>WealthIQ — Import</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Import</MudText>

<MudPaper Class="pa-4 mb-4" Style="max-width: 640px;">
    <MudText Typo="Typo.subtitle2" GutterBottom="true">Interactive Brokers (FlexQuery XML)</MudText>

    <MudTextField @bind-Value="_accountNumber" Label="Account-Nummer" Variant="Variant.Outlined" Class="mb-4" />

    <MudText Typo="Typo.body2" Class="mb-2">Statement-Datei (.xml):</MudText>
    <InputFile OnChange="OnFileSelected" accept=".xml" />

    @if (_selectedFileName is not null)
    {
        <MudText Typo="Typo.caption" Class="mt-2">Ausgewählt: @_selectedFileName</MudText>
    }

    <div class="mt-4">
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Disabled="@(_busy || _tempPath is null || string.IsNullOrWhiteSpace(_accountNumber))"
                   OnClick="RunImport">
            @(_busy ? "Importiere…" : "Import starten")
        </MudButton>
    </div>
</MudPaper>

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

@if (_result is not null)
{
    var committed = _result.Status == ImportStatus.Committed;
    <MudAlert Severity="@(committed ? Severity.Success : Severity.Warning)" Class="mb-4">
        @(committed
            ? $"Import committed. Neu: {_result.InsertedEntries}, übersprungen (Duplikate): {_result.SkippedDuplicateEntries}."
            : "Import abgebrochen — blockierende Diagnostics, nichts wurde gespeichert.")
    </MudAlert>

    <MudText Typo="Typo.h6" GutterBottom="true">Diagnostics (@_result.Diagnostics.Count)</MudText>
    @if (_result.Diagnostics.Count == 0)
    {
        <MudText Typo="Typo.body2">Keine Diagnostics.</MudText>
    }
    else
    {
        <MudTable Items="_result.Diagnostics" Dense="true" Hover="true">
            <HeaderContent>
                <MudTh>Severity</MudTh>
                <MudTh>Code</MudTh>
                <MudTh>Meldung</MudTh>
                <MudTh>Sektion</MudTh>
                <MudTh>Referenz</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd DataLabel="Severity">@context.Severity</MudTd>
                <MudTd DataLabel="Code">@context.Code</MudTd>
                <MudTd DataLabel="Meldung">@context.Message</MudTd>
                <MudTd DataLabel="Sektion">@context.Section</MudTd>
                <MudTd DataLabel="Referenz">@context.SourceReference</MudTd>
            </RowTemplate>
        </MudTable>
    }
}

@code {
    private string _accountNumber = "";
    private string? _selectedFileName;
    private string? _tempPath;
    private bool _busy;
    private string? _error;
    private ImportPipelineResult? _result;

    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        _error = null;
        _result = null;
        try
        {
            var file = e.File;
            _selectedFileName = file.Name;
            var temp = Path.Combine(Path.GetTempPath(), $"wealthiq-upload-{Guid.NewGuid():N}-{file.Name}");
            await using (var fs = File.Create(temp))
            await using (var stream = file.OpenReadStream(maxAllowedSize: 64 * 1024 * 1024))
            {
                await stream.CopyToAsync(fs);
            }
            _tempPath = temp;
        }
        catch (Exception ex)
        {
            _error = $"Datei konnte nicht gelesen werden: {ex.Message}";
            _tempPath = null;
        }
    }

    private async Task RunImport()
    {
        if (_tempPath is null || string.IsNullOrWhiteSpace(_accountNumber))
        {
            return;
        }

        _busy = true;
        _error = null;
        _result = null;
        try
        {
            var accountId = DeterministicAccount.IdFor("InteractiveBrokers", _accountNumber.Trim());
            var account = new Account(accountId, _accountNumber.Trim());
            var command = new ImportStatementCommand(
                new ImportRequest
                {
                    Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, _tempPath),
                    AccountId = accountId
                },
                account);

            _result = await Pipeline.RunAsync(command);
        }
        catch (Exception ex)
        {
            _error = $"Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _busy = false;
            if (_tempPath is not null && File.Exists(_tempPath))
            {
                File.Delete(_tempPath); // the pipeline already copied it into the audit folder
            }
            _tempPath = null;
            _selectedFileName = null;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Import.razor
git commit -m "feat: add Import page (upload IBKR XML -> pipeline -> diagnostics)"
```

---

## Task 11: Diagnostics / Audit page ("/audit") with ISIN drill-down

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/Audit.razor`

- [ ] **Step 1: Create the page**

Create `src/WealthIQ.Web/Components/Pages/Audit.razor`:

```razor
@page "/audit"
@rendermode InteractiveServer
@using WealthIQ.Application.Audit
@using WealthIQ.Application.Audit.Interface
@using WealthIQ.Application.Persistence.Interface
@using WealthIQ.Domain.Model.General
@using WealthIQ.Domain.Model.Ledger
@inject IImportAuditStore AuditStore
@inject ILedgerStore LedgerStore

<PageTitle>WealthIQ — Diagnostics / Audit</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Diagnostics / Audit</MudText>

@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-4">@_error</MudAlert>
}

<MudTextField @bind-Value="_filterIsin" Label="Filter nach ISIN (Quell-Drill-down)" Variant="Variant.Outlined"
              Immediate="true" Clearable="true" Class="mb-4" Style="max-width: 360px;" />

<MudText Typo="Typo.h6" GutterBottom="true">Quell-Einträge (Provenance)</MudText>
@if (FilteredEntries.Count == 0)
{
    <MudText Typo="Typo.body2" Class="mb-6">Keine passenden Einträge.</MudText>
}
else
{
    <MudTable Items="FilteredEntries" Dense="true" Hover="true" Class="mb-6">
        <HeaderContent>
            <MudTh>OccurredAt</MudTh>
            <MudTh>Kategorie</MudTh>
            <MudTh>Symbol</MudTh>
            <MudTh>ISIN</MudTh>
            <MudTh>Quelle</MudTh>
            <MudTh>Datei</MudTh>
            <MudTh>Referenz</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd DataLabel="OccurredAt">@context.OccurredAt.ToString("u")</MudTd>
            <MudTd DataLabel="Kategorie">@context.Category</MudTd>
            <MudTd DataLabel="Symbol">@context.Symbol</MudTd>
            <MudTd DataLabel="ISIN">@context.Isin</MudTd>
            <MudTd DataLabel="Quelle">@context.Provenance.SourceSystem (@context.Provenance.ImportFormat)</MudTd>
            <MudTd DataLabel="Datei">@context.Provenance.SourceLocation</MudTd>
            <MudTd DataLabel="Referenz">@context.Provenance.SourceRecordReference</MudTd>
        </RowTemplate>
    </MudTable>
}

<MudText Typo="Typo.h6" GutterBottom="true">Import-Batches</MudText>
<MudTable Items="_batches" Dense="true" Hover="true" Class="mb-6">
    <HeaderContent>
        <MudTh>Importiert</MudTh>
        <MudTh>Broker</MudTh>
        <MudTh>Format</MudTh>
        <MudTh>Datei</MudTh>
        <MudTh Style="text-align:right">Neu</MudTh>
        <MudTh Style="text-align:right">Übersprungen</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd DataLabel="Importiert">@context.ImportedAt.ToString("u")</MudTd>
        <MudTd DataLabel="Broker">@context.Broker</MudTd>
        <MudTd DataLabel="Format">@context.Format</MudTd>
        <MudTd DataLabel="Datei">@context.RawFilePath</MudTd>
        <MudTd DataLabel="Neu" Style="text-align:right">@context.InsertedEntries</MudTd>
        <MudTd DataLabel="Übersprungen" Style="text-align:right">@context.SkippedDuplicateEntries</MudTd>
    </RowTemplate>
</MudTable>

<MudText Typo="Typo.h6" GutterBottom="true">Diagnostics</MudText>
<MudSelect T="string" @bind-Value="_severityFilter" Label="Severity" Dense="true" Variant="Variant.Outlined" Style="max-width: 200px;" Class="mb-2">
    <MudSelectItem T="string" Value="@AllSeverities">Alle</MudSelectItem>
    <MudSelectItem T="string" Value="@("Info")">Info</MudSelectItem>
    <MudSelectItem T="string" Value="@("Warning")">Warning</MudSelectItem>
    <MudSelectItem T="string" Value="@("Error")">Error</MudSelectItem>
    <MudSelectItem T="string" Value="@("Fatal")">Fatal</MudSelectItem>
</MudSelect>
<MudTable Items="FilteredDiagnostics" Dense="true" Hover="true">
    <HeaderContent>
        <MudTh>Severity</MudTh>
        <MudTh>Code</MudTh>
        <MudTh>Meldung</MudTh>
        <MudTh>Sektion</MudTh>
        <MudTh>Referenz</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd DataLabel="Severity">@context.Severity</MudTd>
        <MudTd DataLabel="Code">@context.Code</MudTd>
        <MudTd DataLabel="Meldung">@context.Message</MudTd>
        <MudTd DataLabel="Sektion">@context.Section</MudTd>
        <MudTd DataLabel="Referenz">@context.SourceReference</MudTd>
    </RowTemplate>
</MudTable>

@code {
    private const string AllSeverities = "Alle";

    [Parameter]
    [SupplyParameterFromQuery(Name = "isin")]
    public string? IsinQuery { get; set; }

    private sealed record EntryView(DateTimeOffset OccurredAt, string Category, string Symbol, string Isin, SourceProvenance Provenance);

    private string? _error;
    private string? _filterIsin;
    private string _severityFilter = AllSeverities;
    private IReadOnlyList<ImportBatchView> _batches = Array.Empty<ImportBatchView>();
    private IReadOnlyList<ImportDiagnosticView> _diagnostics = Array.Empty<ImportDiagnosticView>();
    private List<EntryView> _entries = new();

    private IReadOnlyList<EntryView> FilteredEntries =>
        string.IsNullOrWhiteSpace(_filterIsin)
            ? _entries
            : _entries.Where(e => e.Isin.Contains(_filterIsin.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private IReadOnlyList<ImportDiagnosticView> FilteredDiagnostics =>
        _severityFilter == AllSeverities
            ? _diagnostics
            : _diagnostics.Where(d => d.Severity == _severityFilter).ToList();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _filterIsin = IsinQuery;
            _batches = await AuditStore.GetBatchesAsync();
            _diagnostics = await AuditStore.GetDiagnosticsAsync();

            var ledger = await LedgerStore.LoadLedgerAsync();
            var instrumentById = ledger.Instruments.ToDictionary(i => i.InstrumentId);
            _entries = ledger.Entries.Select(e => ToView(e, instrumentById)).ToList();
        }
        catch (Exception ex)
        {
            _error = $"Audit-Daten konnten nicht geladen werden: {ex.Message}";
        }
    }

    private static EntryView ToView(PortfolioEntry entry, IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var (symbol, isin) = entry switch
        {
            TradeEntry t => Resolve(t.InstrumentId, instrumentById),
            CashEntry c => Resolve(c.RelatedInstrumentId ?? c.CashInstrumentId, instrumentById),
            _ => ("", "")
        };
        return new EntryView(entry.OccurredAt, entry.Category.ToString(), symbol, isin, entry.SourceProvenance);
    }

    private static (string Symbol, string Isin) Resolve(InstrumentId id, IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
        => instrumentById.TryGetValue(id, out var instrument) ? (instrument.Symbol, instrument.ISIN) : ("", "");
}
```

> This page provides the spec §9 provenance drill-down: opening `/audit?isin=…` from a Steuerreport row pre-filters the source-entry grid to that instrument, showing each persisted entry's `SourceSystem`, `SourceLocation` (the audit-folder file), and `SourceRecordReference`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Web/Components/Pages/Audit.razor
git commit -m "feat: add Diagnostics/Audit page with batches, diagnostics, and ISIN provenance drill-down"
```

---

## Task 12: Full-suite green + manual end-to-end smoke test

**Files:** none (verification only).

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass (Plan 1/2 suites plus the five new test classes from Tasks 1–5).

- [ ] **Step 2: Run the web app**

Run: `dotnet run --project "src/WealthIQ.Web/WealthIQ.Web.csproj"`
Expected: console prints "Now listening on: http://localhost:<port>" and "Applying migration 'InitialCreate'" (first run only). A `data/app/wealthiq.db` file is created, and `data/app/audit/` exists.

- [ ] **Step 3: Manually verify the three pages**

Open the printed URL in a browser:

1. **Steuerreport ("/")** — initially shows "Noch keine Daten" (empty DB).
2. **Import ("/import")** — enter account number `U5658230`, choose `data/input/TaxAlpha_Raw_Data_2024.xml`, click **Import starten**. Expected: a green "Import committed" alert with non-zero "Neu", and a diagnostics table (warnings allowed; no Error/Fatal — those would abort).
3. **Steuerreport ("/")** — pick year **2024**. Expected: summary cards populated in EUR (Verkäufe, Dividenden, Zinsen, Vorabpauschale, Quellensteuer, geschätzte Steuer); expanding **Verkäufe** and **Vorabpauschale** shows row tables. Click a row's **Quelle → Anzeigen**.
4. **Audit ("/audit?isin=…")** — the source-entries grid is pre-filtered to the clicked ISIN and shows provenance (IBKR / file path / transaction reference); the Import-Batches and Diagnostics tables list the run.

> Cross-check correctness against the locked baseline: the 2021–2024 sample is the same data the `GermanTaxRegressionTests` golden baseline is built from. After importing all of `data/input/TaxAlpha_Raw_Data_2021..2024.xml`, the 2024 Vorabpauschale and Sell taxable totals on the Steuerreport should match the regression test's expectations (Sell taxable Σ ≈ 10845.26 €, Vorabpauschale taxable Σ ≈ 625.24 €). If they differ, the replay path (catalog enrichment or a DB reference adapter) diverges from the CSV path — debug there, not in the UI.

- [ ] **Step 4: Stop the app and confirm the working tree is clean except intended artifacts**

Stop the running app (Ctrl+C). Then run: `git status`
Expected: no source changes pending. The local `data/app/` database/audit files are runtime artifacts — confirm they are git-ignored or not staged (see Step 5).

- [ ] **Step 5: Ensure runtime data is not committed**

If `data/app/` is not already ignored, append to `.gitignore` at the repo root:

```
data/app/
```

Then commit:

```bash
git add .gitignore
git commit -m "chore: ignore local runtime data (data/app)"
```

---

## Done criteria for Plan 3 (completes WealthIQ v1)

- DB-backed reference adapters (`DbBasisInterestRateProvider`, `DbYearEndPriceProvider`, `DbInstrumentProfileEnricher`, `DbFxRateLookup`) implement the Application ports off the seeded tables; `DbFxRateLookup` reproduces `NextAvailableOnOrAfter` and fails fast on a missing required rate (spec §7).
- `AnnualTaxReportService` replays the persisted ledger (load → enrich catalog → `GermanTaxCalculator`) into per-year summaries (Verkäufe, Dividenden, Zinsen, Vorabpauschale, Quellensteuer, geschätzte Steuer — all EUR) plus the underlying entry sections (spec §6 "Replay & Compute", §9).
- `IImportAuditStore` exposes persisted batches + diagnostics; the Audit page shows them plus source entries with provenance, filterable by ISIN.
- EF Core `InitialCreate` migration + design-time factory exist; the Web host applies migrations and seeds reference data from `data/reference/` on startup (seed-if-empty).
- `WealthIQ.Web` is the sole composition root referencing Infrastructure; it renders the three MudBlazor pages (Steuerreport "/", Import "/import", Diagnostics/Audit "/audit") and runs the full spec §6 pipeline end-to-end.
- `dotnet test "WealthIQ.slnx"` is green; the manual smoke test imports a real IBKR statement and shows a year's tax report with provenance drill-down.

## Spec coverage check

- §4 architecture & DI: Web composition root wires Domain/Application/Infrastructure; only Web references Infrastructure (Tasks 7–8). ✅
- §6 Replay & Compute + Present: `AnnualTaxReportService` + three pages (Tasks 4, 9–11). ✅
- §6 reference data seeded on first start: startup `SeedIfEmptyAsync` from `data/reference/` (Task 8). ✅
- §7 FX rule (event-time, no silent fallback): `DbFxRateLookup` (Task 3). ✅
- §8 fail-fast at compute: missing reference/FX throws and the page surfaces it (Tasks 4, 9). ✅
- §9 three pages with year summary, sections, and provenance drill-down (Tasks 9–11). ✅
- §10 TDD for new logic; UI manually verified for v1 (Tasks 1–5 TDD; Task 12 manual). ✅

## Known v1 limitations / notes for later (not in scope here)

- **Tax-row → exact raw record link.** The domain `GermanTaxEntry` carries no source reference (the tax core is "portieren & verfeinern", left untouched per spec §4). Drill-down is therefore at instrument (ISIN) granularity via the Audit page, not a one-to-one tax-row → raw-record link. Threading provenance through the calculator is a later refinement.
- **DbContext lifetime.** Wired as a scoped `AddDbContext` — correct for a single-user local tool with sequential actions. A multi-operation/concurrent future would move to `AddDbContextFactory` with short-lived contexts.
- **Estimated tax** is a flat 26.375 % orientation figure (no Sparer-Pauschbetrag, loss-pot separation, or Günstigerprüfung); refine after the fachliche Steuer-Review (spec §11).
- **PDF export, charts/valuation, further brokers, multi-currency base** remain explicitly out of v1 (spec §2, §11).
- **`CLAUDE.md` replacing `AGENTS.md`** remains a separate follow-up.
```
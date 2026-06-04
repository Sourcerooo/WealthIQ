# Phase 2 — Data Administration & Vorabpauschale Correction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every dataset the tax engine depends on administrable from inside the app (clear / re-seed / fetch from the internet), store full daily historical prices + FX + Basiszins in SQLite, and correct the Vorabpauschale calculation to be §18-InvStG-correct (uniform year-start rebasing, distributions-in-cap, 1/12-on-final, fund-gating, fail-fast).

**Architecture:** Provider (fetch from internet, `Infrastructure`) → Store (read SQLite, `Infrastructure`) → Refresh service (orchestrate, `Application`). Interfaces live in `Application`; only `Web` wires concrete types. The DB is the single source of truth; committed CSV/JSON remain offline bootstrap seed + CI fixtures. The Vorabpauschale price is **derived** from stored `HistoricalPrice` bars, FX-converted at each bar's own date.

**Tech Stack:** C# / .NET 10, EF Core + SQLite, Blazor Server + MudBlazor, xUnit, thin `HttpClient` via `IHttpClientFactory` (no third-party market-data NuGet).

**Source spec:** `docs/superpowers/specs/2026-05-31-phase2-data-administration-design.md` (read it before starting; section refs below like "§6.2" point into it).

---

## Architecture decisions & clarifications (read before Task 1)

These resolve ambiguities discovered while mapping the codebase. They keep type names consistent across tasks.

1. **`Currency` vs `CurrencyCode`.** They are the **same enum** `WealthIQ.Domain.Enumeration.Currency`. Application-layer files alias it via `using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;`. Use whichever alias the surrounding file already uses.

2. **`IInstrumentMarketDataMap` becomes currency-aware.** Current signature is `InstrumentMarketDataProfile GetProfile(Instrument instrument)`. It changes to `InstrumentMarketDataProfile GetProfile(string isin, Currency currency)`. Two implementations:
   - `DbInstrumentMarketDataMap` (new, Infrastructure) — production, reads `InstrumentListing` table. Registered in DI.
   - `JsonInstrumentMarketDataMap` (existing, refactored) — file-backed, keyed `(isin, currency)`, used **only by tests** so `GermanTaxRegressionTests` stays DB-free and CI-deterministic. The spec says "replaces" — in production it is replaced (DI repoints to `Db…`); the file-backed class is retained as a test adapter.

3. **The regression test stays file-backed.** `GermanTaxRegressionTests` constructs the calculator directly from `data/test/configuration` adapters (`CsvHistoricalPriceLookup`, `CsvFxRateLookup`, `CsvBasisInterestRateProvider`, `JsonInstrumentProfileEnricher`, `JsonInstrumentMarketDataMap`). **Keep `Csv*`/`Json*` adapters** as file-backed implementations of the shared interfaces. Production uses the `Db*` adapters. `DerivedInstrumentPriceProvider` depends only on the interfaces, so it works with either backing.

4. **`Currency` on new EF rows is stored as `string`** (matching `FxRateRow.Currency`), parsed to the enum on read (matching `DbFxRateLookup`).

5. **Two stages, hard checkpoint.** Stage A (Tasks 1–10) ends with `GermanTaxRegressionTests` passing with **unchanged** expected values, proving the new data path moved no number. Stage B (Tasks 21+) deliberately changes the formula and recomputes the baseline. Do not start Stage B until Stage A is green.

6. **No silent defaults anywhere.** Removing the 30% Teilfreistellung / "Auto-Generated" fallback is part of Task 6; a held instrument with no profile becomes a blocking error in Stage B.

---

## Build / test commands (reference)

- Build: `dotnet build WealthIQ.slnx`
- All tests: `dotnet test WealthIQ.slnx`
- Single test class: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxRegressionTests"`
- By display name: `dotnet test WealthIQ.slnx --filter "DisplayName~Vorabpauschale"`
- Add migration: `dotnet ef migrations add <Name> --project src/WealthIQ.Infrastructure`
- Format: `dotnet format WealthIQ.slnx`

Commit after every task whose tests are green. Branch is `feature/Phase2` (already checked out).

---

# STAGE A — Data layer + accessors (behavior-preserving)

## Work unit 1 — Schema: new tables, new columns, drop YearEndPrice

### Task 1: Add the `HistoricalPriceRow` EF entity

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/HistoricalPriceRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`

- [ ] **Step 1: Create the row type**

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>One daily OHLCV bar for a provider listing. Key is (ProviderSymbol, Date).
/// Currency is intrinsic to the listing and stored as text (parsed to <c>Currency</c> on read,
/// mirroring <c>FxRateRow</c>). The tax engine uses <c>Close</c>, never <c>AdjustedClose</c>.</summary>
public sealed class HistoricalPriceRow
{
    public string ProviderSymbol { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal AdjustedClose { get; set; }
    public long Volume { get; set; }
}
```

- [ ] **Step 2: Register the DbSet + key in `WealthIqDbContext`**

Add the DbSet next to the other reference DbSets (after line 16, the `FxRates` set):

```csharp
    public DbSet<HistoricalPriceRow> HistoricalPrices => Set<HistoricalPriceRow>();
```

Add the configuration inside `OnModelCreating` (after the `FxRateRow` block):

```csharp
        modelBuilder.Entity<HistoricalPriceRow>(e =>
        {
            e.HasKey(x => new { x.ProviderSymbol, x.Date });
        });
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build WealthIQ.slnx`
Expected: build succeeds (migration not added yet — that's Task 5).

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/Rows/HistoricalPriceRow.cs src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs
git commit -m "feat(persistence): add HistoricalPriceRow entity"
```

### Task 2: Add the `InstrumentListingRow` EF entity

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentListingRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`

- [ ] **Step 1: Create the row type**

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>A provider listing for an instrument in a specific currency. Key is (Isin, Currency)
/// so the same ISIN can be held in EUR and GBP without mixing currencies (spec §4).</summary>
public sealed class InstrumentListingRow
{
    public string Isin { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderSymbol { get; set; } = "";
    public string? Exchange { get; set; }
    public string? Notes { get; set; }
}
```

- [ ] **Step 2: Register the DbSet + key**

DbSet:

```csharp
    public DbSet<InstrumentListingRow> InstrumentListings => Set<InstrumentListingRow>();
```

Config in `OnModelCreating`:

```csharp
        modelBuilder.Entity<InstrumentListingRow>(e =>
        {
            e.HasKey(x => new { x.Isin, x.Currency });
        });
```

- [ ] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentListingRow.cs src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs
git commit -m "feat(persistence): add InstrumentListingRow entity"
```

### Task 3: Add the `DataRefreshLogRow` EF entity

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/DataRefreshLogRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`

- [ ] **Step 1: Create the row type**

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>One row per dataset, upserted on each refresh. Powers the admin page's
/// "last refreshed" status (spec §4).</summary>
public sealed class DataRefreshLogRow
{
    public string Dataset { get; set; } = "";
    public DateTimeOffset LastRefreshedUtc { get; set; }
    public string? Note { get; set; }
}
```

- [ ] **Step 2: Register the DbSet + key**

```csharp
    public DbSet<DataRefreshLogRow> DataRefreshLog => Set<DataRefreshLogRow>();
```

```csharp
        modelBuilder.Entity<DataRefreshLogRow>(e => e.HasKey(x => x.Dataset));
```

- [ ] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/Rows/DataRefreshLogRow.cs src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs
git commit -m "feat(persistence): add DataRefreshLogRow entity"
```

### Task 4: Extend `InstrumentProfileRow`; drop `YearEndPriceRow`

**Files:**
- Modify: `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`
- Delete: `src/WealthIQ.Infrastructure/Persistence/Rows/YearEndPriceRow.cs`

> Note: `YearEndPriceRow`'s consumer (`DbYearEndPriceProvider`, the seeder's `ReadYearEndPrices`, the `YearEndPrices` DbSet) is removed across Tasks 4, 6, 9. Build will be red until those are done — that is expected; do the three together if the compiler complains, then commit once.

- [ ] **Step 1: Add `Type` + `SubjectToVorabpauschale` to `InstrumentProfileRow`**

Replace the file body with:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentProfileRow
{
    public string Isin { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
    public bool SubjectToVorabpauschale { get; set; }
}
```

- [ ] **Step 2: Remove `YearEndPriceRow` from the context**

In `WealthIqDbContext.cs` delete the `YearEndPrices` DbSet (line 14) and its `OnModelCreating` block (the `YearEndPriceRow` entity config). Then delete the file `src/WealthIQ.Infrastructure/Persistence/Rows/YearEndPriceRow.cs`.

- [ ] **Step 3: Build (expect errors in provider/seeder — resolved in Tasks 6 & 9)**

Run: `dotnet build WealthIQ.slnx`
Expected: compile errors only in `DbYearEndPriceProvider.cs` and `ReferenceDataSeeder.cs`. If you prefer a green build at each commit, complete Tasks 6 and 9's `YearEndPrice` removals now and commit together.

- [ ] **Step 4: Commit (after Tasks 6 & 9 remove the consumers, or now if doing them together)**

```bash
git add -A
git commit -m "feat(persistence): extend InstrumentProfileRow; drop YearEndPriceRow"
```

### Task 5: Generate the EF migration

**Files:**
- Create (generated): `src/WealthIQ.Infrastructure/Persistence/Migrations/<timestamp>_Phase2DataAdministration.cs` (+ Designer + snapshot update)

> Prerequisite: Tasks 1–4 compile (i.e. `YearEndPriceRow` consumers in Tasks 6 & 9 already removed so the project builds). EF migration generation requires a successful build.

- [ ] **Step 1: Add the migration**

Run: `dotnet ef migrations add Phase2DataAdministration --project src/WealthIQ.Infrastructure`
Expected: three files generated under `Persistence/Migrations/`.

- [ ] **Step 2: Inspect the generated `Up()`**

Open the generated migration. Confirm it: creates `HistoricalPrices`, `InstrumentListings`, `DataRefreshLog`; adds `Type` + `SubjectToVorabpauschale` columns to `InstrumentProfiles`; drops `YearEndPrices`. If anything is missing, the model edits in Tasks 1–4 are incomplete — fix and re-generate (`dotnet ef migrations remove --project src/WealthIQ.Infrastructure` first).

- [ ] **Step 3: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/Migrations/
git commit -m "feat(persistence): migration for Phase 2 data-administration schema"
```

---

## Work unit 2 — Price accessors, derived price provider, nullable Basiszins

### Task 6: Make `IBasisInterestRateProvider` nullable; remove the 30% TFS default

**Files:**
- Modify: `src/WealthIQ.Application/Tax/Interface/IBasisInterestRateProvider.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateProvider.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Tax/CsvBasisInterestRateProvider.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Tax/JsonInstrumentProfileEnricher.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/BasisInterestRateProviderTests.cs` (new)

> The calculator already early-returns on `rate <= 0m` (`GermanTaxCalculator.cs:209`). After this task the provider returns `decimal?`; the calculator's interpretation of `null` (blocking) vs `<=0` (skip) is wired in **Stage B (Task 21)**. For Stage A, keep the calculator behavior equivalent by treating `null` the same as the old `0` at the call site (documented in Task 9 wiring).

- [ ] **Step 1: Write a failing test for nullable distinction**

Create `tests/WealthIQ.Tests/Application/Tax/BasisInterestRateProviderTests.cs`:

```csharp
using System.Globalization;
using WealthIQ.Infrastructure.Ibkr.Tax;

namespace WealthIQ.Tests.Application.Tax;

public sealed class BasisInterestRateProviderTests
{
    [Fact]
    public void GetRate_MissingYear_ReturnsNull()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "year,rate\n2023,0.0255\n");
        var provider = new CsvBasisInterestRateProvider(path);

        Assert.Null(provider.GetRate(2099));
        Assert.Equal(0.0255m, provider.GetRate(2023));
    }
}
```

- [ ] **Step 2: Run it — expect compile failure (return type is `decimal`, not `decimal?`)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~BasisInterestRateProviderTests"`
Expected: FAIL (does not compile — `Assert.Null` on a non-nullable `decimal`).

- [ ] **Step 3: Change the interface**

`IBasisInterestRateProvider.cs`:

```csharp
namespace WealthIQ.Application.Tax.Interface;

public interface IBasisInterestRateProvider
{
    /// <summary>The Basiszins for <paramref name="year"/>. <c>null</c> = no value on file
    /// (a data gap → blocking error if the year is in replay scope); <c>≤ 0</c> = an official
    /// zero/negative rate (skip the year, no price lookup); <c>&gt; 0</c> = compute. (spec §5.3)</summary>
    decimal? GetRate(int year);
}
```

- [ ] **Step 4: Update `DbBasisInterestRateProvider`**

Change the last line to:

```csharp
    public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
```

- [ ] **Step 5: Update `CsvBasisInterestRateProvider`**

Change its `GetRate` to:

```csharp
    public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
```

- [ ] **Step 6: Remove the 30%/"Auto-Generated" default in both enrichers**

In `DbInstrumentProfileEnricher.Enrich` and `JsonInstrumentProfileEnricher.Enrich`, replace the fallback branch (the `return instrument with { Name = …"Auto-Generated"…, Teilfreistellungsquote = …0.30m… }`) so an unknown ISIN is returned **unchanged** (no synthesized name, no 30%). The classification fail-fast moves into the calculator in Stage B (Task 21). New fallback:

```csharp
        // No profile on file: return as-is. Stage B turns "held over year-end with no profile"
        // into a blocking error; here we no longer invent a 30% Teilfreistellung (spec §2, §4).
        return instrument;
```

> The enrichers do not yet carry `Type`/`SubjectToVorabpauschale` onto the `Instrument` domain type — that is added in Task 21 when the domain `Instrument` gains those fields. For Stage A, enrichment of *known* ISINs still applies `Name` + `Teilfreistellungsquote` exactly as before, so the regression numbers do not move.

- [ ] **Step 7: Run the new test — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~BasisInterestRateProviderTests"`
Expected: PASS.

- [ ] **Step 8: Build (provider call sites may need null-handling)**

Run: `dotnet build WealthIQ.slnx`
Fix any call site that consumed `decimal` from `GetRate` (the calculator: see Task 9 wiring note — for now adjust `GermanTaxCalculator.PerformYearEndClosing` line 208–212 to `var basisInterestRate = interestRateProvider.GetRate(year); if (basisInterestRate is null or <= 0m) return;` to preserve Stage A behavior).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(tax): nullable Basiszins contract; remove 30% Teilfreistellung default"
```

### Task 7: Add `EarliestOnOrAfter` to price lookups

**Files:**
- Modify: `src/WealthIQ.Application/MarketData/Interface/PriceLookupDateHandling.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/MarketData/CsvHistoricalPriceLookup.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/MarketData/CsvHistoricalPriceLookupTests.cs` (new)

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/MarketData/CsvHistoricalPriceLookupTests.cs`:

```csharp
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Ibkr.MarketData;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class CsvHistoricalPriceLookupTests
{
    private static string WriteCsv()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            "date,provider_symbol,currency,open,high,low,close,adjusted_close,volume\n" +
            "2024-01-02,VUSA.L,GBP,1,1,1,100,100,10\n" +
            "2024-12-30,VUSA.L,GBP,1,1,1,130,130,10\n");
        return path;
    }

    [Fact]
    public void GetPriceBar_EarliestOnOrAfter_ReturnsFirstBarOnOrAfterDate()
    {
        var lookup = new CsvHistoricalPriceLookup(WriteCsv());
        var bar = lookup.GetPriceBar(new DateOnly(2024, 1, 1), "VUSA.L", PriceLookupDateHandling.EarliestOnOrAfter);
        Assert.Equal(new DateOnly(2024, 1, 2), bar.Date);
        Assert.Equal(100m, bar.Close);
    }

    [Fact]
    public void GetPriceBar_LatestOnOrBefore_ReturnsLastBarOnOrBeforeDate()
    {
        var lookup = new CsvHistoricalPriceLookup(WriteCsv());
        var bar = lookup.GetPriceBar(new DateOnly(2024, 12, 31), "VUSA.L", PriceLookupDateHandling.LatestOnOrBefore);
        Assert.Equal(new DateOnly(2024, 12, 30), bar.Date);
        Assert.Equal(130m, bar.Close);
    }
}
```

- [ ] **Step 2: Run — expect FAIL (enum value missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~CsvHistoricalPriceLookupTests"`
Expected: FAIL (compile — `EarliestOnOrAfter` undefined).

- [ ] **Step 3: Add the enum value**

`PriceLookupDateHandling.cs`:

```csharp
namespace WealthIQ.Application.MarketData.Interface;

public enum PriceLookupDateHandling
{
    ExactDate,
    LatestOnOrBefore,
    EarliestOnOrAfter
}
```

- [ ] **Step 4: Implement it in `CsvHistoricalPriceLookup`**

Replace the final `foreach`/throw in `GetPriceBar` (the `LatestOnOrBefore` path) with branch handling for both directions:

```csharp
        if (dateHandling == PriceLookupDateHandling.EarliestOnOrAfter)
        {
            foreach (var candidate in barsByDate)
            {
                if (candidate.Key >= pricingDate)
                {
                    return candidate.Value;
                }
            }

            throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or after '{pricingDate:yyyy-MM-dd}'.");
        }

        foreach (var candidate in barsByDate.Reverse())
        {
            if (candidate.Key <= pricingDate)
            {
                return candidate.Value;
            }
        }

        throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or before '{pricingDate:yyyy-MM-dd}'.");
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~CsvHistoricalPriceLookupTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(marketdata): add EarliestOnOrAfter price lookup handling"
```

### Task 8: `DbHistoricalPriceLookup` (DB-backed `IHistoricalPriceLookup`)

**Files:**
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbHistoricalPriceLookup.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/MarketData/DbHistoricalPriceLookupTests.cs` (new)

Mirror `DbFxRateLookup`: load all bars once on construction into `Dictionary<string, SortedDictionary<DateOnly, PriceBar>>`, parse currency text to the enum, skip unparseable rows. Reuse the exact selection logic from `CsvHistoricalPriceLookup` (Task 7).

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/MarketData/DbHistoricalPriceLookupTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DbHistoricalPriceLookupTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void GetPriceBar_LatestOnOrBefore_ReadsClosingBarInListingCurrency()
    {
        using var db = NewDb();
        db.HistoricalPrices.Add(new HistoricalPriceRow
        {
            ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "GBP",
            Open = 1, High = 1, Low = 1, Close = 130, AdjustedClose = 130, Volume = 10
        });
        db.SaveChanges();

        var lookup = new DbHistoricalPriceLookup(db);
        var bar = lookup.GetPriceBar(new DateOnly(2024, 12, 31), "VUSA.L", PriceLookupDateHandling.LatestOnOrBefore);

        Assert.Equal(130m, bar.Close);
        Assert.Equal(WealthIQ.Domain.Enumeration.Currency.GBP, bar.Currency);
    }
}
```

- [ ] **Step 2: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbHistoricalPriceLookupTests"`
Expected: FAIL (compile).

- [ ] **Step 3: Implement `DbHistoricalPriceLookup`**

```csharp
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Historical bars from the seeded/refreshed <c>HistoricalPrices</c> table. Loaded once on
/// construction. Selection logic mirrors <see cref="WealthIQ.Infrastructure.Ibkr.MarketData.CsvHistoricalPriceLookup"/>:
/// ExactDate, LatestOnOrBefore, EarliestOnOrAfter; a genuinely missing bar is a blocking error.
/// Rows whose currency text is not a known <c>Currency</c> are ignored.</summary>
public sealed class DbHistoricalPriceLookup : IHistoricalPriceLookup
{
    private readonly Dictionary<string, SortedDictionary<DateOnly, PriceBar>> _barsBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    public DbHistoricalPriceLookup(WealthIqDbContext db)
    {
        foreach (var row in db.HistoricalPrices)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency))
            {
                continue;
            }

            if (!_barsBySymbol.TryGetValue(row.ProviderSymbol, out var barsByDate))
            {
                barsByDate = new SortedDictionary<DateOnly, PriceBar>();
                _barsBySymbol[row.ProviderSymbol] = barsByDate;
            }

            barsByDate[row.Date] = new PriceBar(
                row.Date, row.ProviderSymbol, currency,
                row.Open, row.High, row.Low, row.Close, row.AdjustedClose, row.Volume);
        }
    }

    public PriceBar GetPriceBar(
        DateOnly pricingDate,
        string providerSymbol,
        PriceLookupDateHandling dateHandling = PriceLookupDateHandling.LatestOnOrBefore)
    {
        if (!_barsBySymbol.TryGetValue(providerSymbol, out var barsByDate))
        {
            throw new InvalidOperationException($"No historical prices available for provider symbol '{providerSymbol}'.");
        }

        if (dateHandling == PriceLookupDateHandling.ExactDate)
        {
            if (barsByDate.TryGetValue(pricingDate, out var exactBar))
            {
                return exactBar;
            }

            throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on '{pricingDate:yyyy-MM-dd}'.");
        }

        if (dateHandling == PriceLookupDateHandling.EarliestOnOrAfter)
        {
            foreach (var candidate in barsByDate)
            {
                if (candidate.Key >= pricingDate)
                {
                    return candidate.Value;
                }
            }

            throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or after '{pricingDate:yyyy-MM-dd}'.");
        }

        foreach (var candidate in barsByDate.Reverse())
        {
            if (candidate.Key <= pricingDate)
            {
                return candidate.Value;
            }
        }

        throw new InvalidOperationException($"No historical price available for '{providerSymbol}' on or before '{pricingDate:yyyy-MM-dd}'.");
    }
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbHistoricalPriceLookupTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(marketdata): DbHistoricalPriceLookup over HistoricalPrices table"
```

### Task 9: Make `IInstrumentMarketDataMap` currency-aware + `DbInstrumentMarketDataMap`

**Files:**
- Modify: `src/WealthIQ.Application/MarketData/Interface/IInstrumentMarketDataMap.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/MarketData/JsonInstrumentMarketDataMap.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentMarketDataMap.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/MarketData/DbInstrumentMarketDataMapTests.cs` (new)

- [ ] **Step 1: Change the interface to `(isin, currency)`**

```csharp
using WealthIQ.Domain.Model.General;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData.Interface;

public interface IInstrumentMarketDataMap
{
    /// <summary>Resolves the provider listing for an instrument held in <paramref name="currency"/>.
    /// A missing listing for the held (ISIN, currency) is a blocking error (spec §4).</summary>
    InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency);
}
```

> Note: `InstrumentMarketDataProfile` (Provider, ProviderSymbol, Notes) is unchanged. The `using WealthIQ.Application.MarketData;` for that type is already implied by namespace; if the build complains, add it.

- [ ] **Step 2: Refactor `JsonInstrumentMarketDataMap` to key on `(isin, currency)`**

This file becomes the **test/seed adapter** reading a listings JSON shaped:

```json
{
  "IE00B3XXRP09": [
    { "currency": "GBP", "provider": "YahooFinance", "provider_symbol": "VUSA.L", "exchange": "LSE", "notes": "..." }
  ],
  "IE00B53SZB19": [
    { "currency": "EUR", "provider": "YahooFinance", "provider_symbol": "CNDX.AS" }
  ]
}
```

Replace the file with:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.MarketData;

/// <summary>File-backed listings map keyed by (ISIN, currency). Used by tests and as the seed source
/// for <c>InstrumentListings</c>. Production resolves via <c>DbInstrumentMarketDataMap</c>.</summary>
public sealed class JsonInstrumentMarketDataMap : IInstrumentMarketDataMap
{
    private readonly Dictionary<(string Isin, CurrencyCode Currency), InstrumentMarketDataProfile> _profiles = new();

    public JsonInstrumentMarketDataMap(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Instrument listings map file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<ListingDto>>>(json)
            ?? throw new ApplicationException("Instrument listings map file could not be parsed.");

        foreach (var (isin, listings) in raw)
        {
            foreach (var dto in listings)
            {
                if (string.IsNullOrWhiteSpace(dto.ProviderSymbol))
                {
                    throw new ApplicationException($"Missing provider symbol for instrument '{isin}' ({dto.Currency}).");
                }

                if (!Enum.TryParse<CurrencyCode>(dto.Currency, ignoreCase: true, out var currency))
                {
                    throw new ApplicationException($"Invalid currency '{dto.Currency}' for instrument '{isin}'.");
                }

                _profiles[(isin, currency)] = new InstrumentMarketDataProfile(dto.Provider, dto.ProviderSymbol, dto.Notes);
            }
        }
    }

    public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
    {
        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new InvalidOperationException("Instrument has no ISIN and cannot be mapped to market data.");
        }

        if (_profiles.TryGetValue((isin, currency), out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException($"No market-data listing configured for instrument '{isin}' in {currency}.");
    }

    private sealed class ListingDto
    {
        [JsonPropertyName("currency")] public string Currency { get; init; } = "";
        [JsonPropertyName("provider")] public string Provider { get; init; } = "YahooFinance";
        [JsonPropertyName("provider_symbol")] public string ProviderSymbol { get; init; } = "";
        [JsonPropertyName("notes")] public string? Notes { get; init; }
    }
}
```

- [ ] **Step 3: Write the failing test for `DbInstrumentMarketDataMap`**

Create `tests/WealthIQ.Tests/Infrastructure/MarketData/DbInstrumentMarketDataMapTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DbInstrumentMarketDataMapTests
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
    public void GetProfile_ResolvesByIsinAndCurrency()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.SaveChanges();

        var map = new DbInstrumentMarketDataMap(db);
        Assert.Equal("VUSA.L", map.GetProfile("IE00B3XXRP09", CurrencyCode.GBP).ProviderSymbol);
    }

    [Fact]
    public void GetProfile_MissingListing_Throws()
    {
        using var db = NewDb();
        var map = new DbInstrumentMarketDataMap(db);
        Assert.Throws<InvalidOperationException>(() => map.GetProfile("IE00B3XXRP09", CurrencyCode.EUR));
    }
}
```

- [ ] **Step 4: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbInstrumentMarketDataMapTests"`
Expected: FAIL (compile).

- [ ] **Step 5: Implement `DbInstrumentMarketDataMap`**

```csharp
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Resolves (ISIN, currency) → provider listing from the <c>InstrumentListings</c> table.
/// A missing listing for a held (ISIN, currency) is a blocking error (spec §4, §5.4).</summary>
public sealed class DbInstrumentMarketDataMap : IInstrumentMarketDataMap
{
    private readonly Dictionary<(string Isin, CurrencyCode Currency), InstrumentMarketDataProfile> _profiles = new();

    public DbInstrumentMarketDataMap(WealthIqDbContext db)
    {
        foreach (var row in db.InstrumentListings)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency))
            {
                continue;
            }

            _profiles[(row.Isin, currency)] = new InstrumentMarketDataProfile(row.Provider, row.ProviderSymbol, row.Notes);
        }
    }

    public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
    {
        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new InvalidOperationException("Instrument has no ISIN and cannot be mapped to market data.");
        }

        if (_profiles.TryGetValue((isin, currency), out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException($"No market-data listing configured for instrument '{isin}' in {currency}.");
    }
}
```

- [ ] **Step 6: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DbInstrumentMarketDataMapTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(marketdata): currency-aware instrument listing map (Db + Json)"
```

### Task 10: `IInstrumentPriceProvider` + `DerivedInstrumentPriceProvider`

**Files:**
- Create: `src/WealthIQ.Application/Tax/Interface/IInstrumentPriceProvider.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DerivedInstrumentPriceProvider.cs`
- Delete (end of Stage A, Task 13): `src/WealthIQ.Infrastructure/ReferenceData/DbYearEndPriceProvider.cs`, `src/WealthIQ.Application/Tax/Interface/IYearEndPriceProvider.cs`, `src/WealthIQ.Infrastructure/Ibkr/Tax/CsvYearEndPriceProvider.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/MarketData/DerivedInstrumentPriceProviderTests.cs` (new)

- [ ] **Step 1: Create the interface + value types**

```csharp
using WealthIQ.Application.MarketData.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Tax.Interface;

/// <summary>The redemption price (Close) for an instrument's listing in a given currency, resolved by date.
/// Close is in <see cref="Currency"/>; the CALLER converts to EUR (FX stays in the calculator). The
/// calculator turns a <c>null</c> result into a blocking error (spec §5.4).</summary>
public readonly record struct InstrumentQuote(decimal Close, CurrencyCode Currency, DateOnly AsOf);

public enum PriceQuoteHandling
{
    LatestOnOrBefore,
    EarliestOnOrAfter,
    ExactDate
}

public interface IInstrumentPriceProvider
{
    InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling);
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/MarketData/DerivedInstrumentPriceProviderTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class DerivedInstrumentPriceProviderTests
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
    public void GetQuote_ResolvesSymbolAndReturnsCloseInListingCurrency()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.HistoricalPrices.Add(new HistoricalPriceRow
        { ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "GBP",
          Open = 1, High = 1, Low = 1, Close = 90, AdjustedClose = 90, Volume = 1 });
        db.SaveChanges();

        var provider = new DerivedInstrumentPriceProvider(new DbInstrumentMarketDataMap(db), new DbHistoricalPriceLookup(db));
        var quote = provider.GetQuote("IE00B3XXRP09", CurrencyCode.GBP, new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore);

        Assert.NotNull(quote);
        Assert.Equal(90m, quote!.Value.Close);
        Assert.Equal(CurrencyCode.GBP, quote.Value.Currency);
        Assert.Equal(new DateOnly(2024, 12, 30), quote.Value.AsOf);
    }

    [Fact]
    public void GetQuote_BarCurrencyMismatch_Throws()
    {
        using var db = NewDb();
        db.InstrumentListings.Add(new InstrumentListingRow
        { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "YahooFinance", ProviderSymbol = "VUSA.L" });
        db.HistoricalPrices.Add(new HistoricalPriceRow
        { ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "USD",
          Open = 1, High = 1, Low = 1, Close = 90, AdjustedClose = 90, Volume = 1 });
        db.SaveChanges();

        var provider = new DerivedInstrumentPriceProvider(new DbInstrumentMarketDataMap(db), new DbHistoricalPriceLookup(db));
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetQuote("IE00B3XXRP09", CurrencyCode.GBP, new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore));
    }
}
```

- [ ] **Step 3: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DerivedInstrumentPriceProviderTests"`
Expected: FAIL (compile).

- [ ] **Step 4: Implement `DerivedInstrumentPriceProvider`**

```csharp
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Tax.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Derives the redemption price from stored <c>HistoricalPrice</c> bars: resolves the listing
/// symbol via <see cref="IInstrumentMarketDataMap"/>, reads the bar via <see cref="IHistoricalPriceLookup"/>,
/// and returns (Close, barCurrency, barDate). Asserts the bar currency equals the requested currency
/// (else blocking error — mis-mapped listing). Does NOT do FX; conversion stays in the calculator
/// (spec §5.4). Replaces IYearEndPriceProvider / DbYearEndPriceProvider entirely.</summary>
public sealed class DerivedInstrumentPriceProvider(
    IInstrumentMarketDataMap marketDataMap,
    IHistoricalPriceLookup priceLookup) : IInstrumentPriceProvider
{
    public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
    {
        var profile = marketDataMap.GetProfile(isin, currency);
        var bar = priceLookup.GetPriceBar(pricingDate, profile.ProviderSymbol, Map(handling));

        if (bar.Currency != currency)
        {
            throw new InvalidOperationException(
                $"Historical bar for '{isin}' ({profile.ProviderSymbol}) is in {bar.Currency} but the held lot is in {currency}. " +
                $"The listing is mis-mapped.");
        }

        return new InstrumentQuote(bar.Close, bar.Currency, bar.Date);
    }

    private static PriceLookupDateHandling Map(PriceQuoteHandling handling) => handling switch
    {
        PriceQuoteHandling.LatestOnOrBefore => PriceLookupDateHandling.LatestOnOrBefore,
        PriceQuoteHandling.EarliestOnOrAfter => PriceLookupDateHandling.EarliestOnOrAfter,
        PriceQuoteHandling.ExactDate => PriceLookupDateHandling.ExactDate,
        _ => throw new ArgumentOutOfRangeException(nameof(handling))
    };
}
```

> Note: `GetProfile`/`GetPriceBar` throw on a missing listing/bar rather than returning null. The `InstrumentQuote?` return type exists so Stage B's calculator can express "null → blocking error" uniformly; in practice the underlying lookups throw first with a more specific message. That is acceptable — both are blocking errors. Do not swallow these exceptions.

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~DerivedInstrumentPriceProviderTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(tax): IInstrumentPriceProvider + DerivedInstrumentPriceProvider"
```

### Task 11: Extend the seeder for the new tables/columns; drop YearEndPrice seeding

**Files:**
- Modify: `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs`
- Modify: `src/WealthIQ.Application/ReferenceData/ReferenceDataSeedResult.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`
- Modify (seed data): `data/reference/instruments.json` (extend), `data/reference/listings.json` (new), `data/reference/historical_prices.csv` (already exists)
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs` (new)

The seeder gains: historical prices (from `historical_prices.csv`), instrument listings (from `listings.json`), the extended instrument-profile columns (`type`, `subject_to_vorabpauschale` from `instruments.json`). It loses: `YearEndPrices`.

- [ ] **Step 1: Update `ReferenceDataSources`**

Replace `YearEndPriceCsvPath` with the new sources:

```csharp
namespace WealthIQ.Application.ReferenceData;

public sealed record ReferenceDataSources(
    string BasisInterestRateCsvPath,
    string HistoricalPriceCsvPath,
    string InstrumentProfileJsonPath,
    string InstrumentListingJsonPath,
    string FxRateCsvPath);
```

- [ ] **Step 2: Update `ReferenceDataSeedResult`**

```csharp
namespace WealthIQ.Application.ReferenceData;

public sealed record ReferenceDataSeedResult(
    int BasisInterestRates,
    int HistoricalPrices,
    int InstrumentProfiles,
    int InstrumentListings,
    int FxRates);
```

- [ ] **Step 3: Extend the committed `instruments.json` seed**

Add `type` and `subject_to_vorabpauschale` to every entry. ETFs are funds (`true`); the gold ETC `IE00B4ND3602` is seeded `true` for now so its baseline does not move (spec §8 note — flagged for later user determination); bond ETFs that are funds are `true`. Example shape per entry:

```json
{
  "IE00B3XXRP09": { "name": "Vanguard S&P 500 UCITS ETF", "type": "ETF_EQUITY", "tfs_quote": 0.30, "subject_to_vorabpauschale": true },
  "IE00B4ND3602": { "name": "iShares Physical Gold ETC", "type": "ETC", "tfs_quote": 0.00, "subject_to_vorabpauschale": true }
}
```

Apply `subject_to_vorabpauschale: true` to all current entries (they are all funds/ETPs the test holds; keeping `true` preserves Stage A numbers). Mirror the same change into the test fixture `data/test/configuration/instruments.json`.

- [ ] **Step 4: Create the committed `listings.json` seed**

`data/reference/listings.json` — convert `market_data_mappings.json` to the per-currency listings shape (Task 9 Step 2). Use the lot currency each instrument trades in (per the existing notes: `VUSA.L` GBP, `CNDX.AS` EUR, etc.). Mirror to `data/test/configuration/listings.json`.

- [ ] **Step 5: Write the failing seeder test**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class ReferenceDataSeederTests
{
    [Fact]
    public async Task SeedIfEmptyAsync_LoadsPricesProfilesListings()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var dir = Path.Combine(Path.GetTempPath(), "wiq-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "basiszins.csv"), "year,rate\n2024,0.0229\n");
        File.WriteAllText(Path.Combine(dir, "historical_prices.csv"),
            "date,provider_symbol,currency,open,high,low,close,adjusted_close,volume\n2024-12-30,CNDX.AS,EUR,1,1,1,2,2,3\n");
        File.WriteAllText(Path.Combine(dir, "instruments.json"),
            "{\"IE00B53SZB19\":{\"name\":\"x\",\"type\":\"ETF_EQUITY\",\"tfs_quote\":0.30,\"subject_to_vorabpauschale\":true}}");
        File.WriteAllText(Path.Combine(dir, "listings.json"),
            "{\"IE00B53SZB19\":[{\"currency\":\"EUR\",\"provider\":\"YahooFinance\",\"provider_symbol\":\"CNDX.AS\"}]}");
        File.WriteAllText(Path.Combine(dir, "fx_rates.csv"), "date,currency,rate_to_eur\n2024-12-30,USD,0.9\n");

        var seeder = new ReferenceDataSeeder(db);
        var result = await seeder.SeedIfEmptyAsync(new ReferenceDataSources(
            Path.Combine(dir, "basiszins.csv"),
            Path.Combine(dir, "historical_prices.csv"),
            Path.Combine(dir, "instruments.json"),
            Path.Combine(dir, "listings.json"),
            Path.Combine(dir, "fx_rates.csv")));

        Assert.Equal(1, result.HistoricalPrices);
        Assert.Equal(1, result.InstrumentListings);
        var profile = db.InstrumentProfiles.Single();
        Assert.True(profile.SubjectToVorabpauschale);
        Assert.Equal("ETF_EQUITY", profile.Type);
    }
}
```

- [ ] **Step 6: Run — expect FAIL (signature/columns mismatch)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~ReferenceDataSeederTests"`
Expected: FAIL (compile / missing members).

- [ ] **Step 7: Update `ReferenceDataSeeder`**

- Replace the `YearEndPrices` block with a `HistoricalPrices` block reading `historical_prices.csv` (9+ columns: date, provider_symbol, currency, open, high, low, close, adjusted_close, volume) into `HistoricalPriceRow`.
- Add an `InstrumentListings` block reading `listings.json` (per-currency listings) into `InstrumentListingRow`.
- Extend `ReadInstrumentProfiles` to read `type` + `subject_to_vorabpauschale` and write the new columns.
- Update the `InstrumentProfileDto` to include:

```csharp
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("subject_to_vorabpauschale")] public bool SubjectToVorabpauschale { get; init; }
```

and the yielded row:

```csharp
            yield return new InstrumentProfileRow
            {
                Isin = isin, Name = dto.Name, Type = dto.Type,
                Teilfreistellungsquote = tfs, SubjectToVorabpauschale = dto.SubjectToVorabpauschale
            };
```

- New historical-price reader (mirror `ReadFxRates` parsing style, skip unparseable rows rather than throw, since price files are large and provider-shaped):

```csharp
    private static IEnumerable<HistoricalPriceRow> ReadHistoricalPrices(string path)
    {
        foreach (var (_, parts) in ReadCsv(path, "Historical price file not found.", minColumns: 9))
        {
            if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var open)
                || !decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high)
                || !decimal.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low)
                || !decimal.TryParse(parts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var close)
                || !decimal.TryParse(parts[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var adj)
                || !long.TryParse(parts[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            {
                continue;
            }

            yield return new HistoricalPriceRow
            {
                ProviderSymbol = parts[1].Trim(), Date = date, Currency = parts[2].Trim(),
                Open = open, High = high, Low = low, Close = close, AdjustedClose = adj, Volume = volume
            };
        }
    }
```

- New listings reader (deserialize `Dictionary<string, List<ListingDto>>`, one `InstrumentListingRow` per (isin, listing)). Define a private `ListingDto` with `currency`, `provider`, `provider_symbol`, `exchange`, `notes`.
- Update `SeedIfEmptyAsync` to seed `HistoricalPrices`/`InstrumentListings` when empty and return the new `ReferenceDataSeedResult`.

- [ ] **Step 8: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~ReferenceDataSeederTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(referencedata): seed HistoricalPrices, InstrumentListings, profile classification; drop YearEndPrice seeding"
```

---

## Work unit 3 — Stage A wiring + behavior-preserving regression checkpoint

### Task 12: Point the calculator's year-end price at `DerivedInstrumentPriceProvider` (formula unchanged)

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`
- Modify: `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs` (setup only — **expected values unchanged**)
- Modify (fixtures): `data/test/configuration/historical_prices.csv` (new), `data/test/configuration/listings.json` (new), `data/test/configuration/instruments.json` (extended), `data/test/configuration/fx_rates.csv` (add bar-date rows if needed)
- Test helpers: `tests/WealthIQ.Tests/Application/Tax/TaxTestDoubles.cs` (if present, extend)

This is the Stage A checkpoint. The calculator stops calling `IYearEndPriceProvider.GetPrice(isin, year)` and instead calls `IInstrumentPriceProvider.GetQuote(isin, lotCurrency, Dec 31, LatestOnOrBefore)` then FX-converts the Close to EUR at the bar date — **but the surrounding formula (acquisition-cost base) is otherwise unchanged**. Fixtures are engineered so the derived EUR year-end equals the old `prices.csv` value to the cent, so `GermanTaxRegressionTests` passes with **unchanged expected tuples**.

- [ ] **Step 1: Change the calculator constructor dependency**

In `GermanTaxCalculator.cs`, replace `IYearEndPriceProvider yearEndPriceProvider` with `IInstrumentPriceProvider priceProvider`:

```csharp
public sealed class GermanTaxCalculator(
    IBasisInterestRateProvider interestRateProvider,
    IInstrumentPriceProvider priceProvider,
    IFxRateLookup fxRateLookup)
```

- [ ] **Step 2: Replace the year-end price lookup in `PerformYearEndClosing`**

The lookup currently keyed on ISIN+year (lines 226–232) becomes per-lot (because the listing is per currency). Move the year-end quote **inside** the per-lot loop and FX-convert it. Replace the `var yearEndPrice = yearEndPriceProvider…` block and the `var appreciation = …` usage so it reads (keeping the acquisition-cost formula intact):

```csharp
            foreach (var lot in instrumentGroup.ToList())
            {
                var lotCurrency = lot.OpenUnitPrice.Currency;
                var endQuote = priceProvider.GetQuote(instrument.ISIN, lotCurrency, new DateOnly(year, 12, 31), PriceQuoteHandling.LatestOnOrBefore);
                if (endQuote is null)
                {
                    throw new InvalidOperationException(
                        $"Year-end price for ISIN '{instrument.ISIN}' ({lotCurrency}) in {year} is required to compute Vorabpauschale but is missing.");
                }

                var yearEndPriceEur = _fxConverter.Convert(
                    new Money(endQuote.Value.Close, endQuote.Value.Currency), endQuote.Value.AsOf).Amount;

                var acquisitionPrice = CalculateRemainingAcquisitionPriceInEur(lot);
                var months = 12m;
                if (lot.OpenTradeDate.Year == year)
                {
                    months = 12m - lot.OpenTradeDate.Month + 1m;
                }

                var basisYield = acquisitionPrice * basisFactor * (months / 12m);
                var appreciation = Math.Max(0m, yearEndPriceEur - acquisitionPrice);
                var maxVorabpauschale = Math.Min(basisYield, appreciation);
                // … rest of the loop body (distributions, posting) UNCHANGED …
```

Delete the now-unused `var yearEndPrice = yearEndPriceProvider.GetPrice(...)` block that sat above the lot loop. Add `using WealthIQ.Application.Tax.Interface;` if not present (it is).

- [ ] **Step 3: Engineer the Stage A fixtures**

Goal: for every `(ISIN, lotCurrency, year)` a held lot needs a year-end price, the derived EUR (Close × FX@barDate, `LatestOnOrBefore` 31 Dec) **equals the old `data/test/configuration/prices.csv` value to the cent**.

Procedure for each row in the existing `prices.csv` (`year,isin,price_eur`):
  1. Determine the lot currency the test holds that ISIN in (from the trades — EUR-listed vs GBP-listed; see the spec note: `IE00B3XXRP09` → `VUSA.L` GBP; `CNDX.AS` EUR; etc.).
  2. Pick a year-end bar date present in `fx_rates.csv` for that currency (e.g. the last December trading day with an FX row), or add an FX row for a chosen date.
  3. Set the bar `close` so `close × fxRate(barDate)` rounds to `price_eur`. For EUR listings `close = price_eur` and FX = 1. For GBP listings `close = price_eur / fxRate(GBP→EUR, barDate)` (record the exact decimal; the lookup uses `Close` not `AdjustedClose`, so set `adjusted_close = close`).
  4. Write the bar into `data/test/configuration/historical_prices.csv` with `provider_symbol` matching `listings.json` for that `(isin, currency)`.

Use documented round-ish values; **these fixtures are the single source of truth**. Keep `volume` arbitrary (e.g. `1`).

> Why this preserves the numbers: Stage A still computes `appreciation = max(0, yearEndPriceEur − acquisitionPrice)`, identical to before, with `yearEndPriceEur` engineered to equal the prior `prices.csv` value. Nothing else in the formula changes.

- [ ] **Step 4: Update the regression test setup (NOT expectations)**

In `GermanTaxRegressionTests.cs`, replace the calculator construction (lines 34–37) so it builds a `DerivedInstrumentPriceProvider` from file-backed adapters:

```csharp
        var priceProvider = new DerivedInstrumentPriceProvider(
            new JsonInstrumentMarketDataMap(Path.Combine(configurationPath, "listings.json")),
            new CsvHistoricalPriceLookup(Path.Combine(configurationPath, "historical_prices.csv")));

        var calculator = new GermanTaxCalculator(
            new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
            priceProvider,
            new CsvFxRateLookup(Path.Combine(configurationPath, "fx_rates.csv")));
```

Add the needed usings (`WealthIQ.Infrastructure.ReferenceData;`, `WealthIQ.Infrastructure.Ibkr.MarketData;`). **Do not touch the `expectedSellEntries` / `expectedVorabEntries` arrays.**

- [ ] **Step 5: Run the regression test**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxRegressionTests"`
Expected: **PASS with the existing expected values.** If a Vorabpauschale or sell figure moved, a fixture is off — adjust the engineered `close`/FX (Step 3) until the derived EUR year-end matches `prices.csv` to the cent. Do **not** edit expected values in Stage A. If a missing-bar/FX error throws, add the missing fixture row.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test WealthIQ.slnx`
Expected: all green (other tax tests may also construct the calculator — update their setup the same way; see `GermanTaxCalculatorTests`, `GermanTaxCalculatorEdgeCaseTests`, `GermanTaxCalculatorVorabpauschaleTests`, `AnnualTaxReportServiceTests`, and `TaxTestDoubles`). For unit tests that used a fake `IYearEndPriceProvider`, introduce a fake `IInstrumentPriceProvider` in `TaxTestDoubles.cs` returning a fixed `InstrumentQuote`.

- [ ] **Step 7: Commit — STAGE A CHECKPOINT**

```bash
git add -A
git commit -m "feat(tax): source year-end price from DerivedInstrumentPriceProvider (Stage A, behavior-preserving)"
```

### Task 13: Remove `IYearEndPriceProvider` and its implementations

**Files:**
- Delete: `src/WealthIQ.Application/Tax/Interface/IYearEndPriceProvider.cs`
- Delete: `src/WealthIQ.Infrastructure/ReferenceData/DbYearEndPriceProvider.cs`
- Delete: `src/WealthIQ.Infrastructure/Ibkr/Tax/CsvYearEndPriceProvider.cs`

- [ ] **Step 1: Delete the three files**

- [ ] **Step 2: Build — fix any remaining references**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds after removing the DI registration (done in Task 30) and any test usings. If `Program.cs` still references it, comment out the `IYearEndPriceProvider` registration now (full DI rework is Task 30).

- [ ] **Step 3: Run full suite**

Run: `dotnet test WealthIQ.slnx`
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(tax): remove obsolete IYearEndPriceProvider"
```

---

## Work unit 4 — Shared refresh result + Yahoo historical-price provider & refresh service

### Task 14: Shared `DataRefreshResult` type

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/DataRefreshResult.cs`

- [ ] **Step 1: Create the result type** (reuses `ImportDiagnostic` from `WealthIQ.Application.Import.Diagnostic`)

```csharp
using WealthIQ.Application.Import.Diagnostic;

namespace WealthIQ.Application.ReferenceData;

/// <summary>Outcome of a dataset refresh: counts plus structured diagnostics, mirroring the import
/// philosophy (collect all diagnostics; a blocking diagnostic aborts the dataset's transaction). (spec §3)</summary>
public sealed record DataRefreshResult(
    int Added,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasBlockingDiagnostics =>
        Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);

    public static DataRefreshResult Empty { get; } = new(0, 0, 0, []);
}
```

- [ ] **Step 2: Build & commit**

Run: `dotnet build WealthIQ.slnx` (expected: succeeds)

```bash
git add -A
git commit -m "feat(referencedata): shared DataRefreshResult type"
```

### Task 15: Yahoo provider `IHistoricalPriceProvider` + `YahooHistoricalPriceProvider`

**Files:**
- Create: `src/WealthIQ.Application/MarketData/Interface/IHistoricalPriceProvider.cs`
- Create: `src/WealthIQ.Application/MarketData/HistoricalPriceFetchResult.cs`
- Create: `src/WealthIQ.Application/MarketData/HistoricalPriceProviderOptions.cs`
- Create: `src/WealthIQ.Infrastructure/Ibkr/MarketData/YahooHistoricalPriceProvider.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/MarketData/YahooHistoricalPriceProviderTests.cs` (new)
- Test fixture: `tests/WealthIQ.Tests/Fixtures/yahoo_chart_vusa.json` (a committed sample v8 chart payload)

Ports `download_price_history.py` (`fetch_history`). The HTTP send is isolated behind a virtual `SendAsync` so the parser is tested against a committed payload with **no live network**.

- [ ] **Step 1: Create the interface + DTOs**

```csharp
using WealthIQ.Application.MarketData;

namespace WealthIQ.Application.MarketData.Interface;

public interface IHistoricalPriceProvider
{
    /// <summary>Fetches daily bars for one provider symbol in [from, to], plus the reported listing currency.</summary>
    Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct);
}
```

```csharp
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData;

public sealed record HistoricalPriceFetchResult(
    string ProviderSymbol,
    CurrencyCode Currency,
    IReadOnlyList<PriceBar> Bars);
```

```csharp
namespace WealthIQ.Application.MarketData;

/// <summary>Politeness/retry knobs for the Yahoo provider, bound from appsettings (spec §2, §5.1).</summary>
public sealed class HistoricalPriceProviderOptions
{
    public string BaseUrl { get; set; } = "https://query1.finance.yahoo.com/v8/finance/chart/";
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public int InterRequestDelayMs { get; set; } = 500;
    public int MaxRetries { get; set; } = 4;
    public int InitialBackoffMs { get; set; } = 1000;
}
```

- [ ] **Step 2: Create a committed sample payload**

Save a minimal real-shaped v8 chart payload to `tests/WealthIQ.Tests/Fixtures/yahoo_chart_vusa.json`:

```json
{
  "chart": {
    "result": [
      {
        "meta": { "currency": "GBP", "symbol": "VUSA.L" },
        "timestamp": [1704153600, 1704240000],
        "indicators": {
          "quote": [
            { "open": [90.1, 91.2], "high": [90.9, 91.8], "low": [89.5, 90.7], "close": [90.5, 91.5], "volume": [1000, 1100] }
          ],
          "adjclose": [ { "adjclose": [90.5, 91.5] } ]
        }
      }
    ],
    "error": null
  }
}
```

Mark the fixtures folder to copy to output. In `tests/WealthIQ.Tests/WealthIQ.Tests.csproj` add (if not already copying content):

```xml
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing parser test**

`tests/WealthIQ.Tests/Infrastructure/MarketData/YahooHistoricalPriceProviderTests.cs`:

```csharp
using System.Net;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class YahooHistoricalPriceProviderTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task FetchAsync_ParsesBarsAndCurrency()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "yahoo_chart_vusa.json"));
        var client = new HttpClient(new StubHandler(json));
        var provider = new YahooHistoricalPriceProvider(client, new());

        var result = await provider.FetchAsync("VUSA.L", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3), CancellationToken.None);

        Assert.Equal(CurrencyCode.GBP, result.Currency);
        Assert.Equal(2, result.Bars.Count);
        Assert.Equal(90.5m, result.Bars[0].Close);
        Assert.Equal(CurrencyCode.GBP, result.Bars[0].Currency);
    }
}
```

- [ ] **Step 4: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~YahooHistoricalPriceProviderTests"`
Expected: FAIL (compile).

- [ ] **Step 5: Implement `YahooHistoricalPriceProvider`**

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.MarketData;

/// <summary>Thin HttpClient port of download_price_history.py's v8 chart call. Sequential per symbol with
/// bounded exponential back-off on 429/5xx (spec §5.1). Parses chart.result[0]: meta.currency,
/// timestamp[], indicators.quote[0] OHLCV, indicators.adjclose[0]; skips incomplete rows.</summary>
public sealed class YahooHistoricalPriceProvider(HttpClient httpClient, HistoricalPriceProviderOptions options)
    : IHistoricalPriceProvider
{
    public async Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var period1 = ToUnixSeconds(from);
        var period2 = ToUnixSeconds(to.AddDays(1));
        var url = $"{options.BaseUrl}{Uri.EscapeDataString(providerSymbol)}" +
                  $"?period1={period1}&period2={period2}&interval=1d&includePrePost=false&events=history";

        var json = await GetWithRetryAsync(url, providerSymbol, ct);
        using var doc = JsonDocument.Parse(json);

        var chart = doc.RootElement.GetProperty("chart");
        if (!chart.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array || resultArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Yahoo returned no result for '{providerSymbol}'.");
        }

        var result = resultArray[0];
        var currencyText = result.GetProperty("meta").GetProperty("currency").GetString();
        if (!Enum.TryParse<CurrencyCode>(currencyText, ignoreCase: true, out var currency))
        {
            throw new InvalidOperationException($"Yahoo returned unsupported/missing currency '{currencyText}' for '{providerSymbol}'.");
        }

        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var adjclose = result.GetProperty("indicators").GetProperty("adjclose")[0].GetProperty("adjclose");
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var bars = new List<PriceBar>();
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            if (!TryDecimal(opens[i], out var open) || !TryDecimal(highs[i], out var high) || !TryDecimal(lows[i], out var low)
                || !TryDecimal(closes[i], out var close) || !TryDecimal(adjclose[i], out var adj) || volumes[i].ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime);
            bars.Add(new PriceBar(date, providerSymbol, currency, open, high, low, close, adj, volumes[i].GetInt64()));
        }

        return new HistoricalPriceFetchResult(providerSymbol, currency, bars);
    }

    private async Task<string> GetWithRetryAsync(string url, string providerSymbol, CancellationToken ct)
    {
        var backoff = options.InitialBackoffMs;
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(ct);
            }

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!transient || attempt >= options.MaxRetries)
            {
                throw new InvalidOperationException($"Yahoo request for '{providerSymbol}' failed with {(int)response.StatusCode} after {attempt + 1} attempt(s).");
            }

            await Task.Delay(backoff, ct);
            backoff *= 2;
        }
    }

    private static long ToUnixSeconds(DateOnly date)
        => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
    }
}
```

- [ ] **Step 6: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~YahooHistoricalPriceProviderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(marketdata): YahooHistoricalPriceProvider (thin HttpClient port of v8 chart)"
```

### Task 16: `HistoricalPriceRefreshService` (incremental)

**Files:**
- Create: `src/WealthIQ.Application/MarketData/HistoricalPriceRefreshService.cs`
- Test: `tests/WealthIQ.Tests/Application/MarketData/HistoricalPriceRefreshServiceTests.cs` (new)

Behavior (spec §2, §10): for each configured listing symbol, fetch only `(maxStoredDate+1 … today)`; upsert by `(ProviderSymbol, Date)`; **blocking diagnostic** if a fetched bar's currency ≠ the configured listing currency; "force full reload" wipes one symbol then refetches; idempotent. The service takes the provider + a small store port so it is unit-testable without a DB.

- [ ] **Step 1: Define a store port the service writes through**

Add to the same file (or a sibling) an interface the Infrastructure implements over the DB:

```csharp
namespace WealthIQ.Application.MarketData;

public sealed record HistoricalPriceSymbol(string ProviderSymbol, WealthIQ.Domain.Enumeration.Currency Currency);

public interface IHistoricalPriceStore
{
    IReadOnlyList<HistoricalPriceSymbol> GetConfiguredListings();
    DateOnly? GetMaxStoredDate(string providerSymbol);
    void DeleteSymbol(string providerSymbol);
    /// <returns>(added, updated)</returns>
    (int Added, int Updated) Upsert(IReadOnlyList<PriceBar> bars);
    Task SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing service test** (provider + store both faked; **no network, no DB**)

```csharp
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.MarketData;

public sealed class HistoricalPriceRefreshServiceTests
{
    private sealed class FakeProvider(HistoricalPriceFetchResult result) : IHistoricalPriceProvider
    {
        public DateOnly? From;
        public Task<HistoricalPriceFetchResult> FetchAsync(string s, DateOnly from, DateOnly to, CancellationToken ct)
        { From = from; return Task.FromResult(result); }
    }

    private sealed class FakeStore : IHistoricalPriceStore
    {
        public DateOnly? Max;
        public List<PriceBar> Saved = new();
        public IReadOnlyList<HistoricalPriceSymbol> GetConfiguredListings() => [new("VUSA.L", CurrencyCode.GBP)];
        public DateOnly? GetMaxStoredDate(string s) => Max;
        public void DeleteSymbol(string s) => Saved.Clear();
        public (int, int) Upsert(IReadOnlyList<PriceBar> bars) { Saved.AddRange(bars); return (bars.Count, 0); }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task RefreshAsync_FetchesFromDayAfterMaxStored()
    {
        var bar = new PriceBar(new DateOnly(2024, 12, 30), "VUSA.L", CurrencyCode.GBP, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.GBP, [bar]));
        var store = new FakeStore { Max = new DateOnly(2024, 12, 1) };

        var service = new HistoricalPriceRefreshService(provider, store);
        var result = await service.RefreshAsync(new DateOnly(2024, 12, 31), forceFullReload: false, CancellationToken.None);

        Assert.Equal(new DateOnly(2024, 12, 2), provider.From);
        Assert.Equal(1, result.Added);
        Assert.False(result.HasBlockingDiagnostics);
    }

    [Fact]
    public async Task RefreshAsync_CurrencyMismatch_ProducesBlockingDiagnostic()
    {
        var bar = new PriceBar(new DateOnly(2024, 12, 30), "VUSA.L", CurrencyCode.USD, 1, 1, 1, 1, 1, 1);
        var provider = new FakeProvider(new HistoricalPriceFetchResult("VUSA.L", CurrencyCode.USD, [bar]));
        var store = new FakeStore();

        var service = new HistoricalPriceRefreshService(provider, store);
        var result = await service.RefreshAsync(new DateOnly(2024, 12, 31), forceFullReload: false, CancellationToken.None);

        Assert.True(result.HasBlockingDiagnostics);
        Assert.Empty(store.Saved);
    }
}
```

- [ ] **Step 3: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~HistoricalPriceRefreshServiceTests"`
Expected: FAIL (compile).

- [ ] **Step 4: Implement `HistoricalPriceRefreshService`**

```csharp
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.MarketData;

/// <summary>Incremental per-symbol refresh: fetch (maxStoredDate+1 … asOf), upsert by (symbol, date).
/// A fetched bar whose currency ≠ the configured listing currency is a blocking diagnostic and the
/// symbol's bars are not written (spec §3). "Force full reload" wipes the symbol first.</summary>
public sealed class HistoricalPriceRefreshService(IHistoricalPriceProvider provider, IHistoricalPriceStore store)
{
    public async Task<DataRefreshResult> RefreshAsync(DateOnly asOf, bool forceFullReload, CancellationToken ct)
    {
        var diagnostics = new List<ImportDiagnostic>();
        int added = 0, updated = 0, skipped = 0;

        foreach (var listing in store.GetConfiguredListings())
        {
            if (forceFullReload)
            {
                store.DeleteSymbol(listing.ProviderSymbol);
            }

            var from = forceFullReload
                ? asOf.AddYears(-5)
                : (store.GetMaxStoredDate(listing.ProviderSymbol)?.AddDays(1) ?? asOf.AddYears(-5));

            if (from > asOf)
            {
                skipped++;
                continue;
            }

            HistoricalPriceFetchResult fetched;
            try
            {
                fetched = await provider.FetchAsync(listing.ProviderSymbol, from, asOf, ct);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.FileReadFailed,
                    $"Fetch failed for '{listing.ProviderSymbol}': {ex.Message}", Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
                continue;
            }

            if (fetched.Currency != listing.Currency)
            {
                diagnostics.Add(new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord,
                    $"'{listing.ProviderSymbol}' returned {fetched.Currency} but is configured as {listing.Currency}.",
                    Section: "HistoricalPrices", SourceReference: listing.ProviderSymbol));
                continue;
            }

            var (a, u) = store.Upsert(fetched.Bars);
            added += a;
            updated += u;
        }

        if (diagnostics.All(d => d.Severity < ImportDiagnosticSeverity.Error))
        {
            await store.SaveChangesAsync(ct);
        }

        return new DataRefreshResult(added, updated, skipped, diagnostics);
    }
}
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~HistoricalPriceRefreshServiceTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Implement the DB store `DbHistoricalPriceStore : IHistoricalPriceStore`**

Create `src/WealthIQ.Infrastructure/ReferenceData/DbHistoricalPriceStore.cs`. `GetConfiguredListings` reads distinct `(ProviderSymbol, Currency)` from `InstrumentListings`; `GetMaxStoredDate` is `db.HistoricalPrices.Where(...).Max(x => (DateOnly?)x.Date)`; `Upsert` matches existing rows by `(ProviderSymbol, Date)` and updates or adds; `SaveChangesAsync` delegates to the context. No new test required beyond the service test (DB store covered by an integration test in Task 16b if desired).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(marketdata): incremental HistoricalPriceRefreshService + DB store"
```

---

## Work unit 5 — ECB FX provider & refresh service

### Task 17: `IFxRateProvider` + `EcbFxRateProvider`

**Files:**
- Create: `src/WealthIQ.Application/Currency/Interface/IFxRateProvider.cs`
- Create: `src/WealthIQ.Application/Currency/FxRateRecord.cs`
- Create: `src/WealthIQ.Application/Currency/FxRateProviderOptions.cs`
- Create: `src/WealthIQ.Infrastructure/Ibkr/Currency/EcbFxRateProvider.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Currency/EcbFxRateProviderTests.cs` (new)
- Test fixture: `tests/WealthIQ.Tests/Fixtures/ecb_eurofxref_hist.xml`

Ports `download_fx_rates.py`: GET `eurofxref-hist.xml`, parse daily cubes, emit `EUR=1.0` plus `currency_to_eur = 1/rate` for supported currencies, within a date window.

- [ ] **Step 1: Create interface + DTO + options**

```csharp
using WealthIQ.Application.Currency;

namespace WealthIQ.Application.Currency.Interface;

public interface IFxRateProvider
{
    Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
```

```csharp
namespace WealthIQ.Application.Currency;

public sealed record FxRateRecord(DateOnly Date, string Currency, decimal RateToEur);
```

```csharp
namespace WealthIQ.Application.Currency;

public sealed class FxRateProviderOptions
{
    public string HistoricalUrl { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml";
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public IReadOnlyList<string> SupportedCurrencies { get; set; } = ["USD", "GBP", "CHF"];
}
```

- [ ] **Step 2: Create the committed sample XML fixture**

`tests/WealthIQ.Tests/Fixtures/ecb_eurofxref_hist.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
  <Cube>
    <Cube time="2024-12-30">
      <Cube currency="USD" rate="1.0400"/>
      <Cube currency="GBP" rate="0.8300"/>
      <Cube currency="CHF" rate="0.9400"/>
      <Cube currency="JPY" rate="164.00"/>
    </Cube>
    <Cube time="2020-01-02">
      <Cube currency="USD" rate="1.1200"/>
    </Cube>
  </Cube>
</gesmes:Envelope>
```

- [ ] **Step 3: Write the failing test**

```csharp
using System.Net;
using WealthIQ.Infrastructure.Ibkr.Currency;

namespace WealthIQ.Tests.Infrastructure.Currency;

public sealed class EcbFxRateProviderTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task FetchAsync_EmitsEurAndInvertedSupportedRatesWithinWindow()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ecb_eurofxref_hist.xml"));
        var provider = new EcbFxRateProvider(new HttpClient(new StubHandler(xml)), new());

        var rows = await provider.FetchAsync(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), CancellationToken.None);

        Assert.Contains(rows, r => r.Date == new DateOnly(2024, 12, 30) && r.Currency == "EUR" && r.RateToEur == 1m);
        Assert.Contains(rows, r => r.Currency == "USD" && Math.Round(r.RateToEur, 6) == Math.Round(1m / 1.0400m, 6));
        Assert.DoesNotContain(rows, r => r.Currency == "JPY"); // not in SupportedCurrencies
        Assert.DoesNotContain(rows, r => r.Date == new DateOnly(2020, 1, 2)); // outside window
    }
}
```

- [ ] **Step 4: Run — expect FAIL (class missing)**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~EcbFxRateProviderTests"`
Expected: FAIL (compile).

- [ ] **Step 5: Implement `EcbFxRateProvider`**

```csharp
using System.Globalization;
using System.Xml.Linq;
using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;

namespace WealthIQ.Infrastructure.Ibkr.Currency;

/// <summary>Thin HttpClient port of download_fx_rates.py. Parses ECB eurofxref-hist.xml daily cubes,
/// emits EUR=1.0 plus currency_to_eur = 1/rate for the supported currencies, within [from, to] (spec §5.2).</summary>
public sealed class EcbFxRateProvider(HttpClient httpClient, FxRateProviderOptions options) : IFxRateProvider
{
    private static readonly XNamespace Def = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    public async Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.HistoricalUrl);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct);

        var supported = options.SupportedCurrencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = XDocument.Parse(xml);
        var rows = new List<FxRateRecord>();

        foreach (var dayCube in root.Descendants(Def + "Cube").Where(c => c.Attribute("time") is not null))
        {
            if (!DateOnly.TryParseExact(dayCube.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date < from || date > to)
            {
                continue;
            }

            rows.Add(new FxRateRecord(date, "EUR", 1m));
            foreach (var rateCube in dayCube.Elements(Def + "Cube").Where(c => c.Attribute("currency") is not null && c.Attribute("rate") is not null))
            {
                var currency = rateCube.Attribute("currency")!.Value;
                if (!supported.Contains(currency))
                {
                    continue;
                }

                if (decimal.TryParse(rateCube.Attribute("rate")!.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var eurToCurrency) && eurToCurrency > 0m)
                {
                    rows.Add(new FxRateRecord(date, currency, 1m / eurToCurrency));
                }
            }
        }

        return rows;
    }
}
```

- [ ] **Step 6: Run — expect PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~EcbFxRateProviderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(currency): EcbFxRateProvider (port of eurofxref-hist parser)"
```

### Task 18: `FxRateRefreshService`

**Files:**
- Create: `src/WealthIQ.Application/Currency/FxRateRefreshService.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbFxRateStore.cs`
- Test: `tests/WealthIQ.Tests/Application/Currency/FxRateRefreshServiceTests.cs` (new)

Define `IFxRateStore` (`Upsert(IReadOnlyList<FxRateRecord>)` by `(Date, Currency)`, `SaveChangesAsync`). The service fetches `[from, to]`, upserts, returns `DataRefreshResult`. Idempotent (re-run updates, no dupes). Same test pattern as Task 16 (fake provider + fake store; assert added counts and idempotency on second run). Then implement `DbFxRateStore` over `FxRates`.

- [ ] **Step 1: Write the failing test** (fake provider returns two records; fake store records upserts; second run updates not adds).
- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement `IFxRateStore` + `FxRateRefreshService`** (mirror `HistoricalPriceRefreshService`: collect diagnostics, save only if non-blocking, return counts).
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Implement `DbFxRateStore`** over the `FxRates` table (upsert by `(Date, Currency)`).
- [ ] **Step 6: Commit** `feat(currency): FxRateRefreshService + DB store`.

---

## Work unit 6 — BMF Basiszins source & refresh service

### Task 19: `IBasisInterestRateSource` + `BmfBasisInterestRateSource`

**Files:**
- Create: `src/WealthIQ.Application/Tax/Interface/IBasisInterestRateSource.cs`
- Create: `src/WealthIQ.Application/Tax/BasisInterestRateRecord.cs`
- Create: `src/WealthIQ.Application/Tax/BasisInterestRateSourceOptions.cs`
- Create: `src/WealthIQ.Infrastructure/Ibkr/Tax/BmfBasisInterestRateSource.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Tax/BmfBasisInterestRateSourceTests.cs` (new)
- Test fixture: `tests/WealthIQ.Tests/Fixtures/bmf_basiszins_2025.html` (a committed sample page snippet)

Fetches the official BMF "Basiszins zur Berechnung der Vorabpauschale" for a year (e.g. 2.53% → 0.0253 for 2025, 3.20% → 0.0320 for 2026). Defensive parsing; failure → `null` + the caller raises a diagnostic; manual override is the fallback.

- [ ] **Step 1: Interface + DTO + options**

```csharp
namespace WealthIQ.Application.Tax;

public sealed record BasisInterestRateRecord(int Year, decimal Rate);
```

```csharp
using WealthIQ.Application.Tax;

namespace WealthIQ.Application.Tax.Interface;

public interface IBasisInterestRateSource
{
    /// <summary>The official BMF Basiszins for <paramref name="year"/>, or <c>null</c> if it cannot be obtained.</summary>
    Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken ct);
}
```

```csharp
namespace WealthIQ.Application.Tax;

public sealed class BasisInterestRateSourceOptions
{
    public string UserAgent { get; set; } = "Mozilla/5.0";
    /// <summary>URL template; {year} is substituted. Defaults to the BMF publication page (configurable).</summary>
    public string UrlTemplate { get; set; } = "https://www.bundesfinanzministerium.de/Content/DE/Standardartikel/Themen/Steuern/Weitere_Steuerthemen/Abgeltungsteuer/basiszins.html";
}
```

- [ ] **Step 2: Commit a small sample HTML fixture** containing a recognizable "Basiszins … 2025 … 2,53 %" pattern so the parser is testable offline.

- [ ] **Step 3: Write the failing parser test** — feed the fixture via a stub `HttpMessageHandler`; assert `FetchAsync(2025)` returns `0.0253m`; a year not present returns `null`.

- [ ] **Step 4: Run — expect FAIL (class missing).**

- [ ] **Step 5: Implement `BmfBasisInterestRateSource`** — GET the page, regex-scan for the year and a German-formatted percentage near it (e.g. `(?<pct>\d+,\d+)\s*(%|Prozent)` associated with the requested year), parse `de-DE` decimal, divide by 100. On no match → return `null` (do not throw). Keep parsing defensive and well-commented (page format drift is an accepted risk, spec §15).

- [ ] **Step 6: Run — expect PASS.**

- [ ] **Step 7: Commit** `feat(tax): BmfBasisInterestRateSource (defensive scrape + offline fixture)`.

### Task 20: `BasisInterestRateRefreshService` + manual edit

**Files:**
- Create: `src/WealthIQ.Application/Tax/BasisInterestRateRefreshService.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbBasisInterestRateStore.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/BasisInterestRateRefreshServiceTests.cs` (new)

Define `IBasisInterestRateStore` (`Upsert(int year, decimal rate)`, `SaveChangesAsync`). The refresh service calls the source for a year; if `null`, returns a blocking diagnostic and writes nothing; else upserts. A separate `SetManualAsync(year, rate)` path always upserts (the manual override). Returns `DataRefreshResult`.

- [ ] **Step 1: Write the failing test** (fake source returns a record → 1 added; fake source returns null → blocking diagnostic, store empty; manual set always upserts).
- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement `IBasisInterestRateStore` + `BasisInterestRateRefreshService`.**
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Implement `DbBasisInterestRateStore`** over `BasisInterestRates` (upsert by `Year`).
- [ ] **Step 6: Commit** `feat(tax): BasisInterestRateRefreshService + manual override + DB store`.

---

# STAGE B — Vorabpauschale correction (the capstone)

> Do not start until Stage A is green (`GermanTaxRegressionTests` passing with **unchanged** expected values). Stage B deliberately changes numbers.

## Work unit 7 — Corrected Vorabpauschale algorithm

### Task 21: Carry classification onto `Instrument`; fund-gate + classification fail-fast

**Files:**
- Modify: `src/WealthIQ.Domain/Model/General/Instrument.cs`
- Modify: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentProfileEnricher.cs`
- Modify: `src/WealthIQ.Infrastructure/Ibkr/Tax/JsonInstrumentProfileEnricher.cs`
- Test: `tests/WealthIQ.Tests/Application/Tax/InstrumentCatalogBuilderTests.cs` (extend)

- [ ] **Step 1: Add `Type` + `SubjectToVorabpauschale` to the domain `Instrument`**

Add two init-only members so existing positional construction at the importer keeps working (defaults), while enrichment sets them:

```csharp
namespace WealthIQ.Domain.Model.General;

public sealed record Instrument(
    InstrumentId InstrumentId,
    string ISIN,
    string Symbol,
    string Name,
    decimal Teilfreistellungsquote)
{
    /// <summary>Instrument classification mirrored from the profile (e.g. "ETF_EQUITY"). Empty until enriched.</summary>
    public string Type { get; init; } = "";

    /// <summary>Whether §18 InvStG Vorabpauschale applies. Set explicitly by the profile; there is no inference.
    /// A held instrument with no profile is a blocking error at tax replay (spec §2, §6.4).</summary>
    public bool? SubjectToVorabpauschale { get; init; }

    public override string ToString() => $"{Name} ({Symbol}, {ISIN})";
}
```

> `SubjectToVorabpauschale` is **nullable** so "never enriched / no profile" (`null`) is distinguishable from an explicit `false`. The calculator treats `null` for a held lot as a blocking error.

- [ ] **Step 2: Set the new fields in both enrichers (known ISIN only)**

In `DbInstrumentProfileEnricher`, load `Type` + `SubjectToVorabpauschale` from the profile row into the lookup, and set them when enriching a known ISIN:

```csharp
        if (!string.IsNullOrWhiteSpace(instrument.ISIN)
            && _profiles.TryGetValue(instrument.ISIN, out var profile))
        {
            return instrument with
            {
                Name = profile.Name,
                Type = profile.Type,
                Teilfreistellungsquote = profile.Teilfreistellungsquote,
                SubjectToVorabpauschale = profile.SubjectToVorabpauschale,
                Symbol = string.IsNullOrWhiteSpace(instrument.Symbol) ? "Unknown" : instrument.Symbol
            };
        }

        // No profile on file → leave SubjectToVorabpauschale null (Stage B blocks at replay if held).
        return instrument;
```

Mirror in `JsonInstrumentProfileEnricher` (it reads `instruments.json`; extend its DTO with `type` + `subject_to_vorabpauschale`, same as the seeder DTO in Task 11).

- [ ] **Step 3: Write/extend a test asserting enrichment carries the flag**

In `InstrumentCatalogBuilderTests`, add a case: a profile with `subject_to_vorabpauschale=true, type="ETF_EQUITY"` enriches an imported instrument so `result.Single().SubjectToVorabpauschale == true` and `Type == "ETF_EQUITY"`.

- [ ] **Step 4: Run — expect FAIL then implement until PASS**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~InstrumentCatalogBuilderTests"`
Expected: PASS after Steps 1–2.

- [ ] **Step 5: Build full solution**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds (importer's positional `Instrument(...)` calls still compile — new members have defaults).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(tax): carry Type + SubjectToVorabpauschale onto Instrument via enrichment"
```

### Task 22: Implement the corrected §6.2 algorithm in `PerformYearEndClosing`

**Files:**
- Modify: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`
- Tests: targeted tests in Task 23 drive this; write them first if doing strict TDD (recommended — do Task 23 Step-by-step interleaved).

This replaces the acquisition-cost base with **uniform year-start rebasing**, adds distributions into the cap, applies the **1/12 reduction to the final Vorabpauschale**, fund-gates, and fails fast on missing Basiszins/classification. Implements the pseudocode in spec §6.2.

- [ ] **Step 1: Rewrite the Basiszins gate (null = blocking; ≤0 = skip year)**

Replace lines 208–212:

```csharp
        var basisInterestRate = interestRateProvider.GetRate(year);
        if (basisInterestRate is null)
        {
            throw new InvalidOperationException(
                $"Basiszins for {year} is missing but a lot is held over that year-end. Add the rate before computing Vorabpauschale.");
        }

        if (basisInterestRate.Value <= 0m)
        {
            return; // official zero/negative rate → no Vorabpauschale this year, no price lookup (§6.4)
        }

        var basisFactor = basisInterestRate.Value * 0.7m;
```

> **Scope nuance (spec §5.3 / §6.4.1):** `null` is only a blocking error for a year **in replay scope** `[firstYear, lastYear]` where a lot is actually held. Because `PerformYearEndClosing` only runs for years in range and only iterates lots with `RemainingQuantity > 0`, reaching the `null` branch already implies "held over this year-end" — so throwing here is correct. (Quiet years with no held lots never get past the `instrumentGroup` loop, but the gate runs before it; if a quiet year legitimately has no Basiszins and no held lots, guard by checking `openLots.Any(long & remaining>0)` before the gate. Add that guard:)

```csharp
        var hasHeldLongLot = openLots.Any(x => x.Direction == PositionDirection.Long && x.RemainingQuantity.Value > 0m);
        if (!hasHeldLongLot)
        {
            return;
        }
```

Place this guard **before** the Basiszins gate so a quiet year with nothing held never demands a rate.

- [ ] **Step 2: Replace the per-lot body with the corrected algorithm**

Replace the instrument-group loop body. For the instrument-level checks add the fund-gate and classification fail-fast; replace the lot-level math with year-start/year-end rebasing:

```csharp
        foreach (var instrumentGroup in openLots
                     .Where(x => x.Direction == PositionDirection.Long && x.RemainingQuantity.Value > 0m)
                     .GroupBy(x => x.InstrumentId))
        {
            var instrument = GetInstrument(instrumentById, instrumentGroup.Key);

            if (instrument.SubjectToVorabpauschale is null)
            {
                throw new InvalidOperationException(
                    $"Instrument '{instrument.ISIN}' is held over {year} year-end but has no classification profile. " +
                    $"Add an instrument profile (incl. SubjectToVorabpauschale) before computing Vorabpauschale.");
            }

            if (instrument.SubjectToVorabpauschale != true)
            {
                continue; // §18 applies only to investment funds (spec §6.2)
            }

            foreach (var lot in instrumentGroup.ToList())
            {
                var lotCurrency = lot.OpenUnitPrice.Currency;

                var startQuote = priceProvider.GetQuote(instrument.ISIN, lotCurrency, new DateOnly(year, 1, 1), PriceQuoteHandling.EarliestOnOrAfter);
                var endQuote = priceProvider.GetQuote(instrument.ISIN, lotCurrency, new DateOnly(year, 12, 31), PriceQuoteHandling.LatestOnOrBefore);
                if (startQuote is null || endQuote is null)
                {
                    throw new InvalidOperationException(
                        $"Year-start or year-end redemption price for '{instrument.ISIN}' ({lotCurrency}) in {year} is required but missing.");
                }

                var startValueEur = _fxConverter.Convert(new Money(startQuote.Value.Close, startQuote.Value.Currency), startQuote.Value.AsOf).Amount;
                var endValueEur = _fxConverter.Convert(new Money(endQuote.Value.Close, endQuote.Value.Currency), endQuote.Value.AsOf).Amount;

                var distributionPerShare = distributions
                    .Where(d => d.Year == year
                        && d.AccountId == lot.AccountId
                        && d.InstrumentId == instrument.InstrumentId
                        && d.Date >= lot.OpenTradeDate)
                    .Sum(d => d.PerShare);

                var basisErtrag = startValueEur * basisFactor;                          // start × Basiszins × 0.7  (§18(1))
                var cap = Math.Max(0m, (endValueEur - startValueEur) + distributionPerShare); // Mehrbetrag + Ausschüttungen
                var cappedBasisErtrag = Math.Min(basisErtrag, cap);
                var vorabFull = Math.Max(0m, cappedBasisErtrag - distributionPerShare);  // Basisertrag übersteigt Ausschüttungen

                var monthFactor = lot.OpenTradeDate.Year == year
                    ? (13m - lot.OpenTradeDate.Month) / 12m                              // 1/12 per full month before purchase (§18(2))
                    : 1m;
                var vorabPerShare = vorabFull * monthFactor;
                if (vorabPerShare <= 0m)
                {
                    continue;
                }

                var totalVorabpauschale = vorabPerShare * lot.RemainingQuantity.Value;
                ReplaceLot(openLots, lot with
                {
                    AccumulatedVorabpauschale = new Money(lot.AccumulatedVorabpauschale.Amount + totalVorabpauschale, WealthIQ.Domain.Enumeration.Currency.EUR)
                });

                ledger.Add(new GermanTaxEntry(
                    year + 1,
                    new DateOnly(year + 1, 1, 1),
                    GermanTaxEntryType.Vorabpauschale,
                    instrument.Symbol,
                    instrument.ISIN,
                    totalVorabpauschale,
                    totalVorabpauschale * (1m - instrument.Teilfreistellungsquote)));
            }
        }
```

> Verify the month factor: for an acquisition in March (`Month == 3`), `(13 − 3)/12 = 10/12` — ten twelfths retained because two full months (Jan, Feb) precede the purchase month. This matches the old code's `12 − month + 1 = 10` numerator. The **difference from the old code** is that the factor now multiplies the *final* Vorabpauschale (`vorabFull`), not the Basisertrag.

- [ ] **Step 3: Remove the now-dead `CalculateRemainingAcquisitionPriceInEur`** if no longer referenced by Vorabpauschale (it is still used? check — it was only used in `PerformYearEndClosing`). Grep: `dotnet build` will warn if unused/private. Remove it if the build flags it unused, otherwise leave it (it may be referenced elsewhere — keep only if used).

- [ ] **Step 4: Build**

Run: `dotnet build WealthIQ.slnx`
Expected: succeeds. (Regression test will now FAIL — that is expected and fixed in Task 24.)

- [ ] **Step 5: Commit (tests not yet green — note in message)**

```bash
git add -A
git commit -m "feat(tax): §18-correct Vorabpauschale (year-start rebasing, dist-in-cap, 1/12-on-final, fund-gate) [regression baseline updated in next commit]"
```

### Task 23: New targeted Vorabpauschale tests (the witnesses)

**Files:**
- Create: `tests/WealthIQ.Tests/Application/Tax/VorabpauschaleCorrectionTests.cs`
- Extend: `tests/WealthIQ.Tests/Application/Tax/TaxTestDoubles.cs` (a configurable fake `IInstrumentPriceProvider` + `IBasisInterestRateProvider` + in-memory FX)

Write these **before/with** Task 22 if doing strict TDD (recommended). Each builds a tiny ledger (one buy, optional dividend, optional partial sale) and asserts the posted Vorabpauschale from first principles. Provide a fake price provider returning fixed `InstrumentQuote`s for `(EarliestOnOrAfter Jan 1)` and `(LatestOnOrBefore Dec 31)`.

Required tests (spec §13), each with worked arithmetic in comments:

- [ ] `Vorabpauschale_MultiYearHold_RebasesToYearStart` — hold a EUR fund across two full years; assert year 2's Vorabpauschale uses **year-2 start price** (not acquisition cost). Worked example: start₂=100, end₂=130, Basiszins=0.0229 → basisErtrag = 100×0.0229×0.7 = 1.603; cap = max(0,30)=30; capped=1.603; no dist → vorab/share=1.603; × quantity.
- [ ] `Vorabpauschale_AcquisitionYear_UsesYearStartPriceAndProRatesFinalAmount` — buy in March; assert start value is the year-start quote and `monthFactor = 10/12` multiplies the final vorab.
- [ ] `Vorabpauschale_DistributionIncludedInAppreciationCap_WhenCapBinds` — choose start/end so `end−start` < basisErtrag but `end−start+dist` ≥ basisErtrag; assert the cap now includes the distribution (vorab higher than if distribution were omitted from the cap), then the distribution is subtracted from the capped Basisertrag.
- [ ] `Vorabpauschale_OrdinaryStockWithIsin_IsSkipped` — instrument `SubjectToVorabpauschale=false` → no Vorabpauschale entry even though ISIN present.
- [ ] `Vorabpauschale_MissingClassification_ThrowsBlocking` — held instrument with `SubjectToVorabpauschale=null` → `InvalidOperationException`.
- [ ] `Vorabpauschale_MissingBasiszins_ThrowsBlocking` — Basiszins provider returns `null` for a held year → throws.
- [ ] `Vorabpauschale_NonPositiveBasiszins_DoesNotRequirePrices` — Basiszins ≤ 0 → returns without calling the price provider (use a price provider that throws if called).
- [ ] `Vorabpauschale_NonEurLot_ConvertsYearStartAndYearEndAtOwnDates` — GBP lot; assert start converted at the year-start bar date's FX and end at the year-end bar date's FX (different rates), via an in-memory FX lookup.
- [ ] Blocking-error cases: missing listing, missing year-start bar, missing year-end bar, missing FX, currency mismatch (these largely fall out of the provider; assert the calculator surfaces them).

For each: **Step 1** write test → **Step 2** run (red) → **Step 3** confirm Task 22 implementation makes it green → **Step 4** run. Commit once all are green:

```bash
git add -A
git commit -m "test(tax): targeted §18 Vorabpauschale correction witnesses"
```

### Task 24: Recompute the `GermanTaxRegressionTests` baseline (spec §8 — highest risk)

**Files:**
- Modify (fixtures): `data/test/configuration/historical_prices.csv`, `data/test/configuration/listings.json`, `data/test/configuration/instruments.json`, `data/test/configuration/fx_rates.csv`
- Modify (expectations + comments): `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs`
- Create: a worked-arithmetic table appended to this plan file (or a sibling note under `docs/superpowers/plans/`)

Follow spec §8 **exactly**. This is the hardest task; do it deliberately, not by trusting test output.

- [ ] **Step 1: Enumerate Vorabpauschale-bearing lots** from the test statements (`data/test/statements/TaxAlpha_Raw_Data_2021..2024.xml`). For each held **fund** lot up to 2024 record: `ISIN`, lot currency, `OpenTradeDate`, quantity, partial sales, and confirm `SubjectToVorabpauschale=true`. Produce a table (commit it as a comment block or a markdown note). Tip: temporarily log lots from a scratch test, or trace the FIFO by hand from the XML buys/sells.

- [ ] **Step 2: Assemble fixtures** so that for every `(ISIN, currency, year)` a held fund lot needs, `historical_prices.csv` contains the **first** and **last** trading bar of that year in the lot's currency, plus `fx_rates.csv` rows for those two bar dates. Use deliberate, documented round values — these are the single source of truth.

- [ ] **Step 3: Compute per lot, per year** with §6.2 exactly (acquisition year uses the year-start fixture price with `monthFactor`). Record each line of arithmetic.

- [ ] **Step 4: Apply distributions-in-cap and 1/12-on-final**, multiply by `RemainingQuantity`, apply `(1 − tfs)` for the taxable amount.

- [ ] **Step 5: Sum** to the figures the test asserts (per-symbol Vorabpauschale tuples + the 2024 totals; also re-derive the **Sell** entries' `UsedVorabpauschale`, which changes because accumulated Vorabpauschale at sale changes). Update both `expectedVorabEntries` and `expectedSellEntries` (and the two `Assert.Equal` totals) with the computed values, each changed figure commented with its arithmetic + cause, per CLAUDE.md.

- [ ] **Step 6: Run the regression test**

Run: `dotnet test WealthIQ.slnx --filter "FullyQualifiedName~GermanTaxRegressionTests"`
Expected: PASS against the **hand-computed** values. If it disagrees, do **not** blindly paste actual output — reconcile by hand (use superpowers:systematic-debugging); a mismatch means either a fixture or an arithmetic error, both of which must be understood.

- [ ] **Step 7: Sanity check vs §6.1** — rebasing typically *raises* Vorabpauschale for an appreciating multi-year hold; distributions-in-cap *raises* it where the cap binds. Confirm the direction of each change is explainable.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test WealthIQ.slnx`
Expected: all green.

- [ ] **Step 9: Commit — STAGE B COMPLETE (the bug is fixed)**

```bash
git add -A
git commit -m "test(tax): recompute regression baseline for §18-correct Vorabpauschale (worked arithmetic in comments)"
```

---

# STAGE C — Administration UI, clearing, wiring, cleanup

## Work unit 8 — Instrument reference administration

### Task 25: `IInstrumentReferenceAdmin` (CRUD + upload)

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/InstrumentAdminModels.cs` (DTOs)
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IInstrumentReferenceAdmin.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbInstrumentReferenceAdmin.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbInstrumentReferenceAdminTests.cs` (new)

The editable "Instrument" is the union of `InstrumentProfileRow` (profile) + 0..n `InstrumentListingRow` (listings), presented as one entity (spec §9).

- [ ] **Step 1: Define the DTOs**

```csharp
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.ReferenceData;

public sealed record InstrumentListingDto(CurrencyCode Currency, string ProviderSymbol, string Provider, string? Exchange, string? Notes);

public sealed record InstrumentAdminDto(
    string Isin,
    string Name,
    string Type,
    decimal Teilfreistellungsquote,
    bool SubjectToVorabpauschale,
    IReadOnlyList<InstrumentListingDto> Listings);

public enum UploadMode { Merge, Replace }

public sealed record InstrumentUploadResult(int Profiles, int Listings, IReadOnlyList<string> Warnings);
```

- [ ] **Step 2: Define the service interface**

```csharp
using WealthIQ.Application.ReferenceData;

namespace WealthIQ.Application.ReferenceData.Interface;

public interface IInstrumentReferenceAdmin
{
    Task<IReadOnlyList<InstrumentAdminDto>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(InstrumentAdminDto instrument, CancellationToken ct = default);   // add or edit (upsert profile + replace its listings)
    Task<bool> IsReferencedByLedgerAsync(string isin, CancellationToken ct = default); // delete guard
    Task DeleteAsync(string isin, CancellationToken ct = default);
    Task<InstrumentUploadResult> UploadAsync(string instrumentsJson, string listingsJson, UploadMode mode, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing tests** covering: round-trip save+list incl. `SubjectToVorabpauschale`; validation (ISIN non-empty, `Teilfreistellungsquote ∈ [0,1]`, non-empty `ProviderSymbol` per listing, unique `(Isin, Currency)`); delete-guard returns true when a `PortfolioEntryRow` references the ISIN; upload merge vs replace. Use an in-memory SQLite context (pattern from Task 8).

- [ ] **Step 4: Run — expect FAIL (class missing).**

- [ ] **Step 5: Implement `DbInstrumentReferenceAdmin`** — `ListAsync` joins profiles + listings; `SaveAsync` validates then upserts the `InstrumentProfileRow` and **replaces** that ISIN's `InstrumentListingRow`s transactionally; `IsReferencedByLedgerAsync` checks `PortfolioEntries` for the ISIN (instruments are referenced via `InstrumentRow.ISIN`/payload — match how the ledger stores ISIN); `DeleteAsync` removes profile + listings (caller is responsible for confirming when referenced); `UploadAsync` parses the two JSON shapes (instruments.json extended + listings.json), Merge upserts, Replace clears the tables first. Validate `Teilfreistellungsquote ∈ [0,1]` and throw `ArgumentException` with an actionable message on violations.

- [ ] **Step 6: Run — expect PASS.**

- [ ] **Step 7: Commit** `feat(referencedata): instrument reference admin (CRUD + upload, classification)`.

---

## Work unit 9 — Clear services (ledger + per-dataset)

### Task 26: `ILedgerClearService` + `IReferenceDataClearService`

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/Interface/ILedgerClearService.cs`
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IReferenceDataClearService.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbLedgerClearService.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbReferenceDataClearService.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ClearServiceTests.cs` (new)

- [ ] **Step 1: Define the interfaces**

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

public interface ILedgerClearService
{
    /// <summary>Transactionally deletes PortfolioEntries + ImportBatches + ImportDiagnostics + Accounts.
    /// When <paramref name="purgeRawAuditFiles"/> is true, also deletes raw files under data/app/audit (spec §10).</summary>
    Task ClearLedgerAsync(bool purgeRawAuditFiles, CancellationToken ct = default);
}
```

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

public enum ReferenceDataset { BasisInterestRates, HistoricalPrices, FxRates, InstrumentProfiles, InstrumentListings }

public interface IReferenceDataClearService
{
    Task ClearAsync(ReferenceDataset dataset, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write failing tests** — seed an in-memory DB with ledger + reference rows; assert `ClearLedgerAsync` empties ledger tables but leaves reference data intact; `ClearAsync(HistoricalPrices)` empties only that table; a forced failure mid-clear rolls back (wrap in a transaction).

- [ ] **Step 3: Run — expect FAIL.**

- [ ] **Step 4: Implement both services** using `BeginTransactionAsync` + `RemoveRange`/`ExecuteDelete`, committing on success. `DbLedgerClearService` also deletes audit files when requested (inject `IRawFileStore` or the audit dir path; reuse `FileSystemRawFileStore` if it exposes a purge, else delete files in the directory).

- [ ] **Step 5: Run — expect PASS.**

- [ ] **Step 6: Commit** `feat(referencedata): ledger + per-dataset clear services (transactional)`.

---

## Work unit 10 — Data Administration UI

### Task 27: Refresh orchestration helpers (DB-aware "as-of" + log)

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IDataRefreshLog.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/DbDataRefreshLog.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/DbDataRefreshLogTests.cs`

The admin page shows "last refreshed" per dataset (the `DataRefreshLog` table from Task 3).

- [ ] **Step 1: Interface**

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

public interface IDataRefreshLog
{
    Task<DateTimeOffset?> GetLastRefreshedAsync(string dataset, CancellationToken ct = default);
    Task RecordAsync(string dataset, DateTimeOffset whenUtc, string? note, CancellationToken ct = default);
}
```

- [ ] **Step 2: Failing test** → upsert + read back. **Step 3:** implement `DbDataRefreshLog` (upsert by `Dataset`). **Step 4:** PASS. **Step 5:** commit `feat(referencedata): data refresh log`.

> The refresh services (Tasks 16/18/20) should call `IDataRefreshLog.RecordAsync` on success — either inside the service (inject the log) or in the UI handler after a successful refresh. Pass "now" in from the caller (the Web layer supplies `TimeProvider.System.GetUtcNow()`); workflow/script code must not call `DateTimeOffset.Now` in the Application layer per existing conventions — inject `TimeProvider`.

### Task 28: `/data-admin` Blazor page + nav link

**Files:**
- Create: `src/WealthIQ.Web/Components/Pages/DataAdmin.razor`
- Modify: `src/WealthIQ.Web/Components/Layout/MainLayout.razor` (add nav button)

A single MudBlazor page, one collapsible `MudExpansionPanel` card per dataset, each showing status + actions (spec §11 table). Long-running refreshes run async with a progress indicator and a result summary reusing the Import page's diagnostic `MudTable` style.

- [ ] **Step 1: Add the nav link** in `MainLayout.razor`, after the Diagnostics button:

```razor
        <MudButton Href="/data-admin" Color="Color.Inherit">Daten</MudButton>
```

- [ ] **Step 2: Create `DataAdmin.razor`** with `@page "/data-admin"`, injecting the services registered in Task 30 (`HistoricalPriceRefreshService`, `FxRateRefreshService`, `BasisInterestRateRefreshService`, `IInstrumentReferenceAdmin`, `ILedgerClearService`, `IReferenceDataClearService`, `IReferenceDataSeeder`, `IDataRefreshLog`, `TimeProvider`). Cards (spec §11):

  - **Ledger:** status (entries/accounts/batches counts via the DbContext); button "Ledger leeren" with a `MudCheckBox` "Rohdateien löschen" → double-confirm dialog → `ILedgerClearService.ClearLedgerAsync`.
  - **Historical prices:** status (symbols, per-symbol date range, last refreshed); buttons Refresh (incremental → `RefreshAsync(asOf, forceFullReload:false)`), Force full reload (per symbol / all), Clear (`IReferenceDataClearService.ClearAsync(HistoricalPrices)`).
  - **FX rates (ECB):** status (currencies, date range, last refreshed); Refresh / Clear / Re-seed from file.
  - **Basiszins (BMF):** status (years + rates, last refreshed); Refresh from BMF, Manual add/edit (`MudNumericField` for year + rate), Clear, Re-seed.
  - **Instruments:** `MudTable` of `IInstrumentReferenceAdmin.ListAsync()` with inline edit/delete (incl. `SubjectToVorabpauschale` `MudSwitch`), Add, Upload (two file inputs for instruments.json + listings.json, `MudRadioGroup` merge/replace → `UploadAsync`).

  Each action sets a `_busy` flag, awaits the service, and renders a `DataRefreshResult` summary + diagnostic table (copy the `MudTable` block from `Import.razor:55-70`). After a successful refresh, call `IDataRefreshLog.RecordAsync(dataset, TimeProvider.GetUtcNow(), note)`.

  Re-seed actions call `IReferenceDataSeeder.SeedIfEmptyAsync(...)` after the relevant `ClearAsync` (so the table is empty and re-seeds), using the same `referenceDir` paths Program.cs uses (expose them via an injected options object — see Task 30).

- [ ] **Step 3: Build & smoke-run**

Run: `dotnet build WealthIQ.slnx`
Then use the `run` skill (or `dotnet run --project src/WealthIQ.Web`) and open `/data-admin`; confirm the page renders all five cards and the nav link works. (No automated UI test required; keep logic in the injected services, which are unit-tested.)

- [ ] **Step 4: Commit** `feat(web): Data Administration page + nav link`.

---

## Work unit 11 — DI wiring & configuration

### Task 29: Configuration options binding

**Files:**
- Modify: `src/WealthIQ.Web/appsettings.json`
- Modify: `src/WealthIQ.Web/Program.cs`

- [ ] **Step 1: Add config sections to `appsettings.json`**

```json
  "MarketData": {
    "BaseUrl": "https://query1.finance.yahoo.com/v8/finance/chart/",
    "UserAgent": "Mozilla/5.0",
    "InterRequestDelayMs": 500,
    "MaxRetries": 4,
    "InitialBackoffMs": 1000
  },
  "FxRates": {
    "HistoricalUrl": "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml",
    "UserAgent": "Mozilla/5.0",
    "SupportedCurrencies": [ "USD", "GBP", "CHF" ]
  },
  "Basiszins": {
    "UserAgent": "Mozilla/5.0",
    "UrlTemplate": "https://www.bundesfinanzministerium.de/Content/DE/Standardartikel/Themen/Steuern/Weitere_Steuerthemen/Abgeltungsteuer/basiszins.html"
  }
```

- [ ] **Step 2: Bind the options in `Program.cs`** (construct from config and register as singletons, matching how the providers take a plain options object):

```csharp
var marketDataOptions = builder.Configuration.GetSection("MarketData").Get<HistoricalPriceProviderOptions>() ?? new();
var fxOptions = builder.Configuration.GetSection("FxRates").Get<FxRateProviderOptions>() ?? new();
var basiszinsOptions = builder.Configuration.GetSection("Basiszins").Get<BasisInterestRateSourceOptions>() ?? new();
builder.Services.AddSingleton(marketDataOptions);
builder.Services.AddSingleton(fxOptions);
builder.Services.AddSingleton(basiszinsOptions);
```

- [ ] **Step 3: Build & commit** `feat(web): bind provider options from appsettings`.

### Task 30: Repoint and register all new services

**Files:**
- Modify: `src/WealthIQ.Web/Program.cs`

- [ ] **Step 1: Register `IHttpClientFactory` + the providers**

```csharp
builder.Services.AddHttpClient();
builder.Services.AddScoped<IHistoricalPriceProvider>(sp =>
    new YahooHistoricalPriceProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), marketDataOptions));
builder.Services.AddScoped<IFxRateProvider>(sp =>
    new EcbFxRateProvider(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), fxOptions));
builder.Services.AddScoped<IBasisInterestRateSource>(sp =>
    new BmfBasisInterestRateSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), basiszinsOptions));
```

- [ ] **Step 2: Repoint the stores/lookups; remove `IYearEndPriceProvider`**

Replace the reference-data block (Program.cs:59–64):

```csharp
builder.Services.AddScoped<IReferenceDataSeeder, ReferenceDataSeeder>();
builder.Services.AddScoped<IBasisInterestRateProvider, DbBasisInterestRateProvider>();
builder.Services.AddScoped<IInstrumentProfileEnricher, DbInstrumentProfileEnricher>();
builder.Services.AddScoped<IFxRateLookup, DbFxRateLookup>();
builder.Services.AddScoped<IHistoricalPriceLookup, DbHistoricalPriceLookup>();
builder.Services.AddScoped<IInstrumentMarketDataMap, DbInstrumentMarketDataMap>();
builder.Services.AddScoped<IInstrumentPriceProvider, DerivedInstrumentPriceProvider>();
```

(The `IYearEndPriceProvider` registration is gone.)

- [ ] **Step 3: Register stores, refresh services, admin, clear, log**

```csharp
builder.Services.AddScoped<IHistoricalPriceStore, DbHistoricalPriceStore>();
builder.Services.AddScoped<IFxRateStore, DbFxRateStore>();
builder.Services.AddScoped<IBasisInterestRateStore, DbBasisInterestRateStore>();
builder.Services.AddScoped<HistoricalPriceRefreshService>();
builder.Services.AddScoped<FxRateRefreshService>();
builder.Services.AddScoped<BasisInterestRateRefreshService>();
builder.Services.AddScoped<IInstrumentReferenceAdmin, DbInstrumentReferenceAdmin>();
builder.Services.AddScoped<ILedgerClearService, DbLedgerClearService>();
builder.Services.AddScoped<IReferenceDataClearService, DbReferenceDataClearService>();
builder.Services.AddScoped<IDataRefreshLog, DbDataRefreshLog>();
```

- [ ] **Step 4: Update the startup seed sources** (Program.cs:80–84) to the new `ReferenceDataSources` shape (Task 11): `basiszins.csv`, `historical_prices.csv`, `instruments.json`, `listings.json`, `fx_rates.csv`. Expose `referenceDir` to the UI re-seed actions via a small singleton options record (e.g. `record ReferenceDataPaths(string ReferenceDir)`), registered here.

- [ ] **Step 5: Build & run startup**

Run: `dotnet build WealthIQ.slnx` then start the app once (`dotnet run --project src/WealthIQ.Web`) so the migration applies and seeding runs against the new schema. Confirm no startup exception; delete `data/app/wealthiq.db` first if a stale schema blocks the migration (local DB is gitignored).

- [ ] **Step 6: Run full suite**

Run: `dotnet test WealthIQ.slnx`
Expected: all green.

- [ ] **Step 7: Commit** `feat(web): wire Phase 2 providers, stores, refresh/admin/clear services`.

---

## Work unit 12 — Retire Python scripts & file inputs

### Task 31: Remove the Python scripts as live inputs; keep seed CSV/JSON

**Files:**
- Delete: `scripts/download_price_history.py`, `scripts/download_fx_rates.py`
- Keep: `data/reference/*.csv` / `*.json` (bootstrap seed + CI fixtures)
- Possibly remove: `data/reference/market_data_mappings.json` if fully replaced by `listings.json` (confirm nothing references it)

- [ ] **Step 1: Confirm no code references the scripts or `market_data_mappings.json`**

Run a search (Grep) for `download_price_history`, `download_fx_rates`, `market_data_mappings`. The committed `historical_prices.csv` stays as the seed for `HistoricalPrices`.

- [ ] **Step 2: Delete the two Python scripts.** If `market_data_mappings.json` is unreferenced (it was only read by the Python script and the old `JsonInstrumentMarketDataMap`), delete it; otherwise convert+remove. `listings.json` (Task 11) is its replacement.

- [ ] **Step 3: Build & test**

Run: `dotnet test WealthIQ.slnx`
Expected: green (CI reads only committed `data/reference` + `data/test`).

- [ ] **Step 4: Commit** `chore: retire Python market-data scripts; native C# refresh is authoritative`.

---

## Work unit 13 — Documentation

### Task 32: Update `CLAUDE.md`

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the tax-pipeline guardrails** so they describe: uniform **year-start rebasing**, **distributions-in-cap**, **1/12-on-final-Vorabpauschale**, **fund-gating** (`SubjectToVorabpauschale`), and **fail-fast** on missing Basiszins/classification. Replace the old Vorabpauschale bullet (`Basiszins × 0.7 pro-rata months, capped at actual appreciation, minus same-year distributions`) with the corrected description.

- [ ] **Step 2: Update "Known thin spots"** — remove the resolved multi-year Vorabpauschale item; remove the "30% default" note (the Teilfreistellung default is gone); keep the "held beyond last ledger entry" as-of/through-year item (still out of scope).

- [ ] **Step 3: Update data-layer descriptions** — `HistoricalPrice`/`InstrumentListing`/`DataRefreshLog` tables; `YearEndPrice` removed; reference data now refreshable from the internet (Yahoo/ECB/BMF) via the Data Administration page; seed CSV/JSON remain bootstrap + CI fixtures.

- [ ] **Step 4: Use the `claude-md-management:revise-claude-md` skill** for the edit if available, to keep the file's house style.

- [ ] **Step 5: Commit** `docs: update CLAUDE.md for Phase 2 (corrected Vorabpauschale + data administration)`.

---

## Final verification (before finishing the branch)

- [ ] `dotnet format WealthIQ.slnx --verify-no-changes` (run `dotnet format` to fix).
- [ ] `dotnet build WealthIQ.slnx --configuration Release` (CI builds Release).
- [ ] `dotnet test WealthIQ.slnx --configuration Release --no-build` (mirrors CI).
- [ ] Manually open `/data-admin`, run each refresh once against the live internet, confirm DB-only writes + sensible diagnostics.
- [ ] Confirm `GermanTaxRegressionTests` reflects the deliberately-updated baseline with worked-arithmetic comments.
- [ ] Use **superpowers:finishing-a-development-branch** to decide merge/PR.

---

## Spec coverage map (self-review)

| Spec section / requirement | Task(s) |
|---|---|
| §4 `HistoricalPrice` table | 1, 5 |
| §4 `InstrumentListing` table | 2, 5 |
| §4 `DataRefreshLog` table | 3, 27 |
| §4 `InstrumentProfile` +Type/+SubjectToVorabpauschale; drop YearEndPrice | 4, 5, 11, 21 |
| §5.1 Yahoo provider | 15 |
| §5.2 ECB provider | 17 |
| §5.3 BMF source + nullable Basiszins | 6, 19 |
| §5.4 EarliestOnOrAfter; DbHistoricalPriceLookup; DbInstrumentMarketDataMap; DerivedInstrumentPriceProvider | 7, 8, 9, 10 |
| §6.2 corrected algorithm | 21, 22 |
| §6.4 fail-fast ordering/edge cases | 6, 21, 22, 23 |
| §7 Stage A behavior-preserving checkpoint | 12, 13 |
| §8 regression baseline recompute | 24 |
| §9 instrument reference admin | 25 |
| §10 clearing & reload | 26, 28 |
| §11 Data Administration UI | 27, 28 |
| §12 DI / composition | 29, 30 |
| §13 testing (targeted + provider + refresh + admin + clearing) | 8, 9, 10, 11, 15, 16, 17, 18, 19, 20, 23, 24, 25, 26 |
| §14 work-unit ordering | Tasks grouped 1→32 in spec order |
| Retire Python scripts | 31 |
| Update CLAUDE.md | 32 |

> Two spec items deliberately remain **out of scope** (spec §1 Non-Goals): the "held beyond last ledger entry" as-of/through-year parameter, and everything in the v1 non-goals list.









# WealthIQ Foundation & Persistence Implementation Plan (Plan 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the target solution structure and a SQLite-backed persistence layer that can save and load a canonical `PortfolioLedger` (entries + instruments + accounts) idempotently, verified by integration tests, while all existing domain/application tests stay green.

**Architecture:** Layered / Clean Architecture with strict inward dependencies (`Domain ← Application ← Infrastructure`, composition only in the host). The Application layer defines a persistence **port** (`ILedgerStore`); the Infrastructure layer implements it with EF Core + SQLite. The Domain stays pure: persistence uses dedicated row entities + mappers, and each ledger entry is stored as common queryable columns **plus** a JSON payload of the concrete entry (no EF/JSON attributes on Domain types).

**Tech Stack:** C# / .NET 10, EF Core 10 (Microsoft.EntityFrameworkCore.Sqlite), System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-29-wealthiq-neustart-design.md` (§4 architecture, §6 persistence, §8 fail-fast).

---

## Context for the implementer

The repository already contains a working solution under `src/` and `tests/`:

- `src/WealthIQ.Domain` — pure domain: value objects (`Money`, `Quantity`, typed Guid IDs), the canonical ledger (`PortfolioLedger`, abstract `PortfolioEntry` with `TradeEntry`/`CashEntry`/`PositionAdjustmentEntry`/`AssetTransferEntry`), `SourceProvenance`, lots, tax result types, enums.
- `src/WealthIQ.Application` — FIFO matcher, `GermanTaxCalculator`, FX conversion, import contracts, valuation, reference-data ports.
- `src/WealthIQ.Infrastructure.IBKR` — IBKR XML importer + file-based reference-data adapters.
- `src/WealthIQ.Cli` — console host (to be retired).
- `tests/WealthIQ.Tests` — domain, application, and one end-to-end regression test.

The solution file is `WealthIQ.slnx` (XML-based solution). All projects target `net10.0` with `Nullable` and `ImplicitUsings` enabled.

**Relevant existing type signatures** (do not redefine — reference them):

```csharp
// Domain
public abstract record PortfolioEntry {
    public PortfolioEntryId EntryId { get; }
    public AccountId AccountId { get; }
    public DateTimeOffset OccurredAt { get; }
    public DateOnly EffectiveDate { get; }
    public PortfolioEntryCategory Category { get; }   // Trade, Cash, PositionAdjustment, AssetTransfer
    public SourceProvenance SourceProvenance { get; }
}
public sealed record TradeEntry(PortfolioEntryId entryId, AccountId accountId, DateTimeOffset occurredAt, DateOnly effectiveDate, SourceProvenance sourceProvenance, InstrumentId instrumentId, TradeSide side, Quantity quantity, Money unitPrice, Money fees, Money taxes) : PortfolioEntry;
public sealed record CashEntry(PortfolioEntryId entryId, AccountId accountId, DateTimeOffset occurredAt, DateOnly effectiveDate, SourceProvenance sourceProvenance, InstrumentId cashInstrumentId, CashFlowType cashFlowType, Money grossAmount, Money fees, Money taxes, InstrumentId? relatedInstrumentId = null) : PortfolioEntry;
public sealed record PositionAdjustmentEntry(/* ... */) : PortfolioEntry;
public sealed record AssetTransferEntry(/* ... */) : PortfolioEntry;

public sealed record PortfolioLedger {
    public PortfolioLedger(IReadOnlyList<PortfolioEntry> entries, IReadOnlyList<Instrument>? instruments = null, IReadOnlyList<Account>? accounts = null);
    public IReadOnlyList<PortfolioEntry> Entries { get; }       // sorted by OccurredAt in ctor
    public IReadOnlyList<Instrument> Instruments { get; }
    public IReadOnlyList<Account> Accounts { get; }
}
public sealed record SourceProvenance {
    public required string SourceSystem { get; init; }
    public required string ImportFormat { get; init; }
    public required string SourceLocation { get; init; }
    public required string SourceRecordReference { get; init; }
    public string? SourceSection { get; init; }
    public string? SourceLineReference { get; init; }
}
public sealed record Instrument(InstrumentId InstrumentId, string ISIN, string Symbol, string Name, decimal Teilfreistellungsquote);
public sealed record Account(AccountId AccountId, string AccountNumber);
public readonly record struct InstrumentId(Guid Value);   // also AccountId, PortfolioEntryId
public readonly record struct Money(decimal Amount, Currency Currency);
public readonly record struct Quantity(decimal Value);
public enum Currency { USD, EUR, CHF, GBP, JPY, AUD, CAD, NZD, SEK, NOK, DKK, ZAR, HKD, SGD, CNY, INR }
```

**Verification commands** (run from repository root `E:\05 Projects\CSharp\WealthIq`):

- Build: `dotnet build "WealthIQ.slnx"`
- All tests: `dotnet test "WealthIQ.slnx"`
- Single test: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~<Namespace.Class.Method>"`

---

## File Structure (created/modified in this plan)

```
src/WealthIQ.Application/
  Persistence/Interface/ILedgerStore.cs          (new) — persistence port
  Persistence/LedgerSaveResult.cs                (new) — result record

src/WealthIQ.Infrastructure/                      (renamed from WealthIQ.Infrastructure.IBKR)
  WealthIQ.Infrastructure.csproj                  (renamed) — + EF Core Sqlite package
  Ibkr/                                           (existing files moved here, namespace -> .Ibkr)
  Persistence/
    LedgerJson.cs                                 (new) — shared JsonSerializerOptions
    WealthIqDbContext.cs                          (new) — EF Core context
    Rows/PortfolioEntryRow.cs                     (new) — entry table row
    Rows/InstrumentRow.cs                         (new) — instrument table row
    Rows/AccountRow.cs                            (new) — account table row
    Mapping/PortfolioEntryMapper.cs              (new) — domain <-> row
    Mapping/InstrumentMapper.cs                   (new)
    Mapping/AccountMapper.cs                       (new)
    SqliteLedgerStore.cs                          (new) — ILedgerStore implementation

tests/WealthIQ.Tests/
  Infrastructure/Persistence/PortfolioEntryMapperTests.cs   (new) — round-trip unit tests
  Infrastructure/Persistence/SqliteLedgerStoreTests.cs      (new) — integration tests
  Infrastructure/Persistence/InMemorySqlite.cs              (new) — test DB helper

REMOVED: src/WealthIQ.Cli/  (whole project)
```

---

## Task 1: Retire the CLI project

The CLI is no longer the host (spec §4). Removing it first shrinks the rename surface in Task 2. The code remains in git history.

**Files:**
- Delete: `src/WealthIQ.Cli/` (whole directory)
- Modify: `WealthIQ.slnx` (remove the Cli project entry)

- [ ] **Step 1: Remove the CLI project from the solution file**

Open `WealthIQ.slnx` and delete this line inside `<Folder Name="/src/">`:

```xml
<Project Path="src/WealthIQ.Cli/WealthIQ.Cli.csproj" />
```

- [ ] **Step 2: Delete the CLI project directory**

Run: `rm -rf "src/WealthIQ.Cli"`

- [ ] **Step 3: Build and test to confirm nothing else depended on it**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded. (No project references `WealthIQ.Cli`; Tests reference Application/Domain/Infrastructure only.)

Run: `dotnet test "WealthIQ.slnx"`
Expected: All existing tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: retire WealthIQ.Cli host (replaced by Blazor web app per spec)"
```

---

## Task 2: Rename `WealthIQ.Infrastructure.IBKR` to `WealthIQ.Infrastructure`

The spec (§4) uses a single `WealthIQ.Infrastructure` project. Keep IBKR code under an `Ibkr/` folder with namespace suffix `.Ibkr`, making room for `Persistence/`.

**Files:**
- Rename dir: `src/WealthIQ.Infrastructure.IBKR/` → `src/WealthIQ.Infrastructure/`
- Rename file: `WealthIQ.Infrastructure.IBKR.csproj` → `WealthIQ.Infrastructure.csproj`
- Move the 7 existing `.cs` files into an `Ibkr/` subfolder
- Modify: `WealthIQ.slnx`, `tests/WealthIQ.Tests/WealthIQ.Tests.csproj`, and all files that reference the old namespace

- [ ] **Step 1: Rename the project directory and move existing code under `Ibkr/`**

```bash
git mv "src/WealthIQ.Infrastructure.IBKR" "src/WealthIQ.Infrastructure"
git mv "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.IBKR.csproj" "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj"
mkdir -p "src/WealthIQ.Infrastructure/Ibkr"
git mv "src/WealthIQ.Infrastructure/Currency"   "src/WealthIQ.Infrastructure/Ibkr/Currency"
git mv "src/WealthIQ.Infrastructure/Import"     "src/WealthIQ.Infrastructure/Ibkr/Import"
git mv "src/WealthIQ.Infrastructure/MarketData" "src/WealthIQ.Infrastructure/Ibkr/MarketData"
git mv "src/WealthIQ.Infrastructure/Tax"        "src/WealthIQ.Infrastructure/Ibkr/Tax"
```

- [ ] **Step 2: Update the namespace in the 7 moved files**

In every `.cs` file under `src/WealthIQ.Infrastructure/Ibkr/`, change the namespace declaration prefix from `WealthIQ.Infrastructure.IBKR` to `WealthIQ.Infrastructure.Ibkr`. For example in `Ibkr/Import/IbkrStatementImporter.cs`:

```csharp
// before
namespace WealthIQ.Infrastructure.IBKR.Import;
// after
namespace WealthIQ.Infrastructure.Ibkr.Import;
```

Apply the same `WealthIQ.Infrastructure.IBKR` → `WealthIQ.Infrastructure.Ibkr` change to: `Ibkr/Currency/CsvFxRateLookup.cs`, `Ibkr/Import/IbkrStatementImporter.cs`, `Ibkr/MarketData/CsvHistoricalPriceLookup.cs`, `Ibkr/MarketData/JsonInstrumentMarketDataMap.cs`, `Ibkr/Tax/CsvBasisInterestRateProvider.cs`, `Ibkr/Tax/CsvYearEndPriceProvider.cs`, `Ibkr/Tax/JsonInstrumentProfileEnricher.cs`.

- [ ] **Step 3: Update the assembly/root namespace in the csproj (if set explicitly)**

Open `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj`. If it contains `<RootNamespace>` or `<AssemblyName>` referencing the old name, update to `WealthIQ.Infrastructure`. If absent, no change needed (defaults to file name = `WealthIQ.Infrastructure`).

- [ ] **Step 4: Update the solution file**

In `WealthIQ.slnx`, change the Infrastructure project entry to:

```xml
<Project Path="src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj" />
```

- [ ] **Step 5: Update the test project reference and usings**

In `tests/WealthIQ.Tests/WealthIQ.Tests.csproj`, update the ProjectReference path:

```xml
<ProjectReference Include="..\..\src\WealthIQ.Infrastructure\WealthIQ.Infrastructure.csproj" />
```

Then in any test file that has `using WealthIQ.Infrastructure.IBKR...;` (notably `tests/WealthIQ.Tests/Application/Tax/GermanTaxRegressionTests.cs`), change the using prefix to `using WealthIQ.Infrastructure.Ibkr...;`.

- [ ] **Step 6: Build and test**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass (same count as before).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: rename Infrastructure.IBKR to Infrastructure with Ibkr subfolder"
```

---

## Task 3: Add EF Core + SQLite to the Infrastructure project

**Files:**
- Modify: `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj`

- [ ] **Step 1: Add the EF Core SQLite package reference**

In `src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj`, add an `<ItemGroup>` with:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
</ItemGroup>
```

(If `10.0.0` is unavailable at restore time, use the latest `10.*` stable shown by `dotnet add package Microsoft.EntityFrameworkCore.Sqlite` and pin it.)

- [ ] **Step 2: Restore and build**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded, EF Core packages restored.

- [ ] **Step 3: Commit**

```bash
git add src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj
git commit -m "chore: add EF Core Sqlite to Infrastructure"
```

---

## Task 4: Define the persistence port in Application

The port lives in Application so Domain/Application never depend on Infrastructure.

**Files:**
- Create: `src/WealthIQ.Application/Persistence/LedgerSaveResult.cs`
- Create: `src/WealthIQ.Application/Persistence/Interface/ILedgerStore.cs`

- [ ] **Step 1: Create the result record**

Create `src/WealthIQ.Application/Persistence/LedgerSaveResult.cs`:

```csharp
namespace WealthIQ.Application.Persistence;

/// <summary>Outcome of an idempotent ledger save.</summary>
public sealed record LedgerSaveResult(int InsertedEntries, int SkippedDuplicateEntries);
```

- [ ] **Step 2: Create the port interface**

Create `src/WealthIQ.Application/Persistence/Interface/ILedgerStore.cs`:

```csharp
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Stores and loads the canonical portfolio ledger.
/// Saving is idempotent on (SourceSystem, SourceRecordReference) so re-importing
/// overlapping statements never duplicates entries.
/// </summary>
public interface ILedgerStore
{
    Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger ledger, CancellationToken ct = default);

    Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default);
}
```

(If the `PortfolioLedger` namespace differs, confirm with `grep -r "namespace WealthIQ.Domain" src/WealthIQ.Domain/Model/Ledger/PortfolioLedger.cs` and adjust the `using`.)

- [ ] **Step 3: Build**

Run: `dotnet build "src/WealthIQ.Application/WealthIQ.Application.csproj"`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Application/Persistence
git commit -m "feat: add ILedgerStore persistence port"
```

---

## Task 5: Persistence rows, DbContext, and JSON options

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/LedgerJson.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/PortfolioEntryRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/AccountRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`

- [ ] **Step 1: Create shared JSON options**

Create `src/WealthIQ.Infrastructure/Persistence/LedgerJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Shared System.Text.Json options for serializing concrete ledger entries.
/// Enums (e.g. Currency, TradeSide) are stored as strings for readability.
/// </summary>
internal static class LedgerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
```

- [ ] **Step 2: Create the row types**

Create `src/WealthIQ.Infrastructure/Persistence/Rows/PortfolioEntryRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>
/// One canonical ledger entry. Common columns are queryable/dedup-able;
/// the full concrete entry is preserved in <see cref="PayloadJson"/>.
/// </summary>
public sealed class PortfolioEntryRow
{
    public Guid EntryId { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Category { get; set; } = "";

    // Idempotency key (from SourceProvenance)
    public string SourceSystem { get; set; } = "";
    public string SourceRecordReference { get; set; } = "";

    // Full concrete entry serialized as JSON
    public string PayloadJson { get; set; } = "";
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentRow
{
    public Guid InstrumentId { get; set; }
    public string ISIN { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/AccountRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class AccountRow
{
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = "";
}
```

- [ ] **Step 3: Create the DbContext**

Create `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence;

public sealed class WealthIqDbContext(DbContextOptions<WealthIqDbContext> options) : DbContext(options)
{
    public DbSet<PortfolioEntryRow> PortfolioEntries => Set<PortfolioEntryRow>();
    public DbSet<InstrumentRow> Instruments => Set<InstrumentRow>();
    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioEntryRow>(e =>
        {
            e.HasKey(x => x.EntryId);
            e.HasIndex(x => new { x.SourceSystem, x.SourceRecordReference });
            e.Property(x => x.Category).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<InstrumentRow>(e =>
        {
            e.HasKey(x => x.InstrumentId);
        });

        modelBuilder.Entity<AccountRow>(e =>
        {
            e.HasKey(x => x.AccountId);
        });
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj"`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence
git commit -m "feat: add EF Core rows and WealthIqDbContext"
```

---

## Task 6: Mappers (domain <-> row) with round-trip tests (TDD)

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/PortfolioEntryMapperTests.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Mapping/PortfolioEntryMapper.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Mapping/InstrumentMapper.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Mapping/AccountMapper.cs`

- [ ] **Step 1: Write the failing round-trip test for a TradeEntry**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/PortfolioEntryMapperTests.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Mapping;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class PortfolioEntryMapperTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "file.xml",
        SourceRecordReference = reference
    };

    [Fact]
    public void ToRow_ToDomain_TradeEntry_RoundTrips()
    {
        var original = new TradeEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            new DateTimeOffset(2024, 3, 1, 14, 30, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1),
            Provenance("TR-1"),
            InstrumentId.NewId(),
            TradeSide.Buy,
            new Quantity(10m),
            new Money(100.50m, Currency.USD),
            new Money(1.25m, Currency.USD),
            new Money(0m, Currency.USD));

        var row = PortfolioEntryMapper.ToRow(original);
        var restored = PortfolioEntryMapper.ToDomain(row);

        Assert.Equal(original, restored);
        Assert.Equal("Trade", row.Category);
        Assert.Equal("IBKR", row.SourceSystem);
        Assert.Equal("TR-1", row.SourceRecordReference);
    }

    [Fact]
    public void ToRow_ToDomain_CashEntry_RoundTrips()
    {
        var original = new CashEntry(
            PortfolioEntryId.NewId(),
            AccountId.NewId(),
            new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 6, 15),
            Provenance("CT-9"),
            InstrumentId.NewId(),
            CashFlowType.Dividend,
            new Money(42m, Currency.USD),
            new Money(0m, Currency.USD),
            new Money(6.30m, Currency.USD));

        var row = PortfolioEntryMapper.ToRow(original);
        var restored = PortfolioEntryMapper.ToDomain(row);

        Assert.Equal(original, restored);
        Assert.Equal("Cash", row.Category);
    }
}
```

(Confirm the exact `using` namespaces against the real files, e.g. `grep -rn "namespace WealthIQ.Domain" src/WealthIQ.Domain/Model/General/Money.cs`. Adjust if needed.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~PortfolioEntryMapperTests"`
Expected: FAIL — `PortfolioEntryMapper` does not exist (compile error).

- [ ] **Step 3: Implement the entry mapper**

Create `src/WealthIQ.Infrastructure/Persistence/Mapping/PortfolioEntryMapper.cs`:

```csharp
using System.Text.Json;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class PortfolioEntryMapper
{
    public static PortfolioEntryRow ToRow(PortfolioEntry entry)
    {
        string payload = entry switch
        {
            TradeEntry t => JsonSerializer.Serialize(t, LedgerJson.Options),
            CashEntry c => JsonSerializer.Serialize(c, LedgerJson.Options),
            PositionAdjustmentEntry p => JsonSerializer.Serialize(p, LedgerJson.Options),
            AssetTransferEntry a => JsonSerializer.Serialize(a, LedgerJson.Options),
            _ => throw new NotSupportedException($"Unknown entry type {entry.GetType().Name}")
        };

        return new PortfolioEntryRow
        {
            EntryId = entry.EntryId.Value,
            AccountId = entry.AccountId.Value,
            OccurredAt = entry.OccurredAt,
            EffectiveDate = entry.EffectiveDate,
            Category = entry.Category.ToString(),
            SourceSystem = entry.SourceProvenance.SourceSystem,
            SourceRecordReference = entry.SourceProvenance.SourceRecordReference,
            PayloadJson = payload
        };
    }

    public static PortfolioEntry ToDomain(PortfolioEntryRow row)
    {
        var category = Enum.Parse<PortfolioEntryCategory>(row.Category);
        return category switch
        {
            PortfolioEntryCategory.Trade =>
                JsonSerializer.Deserialize<TradeEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.Cash =>
                JsonSerializer.Deserialize<CashEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.PositionAdjustment =>
                JsonSerializer.Deserialize<PositionAdjustmentEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.AssetTransfer =>
                JsonSerializer.Deserialize<AssetTransferEntry>(row.PayloadJson, LedgerJson.Options)!,
            _ => throw new NotSupportedException($"Unknown category {row.Category}")
        };
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~PortfolioEntryMapperTests"`
Expected: PASS (both round-trip tests).

> If a test fails on equality because a `Money`/typed-id property does not round-trip, inspect the serialized JSON (`Console.WriteLine(row.PayloadJson)`); the most likely cause is a value object lacking a public constructor/property name match. Fix by confirming the domain type is a positional record with public members (it is, per the signatures above).

- [ ] **Step 5: Implement the instrument and account mappers**

Create `src/WealthIQ.Infrastructure/Persistence/Mapping/InstrumentMapper.cs`:

```csharp
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class InstrumentMapper
{
    public static InstrumentRow ToRow(Instrument instrument) => new()
    {
        InstrumentId = instrument.InstrumentId.Value,
        ISIN = instrument.ISIN,
        Symbol = instrument.Symbol,
        Name = instrument.Name,
        Teilfreistellungsquote = instrument.Teilfreistellungsquote
    };

    public static Instrument ToDomain(InstrumentRow row) => new(
        new InstrumentId(row.InstrumentId),
        row.ISIN,
        row.Symbol,
        row.Name,
        row.Teilfreistellungsquote);
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Mapping/AccountMapper.cs`:

```csharp
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class AccountMapper
{
    public static AccountRow ToRow(Account account) => new()
    {
        AccountId = account.AccountId.Value,
        AccountNumber = account.AccountNumber
    };

    public static Account ToDomain(AccountRow row) =>
        new(new AccountId(row.AccountId), row.AccountNumber);
}
```

- [ ] **Step 6: Build and run the full test suite**

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass (existing + 2 new mapper tests).

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/Mapping tests/WealthIQ.Tests/Infrastructure
git commit -m "feat: add ledger persistence mappers with round-trip tests"
```

---

## Task 7: SqliteLedgerStore with save/load integration test (TDD)

**Files:**
- Test helper: `tests/WealthIQ.Tests/Infrastructure/Persistence/InMemorySqlite.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs`

- [ ] **Step 1: Create the in-memory SQLite test helper**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/InMemorySqlite.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Tests.Infrastructure.Persistence;

/// <summary>
/// Creates a WealthIqDbContext backed by a private in-memory SQLite database.
/// The open connection must be kept alive for the DB to persist between contexts,
/// so the helper is disposable and owns the connection.
/// </summary>
public sealed class InMemorySqlite : IDisposable
{
    private readonly SqliteConnection _connection;

    public InMemorySqlite()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public WealthIqDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WealthIqDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 2: Write the failing save/load integration test**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs`:

```csharp
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class SqliteLedgerStoreTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "file.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(AccountId account, InstrumentId instrument, string reference, int day) =>
        new(PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 3, day, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, day), Provenance(reference), instrument,
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    [Fact]
    public async Task SaveLedger_ThenLoad_ReturnsSameEntriesInstrumentsAccounts()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);
        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1", 1) },
            new[] { instrument },
            new[] { account });

        LedgerSaveResult result;
        await using (var ctx = db.NewContext())
        {
            var store = new SqliteLedgerStore(ctx);
            result = await store.SaveLedgerAsync(ledger);
        }

        Assert.Equal(1, result.InsertedEntries);
        Assert.Equal(0, result.SkippedDuplicateEntries);

        PortfolioLedger loaded;
        await using (var ctx = db.NewContext())
        {
            var store = new SqliteLedgerStore(ctx);
            loaded = await store.LoadLedgerAsync();
        }

        Assert.Single(loaded.Entries);
        Assert.Equal(ledger.Entries[0], loaded.Entries[0]);
        Assert.Single(loaded.Instruments);
        Assert.Equal(instrument, loaded.Instruments[0]);
        Assert.Single(loaded.Accounts);
        Assert.Equal(account, loaded.Accounts[0]);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteLedgerStoreTests"`
Expected: FAIL — `SqliteLedgerStore` does not exist (compile error).

- [ ] **Step 4: Implement SqliteLedgerStore**

Create `src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Mapping;

namespace WealthIQ.Infrastructure.Persistence;

public sealed class SqliteLedgerStore(WealthIqDbContext db) : ILedgerStore
{
    public async Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger ledger, CancellationToken ct = default)
    {
        int inserted = 0, skipped = 0;

        foreach (var entry in ledger.Entries)
        {
            var system = entry.SourceProvenance.SourceSystem;
            var reference = entry.SourceProvenance.SourceRecordReference;

            bool exists = await db.PortfolioEntries
                .AnyAsync(r => r.SourceSystem == system && r.SourceRecordReference == reference, ct);

            if (exists) { skipped++; continue; }

            db.PortfolioEntries.Add(PortfolioEntryMapper.ToRow(entry));
            inserted++;
        }

        foreach (var instrument in ledger.Instruments)
        {
            var existing = await db.Instruments.FindAsync([instrument.InstrumentId.Value], ct);
            if (existing is null)
            {
                db.Instruments.Add(InstrumentMapper.ToRow(instrument));
            }
            else
            {
                existing.ISIN = instrument.ISIN;
                existing.Symbol = instrument.Symbol;
                existing.Name = instrument.Name;
                existing.Teilfreistellungsquote = instrument.Teilfreistellungsquote;
            }
        }

        foreach (var account in ledger.Accounts)
        {
            var existing = await db.Accounts.FindAsync([account.AccountId.Value], ct);
            if (existing is null)
            {
                db.Accounts.Add(AccountMapper.ToRow(account));
            }
            else
            {
                existing.AccountNumber = account.AccountNumber;
            }
        }

        await db.SaveChangesAsync(ct);
        return new LedgerSaveResult(inserted, skipped);
    }

    public async Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default)
    {
        var entryRows = await db.PortfolioEntries.AsNoTracking().ToListAsync(ct);
        var instrumentRows = await db.Instruments.AsNoTracking().ToListAsync(ct);
        var accountRows = await db.Accounts.AsNoTracking().ToListAsync(ct);

        var entries = entryRows.Select(PortfolioEntryMapper.ToDomain).ToList();
        var instruments = instrumentRows.Select(InstrumentMapper.ToDomain).ToList();
        var accounts = accountRows.Select(AccountMapper.ToDomain).ToList();

        return new PortfolioLedger(entries, instruments, accounts);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteLedgerStoreTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/SqliteLedgerStore.cs tests/WealthIQ.Tests/Infrastructure/Persistence
git commit -m "feat: add SqliteLedgerStore with save/load integration test"
```

---

## Task 8: Idempotency integration test

Proves re-saving the same source records does not duplicate entries (spec §6: dedup over transaction reference).

**Files:**
- Modify: `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs`

- [ ] **Step 1: Add the failing idempotency test**

Append this method to the `SqliteLedgerStoreTests` class:

```csharp
    [Fact]
    public async Task SaveLedger_SameSourceReferences_SkipsDuplicatesOnReSave()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);

        PortfolioLedger BuildLedger() => new(
            new PortfolioEntry[]
            {
                Trade(account.AccountId, instrument.InstrumentId, "T-1", 1),
                Trade(account.AccountId, instrument.InstrumentId, "T-2", 2)
            },
            new[] { instrument },
            new[] { account });

        await using (var ctx = db.NewContext())
        {
            var first = await new SqliteLedgerStore(ctx).SaveLedgerAsync(BuildLedger());
            Assert.Equal(2, first.InsertedEntries);
            Assert.Equal(0, first.SkippedDuplicateEntries);
        }

        // Re-save a ledger that overlaps (T-1, T-2 again) plus a new T-3.
        await using (var ctx = db.NewContext())
        {
            var overlapping = new PortfolioLedger(
                new PortfolioEntry[]
                {
                    Trade(account.AccountId, instrument.InstrumentId, "T-1", 1),
                    Trade(account.AccountId, instrument.InstrumentId, "T-2", 2),
                    Trade(account.AccountId, instrument.InstrumentId, "T-3", 3)
                },
                new[] { instrument },
                new[] { account });

            var second = await new SqliteLedgerStore(ctx).SaveLedgerAsync(overlapping);
            Assert.Equal(1, second.InsertedEntries);
            Assert.Equal(2, second.SkippedDuplicateEntries);
        }

        await using (var ctx = db.NewContext())
        {
            var loaded = await new SqliteLedgerStore(ctx).LoadLedgerAsync();
            Assert.Equal(3, loaded.Entries.Count);
        }
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteLedgerStoreTests.SaveLedger_SameSourceReferences_SkipsDuplicatesOnReSave"`
Expected: PASS (the dedup logic from Task 7 already covers this).

> If it FAILS with 2 inserted on the second save, the dedup query is wrong — confirm `SaveChangesAsync` ran in the first context and the `AnyAsync` filter uses both `SourceSystem` and `SourceRecordReference`.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteLedgerStoreTests.cs
git commit -m "test: prove idempotent ledger save dedups on source reference"
```

---

## Done criteria for Plan 1

- `WealthIQ.Cli` removed; solution builds without it.
- Infrastructure project renamed to `WealthIQ.Infrastructure`, IBKR code under `Ibkr/`.
- EF Core + SQLite wired into Infrastructure.
- `ILedgerStore` port in Application; `SqliteLedgerStore` in Infrastructure.
- A `PortfolioLedger` can be saved to and loaded from SQLite with full round-trip fidelity.
- Re-saving overlapping source records is idempotent (dedup on `SourceSystem` + `SourceRecordReference`).
- `dotnet test "WealthIQ.slnx"` is green.

## Notes for later plans (not in scope here)

- **Migrations:** Plan 1 uses `EnsureCreated()` in tests. When the Web host arrives (Plan 3), add EF Core migrations (`Microsoft.EntityFrameworkCore.Design` + an initial migration) and apply on startup. The production DB path/config also belongs to Plan 3.
- **Diagnostics persistence** and the **import→persist pipeline** are Plan 2.
- **Reference-data tables + seeding** into SQLite are Plan 2.
- **Tax replay from DB + Blazor dashboard** are Plan 3.
```

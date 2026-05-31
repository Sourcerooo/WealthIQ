# WealthIQ Import→Persist Pipeline & Reference-Data Seeding Implementation Plan (Plan 2 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the raw-statement-to-database flow into a single, transactional, fail-fast use-case — ingest the raw broker file to an audit folder, import it to canonical entries, persist entries + instruments + accounts + diagnostics + an import-batch record idempotently in one transaction — and seed the reference-data tables (Basiszins, year-end prices, instrument profiles, FX rates) into SQLite. All existing tests stay green.

**Architecture:** The Application layer gains an import-pipeline use-case (`StatementImportPipeline`) and three new ports (`IRawFileStore`, `IImportStore`, `IReferenceDataSeeder`). The Infrastructure layer implements them on EF Core + SQLite, reusing the Plan 1 mappers and `ILedgerStore`. Persistence is one EF transaction per batch: a blocking diagnostic (`Severity >= Error`) aborts before any write (spec §8); only successful imports are committed. Reference-data seeding reads the shipped CSV/JSON files and inserts rows, seed-if-empty (idempotent). Replay/compute that *reads* reference data and the Blazor host remain Plan 3.

**Tech Stack:** C# / .NET 10, EF Core 10 (Microsoft.EntityFrameworkCore.Sqlite), System.Text.Json, `TimeProvider`, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-29-wealthiq-neustart-design.md` (§6 pipeline & persistence, §8 fail-fast, §10 testing). **Predecessor:** `docs/superpowers/plans/2026-05-29-wealthiq-foundation-persistence.md` (Plan 1 — done).

---

## Context for the implementer

Plan 1 is complete. The solution (`WealthIQ.slnx`) has four projects:

- `src/WealthIQ.Domain` — pure domain (ledger, value objects, typed ids).
- `src/WealthIQ.Application` — ports + use-cases (import contracts, FIFO, tax calc, FX, **persistence port `ILedgerStore`**).
- `src/WealthIQ.Infrastructure` — IBKR importer under `Ibkr/`, EF Core + SQLite persistence under `Persistence/`.
- `tests/WealthIQ.Tests` — xUnit.

All projects target `net10.0` with `Nullable` and `ImplicitUsings` enabled. **Only `Infrastructure` references EF Core**; Domain/Application stay persistence-free (ports only).

### Existing types you will wire together (do NOT redefine — reference them)

```csharp
// --- Application: import contracts (WealthIQ.Application.Import / .Diagnostic / .Enumeration) ---
public interface IStatementImporter            // WealthIQ.Application.Import.Interface
{
    bool CanImport(ImportSource source);
    Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct);
}
public sealed record ImportRequest { public required ImportSource Source { get; init; } public required AccountId AccountId { get; init; } }
public sealed record class ImportSource(Broker Broker, Format Format, string FilePath);
public class ImportResult
{
    public PortfolioLedger PortfolioLedger { get; set; } = new([]);
    public List<Instrument> Instruments { get; set; } = new();
    public List<ImportDiagnostic> Diagnostics { get; set; } = new();
}
public sealed record ImportDiagnostic(ImportDiagnosticSeverity Severity, ImportDiagnosticCode Code, string Message,
    string? Section = null, string? SourceReference = null, string? Field = null);
public enum ImportDiagnosticSeverity { Info = 0, Warning = 1, Error = 2, Fatal = 3 }
public enum ImportDiagnosticCode { UnsupportedSource, InputPathNotFound, FileReadFailed, InvalidRecord, IgnoredAsset, CancellationRemoved }
public enum Broker { None, InteractiveBrokers, Tastytrade, TradersPlace }
public enum Format { Unknown = 0, CSV = 1, Excel = 2, XML = 3, PDF = 4 }

// --- Application: Plan 1 persistence port (WealthIQ.Application.Persistence / .Interface) ---
public sealed record LedgerSaveResult(int InsertedEntries, int SkippedDuplicateEntries);
public interface ILedgerStore
{
    Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger ledger, CancellationToken ct = default);
    Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default);
}

// --- Domain (WealthIQ.Domain.Model.Ledger / .General) ---
public sealed record PortfolioLedger
{
    public PortfolioLedger(IReadOnlyList<PortfolioEntry> entries, IReadOnlyList<Instrument>? instruments = null, IReadOnlyList<Account>? accounts = null);
    public IReadOnlyList<PortfolioEntry> Entries { get; }     // sorted by OccurredAt, then EntryId, in ctor
    public IReadOnlyList<Instrument> Instruments { get; }
    public IReadOnlyList<Account> Accounts { get; }
}
public sealed record Account(AccountId AccountId, string AccountNumber);
public sealed record Instrument(InstrumentId InstrumentId, string ISIN, string Symbol, string Name, decimal Teilfreistellungsquote);
public readonly record struct AccountId(Guid Value) { public static AccountId NewId(); }
public readonly record struct InstrumentId(Guid Value) { public static InstrumentId NewId(); }
public sealed record TradeEntry(/* entryId, accountId, occurredAt, effectiveDate, sourceProvenance, instrumentId, side, quantity, unitPrice, fees, taxes */) : PortfolioEntry;
public sealed record CashEntry(/* ..., cashInstrumentId, cashFlowType, grossAmount, fees, taxes, relatedInstrumentId? */) : PortfolioEntry;

// --- Infrastructure: Plan 1 persistence (WealthIQ.Infrastructure.Persistence) ---
public sealed class WealthIqDbContext(DbContextOptions<WealthIqDbContext> options) : DbContext(options)
{
    public DbSet<PortfolioEntryRow> PortfolioEntries { get; }
    public DbSet<InstrumentRow> Instruments { get; }
    public DbSet<AccountRow> Accounts { get; }
    // OnModelCreating already configures the three Plan 1 row types.
}
public sealed class SqliteLedgerStore(WealthIqDbContext db) : ILedgerStore { /* idempotent save on (SourceSystem, SourceRecordReference) + load */ }
internal static class LedgerJson { public static readonly JsonSerializerOptions Options; }   // string enums

// --- Infrastructure: IBKR importer (WealthIQ.Infrastructure.Ibkr.Import) ---
public sealed class IbkrStatementImporter : IStatementImporter   // parameterless ctor
{
    // CanImport => Broker.InteractiveBrokers && Format.XML.
    // ImportAsync reads request.Source.FilePath (single .xml file OR a directory of *.xml),
    //   parses Trades + CashTransactions, sets entries' AccountId = request.AccountId,
    //   sets SourceProvenance (SourceLocation = the file path it read), collects Diagnostics.
}
```

### Reference-data files on disk (for Part B)

The shipped reference files currently live under
`data/old_project/Frontend/ConsoleUi/Sigmatic.Console/Input/Configuration/`:

| Concern | File | Format |
|---|---|---|
| Basiszins | `basiszins.csv` | header `year,rate`; e.g. `2024,0.0229` |
| Year-end prices | `prices.csv` | header `year,isin,price_eur`; e.g. `2024,IE00B3XXRP09,106.47` |
| Instrument profiles | `instruments.json` | `{ "<ISIN>": { "name": "...", "tfs_quote": 0.30 } }` |
| FX rates | `fx_rates.csv` | header `date,currency,rate_to_eur`; date `yyyy-MM-dd`; e.g. `2021-03-26,USD,0.8487523341` |

Sample IBKR statements (real golden files) are in `data/input/TaxAlpha_Raw_Data_2021..2025.xml`.

### Verification commands (run from repo root `E:\05 Projects\CSharp\WealthIQ`)

- Build: `dotnet build "WealthIQ.slnx"`
- All tests: `dotnet test "WealthIQ.slnx"`
- Single test class: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~<Namespace.Class>"`

---

## File Structure (created/modified in this plan)

```
src/WealthIQ.Application/
  Import/ImportStatus.cs                                 (new) — enum Committed/Aborted
  Import/ImportBatch.cs                                  (new) — batch metadata record
  Import/ImportStatementCommand.cs                       (new) — pipeline input (request + account)
  Import/ImportPipelineResult.cs                         (new) — pipeline output
  Import/StatementImportPipeline.cs                      (new) — the use-case
  Persistence/ImportPersistCounts.cs                     (new) — persist result record
  Persistence/Interface/IRawFileStore.cs                 (new) — ingest port
  Persistence/Interface/IImportStore.cs                  (new) — transactional import-persist port
  ReferenceData/ReferenceDataSources.cs                  (new) — file paths
  ReferenceData/ReferenceDataSeedResult.cs               (new) — seed counts
  ReferenceData/Interface/IReferenceDataSeeder.cs        (new) — seeding port

src/WealthIQ.Infrastructure/
  Ingest/FileSystemRawFileStore.cs                       (new) — IRawFileStore impl
  Persistence/Rows/ImportBatchRow.cs                     (new)
  Persistence/Rows/ImportDiagnosticRow.cs                (new)
  Persistence/Rows/BasisInterestRateRow.cs               (new)
  Persistence/Rows/YearEndPriceRow.cs                    (new)
  Persistence/Rows/InstrumentProfileRow.cs               (new)
  Persistence/Rows/FxRateRow.cs                          (new)
  Persistence/Mapping/ImportBatchMapper.cs               (new)
  Persistence/Mapping/ImportDiagnosticMapper.cs          (new)
  Persistence/WealthIqDbContext.cs                       (modify) — 6 new DbSets + config
  Persistence/SqliteImportStore.cs                       (new) — IImportStore impl
  ReferenceData/ReferenceDataSeeder.cs                   (new) — IReferenceDataSeeder impl

tests/WealthIQ.Tests/
  WealthIQ.Tests.csproj                                  (modify) — copy Fixtures to output
  Infrastructure/Ingest/FileSystemRawFileStoreTests.cs   (new)
  Infrastructure/Persistence/ImportDiagnosticMapperTests.cs (new)
  Infrastructure/Persistence/SqliteImportStoreTests.cs   (new)
  Application/Import/StatementImportPipelineTests.cs      (new)
  Application/Import/Fakes/...                            (new) — test doubles
  Infrastructure/Import/StatementImportEndToEndTests.cs   (new)
  Infrastructure/Import/Fixtures/ibkr_sample.xml          (new)
  Infrastructure/ReferenceData/ReferenceDataSeederTests.cs (new)
  Infrastructure/ReferenceData/Fixtures/*.csv|*.json      (new)

data/reference/                                          (new) — canonical shipped reference files
```

---

# Part A — Import → Persist pipeline & diagnostics

## Task 1: Application — import-pipeline contracts

Define the pure contracts the pipeline and its adapters share. No behavior yet, so no test in this task (the types are exercised by Tasks 4–6).

**Files:**
- Create: `src/WealthIQ.Application/Import/ImportStatus.cs`
- Create: `src/WealthIQ.Application/Import/ImportBatch.cs`
- Create: `src/WealthIQ.Application/Import/ImportStatementCommand.cs`
- Create: `src/WealthIQ.Application/Import/ImportPipelineResult.cs`
- Create: `src/WealthIQ.Application/Persistence/ImportPersistCounts.cs`
- Create: `src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs`
- Create: `src/WealthIQ.Application/Persistence/Interface/IImportStore.cs`

- [ ] **Step 1: Create the status enum**

Create `src/WealthIQ.Application/Import/ImportStatus.cs`:

```csharp
namespace WealthIQ.Application.Import;

/// <summary>Outcome of an import batch: committed to the DB, or aborted before any write.</summary>
public enum ImportStatus
{
    Committed,
    Aborted
}
```

- [ ] **Step 2: Create the batch metadata record**

Create `src/WealthIQ.Application/Import/ImportBatch.cs`:

```csharp
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

/// <summary>One persisted import run. Stored only when the batch commits.</summary>
public sealed record ImportBatch(
    Guid BatchId,
    Broker Broker,
    Format Format,
    AccountId AccountId,
    string RawFilePath,
    DateTimeOffset ImportedAt);
```

- [ ] **Step 3: Create the pipeline input command**

Create `src/WealthIQ.Application/Import/ImportStatementCommand.cs`:

```csharp
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

/// <summary>
/// Drives one import: the broker request plus the account the entries belong to.
/// <paramref name="Request"/>.AccountId must equal <paramref name="Account"/>.AccountId.
/// </summary>
public sealed record ImportStatementCommand(ImportRequest Request, Account Account);
```

- [ ] **Step 4: Create the pipeline result**

Create `src/WealthIQ.Application/Import/ImportPipelineResult.cs`:

```csharp
using WealthIQ.Application.Import.Diagnostic;

namespace WealthIQ.Application.Import;

/// <summary>
/// Result of running the import pipeline. On <see cref="ImportStatus.Aborted"/> nothing was
/// persisted and the counts are zero; <see cref="Diagnostics"/> is always the full collected set.
/// </summary>
public sealed record ImportPipelineResult(
    ImportStatus Status,
    Guid BatchId,
    int InsertedEntries,
    int SkippedDuplicateEntries,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
```

- [ ] **Step 5: Create the persist-counts record**

Create `src/WealthIQ.Application/Persistence/ImportPersistCounts.cs`:

```csharp
namespace WealthIQ.Application.Persistence;

/// <summary>Counts returned by a committed import persist.</summary>
public sealed record ImportPersistCounts(int InsertedEntries, int SkippedDuplicateEntries, int PersistedDiagnostics);
```

- [ ] **Step 6: Create the raw-file ingest port**

Create `src/WealthIQ.Application/Persistence/Interface/IRawFileStore.cs`:

```csharp
namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Copies a raw broker file into the managed audit folder (immutable source of truth, spec §6).
/// Returns the stored path, which becomes the import's <c>SourceLocation</c>.
/// </summary>
public interface IRawFileStore
{
    string Ingest(string sourceFilePath);
}
```

- [ ] **Step 7: Create the transactional import-persist port**

Create `src/WealthIQ.Application/Persistence/Interface/IImportStore.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Persistence.Interface;

/// <summary>
/// Persists a committed import in a single transaction: the batch record, the ledger
/// (entries idempotent on (SourceSystem, SourceRecordReference); instruments/accounts upserted),
/// and the diagnostics linked to the batch. Rolls back entirely on failure (spec §8).
/// </summary>
public interface IImportStore
{
    Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch,
        PortfolioLedger ledger,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default);
}
```

- [ ] **Step 8: Build**

Run: `dotnet build "src/WealthIQ.Application/WealthIQ.Application.csproj"`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/WealthIQ.Application/Import src/WealthIQ.Application/Persistence
git commit -m "feat: add import-pipeline contracts and ports (IRawFileStore, IImportStore)"
```

---

## Task 2: Infrastructure — FileSystemRawFileStore (TDD)

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/Ingest/FileSystemRawFileStoreTests.cs`
- Create: `src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/WealthIQ.Tests/Infrastructure/Ingest/FileSystemRawFileStoreTests.cs`:

```csharp
using WealthIQ.Infrastructure.Ingest;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Ingest;

public sealed class FileSystemRawFileStoreTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-ingest-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Ingest_CopiesFileIntoRoot_ReturnsDestinationPathWithSameContent()
    {
        var sourceDir = Path.Combine(_temp, "src");
        var rootDir = Path.Combine(_temp, "audit");
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "statement.xml");
        File.WriteAllText(source, "<x/>");

        var store = new FileSystemRawFileStore(rootDir);
        var destination = store.Ingest(source);

        Assert.True(File.Exists(destination));
        Assert.StartsWith(rootDir, destination);
        Assert.Equal("statement.xml", Path.GetFileName(destination));
        Assert.Equal("<x/>", File.ReadAllText(destination));
    }

    [Fact]
    public void Ingest_MissingSource_Throws()
    {
        var store = new FileSystemRawFileStore(Path.Combine(_temp, "audit"));
        Assert.Throws<FileNotFoundException>(() => store.Ingest(Path.Combine(_temp, "nope.xml")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~FileSystemRawFileStoreTests"`
Expected: FAIL — `FileSystemRawFileStore` does not exist (compile error).

- [ ] **Step 3: Implement**

Create `src/WealthIQ.Infrastructure/Ingest/FileSystemRawFileStore.cs`:

```csharp
using WealthIQ.Application.Persistence.Interface;

namespace WealthIQ.Infrastructure.Ingest;

/// <summary>
/// Stores raw broker files under a root audit folder. Re-ingesting the same file name overwrites
/// (the bytes are the immutable source; a re-import of the same statement is harmless).
/// </summary>
public sealed class FileSystemRawFileStore(string rootFolder) : IRawFileStore
{
    public string Ingest(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Raw statement file not found.", sourceFilePath);
        }

        Directory.CreateDirectory(rootFolder);
        var destination = Path.Combine(rootFolder, Path.GetFileName(sourceFilePath));
        File.Copy(sourceFilePath, destination, overwrite: true);
        return destination;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~FileSystemRawFileStoreTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/Ingest tests/WealthIQ.Tests/Infrastructure/Ingest
git commit -m "feat: add FileSystemRawFileStore for raw-statement ingest"
```

---

## Task 3: Infrastructure — batch + diagnostic rows, DbContext, mappers (TDD on the mapper)

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/ImportBatchRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/ImportDiagnosticRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Mapping/ImportBatchMapper.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Mapping/ImportDiagnosticMapper.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/ImportDiagnosticMapperTests.cs`

- [ ] **Step 1: Create the row types**

Create `src/WealthIQ.Infrastructure/Persistence/Rows/ImportBatchRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class ImportBatchRow
{
    public Guid BatchId { get; set; }
    public string Broker { get; set; } = "";
    public string Format { get; set; } = "";
    public Guid AccountId { get; set; }
    public string RawFilePath { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public int InsertedEntries { get; set; }
    public int SkippedDuplicateEntries { get; set; }
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/ImportDiagnosticRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class ImportDiagnosticRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string Severity { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Section { get; set; }
    public string? SourceReference { get; set; }
    public string? Field { get; set; }
}
```

- [ ] **Step 2: Create the batch mapper**

Create `src/WealthIQ.Infrastructure/Persistence/Mapping/ImportBatchMapper.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class ImportBatchMapper
{
    public static ImportBatchRow ToRow(ImportBatch batch) => new()
    {
        BatchId = batch.BatchId,
        Broker = batch.Broker.ToString(),
        Format = batch.Format.ToString(),
        AccountId = batch.AccountId.Value,
        RawFilePath = batch.RawFilePath,
        ImportedAt = batch.ImportedAt
    };
}
```

- [ ] **Step 3: Write the failing diagnostic-mapper round-trip test**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/ImportDiagnosticMapperTests.cs`:

```csharp
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Infrastructure.Persistence.Mapping;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class ImportDiagnosticMapperTests
{
    [Fact]
    public void ToRow_ToDomain_RoundTripsAllFields()
    {
        var batchId = Guid.NewGuid();
        var original = new ImportDiagnostic(
            ImportDiagnosticSeverity.Warning,
            ImportDiagnosticCode.IgnoredAsset,
            "Ignored an out-of-scope asset.",
            Section: "Trades",
            SourceReference: "TR-42",
            Field: "assetCategory");

        var row = ImportDiagnosticMapper.ToRow(original, batchId);
        var restored = ImportDiagnosticMapper.ToDomain(row);

        Assert.Equal(batchId, row.BatchId);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(original, restored);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~ImportDiagnosticMapperTests"`
Expected: FAIL — `ImportDiagnosticMapper` does not exist (compile error).

- [ ] **Step 5: Implement the diagnostic mapper**

Create `src/WealthIQ.Infrastructure/Persistence/Mapping/ImportDiagnosticMapper.cs`:

```csharp
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class ImportDiagnosticMapper
{
    public static ImportDiagnosticRow ToRow(ImportDiagnostic diagnostic, Guid batchId) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = batchId,
        Severity = diagnostic.Severity.ToString(),
        Code = diagnostic.Code.ToString(),
        Message = diagnostic.Message,
        Section = diagnostic.Section,
        SourceReference = diagnostic.SourceReference,
        Field = diagnostic.Field
    };

    public static ImportDiagnostic ToDomain(ImportDiagnosticRow row) => new(
        Enum.Parse<ImportDiagnosticSeverity>(row.Severity),
        Enum.Parse<ImportDiagnosticCode>(row.Code),
        row.Message,
        row.Section,
        row.SourceReference,
        row.Field);
}
```

- [ ] **Step 6: Register the new rows in the DbContext**

Edit `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`. Add the two `using`-covered row types to the `DbSet` block and add their configuration in `OnModelCreating`. Insert the new `DbSet`s after the existing `Accounts` set:

```csharp
    public DbSet<ImportBatchRow> ImportBatches => Set<ImportBatchRow>();
    public DbSet<ImportDiagnosticRow> ImportDiagnostics => Set<ImportDiagnosticRow>();
```

And inside `OnModelCreating`, after the existing `AccountRow` block, add:

```csharp
        modelBuilder.Entity<ImportBatchRow>(e =>
        {
            e.HasKey(x => x.BatchId);
            e.HasIndex(x => x.AccountId);
        });

        modelBuilder.Entity<ImportDiagnosticRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BatchId);
            e.Property(x => x.Severity).IsRequired();
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.Message).IsRequired();
        });
```

The existing file already has `using WealthIQ.Infrastructure.Persistence.Rows;`, so the new row types resolve without a new import.

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build "WealthIQ.slnx"`
Expected: Build succeeded.

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~ImportDiagnosticMapperTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence tests/WealthIQ.Tests/Infrastructure/Persistence/ImportDiagnosticMapperTests.cs
git commit -m "feat: add import batch/diagnostic rows, mappers, and DbContext config"
```

---

## Task 4: Infrastructure — SqliteImportStore (TDD)

Persists batch + ledger + diagnostics in one transaction, reusing the Plan 1 `SqliteLedgerStore` (same `WealthIqDbContext` instance → same transaction) so the idempotent entry dedup and instrument/account upsert are not reimplemented.

**Files:**
- Test: `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportStoreTests.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs`

> The in-memory SQLite helper `tests/WealthIQ.Tests/Infrastructure/Persistence/InMemorySqlite.cs` already exists (Plan 1). Its `EnsureCreated()` will create the new tables automatically because they are now registered in the DbContext.

- [ ] **Step 1: Write the failing integration tests**

Create `tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportStoreTests.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class SqliteImportStoreTests
{
    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/file.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(AccountId account, InstrumentId instrument, string reference, int day) =>
        new(PortfolioEntryId.NewId(), account,
            new DateTimeOffset(2024, 3, day, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, day), Provenance(reference), instrument,
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    private static ImportBatch Batch(AccountId account, Guid batchId) =>
        new(batchId, Broker.InteractiveBrokers, Format.XML, account, "audit/file.xml",
            new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task PersistImport_Commits_BatchEntriesInstrumentsAccountsAndDiagnostics()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);
        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1", 1) },
            new[] { instrument },
            new[] { account });
        var diagnostics = new[]
        {
            new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped one")
        };
        var batchId = Guid.NewGuid();

        ImportPersistCounts counts;
        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            counts = await store.PersistImportAsync(Batch(account.AccountId, batchId), ledger, diagnostics);
        }

        Assert.Equal(new ImportPersistCounts(1, 0, 1), counts);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(1, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(1, await ctx.Instruments.CountAsync());
            Assert.Equal(1, await ctx.Accounts.CountAsync());

            var batchRow = Assert.Single(ctx.ImportBatches);
            Assert.Equal(batchId, batchRow.BatchId);
            Assert.Equal("InteractiveBrokers", batchRow.Broker);
            Assert.Equal(1, batchRow.InsertedEntries);
            Assert.Equal(0, batchRow.SkippedDuplicateEntries);

            var diagRow = Assert.Single(ctx.ImportDiagnostics);
            Assert.Equal(batchId, diagRow.BatchId);
            Assert.Equal("Warning", diagRow.Severity);
        }
    }

    [Fact]
    public async Task PersistImport_ReImportingOverlappingReferences_SkipsDuplicateEntries()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U123");
        var instrument = new Instrument(InstrumentId.NewId(), "US0001", "SPY", "S&P 500", 0.3m);

        PortfolioLedger First() => new(
            new PortfolioEntry[] { Trade(account.AccountId, instrument.InstrumentId, "T-1", 1) },
            new[] { instrument }, new[] { account });

        await using (var ctx = db.NewContext())
        {
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            await store.PersistImportAsync(Batch(account.AccountId, Guid.NewGuid()), First(), Array.Empty<ImportDiagnostic>());
        }

        // Second batch overlaps T-1 and adds T-2.
        ImportPersistCounts second;
        await using (var ctx = db.NewContext())
        {
            var overlapping = new PortfolioLedger(
                new PortfolioEntry[]
                {
                    Trade(account.AccountId, instrument.InstrumentId, "T-1", 1),
                    Trade(account.AccountId, instrument.InstrumentId, "T-2", 2)
                },
                new[] { instrument }, new[] { account });
            var store = new SqliteImportStore(ctx, new SqliteLedgerStore(ctx));
            second = await store.PersistImportAsync(Batch(account.AccountId, Guid.NewGuid()), overlapping, Array.Empty<ImportDiagnostic>());
        }

        Assert.Equal(1, second.InsertedEntries);
        Assert.Equal(1, second.SkippedDuplicateEntries);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(2, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(2, await ctx.ImportBatches.CountAsync());
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteImportStoreTests"`
Expected: FAIL — `SqliteImportStore` does not exist (compile error).

- [ ] **Step 3: Implement SqliteImportStore**

Create `src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Mapping;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Persists a committed import atomically. Reuses <see cref="SqliteLedgerStore"/> on the same
/// <see cref="WealthIqDbContext"/> so all writes share one transaction and the idempotent
/// entry dedup / instrument-account upsert are defined in exactly one place.
/// </summary>
public sealed class SqliteImportStore(WealthIqDbContext db, ILedgerStore ledgerStore) : IImportStore
{
    public async Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch,
        PortfolioLedger ledger,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batchRow = ImportBatchMapper.ToRow(batch);
        db.ImportBatches.Add(batchRow);

        var saveResult = await ledgerStore.SaveLedgerAsync(ledger, ct);
        batchRow.InsertedEntries = saveResult.InsertedEntries;
        batchRow.SkippedDuplicateEntries = saveResult.SkippedDuplicateEntries;

        foreach (var diagnostic in diagnostics)
        {
            db.ImportDiagnostics.Add(ImportDiagnosticMapper.ToRow(diagnostic, batch.BatchId));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportPersistCounts(saveResult.InsertedEntries, saveResult.SkippedDuplicateEntries, diagnostics.Count);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~SqliteImportStoreTests"`
Expected: PASS (both tests).

> If the second test reports 2 inserted on the re-import, the dedup query is not seeing the first batch — confirm both stores share the same `ctx` per batch and that `SqliteLedgerStore.SaveLedgerAsync` ran (it calls `SaveChangesAsync` internally; the outer transaction only commits at `CommitAsync`).

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence/SqliteImportStore.cs tests/WealthIQ.Tests/Infrastructure/Persistence/SqliteImportStoreTests.cs
git commit -m "feat: add transactional SqliteImportStore reusing SqliteLedgerStore"
```

---

## Task 5: Application — StatementImportPipeline (TDD with fakes)

The use-case: ingest → import → fail-fast gate → persist.

**Files:**
- Create: `tests/WealthIQ.Tests/Application/Import/Fakes/FakeStatementImporter.cs`
- Create: `tests/WealthIQ.Tests/Application/Import/Fakes/FakeRawFileStore.cs`
- Create: `tests/WealthIQ.Tests/Application/Import/Fakes/FakeImportStore.cs`
- Create: `tests/WealthIQ.Tests/Application/Import/Fakes/FixedTimeProvider.cs`
- Test: `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs`
- Create: `src/WealthIQ.Application/Import/StatementImportPipeline.cs`

- [ ] **Step 1: Create the test doubles**

Create `tests/WealthIQ.Tests/Application/Import/Fakes/FakeStatementImporter.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Interface;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeStatementImporter(ImportResult result) : IStatementImporter
{
    public string? SeenFilePath { get; private set; }

    public bool CanImport(ImportSource source) => true;

    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        SeenFilePath = request.Source.FilePath;
        return Task.FromResult(result);
    }
}
```

Create `tests/WealthIQ.Tests/Application/Import/Fakes/FakeRawFileStore.cs`:

```csharp
using WealthIQ.Application.Persistence.Interface;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeRawFileStore(string ingestedPath) : IRawFileStore
{
    public string? SeenSourcePath { get; private set; }

    public string Ingest(string sourceFilePath)
    {
        SeenSourcePath = sourceFilePath;
        return ingestedPath;
    }
}
```

Create `tests/WealthIQ.Tests/Application/Import/Fakes/FakeImportStore.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FakeImportStore(ImportPersistCounts counts) : IImportStore
{
    public int CallCount { get; private set; }
    public ImportBatch? SeenBatch { get; private set; }
    public PortfolioLedger? SeenLedger { get; private set; }
    public IReadOnlyList<ImportDiagnostic>? SeenDiagnostics { get; private set; }

    public Task<ImportPersistCounts> PersistImportAsync(
        ImportBatch batch, PortfolioLedger ledger, IReadOnlyList<ImportDiagnostic> diagnostics, CancellationToken ct = default)
    {
        CallCount++;
        SeenBatch = batch;
        SeenLedger = ledger;
        SeenDiagnostics = diagnostics;
        return Task.FromResult(counts);
    }
}
```

Create `tests/WealthIQ.Tests/Application/Import/Fakes/FixedTimeProvider.cs`:

```csharp
namespace WealthIQ.Tests.Application.Import.Fakes;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
```

- [ ] **Step 2: Write the failing pipeline tests**

Create `tests/WealthIQ.Tests/Application/Import/StatementImportPipelineTests.cs`:

```csharp
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Persistence;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Tests.Application.Import.Fakes;
using Xunit;

namespace WealthIQ.Tests.Application.Import;

public sealed class StatementImportPipelineTests
{
    private static readonly AccountId TheAccountId = AccountId.NewId();
    private static readonly Account TheAccount = new(TheAccountId, "U123");

    private static ImportStatementCommand Command() => new(
        new ImportRequest
        {
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, @"C:\inbox\statement.xml"),
            AccountId = TheAccountId
        },
        TheAccount);

    private static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "XML",
        SourceLocation = "audit/statement.xml",
        SourceRecordReference = reference
    };

    private static TradeEntry Trade(string reference) =>
        new(PortfolioEntryId.NewId(), TheAccountId,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2024, 3, 1), Provenance(reference), InstrumentId.NewId(),
            TradeSide.Buy, new Quantity(5m),
            new Money(100m, Currency.USD), new Money(1m, Currency.USD), new Money(0m, Currency.USD));

    private static StatementImportPipeline Build(
        ImportResult importResult, FakeImportStore store, out FakeStatementImporter importer, out FakeRawFileStore raw)
    {
        importer = new FakeStatementImporter(importResult);
        raw = new FakeRawFileStore(@"C:\audit\statement.xml");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero));
        return new StatementImportPipeline(importer, raw, store, clock);
    }

    [Fact]
    public async Task Run_NoBlockingDiagnostics_IngestsImportsAndCommits()
    {
        var result = new ImportResult
        {
            PortfolioLedger = new PortfolioLedger(new PortfolioEntry[] { Trade("T-1"), Trade("T-2") }),
            Instruments = new(),
            Diagnostics = { new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.IgnoredAsset, "skipped") }
        };
        var store = new FakeImportStore(new ImportPersistCounts(2, 0, 1));
        var pipeline = Build(result, store, out var importer, out var raw);

        var outcome = await pipeline.RunAsync(Command());

        Assert.Equal(ImportStatus.Committed, outcome.Status);
        Assert.Equal(2, outcome.InsertedEntries);
        Assert.Single(outcome.Diagnostics);

        // Ingest happened before import, and the importer read the ingested copy.
        Assert.Equal(@"C:\inbox\statement.xml", raw.SeenSourcePath);
        Assert.Equal(@"C:\audit\statement.xml", importer.SeenFilePath);

        // The persisted ledger carries the account, and the batch references the ingested path + clock time.
        Assert.Equal(1, store.CallCount);
        Assert.Equal(@"C:\audit\statement.xml", store.SeenBatch!.RawFilePath);
        Assert.Equal(new DateTimeOffset(2026, 5, 30, 9, 0, 0, TimeSpan.Zero), store.SeenBatch.ImportedAt);
        Assert.Single(store.SeenLedger!.Accounts);
        Assert.Equal(TheAccount, store.SeenLedger.Accounts[0]);
    }

    [Fact]
    public async Task Run_BlockingDiagnostic_AbortsWithoutPersisting()
    {
        var result = new ImportResult
        {
            PortfolioLedger = new PortfolioLedger(new PortfolioEntry[] { Trade("T-1") }),
            Diagnostics = { new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidRecord, "bad record") }
        };
        var store = new FakeImportStore(new ImportPersistCounts(0, 0, 0));
        var pipeline = Build(result, store, out _, out _);

        var outcome = await pipeline.RunAsync(Command());

        Assert.Equal(ImportStatus.Aborted, outcome.Status);
        Assert.Equal(0, outcome.InsertedEntries);
        Assert.Equal(0, store.CallCount);              // nothing persisted
        Assert.Single(outcome.Diagnostics);            // diagnostics still surfaced to the caller
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~StatementImportPipelineTests"`
Expected: FAIL — `StatementImportPipeline` does not exist (compile error).

- [ ] **Step 4: Implement the pipeline**

Create `src/WealthIQ.Application/Import/StatementImportPipeline.cs`:

```csharp
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Import;

/// <summary>
/// Runs the v1 import flow (spec §6): ingest the raw file to the audit folder, import it to
/// canonical entries, then fail-fast (spec §8) — any diagnostic of <see cref="ImportDiagnosticSeverity.Error"/>
/// or higher aborts before any write. Otherwise the batch is persisted transactionally.
/// </summary>
public sealed class StatementImportPipeline(
    IStatementImporter importer,
    IRawFileStore rawFileStore,
    IImportStore importStore,
    TimeProvider timeProvider)
{
    public async Task<ImportPipelineResult> RunAsync(ImportStatementCommand command, CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var importedAt = timeProvider.GetUtcNow();

        var storedPath = rawFileStore.Ingest(command.Request.Source.FilePath);
        var ingestedRequest = command.Request with
        {
            Source = command.Request.Source with { FilePath = storedPath }
        };

        var importResult = await importer.ImportAsync(ingestedRequest, ct);

        var hasBlocking = importResult.Diagnostics.Any(d => d.Severity >= ImportDiagnosticSeverity.Error);
        if (hasBlocking)
        {
            return new ImportPipelineResult(ImportStatus.Aborted, batchId, 0, 0, importResult.Diagnostics);
        }

        var ledger = new PortfolioLedger(
            importResult.PortfolioLedger.Entries,
            importResult.PortfolioLedger.Instruments,
            new[] { command.Account });

        var batch = new ImportBatch(
            batchId,
            command.Request.Source.Broker,
            command.Request.Source.Format,
            command.Request.AccountId,
            storedPath,
            importedAt);

        var counts = await importStore.PersistImportAsync(batch, ledger, importResult.Diagnostics, ct);

        return new ImportPipelineResult(
            ImportStatus.Committed, batchId, counts.InsertedEntries, counts.SkippedDuplicateEntries, importResult.Diagnostics);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~StatementImportPipelineTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add src/WealthIQ.Application/Import/StatementImportPipeline.cs tests/WealthIQ.Tests/Application/Import
git commit -m "feat: add StatementImportPipeline (ingest -> import -> fail-fast -> persist)"
```

---

## Task 6: End-to-end import→persist test (real IBKR importer + sample XML + SQLite)

Proves the whole Part-A flow against the real `IbkrStatementImporter`, a real `FileSystemRawFileStore`, and SQLite (spec §10 end-to-end). Uses a small, self-contained IBKR XML fixture for determinism.

**Files:**
- Modify: `tests/WealthIQ.Tests/WealthIQ.Tests.csproj` (copy `Fixtures` folders to output)
- Create: `tests/WealthIQ.Tests/Infrastructure/Import/Fixtures/ibkr_sample.xml`
- Test: `tests/WealthIQ.Tests/Infrastructure/Import/StatementImportEndToEndTests.cs`

- [ ] **Step 1: Make test fixtures copy to the output directory**

Edit `tests/WealthIQ.Tests/WealthIQ.Tests.csproj`. Add this `ItemGroup` (a sibling of the existing `ItemGroup`s):

```xml
  <ItemGroup>
    <Content Include="**\Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Create the IBKR XML fixture**

Create `tests/WealthIQ.Tests/Infrastructure/Import/Fixtures/ibkr_sample.xml` (two STK trades + one dividend; mirrors the real FlexQuery attribute set, no cancellation pairs, no forex pairs):

```xml
<FlexQueryResponse queryName="TaxAlpha_Raw_Data" type="AF">
<FlexStatements count="1">
<FlexStatement accountId="U5658230" fromDate="20210104" toDate="20211231" period="" whenGenerated="20251127;082531">
<Trades>
<Trade accountId="U5658230" currency="EUR" fxRateToBase="1" assetCategory="STK" symbol="VUSA" description="VANG S&amp;P500 USDD" isin="IE00B3XXRP09" dateTime="20210409;103001" tradeDate="20210409" settleDateTarget="20210413" quantity="320" tradePrice="65.39" tradeMoney="20924.8" taxes="0" ibCommission="-10.4624" ibCommissionCurrency="EUR" cost="20935.2624" fifoPnlRealized="0" buySell="BUY" transactionID="176477128" />
<Trade accountId="U5658230" currency="USD" fxRateToBase="0.82006" assetCategory="STK" symbol="IDTM" description="ISHARES USD TREASURY 7-10" isin="IE00B1FZS798" dateTime="20210510;120000" tradeDate="20210510" settleDateTarget="20210512" quantity="10" tradePrice="5.10" tradeMoney="51.0" taxes="0" ibCommission="-1.0" ibCommissionCurrency="USD" cost="52.0" fifoPnlRealized="0" buySell="BUY" transactionID="200000001" />
</Trades>
<CashTransactions>
<CashTransaction accountId="U5658230" currency="USD" fxRateToBase="0.82006" assetCategory="STK" symbol="IDTM" description="IDTM(IE00B1FZS798) CASH DIVIDEND USD 1.192 PER SHARE (Mixed Income)" isin="IE00B1FZS798" dateTime="20210526;202000" amount="11.92" type="Dividends" transactionID="279268408" />
</CashTransactions>
</FlexStatement>
</FlexStatements>
</FlexQueryResponse>
```

- [ ] **Step 3: Write the end-to-end test**

Create `tests/WealthIQ.Tests/Infrastructure/Import/StatementImportEndToEndTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ingest;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Import;

public sealed class StatementImportEndToEndTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-e2e-" + Guid.NewGuid().ToString("N"));

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Import", "Fixtures", "ibkr_sample.xml");

    [Fact]
    public async Task ImportSampleStatement_PersistsLedgerBatchAndIsReloadable()
    {
        using var db = new InMemorySqlite();
        var account = new Account(AccountId.NewId(), "U5658230");
        var command = new ImportStatementCommand(
            new ImportRequest
            {
                Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, FixturePath()),
                AccountId = account.AccountId
            },
            account);

        ImportPipelineResult outcome;
        await using (var ctx = db.NewContext())
        {
            var pipeline = new StatementImportPipeline(
                new IbkrStatementImporter(),
                new FileSystemRawFileStore(Path.Combine(_temp, "audit")),
                new SqliteImportStore(ctx, new SqliteLedgerStore(ctx)),
                TimeProvider.System);

            outcome = await pipeline.RunAsync(command);
        }

        Assert.Equal(ImportStatus.Committed, outcome.Status);
        Assert.True(outcome.InsertedEntries >= 3, $"expected >=3 entries, got {outcome.InsertedEntries}");

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(outcome.InsertedEntries, await ctx.PortfolioEntries.CountAsync());
            Assert.Equal(1, await ctx.ImportBatches.CountAsync());

            var loaded = await new SqliteLedgerStore(ctx).LoadLedgerAsync();
            Assert.Equal(outcome.InsertedEntries, loaded.Entries.Count);
            Assert.Contains(loaded.Instruments, i => i.ISIN == "IE00B3XXRP09");
            Assert.Single(loaded.Accounts);
            Assert.Equal("U5658230", loaded.Accounts[0].AccountNumber);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
```

- [ ] **Step 4: Run the end-to-end test**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~StatementImportEndToEndTests"`
Expected: PASS.

> If it fails on the entry count, print the diagnostics to see what the importer rejected: temporarily add `foreach (var d in outcome.Diagnostics) Console.WriteLine($"{d.Severity} {d.Code} {d.Message}");` before the asserts. The fixture uses only `assetCategory="STK"`, non-forex symbols, and no cancellation pairs, so all three records should import; adjust the fixture (not the production code) if the importer's scope rules reject one.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add tests/WealthIQ.Tests/WealthIQ.Tests.csproj tests/WealthIQ.Tests/Infrastructure/Import
git commit -m "test: end-to-end IBKR import -> persist -> reload against SQLite"
```

---

# Part B — Reference-data tables & seeding

## Task 7: Infrastructure — reference-data rows + DbContext config

**Files:**
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/BasisInterestRateRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/YearEndPriceRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`
- Create: `src/WealthIQ.Infrastructure/Persistence/Rows/FxRateRow.cs`
- Modify: `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`

- [ ] **Step 1: Create the four row types**

Create `src/WealthIQ.Infrastructure/Persistence/Rows/BasisInterestRateRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class BasisInterestRateRow
{
    public int Year { get; set; }
    public decimal Rate { get; set; }
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/YearEndPriceRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class YearEndPriceRow
{
    public int Year { get; set; }
    public string Isin { get; set; } = "";
    public decimal PriceEur { get; set; }
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/InstrumentProfileRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class InstrumentProfileRow
{
    public string Isin { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Teilfreistellungsquote { get; set; }
}
```

Create `src/WealthIQ.Infrastructure/Persistence/Rows/FxRateRow.cs`:

```csharp
namespace WealthIQ.Infrastructure.Persistence.Rows;

public sealed class FxRateRow
{
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "";
    public decimal RateToEur { get; set; }
}
```

- [ ] **Step 2: Register the rows in the DbContext**

Edit `src/WealthIQ.Infrastructure/Persistence/WealthIqDbContext.cs`. Add these `DbSet`s after `ImportDiagnostics`:

```csharp
    public DbSet<BasisInterestRateRow> BasisInterestRates => Set<BasisInterestRateRow>();
    public DbSet<YearEndPriceRow> YearEndPrices => Set<YearEndPriceRow>();
    public DbSet<InstrumentProfileRow> InstrumentProfiles => Set<InstrumentProfileRow>();
    public DbSet<FxRateRow> FxRates => Set<FxRateRow>();
```

And add this configuration at the end of `OnModelCreating` (composite keys mirror the natural keys used by the CSV adapters):

```csharp
        modelBuilder.Entity<BasisInterestRateRow>(e => e.HasKey(x => x.Year));

        modelBuilder.Entity<YearEndPriceRow>(e =>
        {
            e.HasKey(x => new { x.Year, x.Isin });
        });

        modelBuilder.Entity<InstrumentProfileRow>(e => e.HasKey(x => x.Isin));

        modelBuilder.Entity<FxRateRow>(e =>
        {
            e.HasKey(x => new { x.Date, x.Currency });
        });
```

- [ ] **Step 3: Build**

Run: `dotnet build "src/WealthIQ.Infrastructure/WealthIQ.Infrastructure.csproj"`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/WealthIQ.Infrastructure/Persistence
git commit -m "feat: add reference-data rows and DbContext config"
```

---

## Task 8: Application — reference-data seeding contracts

**Files:**
- Create: `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs`
- Create: `src/WealthIQ.Application/ReferenceData/ReferenceDataSeedResult.cs`
- Create: `src/WealthIQ.Application/ReferenceData/Interface/IReferenceDataSeeder.cs`

- [ ] **Step 1: Create the sources record**

Create `src/WealthIQ.Application/ReferenceData/ReferenceDataSources.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData;

/// <summary>Paths to the shipped reference files used for first-run seeding (spec §6).</summary>
public sealed record ReferenceDataSources(
    string BasisInterestRateCsvPath,
    string YearEndPriceCsvPath,
    string InstrumentProfileJsonPath,
    string FxRateCsvPath);
```

- [ ] **Step 2: Create the result record**

Create `src/WealthIQ.Application/ReferenceData/ReferenceDataSeedResult.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData;

/// <summary>Row counts in each reference table after a seed run.</summary>
public sealed record ReferenceDataSeedResult(
    int BasisInterestRates,
    int YearEndPrices,
    int InstrumentProfiles,
    int FxRates);
```

- [ ] **Step 3: Create the port**

Create `src/WealthIQ.Application/ReferenceData/Interface/IReferenceDataSeeder.cs`:

```csharp
namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>
/// Seeds reference data from the shipped files into the database. Seed-if-empty per table, so
/// calling it repeatedly is safe (idempotent) and never overwrites later user edits.
/// </summary>
public interface IReferenceDataSeeder
{
    Task<ReferenceDataSeedResult> SeedIfEmptyAsync(ReferenceDataSources sources, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "src/WealthIQ.Application/WealthIQ.Application.csproj"`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/WealthIQ.Application/ReferenceData
git commit -m "feat: add reference-data seeding contracts (IReferenceDataSeeder)"
```

---

## Task 9: Infrastructure — ReferenceDataSeeder (TDD)

Reads the four shipped files and inserts rows, seed-if-empty. The CSV/JSON formats match the existing adapters (`basiszins.csv`, `prices.csv`, `instruments.json`, `fx_rates.csv`).

**Files:**
- Create: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/basiszins.csv`
- Create: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/prices.csv`
- Create: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json`
- Create: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/fx_rates.csv`
- Test: `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs`
- Create: `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`

> The `Fixtures` copy-to-output rule was added to the test csproj in Task 6, so these new fixtures are picked up automatically.

- [ ] **Step 1: Create the fixture files**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/basiszins.csv`:

```
year,rate
2023,0.0255
2024,0.0229
```

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/prices.csv`:

```
year,isin,price_eur
2024,IE00B3XXRP09,106.47
2024,IE00B4ND3602,48.77
```

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/instruments.json`:

```json
{
  "IE00B3XXRP09": { "name": "Vanguard S&P 500 UCITS ETF", "type": "ETF_EQUITY", "tfs_quote": 0.30 },
  "IE00B4ND3602": { "name": "iShares Physical Gold ETC", "type": "ETC", "tfs_quote": 0.00 }
}
```

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/Fixtures/fx_rates.csv`:

```
date,currency,rate_to_eur
2021-03-26,USD,0.8487523341
2021-03-26,GBP,1.1695496064
2021-03-29,USD,0.8501000000
```

- [ ] **Step 2: Write the failing seeder tests**

Create `tests/WealthIQ.Tests/Infrastructure/ReferenceData/ReferenceDataSeederTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class ReferenceDataSeederTests
{
    private static ReferenceDataSources Sources()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "ReferenceData", "Fixtures");
        return new ReferenceDataSources(
            Path.Combine(dir, "basiszins.csv"),
            Path.Combine(dir, "prices.csv"),
            Path.Combine(dir, "instruments.json"),
            Path.Combine(dir, "fx_rates.csv"));
    }

    [Fact]
    public async Task SeedIfEmpty_LoadsAllFourTables()
    {
        using var db = new InMemorySqlite();

        ReferenceDataSeedResult result;
        await using (var ctx = db.NewContext())
        {
            result = await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 3), result);

        await using (var ctx = db.NewContext())
        {
            var basis = await ctx.BasisInterestRates.SingleAsync(x => x.Year == 2024);
            Assert.Equal(0.0229m, basis.Rate);

            var price = await ctx.YearEndPrices.SingleAsync(x => x.Year == 2024 && x.Isin == "IE00B3XXRP09");
            Assert.Equal(106.47m, price.PriceEur);

            var profile = await ctx.InstrumentProfiles.SingleAsync(x => x.Isin == "IE00B3XXRP09");
            Assert.Equal(0.30m, profile.Teilfreistellungsquote);

            var fx = await ctx.FxRates.SingleAsync(x => x.Date == new DateOnly(2021, 3, 26) && x.Currency == "USD");
            Assert.Equal(0.8487523341m, fx.RateToEur);
        }
    }

    [Fact]
    public async Task SeedIfEmpty_RunTwice_IsIdempotent()
    {
        using var db = new InMemorySqlite();

        await using (var ctx = db.NewContext())
        {
            await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        ReferenceDataSeedResult second;
        await using (var ctx = db.NewContext())
        {
            second = await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 3), second);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(2, await ctx.BasisInterestRates.CountAsync());
            Assert.Equal(3, await ctx.FxRates.CountAsync());
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~ReferenceDataSeederTests"`
Expected: FAIL — `ReferenceDataSeeder` does not exist (compile error).

- [ ] **Step 4: Implement the seeder**

Create `src/WealthIQ.Infrastructure/ReferenceData/ReferenceDataSeeder.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Seeds reference data from the shipped CSV/JSON files. Each table is seeded only when empty,
/// so re-running never duplicates rows or clobbers later edits. Files must exist (fail-fast, spec §8).
/// </summary>
public sealed class ReferenceDataSeeder(WealthIqDbContext db) : IReferenceDataSeeder
{
    public async Task<ReferenceDataSeedResult> SeedIfEmptyAsync(ReferenceDataSources sources, CancellationToken ct = default)
    {
        if (!await db.BasisInterestRates.AnyAsync(ct))
        {
            db.BasisInterestRates.AddRange(ReadBasisInterestRates(sources.BasisInterestRateCsvPath));
        }

        if (!await db.YearEndPrices.AnyAsync(ct))
        {
            db.YearEndPrices.AddRange(ReadYearEndPrices(sources.YearEndPriceCsvPath));
        }

        if (!await db.InstrumentProfiles.AnyAsync(ct))
        {
            db.InstrumentProfiles.AddRange(ReadInstrumentProfiles(sources.InstrumentProfileJsonPath));
        }

        if (!await db.FxRates.AnyAsync(ct))
        {
            db.FxRates.AddRange(ReadFxRates(sources.FxRateCsvPath));
        }

        await db.SaveChangesAsync(ct);

        return new ReferenceDataSeedResult(
            await db.BasisInterestRates.CountAsync(ct),
            await db.YearEndPrices.CountAsync(ct),
            await db.InstrumentProfiles.CountAsync(ct),
            await db.FxRates.CountAsync(ct));
    }

    private static IEnumerable<BasisInterestRateRow> ReadBasisInterestRates(string path)
    {
        foreach (var parts in ReadCsv(path, "Basis interest rate file not found.", minColumns: 2))
        {
            if (int.TryParse(parts[0], out var year)
                && decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            {
                yield return new BasisInterestRateRow { Year = year, Rate = rate };
            }
        }
    }

    private static IEnumerable<YearEndPriceRow> ReadYearEndPrices(string path)
    {
        foreach (var parts in ReadCsv(path, "Year-end price file not found.", minColumns: 3))
        {
            if (int.TryParse(parts[0], out var year)
                && decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                yield return new YearEndPriceRow { Year = year, Isin = parts[1].Trim(), PriceEur = price };
            }
        }
    }

    private static IEnumerable<FxRateRow> ReadFxRates(string path)
    {
        foreach (var parts in ReadCsv(path, "FX rate file not found.", minColumns: 3))
        {
            if (DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate)
                && rate > 0m)
            {
                yield return new FxRateRow { Date = date, Currency = parts[1].Trim(), RateToEur = rate };
            }
        }
    }

    private static IEnumerable<InstrumentProfileRow> ReadInstrumentProfiles(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Instrument profile file not found.", path);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, InstrumentProfileDto>>(json)
            ?? throw new InvalidOperationException("Instrument profile file could not be parsed.");

        foreach (var (isin, dto) in raw)
        {
            if (!decimal.TryParse(dto.TeilfreistellungsquoteRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tfs))
            {
                throw new InvalidOperationException($"Invalid tfs_quote for instrument '{isin}'.");
            }

            yield return new InstrumentProfileRow { Isin = isin, Name = dto.Name, Teilfreistellungsquote = tfs };
        }
    }

    private static IEnumerable<string[]> ReadCsv(string path, string notFoundMessage, int minColumns)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(notFoundMessage, path);
        }

        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length >= minColumns)
            {
                yield return parts;
            }
        }
    }

    private sealed class InstrumentProfileDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("tfs_quote")]
        public object? TeilfreistellungsquoteRaw { get; init; }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~ReferenceDataSeederTests"`
Expected: PASS (both tests).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test "WealthIQ.slnx"`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/WealthIQ.Infrastructure/ReferenceData tests/WealthIQ.Tests/Infrastructure/ReferenceData
git commit -m "feat: add ReferenceDataSeeder (seed-if-empty from shipped files)"
```

---

## Task 10: Place canonical reference files under `data/reference/`

Establish the canonical shipped location the Web host (Plan 3) will point `ReferenceDataSources` at. No code; just move the data into a stable path so seeding has a home that does not live inside `data/old_project/`.

**Files:**
- Create: `data/reference/basiszins.csv`, `prices.csv`, `instruments.json`, `fx_rates.csv` (copies of the shipped files)

- [ ] **Step 1: Copy the four shipped reference files into `data/reference/`**

Run from the repo root:

```bash
mkdir -p "data/reference"
cp "data/old_project/Frontend/ConsoleUi/Sigmatic.Console/Input/Configuration/basiszins.csv"   "data/reference/basiszins.csv"
cp "data/old_project/Frontend/ConsoleUi/Sigmatic.Console/Input/Configuration/prices.csv"       "data/reference/prices.csv"
cp "data/old_project/Frontend/ConsoleUi/Sigmatic.Console/Input/Configuration/instruments.json" "data/reference/instruments.json"
cp "data/old_project/Frontend/ConsoleUi/Sigmatic.Console/Input/Configuration/fx_rates.csv"     "data/reference/fx_rates.csv"
```

- [ ] **Step 2: Confirm the four files exist**

Run: `ls -1 data/reference`
Expected: `basiszins.csv  fx_rates.csv  instruments.json  prices.csv`

- [ ] **Step 3: Commit**

```bash
git add data/reference
git commit -m "chore: add canonical reference-data files under data/reference"
```

---

## Done criteria for Plan 2

- `StatementImportPipeline` runs ingest → import → fail-fast → persist; a blocking diagnostic (`Severity >= Error`) aborts with **nothing persisted** (spec §8), warnings/info do not.
- A committed import persists, in one transaction: an `ImportBatch` row, the ledger (entries idempotent on `(SourceSystem, SourceRecordReference)`; instruments/accounts upserted), and the diagnostics linked to the batch.
- Re-importing overlapping source references is idempotent (no duplicate entries; a new batch row each run).
- Raw files are ingested to an audit folder and referenced as the import's `SourceLocation`.
- Reference-data tables (Basiszins, year-end prices, instrument profiles, FX rates) exist and are seeded from the shipped files, seed-if-empty (idempotent). Canonical files live in `data/reference/`.
- End-to-end test imports a real IBKR XML fixture and reloads it from SQLite.
- `dotnet test "WealthIQ.slnx"` is green.

## Notes for later plans (not in scope here)

- **DB-backed reference adapters** (`IBasisInterestRateProvider`, `IYearEndPriceProvider`, `IInstrumentProfileEnricher`, `IFxRateLookup` reading the seeded rows instead of files) belong to **Plan 3**, where the tax replay consumes them. The `DbFxRateLookup` must reproduce the `NextAvailableOnOrAfter` behavior of `CsvFxRateLookup`.
- **EF Core migrations**: still deferred to Plan 3 (the Web host). Tests use `EnsureCreated()` via `InMemorySqlite`, which now creates all new tables automatically.
- **Tax replay from DB + Blazor dashboard** (Import page, Steuerreport page, Diagnostics/Audit page with drill-down to the persisted batch + provenance) are **Plan 3**.
- **DI wiring + production DB path + reference-data seeding on startup** happen in the Web composition root (**Plan 3**); `ReferenceDataSources` will point at `data/reference/`.
- **`CLAUDE.md` replacing `AGENTS.md`** (which still describes the pre-Plan-1 layout) is a separate follow-up.
```

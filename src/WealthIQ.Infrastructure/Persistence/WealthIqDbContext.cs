using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence;

public sealed class WealthIqDbContext(DbContextOptions<WealthIqDbContext> options) : DbContext(options)
{
    public DbSet<PortfolioEntryRow> PortfolioEntries => Set<PortfolioEntryRow>();
    public DbSet<InstrumentRow> Instruments => Set<InstrumentRow>();
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<ImportBatchRow> ImportBatches => Set<ImportBatchRow>();
    public DbSet<ImportDiagnosticRow> ImportDiagnostics => Set<ImportDiagnosticRow>();
    public DbSet<BasisInterestRateRow> BasisInterestRates => Set<BasisInterestRateRow>();
    public DbSet<YearEndPriceRow> YearEndPrices => Set<YearEndPriceRow>();
    public DbSet<InstrumentProfileRow> InstrumentProfiles => Set<InstrumentProfileRow>();
    public DbSet<FxRateRow> FxRates => Set<FxRateRow>();
    public DbSet<HistoricalPriceRow> HistoricalPrices => Set<HistoricalPriceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioEntryRow>(e =>
        {
            e.HasKey(x => x.EntryId);
            e.HasIndex(x => new { x.SourceSystem, x.SourceRecordReference }).IsUnique();
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

        modelBuilder.Entity<HistoricalPriceRow>(e =>
        {
            e.HasKey(x => new { x.ProviderSymbol, x.Date });
        });
    }
}

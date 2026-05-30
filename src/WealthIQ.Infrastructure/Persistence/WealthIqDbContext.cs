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
    }
}

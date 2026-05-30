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

using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class ClearServiceTests
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
    public async Task ClearLedgerAsync_DeletesLedgerTablesLeavesReferenceDataIntact()
    {
        using var db = NewDb();
        // seed reference data
        db.BasisInterestRates.Add(new BasisInterestRateRow { Year = 2024, Rate = 0.0229m });
        db.InstrumentProfiles.Add(new InstrumentProfileRow { Isin = "IE00TEST", Name = "Test", Type = "ETF_EQUITY", Teilfreistellungsquote = 0.3m, SubjectToVorabpauschale = true });
        // seed ledger-side data
        db.Accounts.Add(new AccountRow { AccountId = Guid.NewGuid(), AccountNumber = "U123" });
        db.ImportBatches.Add(new ImportBatchRow { BatchId = Guid.NewGuid(), AccountId = Guid.NewGuid(), ImportedAt = DateTimeOffset.UtcNow, Status = "Committed" });
        db.SaveChanges();

        var service = new DbLedgerClearService(db, auditDirectory: null);
        await service.ClearLedgerAsync(purgeRawAuditFiles: false);

        Assert.Empty(db.Accounts);
        Assert.Empty(db.ImportBatches);
        // Reference data must be untouched
        Assert.Single(db.BasisInterestRates);
        Assert.Single(db.InstrumentProfiles);
    }

    [Fact]
    public async Task ClearAsync_HistoricalPrices_EmptiesOnlyThatTable()
    {
        using var db = NewDb();
        db.HistoricalPrices.Add(new HistoricalPriceRow { ProviderSymbol = "VUSA.L", Date = new DateOnly(2024, 12, 30), Currency = "GBP", Open = 1, High = 1, Low = 1, Close = 1, AdjustedClose = 1, Volume = 1 });
        db.BasisInterestRates.Add(new BasisInterestRateRow { Year = 2024, Rate = 0.0229m });
        db.SaveChanges();

        var service = new DbReferenceDataClearService(db);
        await service.ClearAsync(ReferenceDataset.HistoricalPrices);

        Assert.Empty(db.HistoricalPrices);
        Assert.Single(db.BasisInterestRates); // untouched
    }
}

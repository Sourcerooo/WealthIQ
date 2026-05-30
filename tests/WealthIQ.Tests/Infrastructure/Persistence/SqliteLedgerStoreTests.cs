using WealthIQ.Application.Persistence;
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

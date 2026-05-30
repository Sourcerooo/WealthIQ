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

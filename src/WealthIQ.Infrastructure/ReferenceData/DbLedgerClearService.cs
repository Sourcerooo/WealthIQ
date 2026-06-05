using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Transactionally clears all ledger data (PortfolioEntries, ImportBatches,
/// ImportDiagnostics, Accounts). Reference/market data is untouched. Optionally purges
/// raw audit files from disk (spec §10).</summary>
public sealed class DbLedgerClearService(WealthIqDbContext db, string? auditDirectory) : ILedgerClearService
{
    public async Task ClearLedgerAsync(bool purgeRawAuditFiles, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.ImportDiagnostics.RemoveRange(db.ImportDiagnostics);
        db.ImportBatches.RemoveRange(db.ImportBatches);
        db.PortfolioEntries.RemoveRange(db.PortfolioEntries);
        db.Instruments.RemoveRange(db.Instruments);
        db.Accounts.RemoveRange(db.Accounts);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (purgeRawAuditFiles && !string.IsNullOrWhiteSpace(auditDirectory) && Directory.Exists(auditDirectory))
        {
            foreach (var file in Directory.GetFiles(auditDirectory, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }
    }
}

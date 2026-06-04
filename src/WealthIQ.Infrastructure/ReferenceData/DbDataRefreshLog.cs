using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

public sealed class DbDataRefreshLog(WealthIqDbContext db) : IDataRefreshLog
{
    public async Task<DateTimeOffset?> GetLastRefreshedAsync(string dataset, CancellationToken ct = default)
    {
        var row = await db.DataRefreshLog.FindAsync(new object[] { dataset }, ct);
        return row?.LastRefreshedUtc;
    }

    public async Task RecordAsync(string dataset, DateTimeOffset whenUtc, string? note, CancellationToken ct = default)
    {
        var row = await db.DataRefreshLog.FindAsync(new object[] { dataset }, ct);
        if (row is null)
        {
            db.DataRefreshLog.Add(new DataRefreshLogRow { Dataset = dataset, LastRefreshedUtc = whenUtc, Note = note });
        }
        else
        {
            row.LastRefreshedUtc = whenUtc;
            row.Note = note;
        }

        await db.SaveChangesAsync(ct);
    }
}

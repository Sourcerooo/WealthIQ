using WealthIQ.Domain.Model.General;

namespace WealthIQ.Domain.Model.Ledger;

public sealed record PortfolioLedger
{
    public PortfolioLedger(
        IReadOnlyList<PortfolioEntry> entries,
        IReadOnlyList<Instrument>? instruments = null,
        IReadOnlyList<Account>? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Deterministic order: chronological, then by the source's record reference
        // (e.g. the broker transaction id, issued in booking order). This keeps FIFO lot
        // matching reproducible for entries that share a timestamp — a random tie-break
        // would otherwise change disposal results from run to run. EntryId is NOT used as a
        // tie-break because it is a random GUID.
        Entries = entries
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.SourceProvenance.SourceRecordReference, StringComparer.Ordinal)
            .ToList();
        Instruments = instruments ?? [];
        Accounts = accounts ?? [];
    }

    public IReadOnlyList<PortfolioEntry> Entries { get; }
    public IReadOnlyList<Instrument> Instruments { get; }
    public IReadOnlyList<Account> Accounts { get; }
}

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

        Entries = entries.OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.EntryId.Value)
            .ToList();
        Instruments = instruments ?? [];
        Accounts = accounts ?? [];
    }

    public IReadOnlyList<PortfolioEntry> Entries { get; }
    public IReadOnlyList<Instrument> Instruments { get; }
    public IReadOnlyList<Account> Accounts { get; }
}

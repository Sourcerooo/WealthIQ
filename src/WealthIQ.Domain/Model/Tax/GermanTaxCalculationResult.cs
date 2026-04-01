using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Domain.Model.Tax;

public sealed record GermanTaxCalculationResult(
    IReadOnlyList<GermanTaxEntry> Entries,
    IReadOnlyList<OpenLot> OpenLots);

using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Tax;

public sealed class InstrumentCatalogBuilder(IInstrumentProfileEnricher profileEnricher)
{
    public IReadOnlyList<Instrument> Build(IReadOnlyList<Instrument> importedInstruments)
    {
        ArgumentNullException.ThrowIfNull(importedInstruments);

        return importedInstruments
            .Select(profileEnricher.Enrich)
            .GroupBy(x => x.InstrumentId)
            .Select(x => x.Last())
            .OrderBy(x => x.Symbol)
            .ThenBy(x => x.ISIN)
            .ToList();
    }
}

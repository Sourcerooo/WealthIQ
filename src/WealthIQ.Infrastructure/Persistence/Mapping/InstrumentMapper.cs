using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class InstrumentMapper
{
    public static InstrumentRow ToRow(Instrument instrument) => new()
    {
        InstrumentId = instrument.InstrumentId.Value,
        ISIN = instrument.ISIN,
        Symbol = instrument.Symbol,
        Name = instrument.Name,
        Teilfreistellungsquote = instrument.Teilfreistellungsquote
    };

    public static Instrument ToDomain(InstrumentRow row) => new(
        new InstrumentId(row.InstrumentId),
        row.ISIN,
        row.Symbol,
        row.Name,
        row.Teilfreistellungsquote);
}

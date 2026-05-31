using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Tax.Interface;

public interface IInstrumentProfileEnricher
{
    Instrument Enrich(Instrument instrument);
}

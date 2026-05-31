using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.MarketData.Interface;

public interface IInstrumentMarketDataMap
{
    InstrumentMarketDataProfile GetProfile(Instrument instrument);
}

using WealthIQ.Application.MarketData;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData.Interface;

public interface IInstrumentMarketDataMap
{
    /// <summary>Resolves the provider listing for an instrument held in <paramref name="currency"/>.
    /// A missing listing for the held (ISIN, currency) is a blocking error (spec §4).</summary>
    InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency);
}

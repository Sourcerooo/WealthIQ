using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Infrastructure.Persistence;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Resolves (ISIN, currency) → provider listing from the <c>InstrumentListings</c> table.
/// A missing listing for a held (ISIN, currency) is a blocking error (spec §4, §5.4).</summary>
public sealed class DbInstrumentMarketDataMap : IInstrumentMarketDataMap
{
    private readonly Dictionary<(string Isin, CurrencyCode Currency), InstrumentMarketDataProfile> _profiles = new();

    public DbInstrumentMarketDataMap(WealthIqDbContext db)
    {
        foreach (var row in db.InstrumentListings)
        {
            if (!Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency))
            {
                continue;
            }

            _profiles[(row.Isin, currency)] = new InstrumentMarketDataProfile(row.Provider, row.ProviderSymbol, row.Notes);
        }
    }

    public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
    {
        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new InvalidOperationException("Instrument has no ISIN and cannot be mapped to market data.");
        }

        if (_profiles.TryGetValue((isin, currency), out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException($"No market-data listing configured for instrument '{isin}' in {currency}.");
    }
}

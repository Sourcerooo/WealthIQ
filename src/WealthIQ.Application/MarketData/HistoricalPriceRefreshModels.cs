using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.MarketData;

public sealed record HistoricalPriceSymbol(string ProviderSymbol, CurrencyCode Currency);

public interface IHistoricalPriceStore
{
    IReadOnlyList<HistoricalPriceSymbol> GetConfiguredListings();
    DateOnly? GetMaxStoredDate(string providerSymbol);
    void DeleteSymbol(string providerSymbol);
    /// <returns>(added, updated)</returns>
    (int Added, int Updated) Upsert(IReadOnlyList<PriceBar> bars);
    Task SaveChangesAsync(CancellationToken ct);
}

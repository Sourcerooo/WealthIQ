using WealthIQ.Application.MarketData;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Writes to the <c>HistoricalPrices</c> table. <c>GetConfiguredListings</c> reads distinct
/// (ProviderSymbol, Currency) from <c>InstrumentListings</c>. <c>Upsert</c> adds new bars or updates
/// existing ones by (ProviderSymbol, Date).</summary>
public sealed class DbHistoricalPriceStore(WealthIqDbContext db) : IHistoricalPriceStore
{
    public IReadOnlyList<HistoricalPriceSymbol> GetConfiguredListings()
    {
        return db.InstrumentListings
            .Where(x => !string.IsNullOrEmpty(x.ProviderSymbol))
            .AsEnumerable()
            .Select(x => Enum.TryParse<CurrencyCode>(x.Currency, ignoreCase: true, out var c)
                ? new HistoricalPriceSymbol(x.ProviderSymbol, c)
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .DistinctBy(x => x.ProviderSymbol)
            .ToList();
    }

    public DateOnly? GetMaxStoredDate(string providerSymbol)
    {
        return db.HistoricalPrices
            .Where(x => x.ProviderSymbol == providerSymbol)
            .Select(x => (DateOnly?)x.Date)
            .Max();
    }

    public void DeleteSymbol(string providerSymbol)
    {
        var existing = db.HistoricalPrices.Where(x => x.ProviderSymbol == providerSymbol).ToList();
        db.HistoricalPrices.RemoveRange(existing);
    }

    public (int Added, int Updated) Upsert(IReadOnlyList<PriceBar> bars)
    {
        var added = 0;
        var updated = 0;

        foreach (var bar in bars)
        {
            var existing = db.HistoricalPrices.Find(bar.ProviderSymbol, bar.Date);
            if (existing is null)
            {
                db.HistoricalPrices.Add(new HistoricalPriceRow
                {
                    ProviderSymbol = bar.ProviderSymbol, Date = bar.Date, Currency = bar.Currency.ToString(),
                    Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close,
                    AdjustedClose = bar.AdjustedClose, Volume = bar.Volume
                });
                added++;
            }
            else
            {
                existing.Currency = bar.Currency.ToString();
                existing.Open = bar.Open; existing.High = bar.High; existing.Low = bar.Low;
                existing.Close = bar.Close; existing.AdjustedClose = bar.AdjustedClose; existing.Volume = bar.Volume;
                updated++;
            }
        }

        return (added, updated);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct).ContinueWith(_ => { }, ct);
}

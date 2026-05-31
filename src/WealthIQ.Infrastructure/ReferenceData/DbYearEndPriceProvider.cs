using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Year-end prices from the seeded <c>YearEndPrices</c> table. Loaded once on construction.</summary>
public sealed class DbYearEndPriceProvider : IYearEndPriceProvider
{
    private readonly Dictionary<(int Year, string Isin), decimal> _prices;

    public DbYearEndPriceProvider(WealthIqDbContext db)
    {
        _prices = db.YearEndPrices.ToDictionary(x => (x.Year, x.Isin), x => x.PriceEur);
    }

    public decimal? GetPrice(string isin, int year)
        => _prices.TryGetValue((year, isin), out var price) ? price : null;
}

using WealthIQ.Application.Tax.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Basis interest rates from the seeded <c>BasisInterestRates</c> table. Loaded once on construction.</summary>
public sealed class DbBasisInterestRateProvider : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates;

    public DbBasisInterestRateProvider(WealthIqDbContext db)
    {
        _rates = db.BasisInterestRates.ToDictionary(x => x.Year, x => x.Rate);
    }

    public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
}

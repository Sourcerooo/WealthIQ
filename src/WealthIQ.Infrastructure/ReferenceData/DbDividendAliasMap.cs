using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>Dividend alias → ISIN lookup from the seeded <c>DividendAliases</c> table.
/// Loaded once on construction (mirrors <c>DbBasisInterestRateProvider</c>).</summary>
public sealed class DbDividendAliasMap : IDividendAliasMap
{
    private readonly Dictionary<string, string> _byNormalizedAlias;

    public DbDividendAliasMap(WealthIqDbContext db)
    {
        _byNormalizedAlias = db.DividendAliases.ToDictionary(x => x.NormalizedAlias, x => x.Isin);
    }

    public string? ResolveIsin(string alias)
        => _byNormalizedAlias.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var isin) ? isin : null;
}

namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>CRUD for the dividend alias → ISIN mapping (Stammdaten UI editing).</summary>
public interface IDividendAliasStore
{
    void Upsert(string alias, string isin);
    void Delete(string normalizedAlias);
    Task SaveChangesAsync(CancellationToken ct);
}

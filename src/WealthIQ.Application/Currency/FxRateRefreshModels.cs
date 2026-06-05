namespace WealthIQ.Application.Currency;

public interface IFxRateStore
{
    /// <summary>Upsert FX rate records by (Date, Currency). Returns (added, updated).</summary>
    (int Added, int Updated) Upsert(IReadOnlyList<FxRateRecord> records);
    Task SaveChangesAsync(CancellationToken ct);
}

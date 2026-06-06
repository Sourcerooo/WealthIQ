namespace WealthIQ.Application.Currency;

public interface IFxRateStore
{
    /// <summary>Upsert FX rate records by (Date, Currency). Returns (added, updated).</summary>
    (int Added, int Updated) Upsert(IReadOnlyList<FxRateRecord> records);

    /// <summary>Currencies that already have stored rows (distinct), excluding the EUR base.</summary>
    IReadOnlyList<string> GetStoredCurrencies();

    /// <summary>Latest stored date across all currencies, or null if the table is empty.</summary>
    DateOnly? GetMaxStoredDate();

    Task SaveChangesAsync(CancellationToken ct);
}

namespace WealthIQ.Application.Tax;

public interface IBasisInterestRateStore
{
    void Upsert(int year, decimal rate);
    Task SaveChangesAsync(CancellationToken ct);
}

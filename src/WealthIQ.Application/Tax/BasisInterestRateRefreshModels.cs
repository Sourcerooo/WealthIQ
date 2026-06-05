namespace WealthIQ.Application.Tax;

public interface IBasisInterestRateStore
{
    void Upsert(int year, decimal rate);
    void Delete(int year);
    Task SaveChangesAsync(CancellationToken ct);
}

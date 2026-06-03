namespace WealthIQ.Application.Tax.Interface;

public interface IBasisInterestRateSource
{
    /// <summary>The official BMF Basiszins for <paramref name="year"/>, or <c>null</c> if it cannot be obtained.</summary>
    Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken ct);
}

namespace WealthIQ.Application.Currency.Interface;

public interface IFxRateProvider
{
    Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

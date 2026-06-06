namespace WealthIQ.Application.Currency.Interface;

public interface IFxRateProvider
{
    /// <summary>Fetches FX rates in [from, to]. When <paramref name="currencies"/> is non-null, only
    /// those currency codes are returned (EUR base is always implied); when null the provider's
    /// configured default set is used.</summary>
    Task<IReadOnlyList<FxRateRecord>> FetchAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<string>? currencies, CancellationToken ct);
}

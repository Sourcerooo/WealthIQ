namespace WealthIQ.Application.Currency;

public sealed record FxRateRecord(DateOnly Date, string Currency, decimal RateToEur);

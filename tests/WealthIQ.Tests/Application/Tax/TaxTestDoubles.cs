using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Tests.Application.Tax;

/// <summary>Configurable test doubles + entry builders shared by the GermanTaxCalculator test suites.</summary>
internal sealed class FakeBasisInterestRateProvider(params (int Year, decimal Rate)[] rates) : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates = rates.ToDictionary(x => x.Year, x => x.Rate);

    public decimal GetRate(int year) => _rates.GetValueOrDefault(year);
}

internal sealed class FakeYearEndPriceProvider(params (string Isin, int Year, decimal Price)[] prices) : IYearEndPriceProvider
{
    private readonly Dictionary<(string Isin, int Year), decimal> _prices = prices.ToDictionary(x => (x.Isin, x.Year), x => x.Price);

    public decimal? GetPrice(string isin, int year) => _prices.TryGetValue((isin, year), out var price) ? price : null;
}

/// <summary>Identity for same-currency conversions; otherwise returns a configured rate or throws.</summary>
internal sealed class FakeFxRateLookup(params (DateOnly Date, Currency Currency, decimal Rate)[] rates) : IFxRateLookup
{
    private readonly Dictionary<(DateOnly Date, Currency Currency), decimal> _rates =
        rates.ToDictionary(x => (x.Date, x.Currency), x => x.Rate);

    public decimal GetRate(
        DateOnly conversionDate,
        Currency sourceCurrency,
        Currency targetCurrency,
        FxRateLookupDateHandling dateHandling = FxRateLookupDateHandling.ExactDate)
    {
        if (sourceCurrency == targetCurrency)
        {
            return 1m;
        }

        return _rates.TryGetValue((conversionDate, sourceCurrency), out var rate)
            ? rate
            : throw new InvalidOperationException($"No FX rate configured for {sourceCurrency} on {conversionDate:yyyy-MM-dd}.");
    }
}

internal static class TaxEntries
{
    public static SourceProvenance Provenance(string reference) => new()
    {
        SourceSystem = "IBKR",
        ImportFormat = "TEST",
        SourceLocation = "unit-test",
        SourceRecordReference = reference
    };

    public static TradeEntry Trade(
        AccountId accountId,
        InstrumentId instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset occurredAt,
        string reference,
        Currency currency = Currency.EUR,
        decimal fees = 0m,
        decimal taxes = 0m)
        => new(
            PortfolioEntryId.NewId(),
            accountId,
            occurredAt,
            DateOnly.FromDateTime(occurredAt.UtcDateTime),
            Provenance(reference),
            instrumentId,
            side,
            new Quantity(quantity),
            new Money(unitPrice, currency),
            new Money(fees, currency),
            new Money(taxes, currency));

    public static CashEntry Dividend(
        AccountId accountId,
        InstrumentId cashInstrumentId,
        InstrumentId relatedInstrumentId,
        decimal grossAmount,
        DateTimeOffset occurredAt,
        string reference,
        Currency currency = Currency.EUR)
        => new(
            PortfolioEntryId.NewId(),
            accountId,
            occurredAt,
            DateOnly.FromDateTime(occurredAt.UtcDateTime),
            Provenance(reference),
            cashInstrumentId,
            CashFlowType.Dividend,
            new Money(grossAmount, currency),
            new Money(0m, currency),
            new Money(0m, currency),
            relatedInstrumentId);
}

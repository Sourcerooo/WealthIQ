using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

/// <summary>Configurable test doubles + entry builders shared by the GermanTaxCalculator test suites.</summary>
internal sealed class FakeBasisInterestRateProvider(params (int Year, decimal Rate)[] rates) : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates = rates.ToDictionary(x => x.Year, x => x.Rate);

    public decimal? GetRate(int year) => _rates.TryGetValue(year, out var rate) ? rate : null;
}

/// <summary>Returns the same price for both year-start and year-end lookups (keyed by ISIN+year).
/// Suitable for tests that don't need to distinguish start vs. end (e.g. cap-test with same price).
/// For tests that need separate start/end prices use <see cref="FakeYearStartAndEndPriceProvider"/>.</summary>
internal sealed class FakeYearEndPriceProvider(params (string Isin, int Year, decimal Price)[] prices) : IInstrumentPriceProvider
{
    private readonly Dictionary<(string Isin, int Year), decimal> _prices = prices.ToDictionary(x => (x.Isin, x.Year), x => x.Price);

    public InstrumentQuote? GetQuote(string isin, WealthIQ.Domain.Enumeration.Currency currency, DateOnly pricingDate, PriceQuoteHandling handling)
    {
        // Look up by ISIN + year; ignore currency and handling (returns EUR directly, no FX needed).
        return _prices.TryGetValue((isin, pricingDate.Year), out var price)
            ? new InstrumentQuote(price, WealthIQ.Domain.Enumeration.Currency.EUR, pricingDate)
            : null;
    }
}

/// <summary>Returns separate year-start and year-end prices. Keys: (ISIN, year, handling).
/// Use <see cref="PriceQuoteHandling.EarliestOnOrAfter"/> for year-start and
/// <see cref="PriceQuoteHandling.LatestOnOrBefore"/> for year-end.</summary>
internal sealed class FakeYearStartAndEndPriceProvider : IInstrumentPriceProvider
{
    private readonly Dictionary<(string Isin, int Year, PriceQuoteHandling Handling), decimal> _prices = new();

    public FakeYearStartAndEndPriceProvider AddStart(string isin, int year, decimal price)
    {
        _prices[(isin, year, PriceQuoteHandling.EarliestOnOrAfter)] = price;
        return this;
    }

    public FakeYearStartAndEndPriceProvider AddEnd(string isin, int year, decimal price)
    {
        _prices[(isin, year, PriceQuoteHandling.LatestOnOrBefore)] = price;
        return this;
    }

    public InstrumentQuote? GetQuote(string isin, WealthIQ.Domain.Enumeration.Currency currency, DateOnly pricingDate, PriceQuoteHandling handling)
    {
        return _prices.TryGetValue((isin, pricingDate.Year, handling), out var price)
            ? new InstrumentQuote(price, WealthIQ.Domain.Enumeration.Currency.EUR, pricingDate)
            : null;
    }
}

/// <summary>Identity for same-currency conversions; otherwise returns a configured rate or throws.</summary>
internal sealed class FakeFxRateLookup(params (DateOnly Date, CurrencyCode Currency, decimal Rate)[] rates) : IFxRateLookup
{
    private readonly Dictionary<(DateOnly Date, CurrencyCode Currency), decimal> _rates =
        rates.ToDictionary(x => (x.Date, x.Currency), x => x.Rate);

    public decimal GetRate(
        DateOnly conversionDate,
        CurrencyCode sourceCurrency,
        CurrencyCode targetCurrency,
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
        CurrencyCode currency = CurrencyCode.EUR,
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
        CurrencyCode currency = CurrencyCode.EUR)
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

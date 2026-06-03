using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Tax.Interface;

/// <summary>The redemption price (Close) for an instrument's listing in a given currency, resolved by date.
/// Close is in <see cref="CurrencyCode"/>; the CALLER converts to EUR (FX stays in the calculator). The
/// calculator turns a <c>null</c> result into a blocking error (spec §5.4).</summary>
public readonly record struct InstrumentQuote(decimal Close, CurrencyCode Currency, DateOnly AsOf);

public enum PriceQuoteHandling
{
    LatestOnOrBefore,
    EarliestOnOrAfter,
    ExactDate
}

public interface IInstrumentPriceProvider
{
    InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling);
}

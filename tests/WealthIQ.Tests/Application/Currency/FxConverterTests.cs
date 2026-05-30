using WealthIQ.Application.Currency;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Tests.Application.Tax;
using Xunit;

namespace WealthIQ.Tests.Application.CurrencyTests;

public sealed class FxConverterTests
{
    [Fact]
    public void Convert_SameCurrency_ReturnsAmountUnchangedInBaseCurrency()
    {
        var converter = new FxConverter(new FakeFxRateLookup(), Currency.EUR);

        var result = converter.Convert(new Money(123.45m, Currency.EUR), new DateOnly(2024, 1, 1));

        Assert.Equal(123.45m, result.Amount);
        Assert.Equal(Currency.EUR, result.Currency);
    }

    [Fact]
    public void Convert_ForeignCurrency_AppliesRateAndReturnsBaseCurrency()
    {
        var converter = new FxConverter(
            new FakeFxRateLookup((new DateOnly(2021, 3, 26), Currency.USD, 0.85m)),
            Currency.EUR);

        var result = converter.Convert(new Money(100m, Currency.USD), new DateOnly(2021, 3, 26));

        Assert.Equal(85m, result.Amount); // 100 USD × 0.85
        Assert.Equal(Currency.EUR, result.Currency);
    }
}

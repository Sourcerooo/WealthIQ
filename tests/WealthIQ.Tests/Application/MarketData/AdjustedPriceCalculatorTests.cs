using WealthIQ.Application.MarketData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.MarketData;

public sealed class AdjustedPriceCalculatorTests
{
    private static PriceBar Bar(decimal o, decimal h, decimal l, decimal c, decimal adj)
        => new(new DateOnly(2024, 1, 2), "TEST", CurrencyCode.EUR, o, h, l, c, adj, 0);

    [Fact]
    public void ToAdjusted_ScalesOhlcByAdjustmentFactor()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 100m, 50m) });

        var bar = Assert.Single(result);
        // factor = 50/100 = 0.5
        Assert.Equal(50m, bar.Open);
        Assert.Equal(55m, bar.High);
        Assert.Equal(45m, bar.Low);
        Assert.Equal(50m, bar.Close);
    }

    [Fact]
    public void ToAdjusted_CloseZero_LeavesBarUnscaled()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 0m, 0m) });

        var bar = Assert.Single(result);
        Assert.Equal(100m, bar.Open);
        Assert.Equal(110m, bar.High);
        Assert.Equal(90m, bar.Low);
        Assert.Equal(0m, bar.Close);
    }

    [Fact]
    public void ToAdjusted_NoAdjustment_ReturnsSameValues()
    {
        var result = AdjustedPriceCalculator.ToAdjusted(new[] { Bar(100m, 110m, 90m, 100m, 100m) });

        var bar = Assert.Single(result);
        Assert.Equal(100m, bar.Open);
        Assert.Equal(110m, bar.High);
        Assert.Equal(90m, bar.Low);
        Assert.Equal(100m, bar.Close);
    }
}

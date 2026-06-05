namespace WealthIQ.Application.MarketData;

/// <summary>
/// Derives split/dividend-adjusted OHLC candles from raw bars that carry a single
/// <see cref="PriceBar.AdjustedClose"/>. For each bar the factor <c>AdjustedClose / Close</c>
/// scales Open/High/Low; Close becomes AdjustedClose. Bars with a non-positive Close are passed
/// through unscaled (no factor can be derived). This is for visual inspection only — the tax
/// engine continues to use raw <c>Close</c>.
/// </summary>
public static class AdjustedPriceCalculator
{
    public static IReadOnlyList<PriceBar> ToAdjusted(IReadOnlyList<PriceBar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var result = new List<PriceBar>(bars.Count);
        foreach (var bar in bars)
        {
            if (bar.Close <= 0m)
            {
                result.Add(bar);
                continue;
            }

            var factor = bar.AdjustedClose / bar.Close;
            result.Add(bar with
            {
                Open = bar.Open * factor,
                High = bar.High * factor,
                Low = bar.Low * factor,
                Close = bar.AdjustedClose,
            });
        }

        return result;
    }
}

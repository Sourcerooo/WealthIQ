using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Lot;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Valuation;

public sealed class PortfolioValuationService(
    IHistoricalPriceLookup historicalPriceLookup,
    IInstrumentMarketDataMap instrumentMarketDataMap,
    IFxRateLookup fxRateLookup)
{
    private readonly FxConverter _fxConverter = new(fxRateLookup, WealthIQ.Domain.Enumeration.Currency.EUR);

    /// <summary>Values the portfolio as of <paramref name="valuationDate"/>. Resilient for the CURRENT
    /// valuation: a missing market-data mapping, current price, or current-FX rate flags that position
    /// (<see cref="PortfolioPositionSnapshot.PriceMissing"/>) instead of throwing. NOTE: a missing FX rate
    /// at a HISTORIC trade's own date (needed for cost basis) is a data-integrity problem and still throws
    /// — callers (e.g. the dashboard page) surface it as an error rather than silently mis-stating cost.</summary>
    public PortfolioValuationSnapshot Calculate(
        PortfolioLedger portfolioLedger,
        IReadOnlyList<Instrument> instruments,
        DateOnly valuationDate)
    {
        ArgumentNullException.ThrowIfNull(portfolioLedger);
        ArgumentNullException.ThrowIfNull(instruments);

        var instrumentById = instruments.ToDictionary(x => x.InstrumentId);
        var openLots = ReplayOpenLots(portfolioLedger, valuationDate);
        var cashByCurrency = ReplayCashBalances(portfolioLedger, valuationDate);

        var positionSnapshots = new List<PortfolioPositionSnapshot>();
        var effectiveMarketDates = new List<DateOnly>();

        foreach (var instrumentLots in openLots
                     .Where(x => x.RemainingQuantity.Value > 0m)
                     .GroupBy(x => new { x.AccountId, x.InstrumentId, x.Direction }))
        {
            var instrument = instrumentById[instrumentLots.Key.InstrumentId];
            var lots = instrumentLots.ToList();
            var quantity = lots.Sum(x => x.RemainingQuantity.Value);

            // Cost basis in EUR: convert each lot's remaining cost at that lot's own open date
            // (the project's FX-at-event-time rule), so mixed-currency lots never average raw prices.
            var costBasisEur = 0m;
            foreach (var lot in lots)
            {
                var lotCostNative = new Money(
                    lot.OpenUnitPrice.Amount * lot.RemainingQuantity.Value
                        + lot.RemainingOpenFees.Amount
                        + lot.RemainingOpenTaxes.Amount,
                    lot.OpenUnitPrice.Currency);
                costBasisEur += _fxConverter.Convert(lotCostNative, lot.OpenTradeDate).Amount;
            }

            var nativeCurrency = lots[0].OpenUnitPrice.Currency;
            var singleCurrency = lots.All(x => x.OpenUnitPrice.Currency == nativeCurrency);
            decimal? avgBuyNative = singleCurrency && quantity != 0m
                ? lots.Sum(x => x.OpenUnitPrice.Amount * x.RemainingQuantity.Value + x.RemainingOpenFees.Amount + x.RemainingOpenTaxes.Amount) / quantity
                : null;
            var avgBuyEur = quantity != 0m ? costBasisEur / quantity : 0m;

            var directionSign = instrumentLots.Key.Direction == PositionDirection.Long ? 1m : -1m;

            // Resilient pricing: a missing mapping/price/FX rate must not blank the dashboard.
            decimal closePrice = 0m;
            CurrencyCode priceCurrency = nativeCurrency;
            decimal marketValueEur = 0m;
            DateOnly effectivePriceDate = valuationDate;
            string? providerSymbol = null;
            bool priceMissing = false;
            try
            {
                var marketDataProfile = instrumentMarketDataMap.GetProfile(instrument.ISIN ?? "", nativeCurrency);
                providerSymbol = marketDataProfile.ProviderSymbol;
                var priceBar = historicalPriceLookup.GetPriceBar(
                    valuationDate, marketDataProfile.ProviderSymbol, PriceLookupDateHandling.LatestOnOrBefore);
                closePrice = priceBar.Close;
                priceCurrency = priceBar.Currency;
                effectivePriceDate = priceBar.Date;
                var grossMarketValue = quantity * priceBar.Close * directionSign;
                marketValueEur = _fxConverter.Convert(new Money(grossMarketValue, priceBar.Currency), priceBar.Date).Amount;
                effectiveMarketDates.Add(priceBar.Date);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                priceMissing = true;
            }

            var unrealizedPnlEur = priceMissing ? 0m : marketValueEur - costBasisEur;
            var unrealizedPnlPct = priceMissing || costBasisEur == 0m ? 0m : unrealizedPnlEur / costBasisEur;

            positionSnapshots.Add(new PortfolioPositionSnapshot(
                instrumentLots.Key.AccountId,
                instrument.InstrumentId,
                instrument.Symbol,
                string.IsNullOrWhiteSpace(instrument.ISIN) ? null : instrument.ISIN,
                instrumentLots.Key.Direction,
                quantity,
                closePrice,
                priceCurrency,
                marketValueEur,
                costBasisEur,
                avgBuyEur,
                avgBuyNative,
                nativeCurrency,
                unrealizedPnlEur,
                unrealizedPnlPct,
                instrument.Type,
                providerSymbol,
                effectivePriceDate,
                priceMissing));
        }

        var cashSnapshots = new List<PortfolioCashSnapshot>();
        foreach (var entry in cashByCurrency.OrderBy(x => x.Key))
        {
            var currency = Enum.Parse<WealthIQ.Domain.Enumeration.Currency>(entry.Key, true);
            // Cash conversion is resilient too: a missing FX rate must not blank the page.
            try
            {
                var amountInBase = _fxConverter.Convert(new Money(entry.Value, currency), valuationDate);
                cashSnapshots.Add(new PortfolioCashSnapshot(entry.Key, entry.Value, amountInBase.Amount));
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                cashSnapshots.Add(new PortfolioCashSnapshot(entry.Key, entry.Value, 0m));
            }
        }

        var effectiveMarketDate = effectiveMarketDates.Count == 0 ? valuationDate : effectiveMarketDates.Min();
        var total = positionSnapshots.Sum(x => x.PriceMissing ? x.CostBasisInBaseCurrency : x.MarketValueInBaseCurrency)
            + cashSnapshots.Sum(x => x.AmountInBaseCurrency);

        return new PortfolioValuationSnapshot(
            valuationDate,
            effectiveMarketDate,
            positionSnapshots,
            cashSnapshots,
            total);
    }

    private static List<OpenLot> ReplayOpenLots(PortfolioLedger portfolioLedger, DateOnly valuationDate)
    {
        var matcher = new Matcher.FiFoMatcher();
        var openLots = new List<OpenLot>();

        foreach (var tradeEntry in portfolioLedger.Entries
                     .OfType<TradeEntry>()
                     .Where(x => x.EffectiveDate <= valuationDate)
                     .OrderBy(x => x.OccurredAt))
        {
            var matchResult = matcher.Match(tradeEntry, openLots, LotMatchingPolicy.FIFO);
            openLots.Clear();
            openLots.AddRange(matchResult.UpdatedOpenLots);
            if (matchResult.NewlyOpenedRemainderLot is not null)
            {
                openLots.Add(matchResult.NewlyOpenedRemainderLot);
            }
        }

        return openLots;
    }

    private static Dictionary<string, decimal> ReplayCashBalances(PortfolioLedger portfolioLedger, DateOnly valuationDate)
    {
        var cashByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in portfolioLedger.Entries.Where(x => x.EffectiveDate <= valuationDate).OrderBy(x => x.OccurredAt))
        {
            switch (entry)
            {
                case TradeEntry tradeEntry:
                    var tradeCurrency = tradeEntry.UnitPrice.Currency.ToString();
                    var grossTradeAmount = tradeEntry.UnitPrice.Amount * tradeEntry.Quantity.Value;
                    var signedCashDelta = tradeEntry.Side == TradeSide.Buy
                        ? -(grossTradeAmount + tradeEntry.Fees.Amount + tradeEntry.Taxes.Amount)
                        : grossTradeAmount - tradeEntry.Fees.Amount - tradeEntry.Taxes.Amount;
                    cashByCurrency[tradeCurrency] = cashByCurrency.GetValueOrDefault(tradeCurrency) + signedCashDelta;
                    break;

                case CashEntry cashEntry:
                    var cashCurrency = cashEntry.GrossAmount.Currency.ToString();
                    var signedCashAmount = cashEntry.GrossAmount.Amount - cashEntry.Fees.Amount - cashEntry.Taxes.Amount;
                    cashByCurrency[cashCurrency] = cashByCurrency.GetValueOrDefault(cashCurrency) + signedCashAmount;
                    break;
            }
        }

        return cashByCurrency;
    }
}

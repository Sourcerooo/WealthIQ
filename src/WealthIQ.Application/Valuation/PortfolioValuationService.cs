using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Lot;

namespace WealthIQ.Application.Valuation;

public sealed class PortfolioValuationService(
    IHistoricalPriceLookup historicalPriceLookup,
    IInstrumentMarketDataMap instrumentMarketDataMap,
    IFxRateLookup fxRateLookup)
{
    private readonly FxConverter _fxConverter = new(fxRateLookup, WealthIQ.Domain.Enumeration.Currency.EUR);

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
            var lotCurrency = instrumentLots.First().OpenUnitPrice.Currency;
            var marketDataProfile = instrumentMarketDataMap.GetProfile(instrument.ISIN ?? "", lotCurrency);
            var priceBar = historicalPriceLookup.GetPriceBar(
                valuationDate,
                marketDataProfile.ProviderSymbol,
                PriceLookupDateHandling.LatestOnOrBefore);

            effectiveMarketDates.Add(priceBar.Date);
            var quantity = instrumentLots.Sum(x => x.RemainingQuantity.Value);
            var grossMarketValue = quantity * priceBar.Close;
            var signedMarketValue = instrumentLots.Key.Direction == PositionDirection.Long
                ? grossMarketValue
                : -grossMarketValue;
            var marketValueInBase = _fxConverter.Convert(new Money(signedMarketValue, priceBar.Currency), priceBar.Date);

            positionSnapshots.Add(new PortfolioPositionSnapshot(
                instrumentLots.Key.AccountId,
                instrument.InstrumentId,
                instrument.Symbol,
                string.IsNullOrWhiteSpace(instrument.ISIN) ? null : instrument.ISIN,
                instrumentLots.Key.Direction,
                quantity,
                priceBar.Close,
                priceBar.Currency,
                marketValueInBase.Amount));
        }

        var cashSnapshots = cashByCurrency
            .OrderBy(x => x.Key)
            .Select(x =>
            {
                var currency = Enum.Parse<WealthIQ.Domain.Enumeration.Currency>(x.Key, true);
                var amountInBase = _fxConverter.Convert(new Money(x.Value, currency), valuationDate);
                return new PortfolioCashSnapshot(x.Key, x.Value, amountInBase.Amount);
            })
            .ToList();

        var effectiveMarketDate = effectiveMarketDates.Count == 0 ? valuationDate : effectiveMarketDates.Min();
        var total = positionSnapshots.Sum(x => x.MarketValueInBaseCurrency)
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
                    var signedCashAmount = cashEntry.CashFlowType == CashFlowType.WithholdingTax
                        ? cashEntry.GrossAmount.Amount - cashEntry.Fees.Amount - cashEntry.Taxes.Amount
                        : cashEntry.GrossAmount.Amount - cashEntry.Fees.Amount - cashEntry.Taxes.Amount;
                    cashByCurrency[cashCurrency] = cashByCurrency.GetValueOrDefault(cashCurrency) + signedCashAmount;
                    break;
            }
        }

        return cashByCurrency;
    }
}

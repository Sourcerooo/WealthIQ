using WealthIQ.Application.Matcher;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax;

public sealed class GermanTaxCalculator(
    IBasisInterestRateProvider interestRateProvider,
    IYearEndPriceProvider yearEndPriceProvider)
{
    private readonly FiFoMatcher _matcher = new();

    public GermanTaxCalculationResult Calculate(
        PortfolioLedger portfolioLedger,
        IReadOnlyList<Instrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(portfolioLedger);
        ArgumentNullException.ThrowIfNull(instruments);

        var instrumentById = instruments.ToDictionary(x => x.InstrumentId);
        var openLots = new List<OpenLot>();
        var ledger = new List<GermanTaxEntry>();
        var distributions = new Dictionary<(int Year, InstrumentId InstrumentId), decimal>();

        foreach (var yearlyEntries in portfolioLedger.Entries
                     .OrderBy(x => x.OccurredAt)
                     .GroupBy(x => x.OccurredAt.Year)
                     .OrderBy(x => x.Key))
        {
            foreach (var portfolioEntry in yearlyEntries)
            {
                switch (portfolioEntry)
                {
                    case TradeEntry tradeEntry:
                        ProcessTrade(tradeEntry, openLots, ledger, instrumentById);
                        break;
                    case CashEntry cashEntry:
                        ProcessCash(cashEntry, openLots, ledger, distributions, instrumentById);
                        break;
                }
            }

            PerformYearEndClosing(yearlyEntries.Key, openLots, ledger, distributions, instrumentById);
        }

        return new GermanTaxCalculationResult(ledger, openLots);
    }

    private void ProcessTrade(
        TradeEntry tradeEntry,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        if (tradeEntry.Side == TradeSide.Buy)
        {
            var matchingShortLots = openLots.Any(x =>
                x.AccountId == tradeEntry.AccountId
                && x.InstrumentId == tradeEntry.InstrumentId
                && x.Direction == PositionDirection.Short
                && x.RemainingQuantity.Value > 0m);

            if (!matchingShortLots)
            {
                openLots.Add(CreateLongLot(tradeEntry));
                return;
            }
        }

        var matchResult = _matcher.Match(tradeEntry, openLots, LotMatchingPolicy.FIFO);
        var instrument = GetInstrument(instrumentById, tradeEntry.InstrumentId);

        foreach (var consumption in matchResult.Consumptions)
        {
            var originalLot = openLots.Single(x => x.LotId == consumption.OpenLotId);
            var updatedLot = matchResult.UpdatedOpenLots.Single(x => x.LotId == consumption.OpenLotId);
            var usedVorabpauschale = originalLot.AccumulatedVorabpauschale.Amount - updatedLot.AccumulatedVorabpauschale.Amount;
            var rawProfit = consumption.RealizedPnL.Amount - usedVorabpauschale;
            var taxableProfit = rawProfit * (1m - instrument.Teilfreistellungsquote);

            ledger.Add(new GermanTaxEntry(
                tradeEntry.OccurredAt.Year,
                DateOnly.FromDateTime(tradeEntry.OccurredAt.UtcDateTime),
                GermanTaxEntryType.Sell,
                instrument.Symbol,
                instrument.ISIN,
                rawProfit,
                taxableProfit,
                usedVorabpauschale,
                QuantitySold: consumption.MatchedQuantity.Value,
                SaleProceeds: consumption.Proceeds.Amount,
                AcquisitionCosts: consumption.CostBasis.Amount));
        }

        openLots.Clear();
        openLots.AddRange(matchResult.UpdatedOpenLots);

        if (matchResult.NewlyOpenedRemainderLot is not null)
        {
            openLots.Add(matchResult.NewlyOpenedRemainderLot);
        }
    }

    private static void ProcessCash(
        CashEntry cashEntry,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        Dictionary<(int Year, InstrumentId InstrumentId), decimal> distributions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var date = DateOnly.FromDateTime(cashEntry.OccurredAt.UtcDateTime);

        switch (cashEntry.CashFlowType)
        {
            case CashFlowType.Dividend:
                var dividendInstrument = GetInstrument(instrumentById, GetRelatedInstrumentId(cashEntry, CashFlowType.Dividend));
                var rawDividend = cashEntry.GrossAmount.Amount;
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Dividend,
                    dividendInstrument.Symbol,
                    dividendInstrument.ISIN,
                    rawDividend,
                    rawDividend * (1m - dividendInstrument.Teilfreistellungsquote)));

                var heldLots = openLots
                    .Where(x => x.InstrumentId == dividendInstrument.InstrumentId && x.RemainingQuantity.Value > 0m)
                    .ToList();

                var totalHeldQuantity = heldLots.Sum(x => x.RemainingQuantity.Value);
                if (totalHeldQuantity > 0m)
                {
                    var dividendPerShare = rawDividend / totalHeldQuantity;
                    var key = (cashEntry.OccurredAt.Year, dividendInstrument.InstrumentId);
                    distributions[key] = distributions.GetValueOrDefault(key) + dividendPerShare;
                }
                break;

            case CashFlowType.Interest:
                var interestInstrument = GetInstrument(instrumentById, cashEntry.CashInstrumentId);
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Interest,
                    interestInstrument.Symbol,
                    interestInstrument.ISIN,
                    cashEntry.GrossAmount.Amount,
                    cashEntry.GrossAmount.Amount));
                break;

            case CashFlowType.WithholdingTax:
                var withholdingInstrument = GetInstrument(instrumentById, GetRelatedInstrumentId(cashEntry, CashFlowType.WithholdingTax));
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.WithholdingTax,
                    withholdingInstrument.Symbol,
                    withholdingInstrument.ISIN,
                    cashEntry.GrossAmount.Amount,
                    0m,
                    ForeignWithholdingTax: Math.Abs(cashEntry.GrossAmount.Amount)));
                break;
        }
    }

    private void PerformYearEndClosing(
        int year,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        Dictionary<(int Year, InstrumentId InstrumentId), decimal> distributions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var basisInterestRate = interestRateProvider.GetRate(year);
        if (basisInterestRate <= 0m)
        {
            return;
        }

        var basisFactor = basisInterestRate * 0.7m;

        foreach (var instrumentGroup in openLots
                     .Where(x => x.Direction == PositionDirection.Long && x.RemainingQuantity.Value > 0m)
                     .GroupBy(x => x.InstrumentId))
        {
            var instrument = GetInstrument(instrumentById, instrumentGroup.Key);
            if (string.IsNullOrWhiteSpace(instrument.ISIN))
            {
                continue;
            }

            var yearEndPrice = yearEndPriceProvider.GetPrice(instrument.ISIN, year);
            if (!yearEndPrice.HasValue)
            {
                continue;
            }

            var distributionPerShare = distributions.GetValueOrDefault((year, instrument.InstrumentId));
            foreach (var lot in instrumentGroup.ToList())
            {
                var acquisitionPrice = lot.OpenUnitPrice.Amount;
                if (lot.RemainingQuantity.Value > 0m)
                {
                    acquisitionPrice += (lot.RemainingOpenFees.Amount + lot.RemainingOpenTaxes.Amount) / lot.RemainingQuantity.Value;
                }
                var months = 12m;
                if (lot.OpenTradeDate.Year == year)
                {
                    months = 12m - lot.OpenTradeDate.Month + 1m;
                }

                var basisYield = acquisitionPrice * basisFactor * (months / 12m);
                var appreciation = Math.Max(0m, yearEndPrice.Value - acquisitionPrice);
                var maxVorabpauschale = Math.Min(basisYield, appreciation);
                var actualVorabpauschalePerShare = Math.Max(0m, maxVorabpauschale - distributionPerShare);
                if (actualVorabpauschalePerShare <= 0m)
                {
                    continue;
                }

                var totalVorabpauschale = actualVorabpauschalePerShare * lot.RemainingQuantity.Value;
                ReplaceLot(openLots, lot with
                {
                    AccumulatedVorabpauschale = new Money(lot.AccumulatedVorabpauschale.Amount + totalVorabpauschale, Currency.EUR)
                });

                ledger.Add(new GermanTaxEntry(
                    year + 1,
                    new DateOnly(year + 1, 1, 1),
                    GermanTaxEntryType.Vorabpauschale,
                    instrument.Symbol,
                    instrument.ISIN,
                    totalVorabpauschale,
                    totalVorabpauschale * (1m - instrument.Teilfreistellungsquote)));
            }
        }
    }

    private static Instrument GetInstrument(
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById,
        InstrumentId instrumentId)
    {
        if (instrumentById.TryGetValue(instrumentId, out var instrument))
        {
            return instrument;
        }

        throw new InvalidOperationException($"Instrument '{instrumentId}' not found in catalog.");
    }

    private static void ReplaceLot(List<OpenLot> openLots, OpenLot updatedLot)
    {
        var index = openLots.FindIndex(x => x.LotId == updatedLot.LotId);
        if (index >= 0)
        {
            openLots[index] = updatedLot;
        }
    }

    private static OpenLot CreateLongLot(TradeEntry tradeEntry) => new()
    {
        LotId = LotId.NewId(),
        AccountId = tradeEntry.AccountId,
        InstrumentId = tradeEntry.InstrumentId,
        OpenEntryId = tradeEntry.EntryId,
        OpenOccurredAt = tradeEntry.OccurredAt,
        OpenTradeDate = DateOnly.FromDateTime(tradeEntry.OccurredAt.UtcDateTime),
        Direction = PositionDirection.Long,
        OriginalQuantity = tradeEntry.Quantity,
        RemainingQuantity = tradeEntry.Quantity,
        OpenUnitPrice = tradeEntry.UnitPrice,
        RemainingOpenFees = tradeEntry.Fees,
        RemainingOpenTaxes = tradeEntry.Taxes,
        AccumulatedVorabpauschale = new Money(0m, Currency.EUR)
    };

    private static InstrumentId GetRelatedInstrumentId(CashEntry cashEntry, CashFlowType expectedType)
    {
        if (cashEntry.RelatedInstrumentId.HasValue)
        {
            return cashEntry.RelatedInstrumentId.Value;
        }

        throw new InvalidOperationException($"Cash entry of type '{expectedType}' requires RelatedInstrumentId.");
    }
}

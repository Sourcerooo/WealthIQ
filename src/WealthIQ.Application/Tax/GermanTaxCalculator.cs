using WealthIQ.Application.Matcher;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax;

public sealed class GermanTaxCalculator(
    IBasisInterestRateProvider interestRateProvider,
    IYearEndPriceProvider yearEndPriceProvider)
{
    private readonly FiFoMatcher _matcher = new();

    public GermanTaxCalculationResult Calculate(
        IReadOnlyList<AccountEvent> accountEvents,
        IReadOnlyList<Instrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(accountEvents);
        ArgumentNullException.ThrowIfNull(instruments);

        var instrumentById = instruments.ToDictionary(x => x.InstrumentId);
        var openLots = new List<OpenLot>();
        var ledger = new List<GermanTaxEntry>();
        var distributions = new Dictionary<(int Year, InstrumentId InstrumentId), decimal>();

        foreach (var yearlyEvents in accountEvents
                     .OrderBy(x => x.OccurredAt)
                     .GroupBy(x => x.OccurredAt.Year)
                     .OrderBy(x => x.Key))
        {
            foreach (var accountEvent in yearlyEvents)
            {
                switch (accountEvent)
                {
                    case ExecutedTradeEvent tradeEvent:
                        ProcessTrade(tradeEvent, openLots, ledger, instrumentById);
                        break;
                    case CashIncomeEvent cashIncomeEvent:
                        ProcessCashIncome(cashIncomeEvent, openLots, ledger, distributions, instrumentById);
                        break;
                    case WithholdingTaxEvent withholdingTaxEvent:
                        var withholdingInstrument = GetInstrument(instrumentById, withholdingTaxEvent.InstrumentId);
                        ledger.Add(new GermanTaxEntry(
                            withholdingTaxEvent.OccurredAt.Year,
                            DateOnly.FromDateTime(withholdingTaxEvent.OccurredAt.UtcDateTime),
                            GermanTaxEntryType.WithholdingTax,
                            withholdingInstrument.Symbol,
                            withholdingInstrument.ISIN,
                            withholdingTaxEvent.Amount.Amount,
                            0m,
                            ForeignWithholdingTax: Math.Abs(withholdingTaxEvent.Amount.Amount)));
                        break;
                }
            }

            PerformYearEndClosing(yearlyEvents.Key, openLots, ledger, distributions, instrumentById);
        }

        return new GermanTaxCalculationResult(ledger, openLots);
    }

    private void ProcessTrade(
        ExecutedTradeEvent tradeEvent,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        if (tradeEvent.Side == TradeSide.Buy)
        {
            var matchingShortLots = openLots.Any(x =>
                x.AccountId == tradeEvent.AccountId
                && x.InstrumentId == tradeEvent.InstrumentId
                && x.Direction == PositionDirection.Short
                && x.RemainingQuantity.Value > 0m);

            if (!matchingShortLots)
            {
                openLots.Add(CreateLongLot(tradeEvent));
                return;
            }
        }

        var matchResult = _matcher.Match(tradeEvent, openLots, LotMatchingPolicy.FIFO);
        var instrument = GetInstrument(instrumentById, tradeEvent.InstrumentId);

        foreach (var consumption in matchResult.Consumptions)
        {
            var originalLot = openLots.Single(x => x.LotId == consumption.OpenLotId);
            var updatedLot = matchResult.UpdatedOpenLots.Single(x => x.LotId == consumption.OpenLotId);
            var usedVorabpauschale = originalLot.AccumulatedVorabpauschale.Amount - updatedLot.AccumulatedVorabpauschale.Amount;
            var rawProfit = consumption.RealizedPnL.Amount - usedVorabpauschale;
            var taxableProfit = rawProfit * (1m - instrument.Teilfreistellungsquote);

            ledger.Add(new GermanTaxEntry(
                tradeEvent.OccurredAt.Year,
                DateOnly.FromDateTime(tradeEvent.OccurredAt.UtcDateTime),
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

    private static void ProcessCashIncome(
        CashIncomeEvent cashIncomeEvent,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        Dictionary<(int Year, InstrumentId InstrumentId), decimal> distributions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var instrument = GetInstrument(instrumentById, cashIncomeEvent.InstrumentId);
        var date = DateOnly.FromDateTime(cashIncomeEvent.OccurredAt.UtcDateTime);

        switch (cashIncomeEvent.IncomeType)
        {
            case CashIncomeType.Dividend:
                var rawDividend = cashIncomeEvent.GrossAmount.Amount;
                ledger.Add(new GermanTaxEntry(
                    cashIncomeEvent.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Dividend,
                    instrument.Symbol,
                    instrument.ISIN,
                    rawDividend,
                    rawDividend * (1m - instrument.Teilfreistellungsquote)));

                var heldLots = openLots
                    .Where(x => x.InstrumentId == cashIncomeEvent.InstrumentId && x.RemainingQuantity.Value > 0m)
                    .ToList();

                var totalHeldQuantity = heldLots.Sum(x => x.RemainingQuantity.Value);
                if (totalHeldQuantity > 0m)
                {
                    var dividendPerShare = rawDividend / totalHeldQuantity;
                    var key = (cashIncomeEvent.OccurredAt.Year, cashIncomeEvent.InstrumentId);
                    distributions[key] = distributions.GetValueOrDefault(key) + dividendPerShare;
                }
                break;

            case CashIncomeType.Interest:
                ledger.Add(new GermanTaxEntry(
                    cashIncomeEvent.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Interest,
                    instrument.Symbol,
                    instrument.ISIN,
                    cashIncomeEvent.GrossAmount.Amount,
                    cashIncomeEvent.GrossAmount.Amount));
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

    private static OpenLot CreateLongLot(ExecutedTradeEvent tradeEvent) => new()
    {
        LotId = LotId.NewId(),
        AccountId = tradeEvent.AccountId,
        InstrumentId = tradeEvent.InstrumentId,
        OpenEventId = tradeEvent.EventId,
        OpenOccurredAt = tradeEvent.OccurredAt,
        OpenTradeDate = DateOnly.FromDateTime(tradeEvent.OccurredAt.UtcDateTime),
        Direction = PositionDirection.Long,
        OriginalQuantity = tradeEvent.Quantity,
        RemainingQuantity = tradeEvent.Quantity,
        OpenUnitPrice = tradeEvent.UnitPrice,
        RemainingOpenFees = tradeEvent.Fees,
        RemainingOpenTaxes = tradeEvent.Taxes,
        AccumulatedVorabpauschale = new Money(0m, Currency.EUR)
    };
}

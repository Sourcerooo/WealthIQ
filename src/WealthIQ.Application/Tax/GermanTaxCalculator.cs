using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
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
    IYearEndPriceProvider yearEndPriceProvider,
    IFxRateLookup fxRateLookup)
{
    private readonly FiFoMatcher _matcher = new();
    private readonly FxConverter _fxConverter = new(fxRateLookup, WealthIQ.Domain.Enumeration.Currency.EUR);

    public GermanTaxCalculationResult Calculate(
        PortfolioLedger portfolioLedger,
        IReadOnlyList<Instrument> instruments)
    {
        ArgumentNullException.ThrowIfNull(portfolioLedger);
        ArgumentNullException.ThrowIfNull(instruments);

        var instrumentById = instruments.ToDictionary(x => x.InstrumentId);
        var openLots = new List<OpenLot>();
        var ledger = new List<GermanTaxEntry>();
        var distributions = new List<Distribution>();

        var orderedEntries = portfolioLedger.Entries.OrderBy(x => x.OccurredAt).ToList();
        var entriesByYear = orderedEntries
            .GroupBy(x => x.OccurredAt.Year)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (orderedEntries.Count > 0)
        {
            var firstYear = orderedEntries[0].OccurredAt.Year;
            var lastYear = orderedEntries[^1].OccurredAt.Year;

            // Close every year in the range — including quiet years with no entries — so a Vorabpauschale
            // is posted for each year a lot is held over year-end (CLAUDE.md tax guardrails).
            for (var year = firstYear; year <= lastYear; year++)
            {
                if (entriesByYear.TryGetValue(year, out var yearEntries))
                {
                    foreach (var portfolioEntry in yearEntries)
                    {
                        switch (portfolioEntry)
                        {
                            case TradeEntry tradeEntry:
                                ProcessTrade(tradeEntry, openLots, ledger, instrumentById);
                                break;
                            case CashEntry cashEntry:
                                ProcessCash(cashEntry, openLots, ledger, distributions, instrumentById);
                                break;
                            default:
                                throw new NotSupportedException(
                                    $"Tax replay does not support entry type '{portfolioEntry.GetType().Name}'.");
                        }
                    }
                }

                PerformYearEndClosing(year, openLots, ledger, distributions, instrumentById);
            }
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
            var saleProceeds = ConvertProceedsToEur(consumption);
            var acquisitionCosts = ConvertCostBasisToEur(consumption);
            var rawProfit = saleProceeds.Amount - acquisitionCosts.Amount - usedVorabpauschale;
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
                SaleProceeds: saleProceeds.Amount,
                AcquisitionCosts: acquisitionCosts.Amount));
        }

        openLots.Clear();
        openLots.AddRange(matchResult.UpdatedOpenLots);

        if (matchResult.NewlyOpenedRemainderLot is not null)
        {
            openLots.Add(matchResult.NewlyOpenedRemainderLot);
        }
    }

    private void ProcessCash(
        CashEntry cashEntry,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        List<Distribution> distributions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var date = DateOnly.FromDateTime(cashEntry.OccurredAt.UtcDateTime);

        switch (cashEntry.CashFlowType)
        {
            case CashFlowType.Dividend:
                var dividendInstrument = GetInstrument(instrumentById, GetRelatedInstrumentId(cashEntry, CashFlowType.Dividend));
                var rawDividend = _fxConverter.Convert(cashEntry.GrossAmount, date).Amount;
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.Dividend,
                    dividendInstrument.Symbol,
                    dividendInstrument.ISIN,
                    rawDividend,
                    rawDividend * (1m - dividendInstrument.Teilfreistellungsquote)));

                var heldLots = openLots
                    .Where(x => x.AccountId == cashEntry.AccountId
                        && x.InstrumentId == dividendInstrument.InstrumentId
                        && x.RemainingQuantity.Value > 0m)
                    .ToList();

                var totalHeldQuantity = heldLots.Sum(x => x.RemainingQuantity.Value);
                if (totalHeldQuantity > 0m)
                {
                    var dividendPerShare = rawDividend / totalHeldQuantity;
                    distributions.Add(new Distribution(
                        cashEntry.OccurredAt.Year,
                        cashEntry.AccountId,
                        dividendInstrument.InstrumentId,
                        date,
                        dividendPerShare));
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
                    _fxConverter.Convert(cashEntry.GrossAmount, date).Amount,
                    _fxConverter.Convert(cashEntry.GrossAmount, date).Amount));
                break;

            case CashFlowType.WithholdingTax:
                var withholdingInstrumentId = cashEntry.RelatedInstrumentId ?? cashEntry.CashInstrumentId;
                var withholdingInstrument = GetInstrument(instrumentById, withholdingInstrumentId);
                var withholdingTaxAmount = _fxConverter.Convert(cashEntry.GrossAmount, date).Amount;
                ledger.Add(new GermanTaxEntry(
                    cashEntry.OccurredAt.Year,
                    date,
                    GermanTaxEntryType.WithholdingTax,
                    withholdingInstrument.Symbol,
                    withholdingInstrument.ISIN,
                    withholdingTaxAmount,
                    0m,
                    ForeignWithholdingTax: Math.Abs(withholdingTaxAmount)));
                break;
        }
    }

    private void PerformYearEndClosing(
        int year,
        List<OpenLot> openLots,
        List<GermanTaxEntry> ledger,
        List<Distribution> distributions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById)
    {
        var basisInterestRate = interestRateProvider.GetRate(year);
        if (basisInterestRate is null or <= 0m)
        {
            return;
        }

        var basisFactor = basisInterestRate.Value * 0.7m;

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
                throw new InvalidOperationException(
                    $"Year-end price for ISIN '{instrument.ISIN}' in {year} is required to compute Vorabpauschale " +
                    $"but is missing. Add it to the reference price data.");
            }

            foreach (var lot in instrumentGroup.ToList())
            {
                var acquisitionPrice = CalculateRemainingAcquisitionPriceInEur(lot);
                var months = 12m;
                if (lot.OpenTradeDate.Year == year)
                {
                    months = 12m - lot.OpenTradeDate.Month + 1m;
                }

                var basisYield = acquisitionPrice * basisFactor * (months / 12m);
                var appreciation = Math.Max(0m, yearEndPrice.Value - acquisitionPrice);
                var maxVorabpauschale = Math.Min(basisYield, appreciation);

                // Only distributions paid into THIS lot's account, on THIS instrument, while the lot
                // was already held (paid on/after the lot's open date), reduce its Vorabpauschale.
                var distributionPerShare = distributions
                    .Where(d => d.Year == year
                        && d.AccountId == lot.AccountId
                        && d.InstrumentId == instrument.InstrumentId
                        && d.Date >= lot.OpenTradeDate)
                    .Sum(d => d.PerShare);

                var actualVorabpauschalePerShare = Math.Max(0m, maxVorabpauschale - distributionPerShare);
                if (actualVorabpauschalePerShare <= 0m)
                {
                    continue;
                }

                var totalVorabpauschale = actualVorabpauschalePerShare * lot.RemainingQuantity.Value;
                ReplaceLot(openLots, lot with
                {
                    AccumulatedVorabpauschale = new Money(lot.AccumulatedVorabpauschale.Amount + totalVorabpauschale, WealthIQ.Domain.Enumeration.Currency.EUR)
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
        OpenSourceReference = tradeEntry.SourceProvenance.SourceRecordReference,
        Direction = PositionDirection.Long,
        OriginalQuantity = tradeEntry.Quantity,
        RemainingQuantity = tradeEntry.Quantity,
        OpenUnitPrice = tradeEntry.UnitPrice,
        RemainingOpenFees = tradeEntry.Fees,
        RemainingOpenTaxes = tradeEntry.Taxes,
        AccumulatedVorabpauschale = new Money(0m, WealthIQ.Domain.Enumeration.Currency.EUR)
    };

    private static InstrumentId GetRelatedInstrumentId(CashEntry cashEntry, CashFlowType expectedType)
    {
        if (cashEntry.RelatedInstrumentId.HasValue)
        {
            return cashEntry.RelatedInstrumentId.Value;
        }

        throw new InvalidOperationException($"Cash entry of type '{expectedType}' requires RelatedInstrumentId.");
    }

    private Money ConvertCostBasisToEur(LotConsumption consumption)
    {
        var sourceMoney = consumption.Direction switch
        {
            PositionDirection.Long => (consumption.OpenUnitPrice * consumption.MatchedQuantity.Value)
                + consumption.AllocatedOpenFees + consumption.AllocatedOpenTaxes,
            PositionDirection.Short => (consumption.CloseUnitPrice * consumption.MatchedQuantity.Value)
                + consumption.AllocatedCloseFees + consumption.AllocatedCloseTaxes,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

        var conversionDate = consumption.Direction switch
        {
            PositionDirection.Long => consumption.OpenTradeDate,
            PositionDirection.Short => consumption.CloseTradeDate,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

        return _fxConverter.Convert(sourceMoney, conversionDate);
    }

    private Money ConvertProceedsToEur(LotConsumption consumption)
    {
        var sourceMoney = consumption.Direction switch
        {
            PositionDirection.Long => (consumption.CloseUnitPrice * consumption.MatchedQuantity.Value)
                - consumption.AllocatedCloseFees - consumption.AllocatedCloseTaxes,
            PositionDirection.Short => (consumption.OpenUnitPrice * consumption.MatchedQuantity.Value)
                - consumption.AllocatedOpenFees - consumption.AllocatedOpenTaxes,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

        var conversionDate = consumption.Direction switch
        {
            PositionDirection.Long => consumption.CloseTradeDate,
            PositionDirection.Short => consumption.OpenTradeDate,
            _ => throw new InvalidOperationException("Invalid position direction.")
        };

        return _fxConverter.Convert(sourceMoney, conversionDate);
    }

    private decimal CalculateRemainingAcquisitionPriceInEur(OpenLot lot)
    {
        var sourceAcquisitionTotal = (lot.OpenUnitPrice * lot.RemainingQuantity.Value)
            + lot.RemainingOpenFees + lot.RemainingOpenTaxes;

        var acquisitionTotalInEur = _fxConverter.Convert(sourceAcquisitionTotal, lot.OpenTradeDate);
        return acquisitionTotalInEur.Amount / lot.RemainingQuantity.Value;
    }

    /// <summary>A per-share distribution recorded for Vorabpauschale reduction, scoped to the account,
    /// instrument and the date it was paid (so only lots held at that date are reduced).</summary>
    private readonly record struct Distribution(
        int Year,
        AccountId AccountId,
        InstrumentId InstrumentId,
        DateOnly Date,
        decimal PerShare);
}

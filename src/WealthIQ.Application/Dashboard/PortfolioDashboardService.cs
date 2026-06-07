using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Valuation;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.Dashboard;

/// <summary>Builds the "Mein Portfolio" dashboard report: per-account holdings grouped by ISIN,
/// a combined "Alle" rollup (EUR), asset-class allocation, and YTD KPIs. Display-only and resilient —
/// a missing price/FX flags a row instead of failing the whole report (unlike the tax engine).</summary>
public sealed class PortfolioDashboardService(
    ILedgerStore ledgerStore,
    InstrumentCatalogBuilder catalogBuilder,
    PortfolioValuationService valuationService,
    IFxRateLookup fxRateLookup)
{
    private const string AllKey = "ALL";
    // Used by the YTD KPI accumulation (DividendsYtdByAccount / RealizedYtdByAccount), which convert
    // ledger amounts to EUR at the event's own date. (Those methods are filled in by a follow-up task.)
    private readonly FxConverter _fxConverter = new(fxRateLookup, CurrencyCode.EUR);

    public async Task<PortfolioDashboardReport> GenerateAsync(DateOnly today, CancellationToken ct = default)
    {
        var ledger = await ledgerStore.LoadLedgerAsync(ct);
        var catalog = catalogBuilder.Build(ledger.Instruments);
        var instrumentById = catalog.ToDictionary(x => x.InstrumentId);

        var valuation = valuationService.Calculate(ledger, catalog, today);

        var accountNumbers = ledger.Accounts.ToDictionary(a => a.AccountId, a => a.AccountNumber);
        string LabelFor(AccountId id) => accountNumbers.TryGetValue(id, out var n) ? n : id.ToString();

        var dividendsByAccount = DividendsYtdByAccount(ledger, today.Year);
        var realizedByAccount = RealizedYtdByAccount(ledger, today.Year);

        var views = new List<DashboardView>();

        views.Add(BuildView(
            AllKey, "Alle Konten",
            valuation.Positions, instrumentById,
            dividendsByAccount.Values.Sum(), realizedByAccount.Values.Sum(),
            accountCount: ledger.Accounts.Count));

        foreach (var accountGroup in valuation.Positions
                     .GroupBy(p => p.AccountId)
                     .OrderBy(g => LabelFor(g.Key), StringComparer.Ordinal))
        {
            views.Add(BuildView(
                accountGroup.Key.Value.ToString(), LabelFor(accountGroup.Key),
                accountGroup.ToList(), instrumentById,
                dividendsByAccount.GetValueOrDefault(accountGroup.Key),
                realizedByAccount.GetValueOrDefault(accountGroup.Key),
                accountCount: 1));
        }

        return new PortfolioDashboardReport(today, valuation.EffectiveMarketDate, views);
    }

    private DashboardView BuildView(
        string accountKey, string accountLabel,
        IReadOnlyList<PortfolioPositionSnapshot> positions,
        IReadOnlyDictionary<InstrumentId, Instrument> instrumentById,
        decimal dividendsYtd, decimal realizedYtd, int accountCount)
    {
        var holdings = positions
            .GroupBy(p => p.InstrumentId)
            .Select(g => BuildHolding(g.ToList(), instrumentById[g.Key]))
            .OrderByDescending(h => h.MarketValueInBaseCurrency ?? 0m)
            .ThenBy(h => h.Symbol, StringComparer.Ordinal)
            .ToList();

        var priced = holdings.Where(h => !h.PriceMissing).ToList();
        var totalValue = priced.Sum(h => h.MarketValueInBaseCurrency ?? 0m);
        var totalCost = priced.Sum(h => h.CostBasisInBaseCurrency);
        var unrealized = priced.Sum(h => h.UnrealizedPnlInBaseCurrency ?? 0m);
        var unrealizedPct = totalCost == 0m ? 0m : unrealized / totalCost;

        var allocation = priced
            .GroupBy(h => string.IsNullOrWhiteSpace(h.AssetClass) ? "Sonstige" : h.AssetClass)
            .Select(g => new { AssetClass = g.Key, Value = g.Sum(h => h.MarketValueInBaseCurrency ?? 0m) })
            .Where(x => x.Value > 0m)
            .OrderByDescending(x => x.Value)
            .Select(x => new DashboardAllocationSlice(
                x.AssetClass, x.Value, totalValue == 0m ? 0m : Math.Round(x.Value / totalValue * 100m, 2)))
            .ToList();

        var kpis = new DashboardKpis(
            totalValue, unrealized, unrealizedPct,
            dividendsYtd, realizedYtd,
            PositionCount: holdings.Count, AccountCount: accountCount,
            PriceMissingCount: holdings.Count(h => h.PriceMissing));

        return new DashboardView(accountKey, accountLabel, holdings, allocation, kpis);
    }

    private static DashboardHolding BuildHolding(IReadOnlyList<PortfolioPositionSnapshot> group, Instrument instrument)
    {
        var quantity = group.Sum(x => x.Quantity);
        var costBasis = group.Sum(x => x.CostBasisInBaseCurrency);
        var anyMissing = group.Any(x => x.PriceMissing);

        var sameCurrency = group.Select(x => x.NativeCurrency).Distinct().Count() == 1;
        CurrencyCode? nativeCurrency = sameCurrency ? group[0].NativeCurrency : null;
        decimal? avgBuyNative = sameCurrency && quantity != 0m && group.All(x => x.AverageBuyPriceNative.HasValue)
            ? group.Sum(x => x.AverageBuyPriceNative!.Value * x.Quantity) / quantity
            : null;
        var avgBuyEur = quantity != 0m ? costBasis / quantity : 0m;

        decimal? marketValue = anyMissing ? null : group.Sum(x => x.MarketValueInBaseCurrency);
        decimal? pnl = anyMissing ? null : marketValue - costBasis;
        decimal? pnlPct = anyMissing || costBasis == 0m ? null : pnl / costBasis;
        decimal? closePrice = anyMissing || !sameCurrency ? null : group[0].ClosePrice;
        CurrencyCode? priceCurrency = anyMissing || !sameCurrency ? null : group[0].PriceCurrency;
        var providerSymbol = group.FirstOrDefault(x => x.ProviderSymbol is not null)?.ProviderSymbol;

        return new DashboardHolding(
            string.IsNullOrWhiteSpace(instrument.ISIN) ? null : instrument.ISIN,
            instrument.Symbol,
            instrument.Name,
            string.IsNullOrWhiteSpace(instrument.Type) ? "Sonstige" : instrument.Type,
            quantity,
            avgBuyEur,
            avgBuyNative,
            nativeCurrency,
            closePrice,
            priceCurrency,
            costBasis,
            marketValue,
            pnl,
            pnlPct,
            providerSymbol,
            anyMissing);
    }

    private Dictionary<AccountId, decimal> DividendsYtdByAccount(PortfolioLedger ledger, int year)
    {
        var result = new Dictionary<AccountId, decimal>();
        foreach (var cash in ledger.Entries.OfType<CashEntry>()
                     .Where(c => c.CashFlowType == Domain.Enumeration.CashFlowType.Dividend && c.EffectiveDate.Year == year))
        {
            try
            {
                var eur = _fxConverter.Convert(cash.GrossAmount, cash.EffectiveDate).Amount;
                result[cash.AccountId] = result.GetValueOrDefault(cash.AccountId) + eur;
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                // Missing FX must not blank the dashboard — skip this entry's contribution.
            }
        }
        return result;
    }

    private Dictionary<AccountId, decimal> RealizedYtdByAccount(PortfolioLedger ledger, int year)
    {
        var matcher = new Matcher.FiFoMatcher();
        var openLots = new List<Domain.Model.Lot.OpenLot>();
        var result = new Dictionary<AccountId, decimal>();

        foreach (var trade in ledger.Entries.OfType<TradeEntry>().OrderBy(x => x.OccurredAt))
        {
            var match = matcher.Match(trade, openLots, Domain.Enumeration.LotMatchingPolicy.FIFO);
            openLots = match.UpdatedOpenLots.ToList();
            if (match.NewlyOpenedRemainderLot is not null)
            {
                openLots.Add(match.NewlyOpenedRemainderLot);
            }

            foreach (var c in match.Consumptions.Where(c => c.CloseTradeDate.Year == year))
            {
                try
                {
                    var realizedEur = _fxConverter.Convert(c.Proceeds, c.CloseTradeDate).Amount
                                      - _fxConverter.Convert(c.CostBasis, c.OpenTradeDate).Amount;
                    result[c.AccountId] = result.GetValueOrDefault(c.AccountId) + realizedEur;
                }
                catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
                {
                    // Missing FX — skip this consumption's contribution.
                }
            }
        }
        return result;
    }
}

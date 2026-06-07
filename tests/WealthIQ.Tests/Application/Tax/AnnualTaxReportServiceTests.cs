using WealthIQ.Application.Persistence;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class AnnualTaxReportServiceTests
{
    private sealed class FixedLedgerStore(PortfolioLedger ledger) : ILedgerStore
    {
        public Task<LedgerSaveResult> SaveLedgerAsync(PortfolioLedger l, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PortfolioLedger> LoadLedgerAsync(CancellationToken ct = default) => Task.FromResult(ledger);
    }

    private sealed class IdentityProfileEnricher : IInstrumentProfileEnricher
    {
        public Instrument Enrich(Instrument instrument) => instrument;
    }

    [Fact]
    public async Task Generate_BuySellAndDividendSameYear_ProducesYearSummaryAndSections()
    {
        var accountId = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0001", "AAA", "Alpha", 0m); // 0 % Teilfreistellung → taxable == raw

        // EUR-only so no FX rate is needed. Buy 10@100 (2024-01-10), sell 10@120 (2024-06-10) → gain 200.
        var buy = TaxEntries.Trade(accountId, instrumentId, TradeSide.Buy, 10m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "BUY-1");
        var sell = TaxEntries.Trade(accountId, instrumentId, TradeSide.Sell, 10m, 120m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "SELL-1");
        var dividend = TaxEntries.Dividend(accountId, instrumentId, instrumentId, 50m,
            new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero), "DIV-1");

        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { buy, sell, dividend },
            new[] { instrument },
            new[] { new Account(accountId, "U1") });

        var service = new AnnualTaxReportService(
            new FixedLedgerStore(ledger),
            new InstrumentCatalogBuilder(new IdentityProfileEnricher()),
            new GermanTaxCalculator(
                new FakeBasisInterestRateProvider((2024, 0m)),   // rate 0 → no Vorabpauschale
                new FakeYearEndPriceProvider(),
                new FakeFxRateLookup()));

        var reports = await service.GenerateAsync();

        var accountReport = Assert.Single(reports);
        Assert.Equal(accountId.Value, accountReport.AccountId);
        Assert.Equal("U1", accountReport.AccountNumber);

        var report = Assert.Single(accountReport.Years);
        Assert.Equal(2024, report.Year);
        Assert.Single(report.Sells);
        Assert.Single(report.Dividends);
        Assert.Empty(report.Vorabpauschale);

        Assert.Equal(200m, report.Summary.NetRealizedGainsTaxable);
        Assert.Equal(50m, report.Summary.DividendsTaxable);
        Assert.Equal(0m, report.Summary.InterestTaxable);
        Assert.Equal(0m, report.Summary.VorabpauschaleTaxable);
        Assert.Equal(0m, report.Summary.ForeignWithholdingTax);
        Assert.Equal(0m, report.Summary.WithheldKESt);
        // (200 + 50) * 0.26375 = 65.9375
        Assert.Equal(65.9375m, report.Summary.EstimatedTax);
    }

    [Fact]
    public async Task Generate_TwoAccounts_ReportsAreSeparated()
    {
        var accountA = AccountId.NewId();
        var accountB = AccountId.NewId();
        var instrumentId = InstrumentId.NewId();
        var instrument = new Instrument(instrumentId, "DE0001", "AAA", "Alpha", 0m)
        {
            SubjectToVorabpauschale = false
        };

        var buyA = TaxEntries.Trade(accountA, instrumentId, TradeSide.Buy, 10m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "A-BUY");
        var sellA = TaxEntries.Trade(accountA, instrumentId, TradeSide.Sell, 10m, 120m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "A-SELL");
        var buyB = TaxEntries.Trade(accountB, instrumentId, TradeSide.Buy, 5m, 100m,
            new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero), "B-BUY");
        var sellB = TaxEntries.Trade(accountB, instrumentId, TradeSide.Sell, 5m, 110m,
            new DateTimeOffset(2024, 6, 10, 12, 0, 0, TimeSpan.Zero), "B-SELL");

        var ledger = new PortfolioLedger(
            new PortfolioEntry[] { buyA, sellA, buyB, sellB },
            new[] { instrument },
            new[] { new Account(accountA, "AAA-1"), new Account(accountB, "BBB-2") });

        var service = new AnnualTaxReportService(
            new FixedLedgerStore(ledger),
            new InstrumentCatalogBuilder(new IdentityProfileEnricher()),
            new GermanTaxCalculator(
                new FakeBasisInterestRateProvider((2024, 0m)),
                new FakeYearEndPriceProvider(),
                new FakeFxRateLookup()));

        var reports = await service.GenerateAsync();

        Assert.Equal(2, reports.Count);
        var a = Assert.Single(reports, r => r.AccountNumber == "AAA-1");
        var b = Assert.Single(reports, r => r.AccountNumber == "BBB-2");
        Assert.Equal(200m, a.Years.Single().Summary.NetRealizedGainsTaxable);  // 10 * (120-100)
        Assert.Equal(50m, b.Years.Single().Summary.NetRealizedGainsTaxable);   // 5 * (110-100)
    }
}

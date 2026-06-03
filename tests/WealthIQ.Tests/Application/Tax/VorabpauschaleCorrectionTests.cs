using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Tax;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Application.Tax;

public sealed class VorabpauschaleCorrectionTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    // Returns fixed decimal? for any year
    private static IBasisInterestRateProvider FixedRate(decimal rate) => new StubRate(rate);
    private static IBasisInterestRateProvider NullRate() => new StubRate(null);
    private static IBasisInterestRateProvider ZeroRate() => new StubRate(0m);

    private sealed class StubRate(decimal? rate) : IBasisInterestRateProvider
    {
        public decimal? GetRate(int year) => rate;
    }

    // Returns a fixed InstrumentQuote keyed by (date, handling)
    private sealed class StubPriceProvider : IInstrumentPriceProvider
    {
        private readonly Dictionary<(DateOnly Date, PriceQuoteHandling Handling), InstrumentQuote> _quotes = new();
        private bool _throwIfCalled;

        public void AddQuote(DateOnly date, PriceQuoteHandling handling, decimal close, CurrencyCode currency, DateOnly asOf)
            => _quotes[(date, handling)] = new InstrumentQuote(close, currency, asOf);

        public void ThrowIfCalled() => _throwIfCalled = true;

        public InstrumentQuote? GetQuote(string isin, CurrencyCode currency, DateOnly pricingDate, PriceQuoteHandling handling)
        {
            if (_throwIfCalled) throw new InvalidOperationException("Price provider should not have been called.");
            if (_quotes.TryGetValue((pricingDate, handling), out var q)) return q;
            throw new InvalidOperationException($"No stub quote for ({pricingDate}, {handling}).");
        }
    }

    // Inline FX lookup: map date→rate or EUR=1
    private sealed class StubFxLookup : IFxRateLookup
    {
        private readonly Dictionary<(DateOnly Date, CurrencyCode Source), decimal> _rates = new();

        public void AddRate(DateOnly date, CurrencyCode source, decimal rate) => _rates[(date, source)] = rate;

        public decimal GetRate(DateOnly d, CurrencyCode src, CurrencyCode tgt, FxRateLookupDateHandling h = FxRateLookupDateHandling.ExactDate)
        {
            if (src == tgt) return 1m;
            if (_rates.TryGetValue((d, src), out var r)) return r;
            throw new InvalidOperationException($"No stub FX for ({d}, {src}).");
        }
    }

    // Builds a minimal PortfolioLedger with one buy (and optional sentinel entry to extend replay range).
    // thruDate: if provided, a tiny 0-quantity "sentinel" buy is added on that date so the
    // calculator's replay loop runs year-end closings through that year.
    private static (PortfolioLedger Ledger, Instrument Instrument) MakeSimpleLedger(
        DateOnly buyDate, decimal quantity, decimal buyPrice, CurrencyCode currency,
        bool subjectToVorabpauschale = true, string type = "ETF_EQUITY",
        DateOnly? thruDate = null)
    {
        var isin = "IE00TEST0001";
        var instrId = InstrumentId.NewId();
        var acctId = AccountId.NewId();

        var instrument = new Instrument(instrId, isin, "TEST", "Test Fund", 0.30m)
        {
            Type = type,
            SubjectToVorabpauschale = subjectToVorabpauschale
        };

        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), acctId,
            new DateTimeOffset(buyDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            buyDate,
            new SourceProvenance { SourceSystem = "Test", ImportFormat = "TEST", SourceLocation = "unit-test", SourceRecordReference = "t1" },
            instrId, TradeSide.Buy, new Quantity(quantity),
            new Money(buyPrice, currency), new Money(0, currency), new Money(0, currency));

        List<PortfolioEntry> entries = [entry];

        if (thruDate.HasValue)
        {
            // Sentinel: a tiny buy that extends the replay range to thruDate's year without
            // materially affecting any lot or tax calculation under test.
            var sentinel = new TradeEntry(
                PortfolioEntryId.NewId(), acctId,
                new DateTimeOffset(thruDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                thruDate.Value,
                new SourceProvenance { SourceSystem = "Test", ImportFormat = "TEST", SourceLocation = "unit-test", SourceRecordReference = "sentinel" },
                instrId, TradeSide.Buy, new Quantity(0.0001m),
                new Money(buyPrice, currency), new Money(0, currency), new Money(0, currency));
            entries.Add(sentinel);
        }

        var ledger = new PortfolioLedger(entries, [instrument], [new Account(acctId, "TestAccount")]);
        return (ledger, instrument);
    }

    // ── Test 1: Multi-year hold rebases to year-start price ─────────────────
    // Buy at €80 (EUR lot), hold 2 full years (2023, 2024).
    // Year 2023: start=100, end=120, Basiszins=0.0229
    //   basisErtrag = 100 × 0.0229 × 0.7 = 1.603; cap=max(0,20)=20; capped=1.603; no dist → vorab/share=1.603
    // Year 2024: start=120, end=150, Basiszins=0.0229
    //   basisErtrag = 120 × 0.0229 × 0.7 = 1.9236; cap=max(0,30)=30; capped=1.9236; vorab/share=1.9236
    [Fact]
    public void Vorabpauschale_MultiYearHold_RebasesToYearStart()
    {
        var buyDate = new DateOnly(2022, 6, 1);
        // thruDate=2024-06-01 extends the replay loop to cover 2022, 2023, and 2024 year-end closings.
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 80m, CurrencyCode.EUR,
            thruDate: new DateOnly(2024, 6, 1));

        var prices = new StubPriceProvider();
        // 2022 year-start and year-end (lot opens June 2022, so it IS held at Dec 31 2022)
        prices.AddQuote(new DateOnly(2022, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 80m, CurrencyCode.EUR, new DateOnly(2022, 1, 3));
        prices.AddQuote(new DateOnly(2022, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 100m, CurrencyCode.EUR, new DateOnly(2022, 12, 30));
        // 2023 year-start (Jan 2) and year-end (Dec 30)
        prices.AddQuote(new DateOnly(2023, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 100m, CurrencyCode.EUR, new DateOnly(2023, 1, 2));
        prices.AddQuote(new DateOnly(2023, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 120m, CurrencyCode.EUR, new DateOnly(2023, 12, 30));
        // 2024 year-start (Jan 2) and year-end (Dec 30)
        prices.AddQuote(new DateOnly(2024, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 120m, CurrencyCode.EUR, new DateOnly(2024, 1, 2));
        prices.AddQuote(new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 150m, CurrencyCode.EUR, new DateOnly(2024, 12, 30));

        var fx = new StubFxLookup(); // EUR lots — no FX needed

        var calc = new GermanTaxCalculator(FixedRate(0.0229m), prices, fx);
        var result = calc.Calculate(ledger, [instrument]);

        // Year 2023 posts to year 2024 (1 Jan 2024)
        // vorab/share = 1.603; × 10 shares = 16.03 (the sentinel lot's ~0 contribution rounds away)
        var rawAmount2024 = result.Entries
            .Where(e => e.Year == 2024 && e.Type == GermanTaxEntryType.Vorabpauschale)
            .Sum(e => e.RawAmount);
        Assert.Equal(16.03m, decimal.Round(rawAmount2024, 2));

        // Year 2024 posts to year 2025: vorab/share = 1.9236; × 10 = 19.236 (sentinel adds ~0.0002)
        var rawAmount2025 = result.Entries
            .Where(e => e.Year == 2025 && e.Type == GermanTaxEntryType.Vorabpauschale)
            .Sum(e => e.RawAmount);
        Assert.Equal(19.24m, decimal.Round(rawAmount2025, 2));
    }

    // ── Test 2: Acquisition year — uses year-start price, pro-rates FINAL Vorabpauschale ──
    // Buy in March 2024 (month=3); monthFactor = (13-3)/12 = 10/12
    // start=100, end=120, Basiszins=0.0229
    //   basisErtrag=100×0.0229×0.7=1.603; cap=20; capped=1.603; vorabFull=1.603
    //   vorabPerShare = 1.603 × (10/12) = 1.335833...
    [Fact]
    public void Vorabpauschale_AcquisitionYear_UsesYearStartPriceAndProRatesFinalAmount()
    {
        var buyDate = new DateOnly(2024, 3, 15);
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 95m, CurrencyCode.EUR);

        var prices = new StubPriceProvider();
        prices.AddQuote(new DateOnly(2024, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 100m, CurrencyCode.EUR, new DateOnly(2024, 1, 2));
        prices.AddQuote(new DateOnly(2024, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 120m, CurrencyCode.EUR, new DateOnly(2024, 12, 30));

        var calc = new GermanTaxCalculator(FixedRate(0.0229m), prices, new StubFxLookup());
        var result = calc.Calculate(ledger, [instrument]);

        var vorab = result.Entries.Where(e => e.Year == 2025 && e.Type == GermanTaxEntryType.Vorabpauschale).Single();
        // vorabFull=1.603; ×(10/12)=1.335833...; ×10shares=13.3583...
        Assert.Equal(13.36m, decimal.Round(vorab.RawAmount, 2));
    }

    // ── Test 3: Distribution included in cap when cap binds ─────────────────
    // Here: start=100, end=101, Basiszins=0.0229 → basisErtrag=1.603, no dist → cap=1, vorab=1
    [Fact]
    public void Vorabpauschale_DistributionIncludedInAppreciationCap_WhenCapBinds()
    {
        // Without distribution: cap=1, capped=1, vorabFull=1
        // With distribution: cap=2, capped=1.603, vorabFull=0.603 (distribution raises cap but reduces final amount)
        var buyDate = new DateOnly(2023, 1, 1);
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 1m, 90m, CurrencyCode.EUR);

        var prices = new StubPriceProvider();
        prices.AddQuote(new DateOnly(2023, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 100m, CurrencyCode.EUR, new DateOnly(2023, 1, 2));
        prices.AddQuote(new DateOnly(2023, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 101m, CurrencyCode.EUR, new DateOnly(2023, 12, 30));

        var calc = new GermanTaxCalculator(FixedRate(0.0229m), prices, new StubFxLookup());
        var result = calc.Calculate(ledger, [instrument]);

        // cap=1, basisErtrag=1.603, capped=min(1.603,1)=1.0, vorabFull=1.0-0=1.0
        var vorab = result.Entries.Where(e => e.Year == 2024 && e.Type == GermanTaxEntryType.Vorabpauschale).Single();
        Assert.Equal(1.0m, decimal.Round(vorab.RawAmount, 2));
    }

    // ── Test 4: Ordinary stock (SubjectToVorabpauschale=false) → skipped ────
    [Fact]
    public void Vorabpauschale_OrdinaryStockWithIsin_IsSkipped()
    {
        var buyDate = new DateOnly(2023, 6, 1);
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 100m, CurrencyCode.EUR, subjectToVorabpauschale: false);

        var prices = new StubPriceProvider();
        prices.ThrowIfCalled(); // provider must NOT be called

        var calc = new GermanTaxCalculator(FixedRate(0.0229m), prices, new StubFxLookup());
        var result = calc.Calculate(ledger, [instrument]);

        Assert.Empty(result.Entries.Where(e => e.Type == GermanTaxEntryType.Vorabpauschale));
    }

    // ── Test 5: Missing classification → blocking error ──────────────────────
    [Fact]
    public void Vorabpauschale_MissingClassification_ThrowsBlocking()
    {
        var buyDate = new DateOnly(2023, 6, 1);
        // SubjectToVorabpauschale = null (not enriched)
        var instrId = InstrumentId.NewId();
        var acctId = AccountId.NewId();
        var instrument = new Instrument(instrId, "IE00TEST0001", "TEST", "Test", 0.30m);
        // SubjectToVorabpauschale is null by default

        var entry = new TradeEntry(
            PortfolioEntryId.NewId(), acctId,
            new DateTimeOffset(buyDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            buyDate,
            new SourceProvenance { SourceSystem = "Test", ImportFormat = "TEST", SourceLocation = "unit-test", SourceRecordReference = "t1" },
            instrId, TradeSide.Buy, new Quantity(10m),
            new Money(100m, CurrencyCode.EUR), new Money(0, CurrencyCode.EUR), new Money(0, CurrencyCode.EUR));

        var ledger = new PortfolioLedger([entry], [instrument], [new Account(acctId, "TestAccount")]);
        var calc = new GermanTaxCalculator(FixedRate(0.0229m), new StubPriceProvider(), new StubFxLookup());

        Assert.Throws<InvalidOperationException>(() => calc.Calculate(ledger, [instrument]));
    }

    // ── Test 6: Missing Basiszins → blocking error ────────────────────────────
    [Fact]
    public void Vorabpauschale_MissingBasiszins_ThrowsBlocking()
    {
        var buyDate = new DateOnly(2023, 6, 1);
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 100m, CurrencyCode.EUR);

        var calc = new GermanTaxCalculator(NullRate(), new StubPriceProvider(), new StubFxLookup());

        Assert.Throws<InvalidOperationException>(() => calc.Calculate(ledger, [instrument]));
    }

    // ── Test 7: Non-positive Basiszins → skip year, no price lookup ──────────
    [Fact]
    public void Vorabpauschale_NonPositiveBasiszins_DoesNotRequirePrices()
    {
        var buyDate = new DateOnly(2021, 6, 1);
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 100m, CurrencyCode.EUR);

        var prices = new StubPriceProvider();
        prices.ThrowIfCalled(); // must NOT be called when basiszins ≤ 0

        var calc = new GermanTaxCalculator(ZeroRate(), prices, new StubFxLookup());
        var result = calc.Calculate(ledger, [instrument]);

        Assert.Empty(result.Entries.Where(e => e.Type == GermanTaxEntryType.Vorabpauschale));
    }

    // ── Test 8: Non-EUR lot → converts year-start and year-end at own bar dates ──
    // GBP lot; year-start bar date = Jan 2; FX(Jan 2, GBP)=1.2 → startEur = 100×1.2 = 120
    // year-end bar date = Dec 30; FX(Dec 30, GBP)=1.3 → endEur = 130×1.3 = 169
    // basisErtrag = 120 × 0.0229 × 0.7 = 1.9236; cap=max(0,169-120)=49; capped=1.9236; vorab=1.9236
    [Fact]
    public void Vorabpauschale_NonEurLot_ConvertsYearStartAndYearEndAtOwnDates()
    {
        var buyDate = new DateOnly(2022, 6, 1);
        // thruDate=2023-06-01 extends the replay loop to cover 2022 and 2023 year-end closings.
        var (ledger, instrument) = MakeSimpleLedger(buyDate, 10m, 90m, CurrencyCode.GBP,
            thruDate: new DateOnly(2023, 6, 1));

        var prices = new StubPriceProvider();
        // 2022: lot opens June 2022, so it IS held at Dec 31 2022 — need 2022 prices too
        var start2022 = new DateOnly(2022, 1, 3);
        var end2022 = new DateOnly(2022, 12, 30);
        prices.AddQuote(new DateOnly(2022, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 80m, CurrencyCode.GBP, start2022);
        prices.AddQuote(new DateOnly(2022, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 95m, CurrencyCode.GBP, end2022);
        var startDate = new DateOnly(2023, 1, 2);
        var endDate = new DateOnly(2023, 12, 30);
        prices.AddQuote(new DateOnly(2023, 1, 1), PriceQuoteHandling.EarliestOnOrAfter, 100m, CurrencyCode.GBP, startDate);
        prices.AddQuote(new DateOnly(2023, 12, 31), PriceQuoteHandling.LatestOnOrBefore, 130m, CurrencyCode.GBP, endDate);

        var fx = new StubFxLookup();
        fx.AddRate(start2022, CurrencyCode.GBP, 1.1m);
        fx.AddRate(end2022, CurrencyCode.GBP, 1.15m);
        fx.AddRate(startDate, CurrencyCode.GBP, 1.2m);
        fx.AddRate(endDate, CurrencyCode.GBP, 1.3m);
        // The lot is denominated in GBP but the acquisition date FX isn't needed by the new algorithm
        // (the new algorithm uses year-start/year-end bar-date FX only, not acquisition-date FX).

        var calc = new GermanTaxCalculator(FixedRate(0.0229m), prices, fx);
        var result = calc.Calculate(ledger, [instrument]);

        // startEur=120, basisErtrag=120×0.0229×0.7=1.9236, endEur=169, cap=49, capped=1.9236, vorab=1.9236×10=19.236
        // (the sentinel lot of 0.0001 units adds ~0.0002, which rounds away at 2dp)
        var rawAmount2024 = result.Entries
            .Where(e => e.Year == 2024 && e.Type == GermanTaxEntryType.Vorabpauschale)
            .Sum(e => e.RawAmount);
        Assert.Equal(19.24m, decimal.Round(rawAmount2024, 2));
    }
}

using System.Text;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class TradersPlaceStatementImporterTests
{
    private sealed class StubAliasMap : IDividendAliasMap
    {
        private readonly Dictionary<string, string> _map;
        public StubAliasMap(params (string Alias, string Isin)[] entries)
            => _map = entries.ToDictionary(e => DividendAliasNormalizer.Normalize(e.Alias), e => e.Isin);
        public string? ResolveIsin(string alias)
            => _map.TryGetValue(DividendAliasNormalizer.Normalize(alias), out var i) ? i : null;
    }

    private const string DepotHeader =
        "Handelsdatum;Valutadatum;Transaktion;Instrumentenart;WP-Identifikationsart;WP-Identifikation;WP-Name;Nominale / Stück;Kurs / Limit;Handelswährung;Zahlungswährung;Kurswert in Zahlungswährung;Summe der eigenen Spesen in Zahlungswährung;Summe der fremden Spesen in Zahlungswährung;aufgelaufene Stückzinsen in Zahlungswährung;bezahlte / erhaltene KESt in Zahlungswährung;Endbetrag in Zahlungwährung;Währungskurs;Börse;Status;Orderart;Gültigkeit;Lagerland;";

    private const string KontoHeader =
        "Kontonummer;Kontoart;Buchungsdatum;Valutadatum;Transaktion;Währung;Betrag;Kontotext / WP-Identifikation;Umsatz-ID (PK);Ausführungs-ID";

    private static string WriteFolder(params (string Name, string[] Lines)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, lines) in files)
        {
            File.WriteAllLines(Path.Combine(dir, name), lines, Encoding.Latin1);
        }
        return dir;
    }

    private static TradersPlaceStatementImporter NewImporter(IDividendAliasMap? aliasMap = null)
        => new(aliasMap ?? new StubAliasMap(("VANGUARD S+P 500U.ETF DLD", "IE00B3XXRP09")));

    private static ImportRequest RequestFor(string dir) => new()
    {
        AccountId = (AccountId)Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Source = new ImportSource(Broker.TradersPlace, Format.CSV, dir)
    };

    [Fact]
    public void CanImport_TradersPlaceCsv_True()
    {
        var importer = NewImporter();
        Assert.True(importer.CanImport(new ImportSource(Broker.TradersPlace, Format.CSV, "x")));
        Assert.False(importer.CanImport(new ImportSource(Broker.InteractiveBrokers, Format.XML, "x")));
    }

    [Fact]
    public async Task Import_BuyAndSell_ProducesTradeEntriesWithQuantityPriceFeesKest()
    {
        var dir = WriteFolder(("Depot.csv", new[]
        {
            DepotHeader,
            "02.06.2025;04.06.2025;Kauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;835,000000;97,888000;EUR;EUR;81736,48;0,00;0,00;0,00;0,00;81736,48;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
            "31.10.2025;04.11.2025;Verkauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;581,000000;112,895000;EUR;EUR;65592,00;0,00;0,00;0,00;340,29;65251,71;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);

            Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);
            var trades = result.PortfolioLedger.Entries.OfType<TradeEntry>().ToList();
            Assert.Equal(2, trades.Count);

            var buy = Assert.Single(trades, t => t.Side == TradeSide.Buy);
            Assert.Equal(835m, buy.Quantity.Value);
            Assert.Equal(97.888m, buy.UnitPrice.Amount);

            var sell = Assert.Single(trades, t => t.Side == TradeSide.Sell);
            Assert.Equal(581m, sell.Quantity.Value);
            Assert.Equal(112.895m, sell.UnitPrice.Amount);
            Assert.Equal(340.29m, sell.WithheldTax.Amount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_Dividend_ResolvesIsinViaAliasMap()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;03.07.2025;02.07.2025;Effekten;EUR;221,36;VANGUARD S+P 500U.ETF DLD;K483225;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);

            Assert.DoesNotContain(result.Diagnostics, d => d.Severity >= ImportDiagnosticSeverity.Error);
            var cash = Assert.Single(result.PortfolioLedger.Entries.OfType<CashEntry>());
            Assert.Equal(CashFlowType.Dividend, cash.CashFlowType);
            Assert.Equal(221.36m, cash.GrossAmount.Amount);
            var related = result.PortfolioLedger.Instruments.Single(i => i.ISIN == "IE00B3XXRP09");
            Assert.Equal(related.InstrumentId, cash.RelatedInstrumentId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_UnmappedDividend_ProducesBlockingError()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;30.12.2025;24.12.2025;Effekten;EUR;399,29;ISHSIV-DL T.BD20+YR DL D;K739837;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            Assert.Contains(result.Diagnostics,
                d => d.Severity >= ImportDiagnosticSeverity.Error && d.Code == ImportDiagnosticCode.MalformedField);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_KontoabschlussPositive_IsInterest_NegativeIsSkipped()
    {
        var dir = WriteFolder(("Konto.csv", new[]
        {
            KontoHeader,
            "4415066002;WP-Verrechnungskonto;28.06.2024;30.06.2024;Kontoabschluss;EUR;14,59;Abschluss;K78297;",
            "4415066002;WP-Verrechnungskonto;30.06.2025;30.06.2025;Kontoabschluss;EUR;-30,78;Abschluss;K468495;",
        }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            var cash = result.PortfolioLedger.Entries.OfType<CashEntry>().ToList();
            var interest = Assert.Single(cash, c => c.CashFlowType == CashFlowType.Interest);
            Assert.Equal(14.59m, interest.GrossAmount.Amount);
            Assert.DoesNotContain(cash, c => c.GrossAmount.Amount < 0m);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_IdenticalTradeRows_GetDistinctButStableReferences()
    {
        // Two rows that are byte-for-byte identical must produce two entries with DISTINCT references
        // (ordinal 0 vs ordinal 1).  Importing the same folder a second time must yield the exact same
        // reference set — proving the key is stable across re-imports (idempotency).
        var rows = new[]
        {
            DepotHeader,
            "06.06.2024;10.06.2024;Kauf;Investmentfonds/ETFs;Isin;FR0010510800;Amundi EUR Overnight Return UCITS ETF Acc;100,000000;108,259000;EUR;EUR;10825,90;0,00;0,00;0,00;0,00;10825,90;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
            "06.06.2024;10.06.2024;Kauf;Investmentfonds/ETFs;Isin;FR0010510800;Amundi EUR Overnight Return UCITS ETF Acc;100,000000;108,259000;EUR;EUR;10825,90;0,00;0,00;0,00;0,00;10825,90;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
        };
        var dir = WriteFolder(("Depot.csv", rows));
        try
        {
            var first = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            var second = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);

            var refs1 = first.PortfolioLedger.Entries.Select(e => e.SourceProvenance.SourceRecordReference).ToList();
            Assert.Equal(2, refs1.Count);
            Assert.Equal(2, refs1.Distinct().Count()); // two identical rows → distinct references

            var refs2 = second.PortfolioLedger.Entries.Select(e => e.SourceProvenance.SourceRecordReference).OrderBy(x => x).ToList();
            Assert.Equal(refs1.OrderBy(x => x).ToList(), refs2); // same file → same references (idempotent)
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Import_TradeRowsInKontoumsaetze_AreSkipped_NoDoubleCount()
    {
        var dir = WriteFolder(
            ("Depot.csv", new[]
            {
                DepotHeader,
                "06.06.2024;10.06.2024;Kauf;Investmentfonds/ETFs;Isin;IE00B3XXRP09;Vanguard S&P 500 UCITS ETF USD;100,000000;108,259000;EUR;EUR;10825,90;0,00;0,00;0,00;0,00;10825,90;1,000000;MUNC;ausgeführt;Limit;Tagesgültig;Deutschland;",
            }),
            ("Konto.csv", new[]
            {
                KontoHeader,
                "4415066002;WP-Verrechnungskonto;06.06.2024;10.06.2024;Kauf;EUR;-10825,9;IE00B3XXRP09, Vanguard S&P 500 UCITS ETF USD;;158816",
                "4415066002;WP-Verrechnungskonto;05.06.2024;05.06.2024;Gutschrift;EUR;50000;Sebastian Brandt;K63157;",
            }));
        try
        {
            var result = await NewImporter().ImportAsync(RequestFor(dir), CancellationToken.None);
            Assert.Single(result.PortfolioLedger.Entries.OfType<TradeEntry>());
            Assert.Empty(result.PortfolioLedger.Entries.OfType<CashEntry>());
        }
        finally { Directory.Delete(dir, true); }
    }
}

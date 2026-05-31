using System.Globalization;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Ibkr.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Import;

/// <summary>
/// Covers IBKR cancellation handling: a "(Ca.)" trade reverses an earlier identical trade. Getting
/// this wrong silently changes which trades exist and therefore the tax result, so the pairing
/// rules (same instrument/side/quantity, sale amount within a 0.05 tolerance) are pinned here.
/// </summary>
public sealed class IbkrCleanCancellationsTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-cancel-" + Guid.NewGuid().ToString("N"));

    private async Task<ImportResult> ImportTradesAsync(string tradesXml)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "statement.xml");
        await File.WriteAllTextAsync(path,
            $"""
            <FlexQueryResponse>
            <FlexStatements count="1">
            <FlexStatement accountId="U1">
            <Trades>
            {tradesXml}
            </Trades>
            </FlexStatement>
            </FlexStatements>
            </FlexQueryResponse>
            """);

        return await new IbkrStatementImporter().ImportAsync(new ImportRequest
        {
            AccountId = AccountId.NewId(),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, path)
        }, CancellationToken.None);
    }

    private static string Trade(string txId, string buySell, decimal quantity, decimal price, string dateTime, string description = "")
    {
        // Format invariantly: the importer parses with InvariantCulture, so a culture-specific
        // decimal separator (e.g. German "100,004") would otherwise be misread.
        var qty = quantity.ToString(CultureInfo.InvariantCulture);
        var px = price.ToString(CultureInfo.InvariantCulture);
        return $"""<Trade transactionID="{txId}" assetCategory="STK" symbol="VUSA" isin="IE00B3XXRP09" currency="EUR" buySell="{buySell}" quantity="{qty}" tradePrice="{px}" dateTime="{dateTime}" description="{description}" />""";
    }

    [Fact]
    public async Task Cancellation_ExactMatch_RemovesBothEntries()
    {
        var result = await ImportTradesAsync(
            Trade("1", "BUY", 10m, 100m, "20240101;120000")
            + Trade("2", "BUY (Ca.)", 10m, 100m, "20240102;120000"));

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics, d => d.Code == ImportDiagnosticCode.CancellationRemoved);
    }

    [Fact]
    public async Task Cancellation_AmountWithinTolerance_RemovesBothEntries()
    {
        // Original sale amount 1000.00, cancellation 1000.04 — within the 0.05 matching tolerance.
        var result = await ImportTradesAsync(
            Trade("1", "BUY", 10m, 100m, "20240101;120000")
            + Trade("2", "BUY (Ca.)", 10m, 100.004m, "20240102;120000"));

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics, d => d.Code == ImportDiagnosticCode.CancellationRemoved);
    }

    [Fact]
    public async Task Cancellation_AmountBeyondTolerance_RemovesOnlyTheCancellation()
    {
        // Cancellation amount differs by 100 (>0.05) → it does not pair; only the "(Ca.)" entry is dropped,
        // the genuine original survives. No CancellationRemoved diagnostic is emitted.
        var result = await ImportTradesAsync(
            Trade("1", "BUY", 10m, 100m, "20240101;120000")
            + Trade("2", "BUY (Ca.)", 10m, 110m, "20240102;120000"));

        var trade = Assert.Single(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Equal("1", trade.SourceProvenance.SourceRecordReference);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == ImportDiagnosticCode.CancellationRemoved);
    }

    [Fact]
    public async Task Cancellation_DifferentSide_DoesNotPair()
    {
        // A SELL "(Ca.)" cannot cancel a BUY (pairing requires the same side) → only the cancellation drops.
        var result = await ImportTradesAsync(
            Trade("1", "BUY", 10m, 100m, "20240101;120000")
            + Trade("2", "SELL (Ca.)", 10m, 100m, "20240102;120000"));

        var trade = Assert.Single(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Equal("1", trade.SourceProvenance.SourceRecordReference);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == ImportDiagnosticCode.CancellationRemoved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}

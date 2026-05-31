using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Ibkr.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Import;

/// <summary>
/// A required field that is missing or unparseable must surface as an Error diagnostic and produce
/// no entry — never a zero-valued or 0001-01-01 entry (fail-fast, CLAUDE.md "no silent drops").
/// </summary>
public sealed class IbkrStatementImporterFailFastTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-failfast-" + Guid.NewGuid().ToString("N"));

    private async Task<ImportResult> ImportTradeAsync(string tradeElement)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "statement.xml");
        await File.WriteAllTextAsync(path,
            $"""
            <FlexQueryResponse><FlexStatements count="1"><FlexStatement accountId="U1">
            <Trades>{tradeElement}</Trades>
            </FlexStatement></FlexStatements></FlexQueryResponse>
            """);

        return await new IbkrStatementImporter().ImportAsync(new ImportRequest
        {
            AccountId = AccountId.NewId(),
            Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, path)
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Trade_MissingQuantity_EmitsErrorAndProducesNoEntry()
    {
        var result = await ImportTradeAsync(
            """<Trade transactionID="1" assetCategory="STK" symbol="VUSA" isin="IE00B3XXRP09" currency="EUR" buySell="BUY" tradePrice="10" dateTime="20240102;100000" />""");

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics,
            d => d.Code == ImportDiagnosticCode.MalformedField
              && d.Severity == ImportDiagnosticSeverity.Error
              && d.Field == "quantity");
    }

    [Fact]
    public async Task Trade_UnparseableDate_EmitsErrorAndProducesNoEntry()
    {
        var result = await ImportTradeAsync(
            """<Trade transactionID="1" assetCategory="STK" symbol="VUSA" isin="IE00B3XXRP09" currency="EUR" buySell="BUY" quantity="5" tradePrice="10" dateTime="not-a-date" />""");

        Assert.Empty(result.PortfolioLedger.Entries.OfType<TradeEntry>());
        Assert.Contains(result.Diagnostics,
            d => d.Code == ImportDiagnosticCode.MalformedField
              && d.Severity == ImportDiagnosticSeverity.Error
              && d.Field == "dateTime");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}

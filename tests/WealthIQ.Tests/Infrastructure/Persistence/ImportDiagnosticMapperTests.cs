using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Infrastructure.Persistence.Mapping;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Persistence;

public sealed class ImportDiagnosticMapperTests
{
    [Fact]
    public void ToRow_ToDomain_RoundTripsAllFields()
    {
        var batchId = Guid.NewGuid();
        var original = new ImportDiagnostic(
            ImportDiagnosticSeverity.Warning,
            ImportDiagnosticCode.IgnoredAsset,
            "Ignored an out-of-scope asset.",
            Section: "Trades",
            SourceReference: "TR-42",
            Field: "assetCategory");

        var row = ImportDiagnosticMapper.ToRow(original, batchId);
        var restored = ImportDiagnosticMapper.ToDomain(row);

        Assert.Equal(batchId, row.BatchId);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(original, restored);
    }
}

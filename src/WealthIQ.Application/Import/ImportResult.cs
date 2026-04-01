using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

namespace WealthIQ.Application.Import;

public class ImportResult
{
    public PortfolioLedger PortfolioLedger { get; set; } = new([]);
    public List<Instrument> Instruments { get; set; } = new List<Instrument>();
    public List<ImportDiagnostic> Diagnostics { get; set; } = new List<ImportDiagnostic>();
}

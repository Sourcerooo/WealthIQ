using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

public class ImportResult
{
    public List<AccountEvent> AccountEvents { get; set; } = new List<AccountEvent>();
    public List<Instrument> Instruments { get; set; } = new List<Instrument>();
    public List<ImportDiagnostic> Diagnostics { get; set; } = new List<ImportDiagnostic>();
}

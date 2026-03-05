using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Model.Event;

namespace WealthIQ.Application.Import;

public class ImportResult
{
    public List<AccountEvent> AccountEvents { get; set; } = new List<AccountEvent>();
    public List<ImportDiagnostic> Diagnostics { get; set; } = new List<ImportDiagnostic>();
}

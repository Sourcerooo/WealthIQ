using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;

namespace WealthIQ.Infrastructure.IBKR.Import;

public sealed class IbkrStatementImporter : IStatementImporter
{
    public bool CanImport(ImportSource source)
    {
        if (source != null && source.Broker == Broker.InteractiveBrokers && source.Format == Format.XML)
        {
            return true;
        }
        return false;
    }

    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

using WealthIQ.Application.Import.Enumeration;

namespace WealthIQ.Application.Import;

public sealed record class ImportSource(Broker Broker, Format Format, string FilePath)
{
}

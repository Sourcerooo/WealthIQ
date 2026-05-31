using WealthIQ.Application.Import;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class ImportBatchMapper
{
    public static ImportBatchRow ToRow(ImportBatch batch) => new()
    {
        BatchId = batch.BatchId,
        Broker = batch.Broker.ToString(),
        Format = batch.Format.ToString(),
        AccountId = batch.AccountId.Value,
        RawFilePath = batch.RawFilePath,
        ImportedAt = batch.ImportedAt
    };
}

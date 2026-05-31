using System.Text.Json;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class PortfolioEntryMapper
{
    public static PortfolioEntryRow ToRow(PortfolioEntry entry)
    {
        string payload = entry switch
        {
            TradeEntry t => JsonSerializer.Serialize(t, LedgerJson.Options),
            CashEntry c => JsonSerializer.Serialize(c, LedgerJson.Options),
            PositionAdjustmentEntry p => JsonSerializer.Serialize(p, LedgerJson.Options),
            AssetTransferEntry a => JsonSerializer.Serialize(a, LedgerJson.Options),
            _ => throw new NotSupportedException($"Unknown entry type {entry.GetType().Name}")
        };

        return new PortfolioEntryRow
        {
            EntryId = entry.EntryId.Value,
            AccountId = entry.AccountId.Value,
            OccurredAt = entry.OccurredAt,
            EffectiveDate = entry.EffectiveDate,
            Category = entry.Category.ToString(),
            SourceSystem = entry.SourceProvenance.SourceSystem,
            SourceRecordReference = entry.SourceProvenance.SourceRecordReference,
            PayloadJson = payload
        };
    }

    public static PortfolioEntry ToDomain(PortfolioEntryRow row)
    {
        var category = Enum.Parse<PortfolioEntryCategory>(row.Category);
        return category switch
        {
            PortfolioEntryCategory.Trade =>
                JsonSerializer.Deserialize<TradeEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.Cash =>
                JsonSerializer.Deserialize<CashEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.PositionAdjustment =>
                JsonSerializer.Deserialize<PositionAdjustmentEntry>(row.PayloadJson, LedgerJson.Options)!,
            PortfolioEntryCategory.AssetTransfer =>
                JsonSerializer.Deserialize<AssetTransferEntry>(row.PayloadJson, LedgerJson.Options)!,
            _ => throw new NotSupportedException($"Unknown category {row.Category}")
        };
    }
}

namespace WealthIQ.Application.Tax.Report;

/// <summary>All tax years for a single account. The report is strictly per account so data from
/// different brokers/accounts is never mixed (spec §8).</summary>
/// <param name="SourceSystem">
/// The importing broker as recorded on the account's ledger entries (e.g. "IBKR", "TradersPlace"),
/// empty when the account has no entries. Presentation-only — the printed tax report brands its
/// header per broker. Never used in tax math.
/// </param>
public sealed record AccountTaxReport(
    Guid AccountId,
    string AccountNumber,
    IReadOnlyList<AnnualTaxReport> Years,
    string SourceSystem = "");

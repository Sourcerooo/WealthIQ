namespace WealthIQ.Application.Tax.Report;

/// <summary>All tax years for a single account. The report is strictly per account so data from
/// different brokers/accounts is never mixed (spec §8).</summary>
public sealed record AccountTaxReport(
    Guid AccountId,
    string AccountNumber,
    IReadOnlyList<AnnualTaxReport> Years);

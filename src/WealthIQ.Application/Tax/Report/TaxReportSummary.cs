namespace WealthIQ.Application.Tax.Report;

/// <summary>
/// One year's tax totals, all in EUR. <see cref="EstimatedTax"/> is a rough Abgeltungsteuer estimate
/// (25 % + 5.5 % Solidaritätszuschlag = 26.375 %) on the positive taxable base, less foreign withholding
/// tax already paid. It is an estimate for orientation, not a Finanzamt-binding figure (spec §1, §9).
/// </summary>
public sealed record TaxReportSummary(
    decimal NetRealizedGainsTaxable,
    decimal DividendsTaxable,
    decimal InterestTaxable,
    decimal VorabpauschaleTaxable,
    decimal ForeignWithholdingTax,
    decimal EstimatedTax,
    decimal WithheldKESt = 0m);

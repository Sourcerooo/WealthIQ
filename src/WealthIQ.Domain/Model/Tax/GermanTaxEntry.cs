using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Domain.Model.Tax;

public readonly record struct GermanTaxEntry(
    int Year,
    DateOnly Date,
    GermanTaxEntryType Type,
    string Symbol,
    string Isin,
    decimal RawAmount,
    decimal TaxableAmount,
    decimal UsedVorabpauschale = 0m,
    decimal ForeignWithholdingTax = 0m,
    decimal QuantitySold = 0m,
    decimal SaleProceeds = 0m,
    decimal AcquisitionCosts = 0m,
    DateOnly OpenedOn = default,
    // Total transaction fees (open + close) in EUR, for display only. Fees are already embedded in
    // AcquisitionCosts (cost basis) and SaleProceeds — do NOT add Fees to those again.
    decimal Fees = 0m,
    string Origin = "");

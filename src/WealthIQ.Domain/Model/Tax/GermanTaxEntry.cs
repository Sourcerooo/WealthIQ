using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;

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
    string Origin = "",
    // --- Display-only source/explanation fields (item 9). Not used by tax math. ---
    // Cash + sell: broker references and source file for the in-place "Quelle" expander.
    string SourceReference = "",     // cash: txn ref; sell: OPEN trade ref
    string CloseReference = "",      // sell: CLOSE trade ref (empty for cash)
    string SourceFile = "",          // originating statement file
    decimal OriginalAmount = 0m,     // cash: gross amount in original currency
    string OriginalCurrency = "",    // cash: original currency code
                                     // Vorabpauschale: the §18 calculation inputs for the "warum?" expander.
    decimal YearStartPrice = 0m,     // year-start redemption price, EUR
    decimal YearEndPrice = 0m,       // year-end redemption price, EUR
    decimal BasisRate = 0m,          // Basiszins used
    decimal HeldQuantity = 0m,       // shares held in the lot
    decimal DistributionPerShare = 0m,
    decimal MonthFactor = 0m,
    // --- Per-account reporting + broker-withheld German KeSt (display/aggregation only) ---
    AccountId AccountId = default,
    decimal WithheldKESt = 0m,
    // --- Form-line mapping (display/aggregation only, never tax math) ---
    // Anlage KAP-INV needs the fund category per line and the fund name in the Ermittlung.
    TaxAssetClass? AssetClass = null,
    string InstrumentName = "");

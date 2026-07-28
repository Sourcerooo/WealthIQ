using WealthIQ.Domain.Enumeration;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Application.ReferenceData;

public sealed record InstrumentListingDto(
    CurrencyCode Currency,
    string ProviderSymbol,
    string Provider,
    string? Exchange,
    string? Notes);

public sealed record InstrumentAdminDto(
    string Isin,
    string Name,
    string Type,
    decimal Teilfreistellungsquote,
    bool SubjectToVorabpauschale,
    TaxAssetClass? AssetClass,
    IReadOnlyList<InstrumentListingDto> Listings);

public enum UploadMode { Merge, Replace }

public sealed record InstrumentUploadResult(int Profiles, int Listings, IReadOnlyList<string> Warnings);

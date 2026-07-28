using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.ReferenceData;

public sealed class DbInstrumentReferenceAdmin(WealthIqDbContext db) : IInstrumentReferenceAdmin
{
    public async Task<IReadOnlyList<InstrumentAdminDto>> ListAsync(CancellationToken ct = default)
    {
        var profiles = await db.InstrumentProfiles.ToListAsync(ct);
        var listings = await db.InstrumentListings.ToListAsync(ct);
        var listingsByIsin = listings.GroupBy(x => x.Isin).ToDictionary(g => g.Key, g => g.ToList());

        return profiles.Select(p => new InstrumentAdminDto(
            p.Isin, p.Name, p.Type, p.Teilfreistellungsquote, p.SubjectToVorabpauschale,
            TaxAssetClassCode.Parse(p.TaxAssetClass),
            listingsByIsin.TryGetValue(p.Isin, out var lst)
                ? lst.Select(MapListing).ToList()
                : []))
            .ToList();
    }

    public async Task SaveAsync(InstrumentAdminDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Isin)) throw new ArgumentException("ISIN is required.");
        if (dto.Teilfreistellungsquote < 0m || dto.Teilfreistellungsquote > 1m)
            throw new ArgumentException($"Teilfreistellungsquote must be in [0, 1] but was {dto.Teilfreistellungsquote}.");
        if (dto.Listings.Any(l => string.IsNullOrWhiteSpace(l.ProviderSymbol)))
            throw new ArgumentException("All listings must have a non-empty ProviderSymbol.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.InstrumentProfiles.FindAsync(new object[] { dto.Isin }, ct);
        if (existing is null)
        {
            db.InstrumentProfiles.Add(new InstrumentProfileRow
            {
                Isin = dto.Isin,
                Name = dto.Name,
                Type = dto.Type,
                Teilfreistellungsquote = dto.Teilfreistellungsquote,
                SubjectToVorabpauschale = dto.SubjectToVorabpauschale,
                TaxAssetClass = TaxAssetClassCode.ToCode(dto.AssetClass)
            });
        }
        else
        {
            existing.Name = dto.Name; existing.Type = dto.Type;
            existing.Teilfreistellungsquote = dto.Teilfreistellungsquote;
            existing.SubjectToVorabpauschale = dto.SubjectToVorabpauschale;
            existing.TaxAssetClass = TaxAssetClassCode.ToCode(dto.AssetClass);
        }

        // Replace all listings for this ISIN
        var oldListings = db.InstrumentListings.Where(l => l.Isin == dto.Isin);
        db.InstrumentListings.RemoveRange(oldListings);
        foreach (var l in dto.Listings)
        {
            db.InstrumentListings.Add(new InstrumentListingRow
            {
                Isin = dto.Isin,
                Currency = l.Currency.ToString(),
                Provider = l.Provider,
                ProviderSymbol = l.ProviderSymbol,
                Exchange = l.Exchange,
                Notes = l.Notes
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> IsReferencedByLedgerAsync(string isin, CancellationToken ct = default)
    {
        return await db.Instruments.AnyAsync(x => x.ISIN == isin, ct);
    }

    public async Task DeleteAsync(string isin, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var profile = await db.InstrumentProfiles.FindAsync(new object[] { isin }, ct);
        if (profile is not null) db.InstrumentProfiles.Remove(profile);
        var listings = db.InstrumentListings.Where(l => l.Isin == isin);
        db.InstrumentListings.RemoveRange(listings);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<InstrumentUploadResult> UploadAsync(string instrumentsJson, string listingsJson, UploadMode mode, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (mode == UploadMode.Replace)
        {
            db.InstrumentProfiles.RemoveRange(db.InstrumentProfiles);
            db.InstrumentListings.RemoveRange(db.InstrumentListings);
            await db.SaveChangesAsync(ct);
        }

        var profiles = JsonSerializer.Deserialize<Dictionary<string, InstrumentProfileDto>>(instrumentsJson)
            ?? throw new ArgumentException("Invalid instruments JSON.");
        var listingsMap = JsonSerializer.Deserialize<Dictionary<string, List<ListingDto>>>(listingsJson)
            ?? throw new ArgumentException("Invalid listings JSON.");

        var profileCount = 0;
        var listingCount = 0;

        foreach (var (isin, dto) in profiles)
        {
            decimal tfs;
            if (dto.TfsRaw is JsonElement elem)
            {
                tfs = elem.ValueKind switch
                {
                    JsonValueKind.Number => elem.GetDecimal(),
                    JsonValueKind.String when decimal.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
                    _ => -1m
                };
            }
            else if (!decimal.TryParse(dto.TfsRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out tfs))
            {
                tfs = -1m;
            }

            if (tfs < 0m || tfs > 1m)
            {
                warnings.Add($"Invalid tfs_quote for {isin}.");
                continue;
            }

            var existing = await db.InstrumentProfiles.FindAsync(new object[] { isin }, ct);
            if (existing is null)
            {
                db.InstrumentProfiles.Add(new InstrumentProfileRow { Isin = isin, Name = dto.Name, Type = dto.Type, Teilfreistellungsquote = tfs, SubjectToVorabpauschale = dto.SubjectToVorabpauschale, TaxAssetClass = dto.TaxAssetClass });
            }
            else
            {
                existing.Name = dto.Name; existing.Type = dto.Type;
                existing.Teilfreistellungsquote = tfs; existing.SubjectToVorabpauschale = dto.SubjectToVorabpauschale;
                existing.TaxAssetClass = dto.TaxAssetClass;
            }

            profileCount++;
        }

        foreach (var (isin, listings) in listingsMap)
        {
            if (mode == UploadMode.Merge)
            {
                db.InstrumentListings.RemoveRange(db.InstrumentListings.Where(l => l.Isin == isin));
            }

            foreach (var l in listings)
            {
                db.InstrumentListings.Add(new InstrumentListingRow
                {
                    Isin = isin,
                    Currency = l.Currency,
                    Provider = l.Provider,
                    ProviderSymbol = l.ProviderSymbol,
                    Exchange = l.Exchange,
                    Notes = l.Notes
                });
                listingCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new InstrumentUploadResult(profileCount, listingCount, warnings);
    }

    private static InstrumentListingDto MapListing(InstrumentListingRow row)
    {
        Enum.TryParse<CurrencyCode>(row.Currency, ignoreCase: true, out var currency);
        return new InstrumentListingDto(currency, row.ProviderSymbol, row.Provider, row.Exchange, row.Notes);
    }

    private sealed class InstrumentProfileDto
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("type")] public string Type { get; init; } = "";
        [JsonPropertyName("tfs_quote")] public object? TfsRaw { get; init; }
        [JsonPropertyName("subject_to_vorabpauschale")] public bool SubjectToVorabpauschale { get; init; }
        [JsonPropertyName("tax_asset_class")] public string? TaxAssetClass { get; init; }
    }

    private sealed class ListingDto
    {
        [JsonPropertyName("currency")] public string Currency { get; init; } = "";
        [JsonPropertyName("provider")] public string Provider { get; init; } = "YahooFinance";
        [JsonPropertyName("provider_symbol")] public string ProviderSymbol { get; init; } = "";
        [JsonPropertyName("exchange")] public string? Exchange { get; init; }
        [JsonPropertyName("notes")] public string? Notes { get; init; }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Seeds reference data from the shipped CSV/JSON files. Each table is seeded only when empty,
/// so re-running never duplicates rows or clobbers later edits. Files must exist (fail-fast, spec §8).
/// </summary>
public sealed class ReferenceDataSeeder(WealthIqDbContext db) : IReferenceDataSeeder
{
    public async Task<ReferenceDataSeedResult> SeedIfEmptyAsync(ReferenceDataSources sources, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await db.BasisInterestRates.AnyAsync(ct))
        {
            db.BasisInterestRates.AddRange(ReadBasisInterestRates(sources.BasisInterestRateCsvPath));
        }

        if (!await db.HistoricalPrices.AnyAsync(ct))
        {
            db.HistoricalPrices.AddRange(ReadHistoricalPrices(sources.HistoricalPriceCsvPath));
        }

        if (!await db.InstrumentProfiles.AnyAsync(ct))
        {
            db.InstrumentProfiles.AddRange(ReadInstrumentProfiles(sources.InstrumentProfileJsonPath));
        }

        if (!await db.InstrumentListings.AnyAsync(ct))
        {
            db.InstrumentListings.AddRange(ReadInstrumentListings(sources.InstrumentListingJsonPath));
        }

        if (!await db.FxRates.AnyAsync(ct))
        {
            db.FxRates.AddRange(ReadFxRates(sources.FxRateCsvPath));
        }

        if (!await db.DividendAliases.AnyAsync(ct))
        {
            db.DividendAliases.AddRange(ReadDividendAliases(sources.DividendAliasCsvPath));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ReferenceDataSeedResult(
            await db.BasisInterestRates.CountAsync(ct),
            await db.HistoricalPrices.CountAsync(ct),
            await db.InstrumentProfiles.CountAsync(ct),
            await db.InstrumentListings.CountAsync(ct),
            await db.FxRates.CountAsync(ct));
    }

    private static IEnumerable<BasisInterestRateRow> ReadBasisInterestRates(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "Basis interest rate file not found.", minColumns: 2))
        {
            if (!int.TryParse(parts[0], out var year)
                || !decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid year or rate.");
            }

            yield return new BasisInterestRateRow { Year = year, Rate = rate };
        }
    }

    private static IEnumerable<HistoricalPriceRow> ReadHistoricalPrices(string path)
    {
        foreach (var (_, parts) in ReadCsv(path, "Historical price file not found.", minColumns: 9))
        {
            if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var open)
                || !decimal.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high)
                || !decimal.TryParse(parts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low)
                || !decimal.TryParse(parts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var close)
                || !decimal.TryParse(parts[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var adj)
                || !long.TryParse(parts[8].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            {
                continue;
            }

            yield return new HistoricalPriceRow
            {
                ProviderSymbol = parts[1].Trim(), Date = date, Currency = parts[2].Trim(),
                Open = open, High = high, Low = low, Close = close, AdjustedClose = adj, Volume = volume
            };
        }
    }

    private static IEnumerable<FxRateRow> ReadFxRates(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "FX rate file not found.", minColumns: 3))
        {
            if (!DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate)
                || rate <= 0m)
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid date or non-positive rate.");
            }

            yield return new FxRateRow { Date = date, Currency = parts[1].Trim(), RateToEur = rate };
        }
    }

    private static IEnumerable<InstrumentProfileRow> ReadInstrumentProfiles(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Instrument profile file not found.", path);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, InstrumentProfileDto>>(json)
            ?? throw new InvalidOperationException("Instrument profile file could not be parsed.");

        foreach (var (isin, dto) in raw)
        {
            if (!decimal.TryParse(dto.TeilfreistellungsquoteRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tfs))
            {
                throw new InvalidOperationException($"Invalid tfs_quote for instrument '{isin}'.");
            }

            yield return new InstrumentProfileRow
            {
                Isin = isin, Name = dto.Name, Type = dto.Type,
                Teilfreistellungsquote = tfs, SubjectToVorabpauschale = dto.SubjectToVorabpauschale
            };
        }
    }

    private static IEnumerable<InstrumentListingRow> ReadInstrumentListings(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Instrument listings file not found.", path);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<ListingDto>>>(json)
            ?? throw new InvalidOperationException("Instrument listings file could not be parsed.");

        foreach (var (isin, listings) in raw)
        {
            foreach (var dto in listings)
            {
                if (string.IsNullOrWhiteSpace(dto.ProviderSymbol))
                {
                    continue;
                }

                yield return new InstrumentListingRow
                {
                    Isin = isin,
                    Currency = dto.Currency,
                    Provider = dto.Provider,
                    ProviderSymbol = dto.ProviderSymbol,
                    Exchange = dto.Exchange,
                    Notes = dto.Notes
                };
            }
        }
    }

    private static IEnumerable<DividendAliasRow> ReadDividendAliases(string path)
    {
        foreach (var (_, parts) in ReadCsv(path, "Dividend alias file not found.", minColumns: 2))
        {
            var alias = parts[0].Trim();
            var isin = parts[1].Trim();
            if (alias.Length == 0 || isin.Length == 0)
            {
                continue;
            }

            yield return new DividendAliasRow
            {
                NormalizedAlias = WealthIQ.Application.ReferenceData.DividendAliasNormalizer.Normalize(alias),
                Alias = alias,
                Isin = isin
            };
        }
    }

    private static IEnumerable<(int LineNumber, string[] Parts)> ReadCsv(string path, string notFoundMessage, int minColumns)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(notFoundMessage, path);
        }

        var lineNumber = 1; // header is line 1
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < minColumns)
            {
                throw new FormatException(
                    $"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: expected at least {minColumns} columns, got {parts.Length}.");
            }

            yield return (lineNumber, parts);
        }
    }

    private sealed class InstrumentProfileDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("tfs_quote")]
        public object? TeilfreistellungsquoteRaw { get; init; }

        [JsonPropertyName("subject_to_vorabpauschale")]
        public bool SubjectToVorabpauschale { get; init; }
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

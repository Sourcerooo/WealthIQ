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

        if (!await db.YearEndPrices.AnyAsync(ct))
        {
            db.YearEndPrices.AddRange(ReadYearEndPrices(sources.YearEndPriceCsvPath));
        }

        if (!await db.InstrumentProfiles.AnyAsync(ct))
        {
            db.InstrumentProfiles.AddRange(ReadInstrumentProfiles(sources.InstrumentProfileJsonPath));
        }

        if (!await db.FxRates.AnyAsync(ct))
        {
            db.FxRates.AddRange(ReadFxRates(sources.FxRateCsvPath));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ReferenceDataSeedResult(
            await db.BasisInterestRates.CountAsync(ct),
            await db.YearEndPrices.CountAsync(ct),
            await db.InstrumentProfiles.CountAsync(ct),
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

    private static IEnumerable<YearEndPriceRow> ReadYearEndPrices(string path)
    {
        foreach (var (lineNumber, parts) in ReadCsv(path, "Year-end price file not found.", minColumns: 3))
        {
            if (!int.TryParse(parts[0], out var year)
                || !decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                throw new FormatException($"Malformed row in '{Path.GetFileName(path)}' line {lineNumber}: invalid year or price.");
            }

            yield return new YearEndPriceRow { Year = year, Isin = parts[1].Trim(), PriceEur = price };
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

            yield return new InstrumentProfileRow { Isin = isin, Name = dto.Name, Teilfreistellungsquote = tfs };
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

        [JsonPropertyName("tfs_quote")]
        public object? TeilfreistellungsquoteRaw { get; init; }
    }
}

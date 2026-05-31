using System.Globalization;
using WealthIQ.Application.Tax.Interface;

namespace WealthIQ.Infrastructure.Ibkr.Tax;

public sealed class CsvBasisInterestRateProvider : IBasisInterestRateProvider
{
    private readonly Dictionary<int, decimal> _rates = new();

    public CsvBasisInterestRateProvider(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Basis interest rate file not found.", filePath);
        }

        foreach (var line in File.ReadLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 2)
            {
                continue;
            }

            if (int.TryParse(parts[0], out var year)
                && decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            {
                _rates[year] = rate;
            }
        }
    }

    public decimal GetRate(int year) => _rates.GetValueOrDefault(year);
}

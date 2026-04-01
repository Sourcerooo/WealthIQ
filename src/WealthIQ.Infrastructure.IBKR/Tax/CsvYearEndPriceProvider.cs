using System.Globalization;
using WealthIQ.Application.Tax.Interface;

namespace WealthIQ.Infrastructure.IBKR.Tax;

public sealed class CsvYearEndPriceProvider : IYearEndPriceProvider
{
    private readonly Dictionary<(int Year, string Isin), decimal> _prices = new();

    public CsvYearEndPriceProvider(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Year-end price file not found.", filePath);
        }

        foreach (var line in File.ReadLines(filePath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 3)
            {
                continue;
            }

            if (int.TryParse(parts[0], out var year)
                && decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
            {
                _prices[(year, parts[1].Trim())] = price;
            }
        }
    }

    public decimal? GetPrice(string isin, int year)
        => _prices.TryGetValue((year, isin), out var price) ? price : null;
}

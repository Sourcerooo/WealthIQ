using System.Globalization;
using System.Text;

namespace WealthIQ.Infrastructure.TradersPlace.Import;

/// <summary>Low-level parsing for Trader's Place CSV exports: Windows-1252 (Latin1) decoding,
/// German decimal/date formats, semicolon separation. Latin1 is built-in and ICU-independent so it
/// works the same on the ubuntu CI runner.</summary>
public static class TradersPlaceCsv
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static IReadOnlyList<string> ReadLines(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Trader's Place CSV not found.", path);
        }

        // Latin1 == ISO-8859-1; the umlauts used by Trader's Place (0xC0–0xFF) coincide with
        // Windows-1252, so this decodes ä/ö/ü/ß correctly without the code-pages package.
        return File.ReadAllLines(path, Encoding.Latin1);
    }

    public static string[] SplitRow(string line) => line.Split(';');

    public static decimal ParseDecimal(string value)
        => decimal.Parse(value.Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, German);

    public static bool TryParseDecimal(string? value, out decimal result)
        => decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign, German, out result);

    public static DateOnly ParseDate(string value)
        => DateOnly.ParseExact(value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? value, out DateOnly result)
        => DateOnly.TryParseExact((value ?? string.Empty).Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}

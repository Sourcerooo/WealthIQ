using System.Globalization;
using System.Text.RegularExpressions;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;

namespace WealthIQ.Infrastructure.Ibkr.Tax;

/// <summary>Fetches the official BMF Basiszins for a year by GET-ing the publication page and
/// regex-scanning for the year + German percentage near it. Returns null on failure — caller raises
/// a diagnostic. Page-format drift is an accepted risk (spec §15); manual override is the fallback.</summary>
public sealed class BmfBasisInterestRateSource(HttpClient httpClient, BasisInterestRateSourceOptions options)
    : IBasisInterestRateSource
{
    // Matches e.g. "2025: 2,53 Prozent" or "2025: 2,53 %" in various spacing/HTML forms.
    private static readonly Regex YearRatePattern = new(
        @"(?<year>\d{4})[^0-9]{0,50}?(?<pct>\d+,\d+)\s*(?:Prozent|%)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<BasisInterestRateRecord?> FetchAsync(int year, CancellationToken ct)
    {
        string html;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            using var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return null; // fetch failure → null; caller raises diagnostic
        }

        foreach (Match match in YearRatePattern.Matches(html))
        {
            if (!int.TryParse(match.Groups["year"].Value, out var matchedYear) || matchedYear != year)
            {
                continue;
            }

            var pctText = match.Groups["pct"].Value.Replace(',', '.');
            if (decimal.TryParse(pctText, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
            {
                return new BasisInterestRateRecord(year, pct / 100m);
            }
        }

        return null; // year not found on page
    }
}

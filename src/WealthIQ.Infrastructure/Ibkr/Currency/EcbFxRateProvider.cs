using System.Globalization;
using System.Xml.Linq;
using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;

namespace WealthIQ.Infrastructure.Ibkr.Currency;

/// <summary>Thin HttpClient port of download_fx_rates.py. Parses ECB eurofxref-hist.xml daily cubes,
/// emits EUR=1.0 plus currency_to_eur = 1/rate for the supported currencies, within [from, to] (spec §5.2).</summary>
public sealed class EcbFxRateProvider(HttpClient httpClient, FxRateProviderOptions options) : IFxRateProvider
{
    private static readonly XNamespace Def = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    public async Task<IReadOnlyList<FxRateRecord>> FetchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.HistoricalUrl);
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct);

        var supported = options.SupportedCurrencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = XDocument.Parse(xml);
        var rows = new List<FxRateRecord>();

        foreach (var dayCube in root.Descendants(Def + "Cube").Where(c => c.Attribute("time") is not null))
        {
            if (!DateOnly.TryParseExact(dayCube.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date < from || date > to)
            {
                continue;
            }

            rows.Add(new FxRateRecord(date, "EUR", 1m));
            foreach (var rateCube in dayCube.Elements(Def + "Cube").Where(c => c.Attribute("currency") is not null && c.Attribute("rate") is not null))
            {
                var currency = rateCube.Attribute("currency")!.Value;
                if (!supported.Contains(currency))
                {
                    continue;
                }

                if (decimal.TryParse(rateCube.Attribute("rate")!.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var eurToCurrency) && eurToCurrency > 0m)
                {
                    rows.Add(new FxRateRecord(date, currency, 1m / eurToCurrency));
                }
            }
        }

        return rows;
    }
}

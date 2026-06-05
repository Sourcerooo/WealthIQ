using System.Net;
using System.Text.Json;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.MarketData;

/// <summary>Thin HttpClient port of download_price_history.py's v8 chart call. Sequential per symbol with
/// bounded exponential back-off on 429/5xx (spec §5.1). Parses chart.result[0]: meta.currency,
/// timestamp[], indicators.quote[0] OHLCV, indicators.adjclose[0]; skips incomplete rows.</summary>
public sealed class YahooHistoricalPriceProvider(HttpClient httpClient, HistoricalPriceProviderOptions options)
    : IHistoricalPriceProvider
{
    public async Task<HistoricalPriceFetchResult> FetchAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var period1 = ToUnixSeconds(from);
        var period2 = ToUnixSeconds(to.AddDays(1));
        var url = $"{options.BaseUrl}{Uri.EscapeDataString(providerSymbol)}" +
                  $"?period1={period1}&period2={period2}&interval=1d&includePrePost=false&events=history";

        var json = await GetWithRetryAsync(url, providerSymbol, ct);
        using var doc = JsonDocument.Parse(json);

        var chart = doc.RootElement.GetProperty("chart");
        if (!chart.TryGetProperty("result", out var resultArray) || resultArray.ValueKind != JsonValueKind.Array || resultArray.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Yahoo returned no result for '{providerSymbol}'.");
        }

        var result = resultArray[0];
        var currencyText = result.GetProperty("meta").GetProperty("currency").GetString();
        if (!Enum.TryParse<CurrencyCode>(currencyText, ignoreCase: true, out var currency))
        {
            throw new InvalidOperationException($"Yahoo returned unsupported/missing currency '{currencyText}' for '{providerSymbol}'.");
        }

        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var adjclose = result.GetProperty("indicators").GetProperty("adjclose")[0].GetProperty("adjclose");
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var bars = new List<PriceBar>();
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            if (!TryDecimal(opens[i], out var open) || !TryDecimal(highs[i], out var high) || !TryDecimal(lows[i], out var low)
                || !TryDecimal(closes[i], out var close) || !TryDecimal(adjclose[i], out var adj) || volumes[i].ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime);
            bars.Add(new PriceBar(date, providerSymbol, currency, open, high, low, close, adj, volumes[i].GetInt64()));
        }

        return new HistoricalPriceFetchResult(providerSymbol, currency, bars);
    }

    private async Task<string> GetWithRetryAsync(string url, string providerSymbol, CancellationToken ct)
    {
        var backoff = options.InitialBackoffMs;
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            using var response = await httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(ct);
            }

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!transient || attempt >= options.MaxRetries)
            {
                throw new InvalidOperationException($"Yahoo request for '{providerSymbol}' failed with {(int)response.StatusCode} after {attempt + 1} attempt(s).");
            }

            await Task.Delay(backoff, ct);
            backoff *= 2;
        }
    }

    private static long ToUnixSeconds(DateOnly date)
        => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = 0m;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value);
    }
}

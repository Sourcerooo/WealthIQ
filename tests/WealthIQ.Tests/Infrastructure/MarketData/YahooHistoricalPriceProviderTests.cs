using System.Net;
using WealthIQ.Application.MarketData;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.MarketData;

public sealed class YahooHistoricalPriceProviderTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task FetchAsync_ParsesBarsAndCurrency()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "yahoo_chart_vusa.json"));
        var client = new HttpClient(new StubHandler(json));
        var provider = new YahooHistoricalPriceProvider(client, new HistoricalPriceProviderOptions());

        var result = await provider.FetchAsync("VUSA.L", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3), CancellationToken.None);

        Assert.Equal(CurrencyCode.GBP, result.Currency);
        Assert.Equal(2, result.Bars.Count);
        Assert.Equal(90.5m, result.Bars[0].Close);
        Assert.Equal(CurrencyCode.GBP, result.Bars[0].Currency);
    }
}

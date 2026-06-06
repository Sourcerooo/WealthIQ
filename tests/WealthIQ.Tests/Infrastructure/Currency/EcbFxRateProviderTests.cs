using System.Net;
using WealthIQ.Infrastructure.Ibkr.Currency;

namespace WealthIQ.Tests.Infrastructure.Currency;

public sealed class EcbFxRateProviderTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task FetchAsync_EmitsEurAndInvertedSupportedRatesWithinWindow()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ecb_eurofxref_hist.xml"));
        var provider = new EcbFxRateProvider(new HttpClient(new StubHandler(xml)), new());

        var rows = await provider.FetchAsync(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), null, CancellationToken.None);

        Assert.Contains(rows, r => r.Date == new DateOnly(2024, 12, 30) && r.Currency == "EUR" && r.RateToEur == 1m);
        Assert.Contains(rows, r => r.Currency == "USD" && Math.Round(r.RateToEur, 6) == Math.Round(1m / 1.0400m, 6));
        Assert.DoesNotContain(rows, r => r.Currency == "JPY"); // not in SupportedCurrencies
        Assert.DoesNotContain(rows, r => r.Date == new DateOnly(2020, 1, 2)); // outside window
    }

    [Fact]
    public async Task FetchAsync_WithCurrencyFilter_ReturnsOnlyRequestedPlusEur()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ecb_eurofxref_hist.xml"));
        var provider = new EcbFxRateProvider(new HttpClient(new StubHandler(xml)), new());

        var rows = await provider.FetchAsync(new DateOnly(1999, 1, 1), new DateOnly(2099, 1, 1),
            new[] { "GBP" }, CancellationToken.None);

        Assert.Contains(rows, r => r.Currency == "GBP");
        Assert.DoesNotContain(rows, r => r.Currency == "USD");
        Assert.Contains(rows, r => r.Currency == "EUR"); // base always emitted
    }
}

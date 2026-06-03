using System.Net;
using WealthIQ.Application.Tax;
using WealthIQ.Infrastructure.Ibkr.Tax;

namespace WealthIQ.Tests.Infrastructure.Tax;

public sealed class BmfBasisInterestRateSourceTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task FetchAsync_ParsesYearAndPercentage()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bmf_basiszins_2025.html"));
        var source = new BmfBasisInterestRateSource(new HttpClient(new StubHandler(html)), new BasisInterestRateSourceOptions());

        var record = await source.FetchAsync(2025, CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal(2025, record!.Year);
        Assert.Equal(0.0253m, record.Rate);
    }

    [Fact]
    public async Task FetchAsync_YearNotFound_ReturnsNull()
    {
        var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bmf_basiszins_2025.html"));
        var source = new BmfBasisInterestRateSource(new HttpClient(new StubHandler(html)), new BasisInterestRateSourceOptions());

        var record = await source.FetchAsync(2099, CancellationToken.None);

        Assert.Null(record);
    }
}

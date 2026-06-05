using WealthIQ.Infrastructure.Ibkr.Tax;

namespace WealthIQ.Tests.Application.Tax;

public sealed class BasisInterestRateProviderTests
{
    [Fact]
    public void GetRate_MissingYear_ReturnsNull()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "year,rate\n2023,0.0255\n");
        var provider = new CsvBasisInterestRateProvider(path);

        Assert.Null(provider.GetRate(2099));
        Assert.Equal(0.0255m, provider.GetRate(2023));
    }
}

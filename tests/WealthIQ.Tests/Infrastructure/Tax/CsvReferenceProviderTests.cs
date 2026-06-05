using WealthIQ.Infrastructure.Ibkr.Tax;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Tax;

public sealed class CsvReferenceProviderTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-refcsv-" + Guid.NewGuid().ToString("N"));

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void BasisInterestRate_ParsesYears_AndReturnsNullForUnknownYear()
    {
        var provider = new CsvBasisInterestRateProvider(Write("basiszins.csv",
            """
            year,rate
            2023,0.0255
            2024,0.0229
            bad,row
            """));

        Assert.Equal(0.0255m, provider.GetRate(2023));
        Assert.Equal(0.0229m, provider.GetRate(2024));
        Assert.Null(provider.GetRate(1999)); // unknown year → null (data gap)
    }

    [Fact]
    public void BasisInterestRate_FileNotFound_Throws()
        => Assert.Throws<FileNotFoundException>(() => new CsvBasisInterestRateProvider(Path.Combine(_temp, "nope.csv")));

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}

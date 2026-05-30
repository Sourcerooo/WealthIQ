using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbReferenceProviderTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.BasisInterestRates.AddRange(
            new BasisInterestRateRow { Year = 2023, Rate = 0.0255m },
            new BasisInterestRateRow { Year = 2024, Rate = 0.0229m });
        ctx.YearEndPrices.AddRange(
            new YearEndPriceRow { Year = 2024, Isin = "IE00B3XXRP09", PriceEur = 106.47m },
            new YearEndPriceRow { Year = 2024, Isin = "IE00B4ND3602", PriceEur = 48.77m });
        ctx.SaveChanges();
        return db;
    }

    [Fact]
    public void BasisInterestRate_ReturnsRate_AndZeroForUnknownYear()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var provider = new DbBasisInterestRateProvider(ctx);

        Assert.Equal(0.0255m, provider.GetRate(2023));
        Assert.Equal(0.0229m, provider.GetRate(2024));
        Assert.Equal(0m, provider.GetRate(1999)); // unknown year → 0 (no Vorabpauschale)
    }

    [Fact]
    public void YearEndPrice_ReturnsPrice_AndNullForUnknown()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var provider = new DbYearEndPriceProvider(ctx);

        Assert.Equal(106.47m, provider.GetPrice("IE00B3XXRP09", 2024));
        Assert.Equal(48.77m, provider.GetPrice("IE00B4ND3602", 2024));
        Assert.Null(provider.GetPrice("IE00B3XXRP09", 2023)); // right ISIN, wrong year
        Assert.Null(provider.GetPrice("UNKNOWN", 2024));        // unknown ISIN
    }
}

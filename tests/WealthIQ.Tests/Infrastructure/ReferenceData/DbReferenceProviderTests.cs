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
        ctx.SaveChanges();
        return db;
    }

    [Fact]
    public void BasisInterestRate_ReturnsRate_AndNullForUnknownYear()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var provider = new DbBasisInterestRateProvider(ctx);

        Assert.Equal(0.0255m, provider.GetRate(2023));
        Assert.Equal(0.0229m, provider.GetRate(2024));
        Assert.Null(provider.GetRate(1999)); // unknown year → null (data gap)
    }
}

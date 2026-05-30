using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class ReferenceDataSeederTests
{
    private static ReferenceDataSources Sources()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "ReferenceData", "Fixtures");
        return new ReferenceDataSources(
            Path.Combine(dir, "basiszins.csv"),
            Path.Combine(dir, "prices.csv"),
            Path.Combine(dir, "instruments.json"),
            Path.Combine(dir, "fx_rates.csv"));
    }

    [Fact]
    public async Task SeedIfEmpty_LoadsAllFourTables()
    {
        using var db = new InMemorySqlite();

        ReferenceDataSeedResult result;
        await using (var ctx = db.NewContext())
        {
            result = await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 3), result);

        await using (var ctx = db.NewContext())
        {
            var basis = await ctx.BasisInterestRates.SingleAsync(x => x.Year == 2024);
            Assert.Equal(0.0229m, basis.Rate);

            var price = await ctx.YearEndPrices.SingleAsync(x => x.Year == 2024 && x.Isin == "IE00B3XXRP09");
            Assert.Equal(106.47m, price.PriceEur);

            var profile = await ctx.InstrumentProfiles.SingleAsync(x => x.Isin == "IE00B3XXRP09");
            Assert.Equal(0.30m, profile.Teilfreistellungsquote);

            var fx = await ctx.FxRates.SingleAsync(x => x.Date == new DateOnly(2021, 3, 26) && x.Currency == "USD");
            Assert.Equal(0.8487523341m, fx.RateToEur);
        }
    }

    [Fact]
    public async Task SeedIfEmpty_RunTwice_IsIdempotent()
    {
        using var db = new InMemorySqlite();

        await using (var ctx = db.NewContext())
        {
            await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        ReferenceDataSeedResult second;
        await using (var ctx = db.NewContext())
        {
            second = await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 3), second);

        await using (var ctx = db.NewContext())
        {
            Assert.Equal(2, await ctx.BasisInterestRates.CountAsync());
            Assert.Equal(3, await ctx.FxRates.CountAsync());
        }
    }
}

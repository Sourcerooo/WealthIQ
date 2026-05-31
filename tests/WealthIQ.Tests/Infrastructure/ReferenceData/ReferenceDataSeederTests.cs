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

    [Fact]
    public async Task SeedIfEmpty_MalformedFxRow_ThrowsWithFileAndLine()
    {
        using var db = new InMemorySqlite();
        var dir = Path.Combine(Path.GetTempPath(), "wealthiq-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Header + one good row + one malformed row (non-numeric rate) on line 3.
            var fxPath = Path.Combine(dir, "fx_rates.csv");
            await File.WriteAllTextAsync(fxPath, "date,currency,rate\n2024-01-02,USD,0.91\n2024-01-03,USD,not-a-rate\n");

            // Minimal valid files for the other three sources so FX is what fails.
            var basisPath = Path.Combine(dir, "basiszins.csv");
            await File.WriteAllTextAsync(basisPath, "year,rate\n2024,0.0255\n");
            var pricesPath = Path.Combine(dir, "prices.csv");
            await File.WriteAllTextAsync(pricesPath, "year,isin,price\n2024,IE00B3XXRP09,200\n");
            var instrumentsPath = Path.Combine(dir, "instruments.json");
            await File.WriteAllTextAsync(instrumentsPath, "{}");

            var sources = new ReferenceDataSources(basisPath, pricesPath, instrumentsPath, fxPath);

            await using var ctx = db.NewContext();
            var seeder = new ReferenceDataSeeder(ctx);

            var ex = await Assert.ThrowsAsync<FormatException>(() => seeder.SeedIfEmptyAsync(sources));
            Assert.Contains("fx_rates.csv", ex.Message);
            Assert.Contains("line 3", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

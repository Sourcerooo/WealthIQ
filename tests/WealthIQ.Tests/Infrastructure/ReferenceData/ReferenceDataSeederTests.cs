using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Infrastructure.Persistence;
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
            Path.Combine(dir, "historical_prices.csv"),
            Path.Combine(dir, "instruments.json"),
            Path.Combine(dir, "listings.json"),
            Path.Combine(dir, "fx_rates.csv"),
            Path.Combine(dir, "dividend_aliases.csv"));
    }

    [Fact]
    public async Task SeedIfEmpty_LoadsAllTables()
    {
        using var db = new InMemorySqlite();

        ReferenceDataSeedResult result;
        await using (var ctx = db.NewContext())
        {
            result = await new ReferenceDataSeeder(ctx).SeedIfEmptyAsync(Sources());
        }

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 2, 3), result);

        await using (var ctx = db.NewContext())
        {
            var basis = await ctx.BasisInterestRates.SingleAsync(x => x.Year == 2024);
            Assert.Equal(0.0229m, basis.Rate);

            var profile = await ctx.InstrumentProfiles.SingleAsync(x => x.Isin == "IE00B3XXRP09");
            Assert.Equal(0.30m, profile.Teilfreistellungsquote);
            Assert.Equal("ETF_EQUITY", profile.Type);
            Assert.True(profile.SubjectToVorabpauschale);

            var listing = await ctx.InstrumentListings.SingleAsync(x => x.Isin == "IE00B3XXRP09");
            Assert.Equal("VUSA.L", listing.ProviderSymbol);
            Assert.Equal("GBP", listing.Currency);

            var price = await ctx.HistoricalPrices.SingleAsync(x => x.ProviderSymbol == "VUSA.L");
            Assert.Equal(new DateOnly(2024, 12, 30), price.Date);
            Assert.Equal("GBP", price.Currency);

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

        Assert.Equal(new ReferenceDataSeedResult(2, 2, 2, 2, 3), second);

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

            // Minimal valid files for the other sources so FX is what fails.
            var basisPath = Path.Combine(dir, "basiszins.csv");
            await File.WriteAllTextAsync(basisPath, "year,rate\n2024,0.0255\n");
            var historicalPricesPath = Path.Combine(dir, "historical_prices.csv");
            await File.WriteAllTextAsync(historicalPricesPath, "date,provider_symbol,currency,open,high,low,close,adjusted_close,volume\n");
            var instrumentsPath = Path.Combine(dir, "instruments.json");
            await File.WriteAllTextAsync(instrumentsPath, "{}");
            var listingsPath = Path.Combine(dir, "listings.json");
            await File.WriteAllTextAsync(listingsPath, "{}");

            var aliasCsvPath = Path.Combine(dir, "dividend_aliases.csv");
            await File.WriteAllTextAsync(aliasCsvPath, "alias,isin\n");
            var sources = new ReferenceDataSources(basisPath, historicalPricesPath, instrumentsPath, listingsPath, fxPath, aliasCsvPath);

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

    [Fact]
    public async Task SeedIfEmptyAsync_LoadsPricesProfilesListings()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var dir = Path.Combine(Path.GetTempPath(), "wiq-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "basiszins.csv"), "year,rate\n2024,0.0229\n");
            File.WriteAllText(Path.Combine(dir, "historical_prices.csv"),
                "date,provider_symbol,currency,open,high,low,close,adjusted_close,volume\n2024-12-30,CNDX.AS,EUR,1,1,1,2,2,3\n");
            File.WriteAllText(Path.Combine(dir, "instruments.json"),
                "{\"IE00B53SZB19\":{\"name\":\"x\",\"type\":\"ETF_EQUITY\",\"tfs_quote\":0.30,\"subject_to_vorabpauschale\":true}}");
            File.WriteAllText(Path.Combine(dir, "listings.json"),
                "{\"IE00B53SZB19\":[{\"currency\":\"EUR\",\"provider\":\"YahooFinance\",\"provider_symbol\":\"CNDX.AS\"}]}");
            File.WriteAllText(Path.Combine(dir, "fx_rates.csv"), "date,currency,rate_to_eur\n2024-12-30,USD,0.9\n");

            var aliasCsvPath2 = Path.Combine(dir, "dividend_aliases.csv");
            File.WriteAllText(aliasCsvPath2, "alias,isin\n");
            var seeder = new ReferenceDataSeeder(db);
            var result = await seeder.SeedIfEmptyAsync(new ReferenceDataSources(
                Path.Combine(dir, "basiszins.csv"),
                Path.Combine(dir, "historical_prices.csv"),
                Path.Combine(dir, "instruments.json"),
                Path.Combine(dir, "listings.json"),
                Path.Combine(dir, "fx_rates.csv"),
                aliasCsvPath2));

            Assert.Equal(1, result.HistoricalPrices);
            Assert.Equal(1, result.InstrumentListings);
            var profile = db.InstrumentProfiles.Single();
            Assert.True(profile.SubjectToVorabpauschale);
            Assert.Equal("ETF_EQUITY", profile.Type);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

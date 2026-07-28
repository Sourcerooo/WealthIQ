using Microsoft.EntityFrameworkCore;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbInstrumentReferenceAdminTests
{
    private static WealthIqDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new WealthIqDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task SaveAndList_RoundTripsProfileAndListings()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        var dto = new InstrumentAdminDto(
            "IE00B3XXRP09", "Vanguard S&P 500", "ETF_EQUITY", 0.30m, true, null,
            [new InstrumentListingDto(CurrencyCode.GBP, "VUSA.L", "YahooFinance", "LSE", null)]);

        await admin.SaveAsync(dto);
        var list = await admin.ListAsync();

        Assert.Single(list);
        Assert.Equal("IE00B3XXRP09", list[0].Isin);
        Assert.True(list[0].SubjectToVorabpauschale);
        Assert.Equal("ETF_EQUITY", list[0].Type);
        Assert.Single(list[0].Listings);
        Assert.Equal("VUSA.L", list[0].Listings[0].ProviderSymbol);
    }

    [Fact]
    public async Task SaveAsync_Validation_ThrowsOnInvalidTfs()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        var dto = new InstrumentAdminDto("IE00B3XXRP09", "Test", "ETF_EQUITY", 1.5m, true, null, []);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.SaveAsync(dto));
    }

    [Fact]
    public async Task DeleteAsync_RemovesProfileAndListings()
    {
        using var db = NewDb();
        db.InstrumentProfiles.Add(new InstrumentProfileRow { Isin = "IE00B3XXRP09", Name = "Test", Type = "ETF_EQUITY", Teilfreistellungsquote = 0.30m, SubjectToVorabpauschale = true });
        db.InstrumentListings.Add(new InstrumentListingRow { Isin = "IE00B3XXRP09", Currency = "GBP", Provider = "Yahoo", ProviderSymbol = "VUSA.L" });
        db.SaveChanges();

        var admin = new DbInstrumentReferenceAdmin(db);
        await admin.DeleteAsync("IE00B3XXRP09");

        Assert.Empty(db.InstrumentProfiles);
        Assert.Empty(db.InstrumentListings);
    }

    [Fact]
    public async Task UploadAsync_MergeMode_UpsertsBothTables()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        var instrumentsJson = """{"IE00B3XXRP09":{"name":"Vanguard S&P 500","type":"ETF_EQUITY","tfs_quote":0.30,"subject_to_vorabpauschale":true}}""";
        var listingsJson = """{"IE00B3XXRP09":[{"currency":"GBP","provider":"YahooFinance","provider_symbol":"VUSA.L"}]}""";

        var result = await admin.UploadAsync(instrumentsJson, listingsJson, UploadMode.Merge);

        Assert.Equal(1, result.Profiles);
        Assert.Equal(1, result.Listings);
        Assert.Single(db.InstrumentProfiles);
        Assert.Single(db.InstrumentListings);
    }

    [Fact]
    public async Task SaveAsync_ThenListAsync_RoundTripsTheAssetClass()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        await admin.SaveAsync(new InstrumentAdminDto(
            "TESTISIN0001", "Probe", "ETF_EQUITY", 0.30m, true, TaxAssetClass.EquityFund, []));

        var listed = (await admin.ListAsync()).Single(x => x.Isin == "TESTISIN0001");

        Assert.Equal(TaxAssetClass.EquityFund, listed.AssetClass);
    }

    [Fact]
    public async Task SaveAsync_WithoutAssetClass_RoundTripsAsNull()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        await admin.SaveAsync(new InstrumentAdminDto(
            "TESTISIN0002", "Probe", "SOMETHING", 0m, false, null, []));

        var listed = (await admin.ListAsync()).Single(x => x.Isin == "TESTISIN0002");

        Assert.Null(listed.AssetClass);
    }

    [Fact]
    public async Task SaveAsync_UndefinedAssetClass_Throws()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        var dto = new InstrumentAdminDto(
            "TESTISIN0003", "Probe", "ETF_EQUITY", 0.30m, true, (TaxAssetClass)99, []);

        await Assert.ThrowsAsync<ArgumentException>(() => admin.SaveAsync(dto));
        Assert.Empty(db.InstrumentProfiles);
    }

    [Fact]
    public async Task UploadAsync_InvalidTaxAssetClass_SkipsTheRowWithAWarning()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        // "aktienfonds" is not a code — it must not be persisted, because every later read parses
        // the stored value, including the admin page that would have to repair it.
        var instrumentsJson = """
            {
              "IE00B3XXRP09":{"name":"Good","type":"ETF_EQUITY","tfs_quote":0.30,"tax_asset_class":"equity_fund"},
              "IE00BSKRJZ44":{"name":"Bad","type":"ETF_EQUITY","tfs_quote":0.30,"tax_asset_class":"aktienfonds"}
            }
            """;

        var result = await admin.UploadAsync(instrumentsJson, "{}", UploadMode.Merge);

        Assert.Equal(1, result.Profiles);
        Assert.Contains(result.Warnings, w => w.Contains("aktienfonds") && w.Contains("IE00BSKRJZ44"));
        Assert.Single(db.InstrumentProfiles);
        Assert.Equal("equity_fund", db.InstrumentProfiles.Single().TaxAssetClass);
    }

    [Fact]
    public async Task UploadAsync_MixedCaseTaxAssetClass_IsStoredCanonically()
    {
        using var db = NewDb();
        var admin = new DbInstrumentReferenceAdmin(db);

        var instrumentsJson =
            """{"IE00B3XXRP09":{"name":"Good","type":"ETF_EQUITY","tfs_quote":0.30,"tax_asset_class":"Equity_Fund"}}""";

        await admin.UploadAsync(instrumentsJson, "{}", UploadMode.Merge);

        Assert.Equal("equity_fund", db.InstrumentProfiles.Single().TaxAssetClass);
    }
}

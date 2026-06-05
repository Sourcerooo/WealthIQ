using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Tests.Infrastructure.Persistence;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.ReferenceData;

public sealed class DbInstrumentProfileEnricherTests
{
    private static InMemorySqlite SeededDb()
    {
        var db = new InMemorySqlite();
        using var ctx = db.NewContext();
        ctx.InstrumentProfiles.AddRange(
            new InstrumentProfileRow { Isin = "IE00B3XXRP09", Name = "Vanguard S&P 500", Teilfreistellungsquote = 0.30m },
            new InstrumentProfileRow { Isin = "IE00B4ND3602", Name = "iShares Physical Gold", Teilfreistellungsquote = 0m });
        ctx.SaveChanges();
        return db;
    }

    private static Instrument Raw(string isin, string symbol, string name = "raw", decimal tfs = 0m)
        => new(InstrumentId.NewId(), isin, symbol, name, tfs);

    [Fact]
    public void Enrich_KnownIsin_AppliesProfileNameAndTeilfreistellung_KeepsSymbol()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B3XXRP09", "VUSA"));

        Assert.Equal("Vanguard S&P 500", enriched.Name);
        Assert.Equal(0.30m, enriched.Teilfreistellungsquote);
        Assert.Equal("VUSA", enriched.Symbol);
    }

    [Fact]
    public void Enrich_KnownIsin_ZeroTeilfreistellung_IsRespected()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B4ND3602", "SGLN"));
        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }

    [Fact]
    public void Enrich_KnownIsin_EmptySymbol_UsesUnknownFallback()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("IE00B3XXRP09", ""));
        Assert.Equal("Unknown", enriched.Symbol);
    }

    [Fact]
    public void Enrich_UnknownIsin_WithIsin_ReturnsAsIs()
    {
        // No profile on file: enricher returns the instrument unchanged (spec §2, §4).
        // Stage B will turn "held over year-end with no profile" into a blocking error.
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("XX0000000000", "ABC", name: ""));
        Assert.Equal(0m, enriched.Teilfreistellungsquote);
        Assert.Equal("", enriched.Name);
    }

    [Fact]
    public void Enrich_NoIsin_DoesNotInventTeilfreistellung()
    {
        using var db = SeededDb();
        using var ctx = db.NewContext();
        var enriched = new DbInstrumentProfileEnricher(ctx).Enrich(Raw("", "EUR", name: ""));
        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }
}

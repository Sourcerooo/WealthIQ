using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Tax;
using Xunit;

namespace WealthIQ.Tests.Application.Tax;

public sealed class InstrumentCatalogBuilderTests
{
    /// <summary>Marks every instrument's Teilfreistellung at 30 % so we can prove the enricher ran.</summary>
    private sealed class StampingEnricher : IInstrumentProfileEnricher
    {
        public Instrument Enrich(Instrument instrument) => instrument with { Teilfreistellungsquote = 0.30m };
    }

    [Fact]
    public void Build_AppliesEnricherToEveryInstrument()
    {
        var builder = new InstrumentCatalogBuilder(new StampingEnricher());

        var result = builder.Build([
            new Instrument(InstrumentId.NewId(), "IE0001", "AAA", "raw", 0m)
        ]);

        Assert.Equal(0.30m, Assert.Single(result).Teilfreistellungsquote);
    }

    [Fact]
    public void Build_DeduplicatesByInstrumentId_LastWins()
    {
        var sharedId = InstrumentId.NewId();
        var builder = new InstrumentCatalogBuilder(new StampingEnricher());

        var result = builder.Build([
            new Instrument(sharedId, "IE0001", "AAA", "first", 0m),
            new Instrument(sharedId, "IE0001", "AAA", "second", 0m)
        ]);

        var single = Assert.Single(result);
        Assert.Equal("second", single.Name);
    }

    [Fact]
    public void Build_OrdersBySymbolThenIsin()
    {
        var builder = new InstrumentCatalogBuilder(new StampingEnricher());

        var result = builder.Build([
            new Instrument(InstrumentId.NewId(), "IE0002", "ZZZ", "z", 0m),
            new Instrument(InstrumentId.NewId(), "IE0001", "AAA", "a2", 0m),
            new Instrument(InstrumentId.NewId(), "IE0000", "AAA", "a1", 0m)
        ]);

        Assert.Equal(["AAA", "AAA", "ZZZ"], result.Select(x => x.Symbol));
        Assert.Equal(["IE0000", "IE0001"], result.Take(2).Select(x => x.ISIN)); // tie broken by ISIN
    }

    [Fact]
    public void Build_NullInput_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InstrumentCatalogBuilder(new StampingEnricher()).Build(null!));

    [Fact]
    public void Build_KnownIsin_EnrichesClassificationFields()
    {
        var dir = Path.GetTempPath();
        var jsonPath = Path.Combine(dir, $"inst_{Guid.NewGuid():N}.json");
        File.WriteAllText(jsonPath, """{"IE00B3XXRP09":{"name":"Vanguard S&P 500","type":"ETF_EQUITY","tfs_quote":0.30,"subject_to_vorabpauschale":true}}""");

        var enricher = new JsonInstrumentProfileEnricher(jsonPath);
        var builder = new InstrumentCatalogBuilder(enricher);

        var instrument = new Instrument(
            InstrumentId.NewId(),
            "IE00B3XXRP09", "VUSA", "Unknown", 0m);

        var result = builder.Build([instrument]);

        Assert.Single(result);
        Assert.Equal("ETF_EQUITY", result[0].Type);
        Assert.True(result[0].SubjectToVorabpauschale);
    }
}

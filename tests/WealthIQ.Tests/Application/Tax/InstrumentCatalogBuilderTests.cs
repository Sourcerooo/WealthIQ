using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;
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
}

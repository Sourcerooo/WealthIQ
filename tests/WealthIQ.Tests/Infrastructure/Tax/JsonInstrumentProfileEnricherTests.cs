using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Ibkr.Tax;
using Xunit;

namespace WealthIQ.Tests.Infrastructure.Tax;

public sealed class JsonInstrumentProfileEnricherTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "wealthiq-profiles-" + Guid.NewGuid().ToString("N"));

    private string Write(string content)
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "instruments.json");
        File.WriteAllText(path, content);
        return path;
    }

    private JsonInstrumentProfileEnricher Enricher() => new(Write(
        """
        {
          "IE00B3XXRP09": { "name": "Vanguard S&P 500", "tfs_quote": 0.30 },
          "IE00B4ND3602": { "name": "iShares Physical Gold", "tfs_quote": 0 }
        }
        """));

    [Fact]
    public void Enrich_KnownIsin_AppliesProfileNameAndTeilfreistellung()
    {
        var enriched = Enricher().Enrich(new Instrument(InstrumentId.NewId(), "IE00B3XXRP09", "VUSA", "raw", 0m));

        Assert.Equal("Vanguard S&P 500", enriched.Name);
        Assert.Equal(0.30m, enriched.Teilfreistellungsquote);
        Assert.Equal("VUSA", enriched.Symbol); // existing symbol preserved
    }

    [Fact]
    public void Enrich_KnownIsin_ZeroTeilfreistellung_IsRespected()
    {
        // A gold ETC legitimately has a 0 % Teilfreistellung — the enricher must not "fix" it to 30 %.
        var enriched = Enricher().Enrich(new Instrument(InstrumentId.NewId(), "IE00B4ND3602", "SGLN", "raw", 0m));

        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }

    [Fact]
    public void Enrich_KnownIsin_EmptySymbol_UsesFallback()
    {
        var enriched = Enricher().Enrich(new Instrument(InstrumentId.NewId(), "IE00B3XXRP09", "", "raw", 0m));

        Assert.Equal("Unknown", enriched.Symbol);
    }

    [Fact]
    public void Enrich_UnknownIsin_WithIsin_ReturnsAsIs()
    {
        // No profile on file: enricher returns the instrument unchanged (spec §2, §4).
        // Stage B will turn "held over year-end with no profile" into a blocking error.
        var enriched = Enricher().Enrich(new Instrument(InstrumentId.NewId(), "XX0000000000", "ABC", "", 0m));

        Assert.Equal(0m, enriched.Teilfreistellungsquote);
        Assert.Equal("", enriched.Name);
    }

    [Fact]
    public void Enrich_NoIsin_DoesNotInventTeilfreistellung()
    {
        // Without an ISIN (e.g. a cash position) the 30 % default must NOT be applied.
        var enriched = Enricher().Enrich(new Instrument(InstrumentId.NewId(), "", "EUR", "", 0m));

        Assert.Equal(0m, enriched.Teilfreistellungsquote);
    }

    [Fact]
    public void Constructor_InvalidTeilfreistellung_Throws()
    {
        Assert.Throws<ApplicationException>(() => new JsonInstrumentProfileEnricher(Write(
            """
            { "IE00B3XXRP09": { "name": "x", "tfs_quote": "not-a-number" } }
            """)));
    }

    [Fact]
    public void Constructor_FileNotFound_Throws()
        => Assert.Throws<FileNotFoundException>(() => new JsonInstrumentProfileEnricher(Path.Combine(_temp, "nope.json")));

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}

using System.Text;
using WealthIQ.Infrastructure.TradersPlace.Import;
using Xunit;

namespace WealthIQ.Tests.Infrastructure;

public sealed class TradersPlaceCsvTests
{
    [Fact]
    public void ReadLines_DecodesWindows1252Umlauts()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            // 0xFC = ü, 0xE4 = ä in Windows-1252/Latin1.
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes("St"));
            bytes.Add(0xFC); // ü
            bytes.AddRange(Encoding.ASCII.GetBytes("ck;W"));
            bytes.Add(0xE4); // ä
            bytes.AddRange(Encoding.ASCII.GetBytes("hrung"));
            File.WriteAllBytes(tmp, bytes.ToArray());

            var lines = TradersPlaceCsv.ReadLines(tmp);

            Assert.Single(lines);
            Assert.Equal("Stück;Währung", lines[0]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Theory]
    [InlineData("108,259000", 108.259000)]
    [InlineData("14,59", 14.59)]
    [InlineData("-30,78", -30.78)]
    [InlineData("90063,30", 90063.30)]
    public void ParseDecimal_ParsesGermanFormat(string input, double expected)
        => Assert.Equal((decimal)expected, TradersPlaceCsv.ParseDecimal(input));

    [Fact]
    public void ParseDate_ParsesGermanDate()
        => Assert.Equal(new DateOnly(2024, 6, 6), TradersPlaceCsv.ParseDate("06.06.2024"));
}

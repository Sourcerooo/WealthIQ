using System.Text.Json;
using System.Text.Json.Serialization;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Infrastructure.IBKR.MarketData;

public sealed class JsonInstrumentMarketDataMap : IInstrumentMarketDataMap
{
    private readonly Dictionary<string, InstrumentMarketDataProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public JsonInstrumentMarketDataMap(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Instrument market-data map file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var rawProfiles = JsonSerializer.Deserialize<Dictionary<string, InstrumentMarketDataProfileDto>>(json)
            ?? throw new ApplicationException("Instrument market-data map file could not be parsed.");

        foreach (var (isin, dto) in rawProfiles)
        {
            if (string.IsNullOrWhiteSpace(dto.ProviderSymbol))
            {
                throw new ApplicationException($"Missing provider symbol for instrument '{isin}'.");
            }

            _profiles[isin] = new InstrumentMarketDataProfile(dto.Provider, dto.ProviderSymbol, dto.Notes);
        }
    }

    public InstrumentMarketDataProfile GetProfile(Instrument instrument)
    {
        if (string.IsNullOrWhiteSpace(instrument.ISIN))
        {
            throw new InvalidOperationException($"Instrument '{instrument.Symbol}' has no ISIN and cannot be mapped to market data.");
        }

        if (_profiles.TryGetValue(instrument.ISIN, out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException($"No market-data mapping configured for instrument '{instrument.ISIN}'.");
    }

    private sealed class InstrumentMarketDataProfileDto
    {
        [JsonPropertyName("provider")]
        public string Provider { get; init; } = "YahooFinance";

        [JsonPropertyName("provider_symbol")]
        public string ProviderSymbol { get; init; } = string.Empty;

        [JsonPropertyName("notes")]
        public string? Notes { get; init; }
    }
}

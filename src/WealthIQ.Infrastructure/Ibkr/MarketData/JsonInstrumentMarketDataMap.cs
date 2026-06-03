using System.Text.Json;
using System.Text.Json.Serialization;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.MarketData;

/// <summary>File-backed listings map keyed by (ISIN, currency). Used by tests and as the seed source
/// for <c>InstrumentListings</c>. Production resolves via <c>DbInstrumentMarketDataMap</c>.</summary>
public sealed class JsonInstrumentMarketDataMap : IInstrumentMarketDataMap
{
    private readonly Dictionary<(string Isin, CurrencyCode Currency), InstrumentMarketDataProfile> _profiles = new();

    public JsonInstrumentMarketDataMap(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Instrument listings map file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<ListingDto>>>(json)
            ?? throw new ApplicationException("Instrument listings map file could not be parsed.");

        foreach (var (isin, listings) in raw)
        {
            foreach (var dto in listings)
            {
                if (string.IsNullOrWhiteSpace(dto.ProviderSymbol))
                {
                    throw new ApplicationException($"Missing provider symbol for instrument '{isin}' ({dto.Currency}).");
                }

                if (!Enum.TryParse<CurrencyCode>(dto.Currency, ignoreCase: true, out var currency))
                {
                    throw new ApplicationException($"Invalid currency '{dto.Currency}' for instrument '{isin}'.");
                }

                _profiles[(isin, currency)] = new InstrumentMarketDataProfile(dto.Provider, dto.ProviderSymbol, dto.Notes);
            }
        }
    }

    public InstrumentMarketDataProfile GetProfile(string isin, CurrencyCode currency)
    {
        if (string.IsNullOrWhiteSpace(isin))
        {
            throw new InvalidOperationException("Instrument has no ISIN and cannot be mapped to market data.");
        }

        if (_profiles.TryGetValue((isin, currency), out var profile))
        {
            return profile;
        }

        throw new InvalidOperationException($"No market-data listing configured for instrument '{isin}' in {currency}.");
    }

    private sealed class ListingDto
    {
        [JsonPropertyName("currency")] public string Currency { get; init; } = "";
        [JsonPropertyName("provider")] public string Provider { get; init; } = "YahooFinance";
        [JsonPropertyName("provider_symbol")] public string ProviderSymbol { get; init; } = "";
        [JsonPropertyName("notes")] public string? Notes { get; init; }
    }
}

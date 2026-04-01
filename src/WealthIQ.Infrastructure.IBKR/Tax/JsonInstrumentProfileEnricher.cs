using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Infrastructure.IBKR.Tax;

public sealed class JsonInstrumentProfileEnricher : IInstrumentProfileEnricher
{
    private readonly Dictionary<string, InstrumentProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public JsonInstrumentProfileEnricher(string filePath)
    {
        Load(filePath);
    }

    public Instrument Enrich(Instrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(instrument.ISIN)
            && _profiles.TryGetValue(instrument.ISIN, out var profile))
        {
            return instrument with
            {
                Name = profile.Name,
                Teilfreistellungsquote = profile.Teilfreistellungsquote,
                Symbol = string.IsNullOrWhiteSpace(instrument.Symbol) ? profile.SymbolFallback : instrument.Symbol
            };
        }

        return instrument with
        {
            Name = string.IsNullOrWhiteSpace(instrument.Name) ? "Auto-Generated" : instrument.Name,
            Teilfreistellungsquote = instrument.Teilfreistellungsquote == 0m && !string.IsNullOrWhiteSpace(instrument.ISIN) ? 0.30m : instrument.Teilfreistellungsquote
        };
    }

    private void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Instrument profile file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var rawProfiles = JsonSerializer.Deserialize<Dictionary<string, InstrumentProfileDto>>(json)
            ?? throw new ApplicationException("Instrument profile file could not be parsed.");

        foreach (var (isin, profile) in rawProfiles)
        {
            if (!decimal.TryParse(profile.TeilfreistellungsquoteRaw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var teilfreistellungsquote))
            {
                throw new ApplicationException($"Invalid teilfreistellungsquote for instrument '{isin}'.");
            }

            _profiles[isin] = new InstrumentProfile(profile.Name, teilfreistellungsquote);
        }
    }

    private sealed record InstrumentProfile(string Name, decimal Teilfreistellungsquote)
    {
        public string SymbolFallback => "Unknown";
    }

    private sealed class InstrumentProfileDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("tfs_quote")]
        public object? TeilfreistellungsquoteRaw { get; init; }
    }
}

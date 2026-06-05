using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Infrastructure.Ibkr.Tax;

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
                Type = profile.Type,
                Teilfreistellungsquote = profile.Teilfreistellungsquote,
                SubjectToVorabpauschale = profile.SubjectToVorabpauschale,
                Symbol = string.IsNullOrWhiteSpace(instrument.Symbol) ? profile.SymbolFallback : instrument.Symbol
            };
        }

        // No profile on file: return as-is. Stage B turns "held over year-end with no profile"
        // into a blocking error; here we no longer invent a 30% Teilfreistellung (spec §2, §4).
        return instrument;
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

            _profiles[isin] = new InstrumentProfile(profile.Name, profile.Type, teilfreistellungsquote, profile.SubjectToVorabpauschale);
        }
    }

    private sealed record InstrumentProfile(string Name, string Type, decimal Teilfreistellungsquote, bool SubjectToVorabpauschale)
    {
        public string SymbolFallback => "Unknown";
    }

    private sealed class InstrumentProfileDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("tfs_quote")]
        public object? TeilfreistellungsquoteRaw { get; init; }

        [JsonPropertyName("subject_to_vorabpauschale")]
        public bool SubjectToVorabpauschale { get; init; }
    }
}

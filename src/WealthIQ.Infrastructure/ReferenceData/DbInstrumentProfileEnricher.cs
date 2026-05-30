using WealthIQ.Application.Tax.Interface;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Infrastructure.ReferenceData;

/// <summary>
/// Enriches instruments from the seeded <c>InstrumentProfiles</c> table. Behaviour matches
/// <see cref="WealthIQ.Infrastructure.Ibkr.Tax.JsonInstrumentProfileEnricher"/>: known ISIN applies the
/// stored profile (symbol falls back to "Unknown" when empty); an unknown but ISIN-bearing fund defaults
/// to 30 % Teilfreistellung and an "Auto-Generated" name; an instrument without an ISIN is never defaulted.
/// </summary>
public sealed class DbInstrumentProfileEnricher : IInstrumentProfileEnricher
{
    private readonly Dictionary<string, (string Name, decimal Teilfreistellungsquote)> _profiles;

    public DbInstrumentProfileEnricher(WealthIqDbContext db)
    {
        _profiles = db.InstrumentProfiles.ToDictionary(
            x => x.Isin,
            x => (x.Name, x.Teilfreistellungsquote),
            StringComparer.OrdinalIgnoreCase);
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
                Symbol = string.IsNullOrWhiteSpace(instrument.Symbol) ? "Unknown" : instrument.Symbol
            };
        }

        return instrument with
        {
            Name = string.IsNullOrWhiteSpace(instrument.Name) ? "Auto-Generated" : instrument.Name,
            Teilfreistellungsquote = instrument.Teilfreistellungsquote == 0m && !string.IsNullOrWhiteSpace(instrument.ISIN)
                ? 0.30m
                : instrument.Teilfreistellungsquote
        };
    }
}

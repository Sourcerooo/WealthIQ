namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>Resolves the mangled dividend names in Trader's Place statements (which carry no ISIN)
/// to a canonical ISIN. Explicit, user-maintained mapping — no fuzzy matching (spec §6).</summary>
public interface IDividendAliasMap
{
    /// <summary>Returns the ISIN for an alias, or <c>null</c> if unmapped (caller must fail loud).</summary>
    string? ResolveIsin(string alias);
}

namespace WealthIQ.Web.Services;

/// <summary>Per-circuit memory of the user's chart selections so navigating away and back
/// (within the same Blazor Server session) restores the last-viewed instrument. Resets on full reload.</summary>
public sealed class ChartSelectionState
{
    /// <summary>Selected Kurschart provider symbol, or null if none chosen yet.</summary>
    public string? SelectedPriceSymbol { get; set; }
}

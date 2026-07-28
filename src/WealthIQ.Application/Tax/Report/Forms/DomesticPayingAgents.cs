namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>
/// The broker source systems that act as an <i>inländische Zahlstelle</i> — a domestic paying agent
/// that withholds German Kapitalertragsteuer at source and issues a Steuerbescheinigung.
/// </summary>
/// <remarks>
/// Membership means: this account's capital income has already been taxed at source and is declared
/// from the broker's Steuerbescheinigung on Anlage KAP Zeile 7. It must therefore <b>not</b> be
/// entered again on Anlage KAP-INV, which exists for income <i>without</i> domestic withholding —
/// doing so would declare the same income twice.
/// <para>
/// Add a broker here only when it is a German paying agent that withholds KESt itself. A foreign
/// broker such as IBKR does not, and stays off this list.
/// </para>
/// </remarks>
public static class DomesticPayingAgents
{
    /// <summary>Source systems as recorded on the account's ledger entries
    /// (<see cref="AccountTaxReport.SourceSystem"/>). Compared case-insensitively.</summary>
    public static IReadOnlySet<string> SourceSystems { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TradersPlace" };

    /// <summary>An unknown or empty source system is treated as foreign. That is the safe default:
    /// a foreign account declared on Anlage KAP-INV is the normal case, whereas wrongly claiming a
    /// Steuerbescheinigung exists would leave the income undeclared.</summary>
    public static bool IsDomestic(string? sourceSystem)
        => !string.IsNullOrWhiteSpace(sourceSystem) && SourceSystems.Contains(sourceSystem.Trim());
}

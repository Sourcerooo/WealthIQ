using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Application.Tax.Report.Forms;

/// <summary>
/// Translates one account-year into the lines of Anlage KAP / KAP-INV (spec §3). Pure: it only
/// regroups and relabels what <see cref="AnnualTaxReportService"/> already computed, and never
/// touches tax math.
/// </summary>
public static class TaxFormReportBuilder
{
    /// <summary>Bestandsgeschützte Alt-Anteile are units acquired before this date.</summary>
    private static readonly DateOnly AltAnteilCutoff = new(2009, 1, 1);

    /// <param name="sourceSystem">The account's importing broker (<see cref="AccountTaxReport.SourceSystem"/>).
    /// It — not the amount of KESt actually withheld — decides the route: a domestic paying agent
    /// certifies the income on a Steuerbescheinigung even in a year where the Sparer-Pauschbetrag or
    /// the broker's own Verlustverrechnung drove the withholding to zero.</param>
    public static TaxFormReport Build(AnnualTaxReport report, string sourceSystem)
    {
        ArgumentNullException.ThrowIfNull(report);

        // A German paying agent certifies the income on a Steuerbescheinigung, so it is declared on
        // Anlage KAP Zeile 7 — Anlage KAP-INV is for income WITHOUT domestic withholding only.
        var domestic = DomesticPayingAgents.IsDomestic(sourceSystem);

        var sections = new List<TaxFormSection>();

        if (domestic)
        {
            sections.Add(BuildDomestic(report));
        }
        else
        {
            sections.Add(BuildDistributions(report));
            sections.Add(BuildVorabpauschalen(report));
            sections.Add(BuildSales(report));
            sections.Add(BuildKap(report));
        }

        return new TaxFormReport(report.Year, domestic, sections);
    }

    private static TaxFormSection BuildDistributions(AnnualTaxReport report) =>
        new("KAP-INV",
            "Anlage KAP-INV: Erträge aus Investmentanteilen (Zeilen 4 bis 8)",
            "Alle Beträge vor Teilfreistellung — das Finanzamt kürzt selbst.",
            KapInvRows.All
                .Select(row => new TaxFormLine(
                    row.DistributionLine,
                    row.DistributionCaption,
                    SumRaw(report.Dividends, row.Class),
                    Nachweis: "B"))
                .ToList());

    private static TaxFormSection BuildVorabpauschalen(AnnualTaxReport report)
    {
        // Zeilen 9 to 13 cover the five fund classes and nothing else, so a Vorabpauschale booked on
        // a Share/OtherSecurity would reach no form line at all while still counting toward
        // Summary.VorabpauschaleTaxable — the form and the estimated tax on the same page would
        // silently disagree. Fail loudly instead (CLAUDE.md: fail-fast everywhere).
        foreach (var entry in report.Vorabpauschale)
        {
            var assetClass = ClassOf(entry);
            if (!assetClass.IsFund())
            {
                throw new InvalidOperationException(
                    $"Instrument '{entry.Isin}' is classified as '{assetClass}' but carries a "
                    + "Vorabpauschale, which under § 18 InvStG can only arise on an investment fund. "
                    + "Either the Assetklasse or the Vorabpauschale flag of this instrument is wrong. "
                    + "Correct it under Stammdaten → Instrumente.");
            }
        }

        return new TaxFormSection("KAP-INV",
            "Anlage KAP-INV: Vorabpauschalen (Zeilen 9 bis 13)",
            "Ermittlung je Fonds siehe Nachweis D (entspricht Zeilen 30 bis 45).",
            KapInvRows.All
                .Select(row => new TaxFormLine(
                    row.VorabLine,
                    row.VorabCaption,
                    SumRaw(report.Vorabpauschale, row.Class),
                    Nachweis: "D"))
                .ToList());
    }

    private static TaxFormSection BuildSales(AnnualTaxReport report)
    {
        var lines = new List<TaxFormLine>();

        foreach (var row in KapInvRows.All)
        {
            lines.Add(new TaxFormLine(
                row.SaleLine, row.SaleCaption, SumRaw(report.Sells, row.Class), Nachweis: "A"));

            // Gains on units bought before 2009 are only taxable above a 100.000 EUR allowance,
            // which WealthIQ does not model. Normally zero; when it is not, the line un-mutes so
            // the figure is visible instead of quietly wrong.
            var altAnteile = report.Sells
                .Where(e => ClassOf(e) == row.Class && e.OpenedOn < AltAnteilCutoff && e.RawAmount > 0m)
                .Sum(e => e.RawAmount);

            lines.Add(new TaxFormLine(
                row.AltLine, row.AltCaption, altAnteile, Nachweis: "A", Muted: altAnteile == 0m));

            // Deemed disposal as of 31.12.2017 is not modelled: every lot in the ledger was
            // acquired after that date.
            lines.Add(new TaxFormLine(row.FiktivLine, row.FiktivCaption, 0m, Muted: true));
        }

        lines.Add(new TaxFormLine(
            "29", "Zwischengewinne aus fiktiven Verkäufen zum 31.12.2017", 0m, Muted: true));

        return new TaxFormSection(
            "KAP-INV",
            "Anlage KAP-INV: Erträge aus dem Verkauf (Zeilen 14 bis 29)",
            "Ermittlung je Fonds siehe Nachweis A (entspricht Zeilen 46 bis 56). Die bereits "
                + "versteuerte Vorabpauschale ist nach § 19 InvStG bereits abgezogen.",
            lines);
    }

    private static decimal SumRaw(IReadOnlyList<GermanTaxEntry> entries, TaxAssetClass assetClass)
        => entries.Where(e => ClassOf(e) == assetClass).Sum(e => e.RawAmount);

    /// <summary>An entry whose instrument was never classified cannot be placed on a form line.
    /// Fail loudly rather than dropping it into a default bucket (CLAUDE.md: fail-fast everywhere).</summary>
    private static TaxAssetClass ClassOf(GermanTaxEntry entry)
        => entry.AssetClass ?? throw new InvalidOperationException(
            $"Instrument '{entry.Isin}' has no tax asset class, so its {entry.Type} entry cannot be "
            + "mapped to a form line. Set the Assetklasse under Stammdaten → Instrumente.");

    private static TaxFormSection BuildKap(AnnualTaxReport report)
    {
        // Zeile 19 is a NET sum of everything that is not an investment fund; funds are declared on
        // KAP-INV instead and must not be counted here as well.
        var nonFundSells = report.Sells.Where(e => !ClassOf(e).IsFund()).ToList();
        var nonFundDividends = report.Dividends.Where(e => !ClassOf(e).IsFund()).ToList();

        var total = report.Interest.Sum(e => e.RawAmount)
            + nonFundSells.Sum(e => e.RawAmount)
            + nonFundDividends.Sum(e => e.RawAmount);

        var shareGains = nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.RawAmount > 0m)
            .Sum(e => e.RawAmount);

        // Zeilen 22 and 23 restate the losses ALREADY contained in Zeile 19, as positive figures.
        var shareLosses = -nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.RawAmount < 0m)
            .Sum(e => e.RawAmount);

        var otherLosses = -nonFundSells
            .Where(e => ClassOf(e) == TaxAssetClass.OtherSecurity && e.RawAmount < 0m)
            .Sum(e => e.RawAmount);

        // Safety net: this account is not treated as a domestic paying agent, yet German KESt was
        // withheld on it. That is a genuine inconsistency — either the broker is a Zahlstelle after
        // all (and the income belongs on Zeile 7) or the withheld amount is misattributed.
        var note = "Ohne Investmenterträge — die stehen in der Anlage KAP-INV.";
        if (report.Summary.WithheldKESt > 0m)
        {
            note += " Achtung: Auf diesem Konto wurde deutsche Kapitalertragsteuer einbehalten, "
                + "obwohl es nicht als inländische Zahlstelle geführt wird. Bitte die "
                + "Steuerbescheinigung des Kontos prüfen — die Erträge gehören dann in die "
                + "Anlage KAP Zeile 7 statt in die Anlage KAP-INV.";
        }

        return new TaxFormSection(
            "KAP",
            "Anlage KAP: Kapitalerträge ohne inländischen Steuerabzug",
            note,
            [
                new TaxFormLine("19", "Ausländische Kapitalerträge", total, Nachweis: "A · B · C"),
                new TaxFormLine("20", "Darin enthaltene Gewinne aus Aktienveräußerungen", shareGains, Nachweis: "A",
                    Muted: shareGains == 0m),
                new TaxFormLine("22", "Darin enthaltene Verluste ohne Aktienveräußerungen", otherLosses, Nachweis: "A",
                    Muted: otherLosses == 0m),
                new TaxFormLine("23", "Darin enthaltene Verluste aus Aktienveräußerungen", shareLosses, Nachweis: "A",
                    Muted: shareLosses == 0m),
                new TaxFormLine("37", "Einbehaltene deutsche Kapitalertragsteuer", report.Summary.WithheldKESt,
                    Muted: report.Summary.WithheldKESt == 0m),
                new TaxFormLine("41", "Anrechenbare, noch nicht angerechnete ausländische Steuern",
                    report.WithholdingTaxes.Sum(e => e.ForeignWithholdingTax), Nachweis: "E"),
                new TaxFormLine("42", "Fiktive ausländische Steuern", 0m, Muted: true)
            ]);
    }

    private static TaxFormSection BuildDomestic(AnnualTaxReport report)
    {
        // Certified figures are already net of Teilfreistellung, so this route uses TaxableAmount.
        var certified = report.Sells.Sum(e => e.TaxableAmount)
            + report.Dividends.Sum(e => e.TaxableAmount)
            + report.Interest.Sum(e => e.TaxableAmount)
            + report.Vorabpauschale.Sum(e => e.TaxableAmount);

        var shareGains = report.Sells
            .Where(e => ClassOf(e) == TaxAssetClass.Share && e.TaxableAmount > 0m)
            .Sum(e => e.TaxableAmount);

        return new TaxFormSection(
            "KAP",
            "Anlage KAP: Kapitalerträge mit inländischem Steuerabzug",
            "Maßgeblich ist die Steuerbescheinigung des Brokers. Die folgenden Zahlen dienen der "
                + "Kontrolle. Solidaritätszuschlag und Kirchensteuer erfasst WealthIQ nicht.",
            [
                new TaxFormLine("7", "Kapitalerträge, die dem inländischen Steuerabzug unterlegen haben",
                    certified, Nachweis: "A · B · C · D"),
                new TaxFormLine("8", "Darin enthaltene Gewinne aus Aktienveräußerungen", shareGains, Nachweis: "A",
                    Muted: shareGains == 0m),
                new TaxFormLine("37", "Kapitalertragsteuer", report.Summary.WithheldKESt),
                new TaxFormLine("38", "Solidaritätszuschlag", 0m, Muted: true),
                new TaxFormLine("39", "Kirchensteuer zur Kapitalertragsteuer", 0m, Muted: true),
                new TaxFormLine("41", "Anrechenbare, noch nicht angerechnete ausländische Steuern",
                    report.WithholdingTaxes.Sum(e => e.ForeignWithholdingTax), Nachweis: "E")
            ]);
    }
}

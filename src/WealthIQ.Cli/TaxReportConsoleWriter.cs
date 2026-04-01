using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;

namespace WealthIQ.Cli;

internal static class TaxReportConsoleWriter
{
    public static void PrintDiagnostics(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        var ignoredAssets = diagnostics
            .Where(x => x.Code == ImportDiagnosticCode.IgnoredAsset)
            .GroupBy(x => x.Message)
            .ToList();

        if (ignoredAssets.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("--- BERICHT: IGNORIERTE ASSETS ---");
        foreach (var ignoredAsset in ignoredAssets)
        {
            Console.WriteLine($"{ignoredAsset.Key} ({ignoredAsset.Count()})");
        }
    }

    public static void PrintReport(IReadOnlyList<GermanTaxEntry> entries, IReadOnlyList<Instrument> instruments, int year)
    {
        var yearlyEntries = entries.Where(x => x.Year == year).OrderBy(x => x.Date).ToList();
        if (yearlyEntries.Count == 0)
        {
            return;
        }

        var instrumentByIsin = instruments
            .Where(x => !string.IsNullOrWhiteSpace(x.ISIN))
            .GroupBy(x => x.ISIN, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        Console.WriteLine(new string('#', 80));
        Console.WriteLine($"STEUERREPORT {year} (Simulation Anlage KAP)");
        Console.WriteLine(new string('#', 80));

        var sumSellTaxable = PrintSellSection(yearlyEntries, instrumentByIsin);
        var sumVorabTaxable = PrintVorabSection(yearlyEntries, instrumentByIsin);
        var sumDividendTaxable = PrintDividendSection(yearlyEntries, instrumentByIsin);
        var sumInterestTaxable = PrintInterestSection(yearlyEntries);
        var sumWithholdingTax = PrintWithholdingTaxSection(yearlyEntries);

        var totalIncome = sumSellTaxable + sumVorabTaxable + sumDividendTaxable + sumInterestTaxable;
        var estimatedTax = totalIncome * 0.25m * 1.055m;
        var estimatedFinalTax = estimatedTax - sumWithholdingTax;

        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"GESAMTERGEBNIS {year}");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Summe der steuerpflichtigen Kapitalertraege: {totalIncome,20:F2} EUR");
        Console.WriteLine($"  davon Aktienveraeusserungen (Verlusttopf):  {sumSellTaxable,20:F2} EUR");
        Console.WriteLine($"  davon Sonstiges (Vorab/Div/Zins):         {sumVorabTaxable + sumDividendTaxable + sumInterestTaxable,20:F2} EUR");
        Console.WriteLine(new string('-', 65));
        Console.WriteLine($"Geschaetzte Steuerlast (25% + Soli):         {estimatedTax,20:F2} EUR");
        Console.WriteLine($"Abzueglich anrechenbare Quellensteuer:       {-sumWithholdingTax,20:F2} EUR");
        Console.WriteLine(new string('=', 65));
        Console.WriteLine($"NACHZAHLUNG / ERSTATTUNG (ca.):             {estimatedFinalTax,20:F2} EUR");
        Console.WriteLine(new string('=', 80));
    }

    private static decimal PrintSellSection(IReadOnlyList<GermanTaxEntry> entries, IReadOnlyDictionary<string, Instrument> instrumentByIsin)
    {
        Console.WriteLine();
        Console.WriteLine("1. VERAEUSSERUNGSGEWINNE (Inkl. VAP-Korrektur)");
        Console.WriteLine($"{"Datum",-12} | {"Symbol",-7} | {"TFS",-7} | {"Roh-Gewinn",12} | {"VAP Abzug",10} | {"Steuerpfl.",12}");
        Console.WriteLine(new string('-', 80));

        decimal sum = 0m;
        foreach (var entry in entries.Where(x => x.Type == GermanTaxEntryType.Sell))
        {
            var tfsQuote = instrumentByIsin.TryGetValue(entry.Isin, out var instrument) ? instrument.Teilfreistellungsquote * 100m : 0m;
            Console.WriteLine($"{entry.Date,-12:yyyy-MM-dd} | {entry.Symbol,-7} | {tfsQuote,6:F0}% | {entry.RawAmount,12:F2} | {entry.UsedVorabpauschale,10:F2} | {entry.TaxableAmount,13:F2}");
            sum += entry.TaxableAmount;
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"SUMME Veraeusserungen:{sum,56:F2} EUR");
        return sum;
    }

    private static decimal PrintVorabSection(IReadOnlyList<GermanTaxEntry> entries, IReadOnlyDictionary<string, Instrument> instrumentByIsin)
    {
        Console.WriteLine();
        Console.WriteLine("2. VORABPAUSCHALEN (VAP) - Fiktiver Zufluss");
        Console.WriteLine($"{"Datum",-12} | {"Symbol",-8} | {"TFS",-5} | {"VAP Roh",12} | {"Steuerpfl.",29}");
        Console.WriteLine(new string('-', 80));

        decimal sum = 0m;
        foreach (var entry in entries.Where(x => x.Type == GermanTaxEntryType.Vorabpauschale))
        {
            var tfsQuote = instrumentByIsin.TryGetValue(entry.Isin, out var instrument) ? instrument.Teilfreistellungsquote * 100m : 0m;
            Console.WriteLine($"{entry.Date,-12:yyyy-MM-dd} | {entry.Symbol,-8} | {tfsQuote,4:F0}% | {entry.RawAmount,12:F2} | {entry.TaxableAmount,27:F2}");
            sum += entry.TaxableAmount;
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"SUMME Vorabpauschalen:{sum,54:F2} EUR");
        return sum;
    }

    private static decimal PrintDividendSection(IReadOnlyList<GermanTaxEntry> entries, IReadOnlyDictionary<string, Instrument> instrumentByIsin)
    {
        Console.WriteLine();
        Console.WriteLine("3. DIVIDENDEN (Laufende Ertraege)");
        Console.WriteLine($"{"Datum",-12} | {"Symbol",-8} | {"TFS",-5} | {"Brutto",12} | {"Steuerpfl.",29}");
        Console.WriteLine(new string('-', 80));

        decimal sum = 0m;
        foreach (var entry in entries.Where(x => x.Type == GermanTaxEntryType.Dividend))
        {
            var tfsQuote = instrumentByIsin.TryGetValue(entry.Isin, out var instrument) ? instrument.Teilfreistellungsquote * 100m : 0m;
            Console.WriteLine($"{entry.Date,-12:yyyy-MM-dd} | {entry.Symbol,-8} | {tfsQuote,4:F0}% | {entry.RawAmount,12:F2} | {entry.TaxableAmount,27:F2}");
            sum += entry.TaxableAmount;
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"SUMME Dividenden:{sum,59:F2} EUR");
        return sum;
    }

    private static decimal PrintInterestSection(IReadOnlyList<GermanTaxEntry> entries)
    {
        Console.WriteLine();
        Console.WriteLine("4. ZINSEN (Fremdwaehrung & Cash)");
        Console.WriteLine($"{"Datum",-12} | {"Waehrung",-8} | {"Brutto EUR",53}");
        Console.WriteLine(new string('-', 80));

        decimal sum = 0m;
        foreach (var entry in entries.Where(x => x.Type == GermanTaxEntryType.Interest))
        {
            Console.WriteLine($"{entry.Date,-12:yyyy-MM-dd} | {entry.Symbol,-8} | {entry.RawAmount,50:F2}");
            sum += entry.TaxableAmount;
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"SUMME Zinsen:{sum,63:F2} EUR");
        return sum;
    }

    private static decimal PrintWithholdingTaxSection(IReadOnlyList<GermanTaxEntry> entries)
    {
        Console.WriteLine();
        Console.WriteLine("5. GEZAHLTE QUELLENSTEUER (Anrechenbar)");
        Console.WriteLine(new string('-', 80));

        decimal sum = 0m;
        foreach (var entry in entries.Where(x => x.Type == GermanTaxEntryType.WithholdingTax || x.ForeignWithholdingTax > 0m))
        {
            Console.WriteLine($"{entry.Date,-12:yyyy-MM-dd} | {entry.Symbol,-8} | {entry.ForeignWithholdingTax,50:F2}");
            sum += entry.ForeignWithholdingTax;
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"SUMME Anrechenbare QSt:{sum,53:F2} EUR");
        return sum;
    }
}

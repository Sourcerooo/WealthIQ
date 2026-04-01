using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Tax;
using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.IBKR.Import;
using WealthIQ.Infrastructure.IBKR.Tax;

namespace WealthIQ.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var inputPath = ResolveInputPath(args);
            var configurationPath = Path.Combine(inputPath, "Configuration");

            Console.WriteLine("--- WealthIQ Tax Report ---");
            Console.WriteLine($"Input: {inputPath}");

            var importer = new IbkrStatementImporter();
            var importResult = await importer.ImportAsync(new ImportRequest
            {
                AccountId = (AccountId)Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Source = new ImportSource(Broker.InteractiveBrokers, Format.XML, inputPath)
            }, CancellationToken.None);

            foreach (var diagnostic in importResult.Diagnostics.Where(x => x.Severity >= ImportDiagnosticSeverity.Warning))
            {
                Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
            }

            if (importResult.Diagnostics.Any(x => x.Severity == ImportDiagnosticSeverity.Fatal))
            {
                return 1;
            }

            var instrumentCatalog = new InstrumentCatalogBuilder(
                new JsonInstrumentProfileEnricher(Path.Combine(configurationPath, "instruments.json")))
                .Build(importResult.Instruments);

            var taxCalculator = new GermanTaxCalculator(
                new CsvBasisInterestRateProvider(Path.Combine(configurationPath, "basiszins.csv")),
                new CsvYearEndPriceProvider(Path.Combine(configurationPath, "prices.csv")));

            var calculationResult = taxCalculator.Calculate(importResult.AccountEvents, instrumentCatalog);
            TaxReportConsoleWriter.PrintDiagnostics(importResult.Diagnostics);

            foreach (var year in calculationResult.Entries.Select(x => x.Year).Distinct().OrderBy(x => x))
            {
                TaxReportConsoleWriter.PrintReport(calculationResult.Entries, instrumentCatalog, year);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static string ResolveInputPath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        return Path.Combine(AppContext.BaseDirectory, "Input");
    }
}

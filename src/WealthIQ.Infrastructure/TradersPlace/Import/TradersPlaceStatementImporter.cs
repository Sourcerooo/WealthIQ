using System.Security.Cryptography;
using System.Text;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.TradersPlace.Import;

/// <summary>
/// Imports Trader's Place CSV exports (spec 2026-06-06). Ingests BOTH the Depotumsätze (trades) and
/// Kontoumsätze (cash) files in one pass, classifying each by header signature and routing by
/// transaction type so trade rows that appear in both files are never double-counted. All entries are
/// produced under the single requested account.
/// </summary>
public sealed class TradersPlaceStatementImporter(IDividendAliasMap dividendAliasMap) : IStatementImporter
{
    public bool CanImport(ImportSource source)
        => source is not null
           && source.Broker == Broker.TradersPlace
           && source.Format == Format.CSV;

    public Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ImportResult();

        if (!CanImport(request.Source))
        {
            result.Diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.UnsupportedSource,
                $"Unsupported import source '{request.Source.Broker}/{request.Source.Format}'."));
            return Task.FromResult(result);
        }

        var files = ResolveFiles(request.Source.FilePath);
        if (files.Count == 0)
        {
            result.Diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.InputPathNotFound,
                $"No CSV files found at '{request.Source.FilePath}'."));
            return Task.FromResult(result);
        }

        var instrumentCatalog = new Dictionary<InstrumentId, Instrument>();
        var entries = new List<PortfolioEntry>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var lines = TradersPlaceCsv.ReadLines(file);
            if (lines.Count == 0)
            {
                continue;
            }

            var header = lines[0];
            if (header.StartsWith("Handelsdatum;", StringComparison.Ordinal))
            {
                ParseDepotumsaetze(lines, file, request.AccountId, instrumentCatalog, entries, result.Diagnostics);
            }
            else if (header.StartsWith("Kontonummer;", StringComparison.Ordinal))
            {
                ParseKontoumsaetze(lines, file, request.AccountId, instrumentCatalog, entries, result.Diagnostics);
            }
            else
            {
                result.Diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Fatal, ImportDiagnosticCode.UnsupportedSource,
                    $"Unrecognized Trader's Place CSV header in '{Path.GetFileName(file)}'.",
                    SourceReference: file));
            }
        }

        result.Instruments = instrumentCatalog.Values.OrderBy(x => x.Symbol).ThenBy(x => x.ISIN).ToList();
        result.PortfolioLedger = new PortfolioLedger(
            entries.OrderBy(x => x.OccurredAt).ToList(), result.Instruments);
        return Task.FromResult(result);
    }

    private static List<string> ResolveFiles(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return Directory.GetFiles(inputPath, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return File.Exists(inputPath) ? new List<string> { inputPath } : new List<string>();
    }

    private void ParseDepotumsaetze(
        IReadOnlyList<string> lines, string file, AccountId accountId,
        Dictionary<InstrumentId, Instrument> catalog, List<PortfolioEntry> entries, List<ImportDiagnostic> diagnostics)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Depotumsätze;", StringComparison.Ordinal))
            {
                continue;
            }

            var c = TradersPlaceCsv.SplitRow(line);
            if (c.Length < 17)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped malformed trade row {i + 1}.", file));
                continue;
            }

            var transaktion = c[2].Trim();
            var side = transaktion switch
            {
                "Kauf" => TradeSide.Buy,
                "Verkauf" => TradeSide.Sell,
                _ => (TradeSide?)null
            };
            if (side is null)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped unknown trade transaction '{transaktion}' (row {i + 1}).", file));
                continue;
            }

            var isin = c[5].Trim();
            var name = c[6].Trim();
            if (isin.Length == 0)
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has no ISIN.", file));
                continue;
            }

            if (!TradersPlaceCsv.TryParseDate(c[0], out var handelsdatum)
                || !TradersPlaceCsv.TryParseDecimal(c[7], out var quantity)
                || !TradersPlaceCsv.TryParseDecimal(c[8], out var price))
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has an unparseable date/quantity/price.", file));
                continue;
            }

            if (quantity <= 0m || price <= 0m)
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Trade row {i + 1} has non-positive quantity or price.", file));
                continue;
            }

            var tradeCurrency = ParseCurrency(c[9].Trim(), diagnostics, file, i + 1);
            var paymentCurrency = ParseCurrency(c[10].Trim(), diagnostics, file, i + 1);
            if (tradeCurrency is null || paymentCurrency is null)
            {
                continue;
            }

            TradersPlaceCsv.TryParseDecimal(c[12], out var ownFees);
            TradersPlaceCsv.TryParseDecimal(c[13], out var foreignFees);
            TradersPlaceCsv.TryParseDecimal(c[15], out var kest);

            var instrument = EnsureInstrument(catalog, isin, name);
            var occurredAt = new DateTimeOffset(handelsdatum.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));

            var reference = StableTradeReference(handelsdatum, isin, transaktion, c[7].Trim(), c[8].Trim(), c[16].Trim(), i);

            entries.Add(new TradeEntry(
                PortfolioEntryId.NewId(), accountId, occurredAt, handelsdatum,
                new SourceProvenance
                {
                    SourceSystem = "TradersPlace",
                    ImportFormat = "CSV",
                    SourceLocation = file,
                    SourceRecordReference = reference,
                    SourceSection = "Depotumsätze"
                },
                instrument.InstrumentId, side.Value, new Quantity(quantity),
                new Money(price, tradeCurrency.Value),
                new Money(Math.Abs(ownFees) + Math.Abs(foreignFees), paymentCurrency.Value),
                new Money(0m, paymentCurrency.Value),
                new Money(Math.Abs(kest), paymentCurrency.Value)));
        }
    }

    private void ParseKontoumsaetze(
        IReadOnlyList<string> lines, string file, AccountId accountId,
        Dictionary<InstrumentId, Instrument> catalog, List<PortfolioEntry> entries, List<ImportDiagnostic> diagnostics)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Kontoumsätze;", StringComparison.Ordinal))
            {
                continue;
            }

            var c = TradersPlaceCsv.SplitRow(line);
            if (c.Length < 9)
            {
                diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped malformed cash row {i + 1}.", file));
                continue;
            }

            var transaktion = c[4].Trim();
            var reference = c[8].Trim();

            if (transaktion is "Gutschrift" or "Überweisung" or "Einzahlung" or "Kauf" or "Verkauf")
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Info, ImportDiagnosticCode.IgnoredAsset,
                    $"Ignored '{transaktion}' (not a taxable event in this import).", SourceReference: reference));
                continue;
            }

            if (!TradersPlaceCsv.TryParseDate(c[2], out var buchungsdatum)
                || !TradersPlaceCsv.TryParseDecimal(c[6], out var amount))
            {
                diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Cash row {i + 1} has an unparseable date/amount.", file));
                continue;
            }

            var currency = ParseCurrency(c[5].Trim(), diagnostics, file, i + 1);
            if (currency is null)
            {
                continue;
            }

            var occurredAt = new DateTimeOffset(buchungsdatum.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
            var text = c[7].Trim();

            var provenance = new SourceProvenance
            {
                SourceSystem = "TradersPlace",
                ImportFormat = "CSV",
                SourceLocation = file,
                SourceRecordReference = reference,
                SourceSection = "Kontoumsätze"
            };

            if (transaktion == "Effekten")
            {
                var isin = dividendAliasMap.ResolveIsin(text);
                if (isin is null)
                {
                    diagnostics.Add(Error(ImportDiagnosticCode.MalformedField,
                        $"Dividend alias '{text}' (row {i + 1}) is not mapped to an ISIN. Add it under Stammdaten.", file));
                    continue;
                }

                var cashInstrument = EnsureCashInstrument(catalog, currency.Value);
                var related = EnsureInstrument(catalog, isin, text);
                entries.Add(new CashEntry(
                    PortfolioEntryId.NewId(), accountId, occurredAt, buchungsdatum, provenance,
                    cashInstrument.InstrumentId, WealthIQ.Domain.Enumeration.CashFlowType.Dividend,
                    new Money(amount, currency.Value), new Money(0m, currency.Value), new Money(0m, currency.Value),
                    related.InstrumentId));
                continue;
            }

            if (transaktion == "Kontoabschluss")
            {
                if (amount <= 0m)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Info, ImportDiagnosticCode.IgnoredAsset,
                        $"Ignored non-positive Kontoabschluss (debit interest/fee) row {i + 1}.", SourceReference: reference));
                    continue;
                }

                var cashInstrument = EnsureCashInstrument(catalog, currency.Value);
                entries.Add(new CashEntry(
                    PortfolioEntryId.NewId(), accountId, occurredAt, buchungsdatum, provenance,
                    cashInstrument.InstrumentId, WealthIQ.Domain.Enumeration.CashFlowType.Interest,
                    new Money(amount, currency.Value), new Money(0m, currency.Value), new Money(0m, currency.Value)));
                continue;
            }

            diagnostics.Add(Warn(ImportDiagnosticCode.InvalidRecord, $"Skipped unknown cash transaction '{transaktion}' (row {i + 1}).", file));
        }
    }

    private static Instrument EnsureInstrument(Dictionary<InstrumentId, Instrument> catalog, string isin, string name)
    {
        var id = StableInstrumentId(isin);
        if (!catalog.ContainsKey(id))
        {
            catalog[id] = new Instrument(id, isin, isin, string.IsNullOrWhiteSpace(name) ? isin : name, 0m);
        }

        return catalog[id];
    }

    private static Instrument EnsureCashInstrument(Dictionary<InstrumentId, Instrument> catalog, CurrencyCode currency)
    {
        var symbol = currency.ToString();
        var id = StableInstrumentId($"CASH:{symbol}");
        if (!catalog.ContainsKey(id))
        {
            catalog[id] = new Instrument(id, string.Empty, symbol, $"{symbol} cash", 0m);
        }

        return catalog[id];
    }

    private static CurrencyCode? ParseCurrency(string currency, List<ImportDiagnostic> diagnostics, string file, int row)
    {
        if (Enum.TryParse<CurrencyCode>(currency, true, out var parsed))
        {
            return parsed;
        }

        diagnostics.Add(Error(ImportDiagnosticCode.MalformedField, $"Unsupported currency '{currency}' (row {row}).", file));
        return null;
    }

    private static string StableTradeReference(DateOnly date, string isin, string transaktion, string qty, string price, string endbetrag, int rowIndex)
    {
        var key = $"{date:yyyy-MM-dd}|{isin}|{transaktion}|{qty}|{price}|{endbetrag}|{rowIndex}";
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return $"TP-DEPOT-{new Guid(bytes):N}";
    }

    private static InstrumentId StableInstrumentId(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return (InstrumentId)new Guid(bytes);
    }

    private static ImportDiagnostic Warn(ImportDiagnosticCode code, string message, string file)
        => new(ImportDiagnosticSeverity.Warning, code, message, SourceReference: file);

    private static ImportDiagnostic Error(ImportDiagnosticCode code, string message, string file)
        => new(ImportDiagnosticSeverity.Error, code, message, SourceReference: file);
}

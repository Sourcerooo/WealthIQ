using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Infrastructure.IBKR.Import;

public sealed class IbkrStatementImporter : IStatementImporter
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public bool CanImport(ImportSource source)
        => source is not null
           && source.Broker == Broker.InteractiveBrokers
           && source.Format == Format.XML;

    public async Task<ImportResult> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanImport(request.Source))
        {
            return new ImportResult
            {
                Diagnostics =
                {
                    new ImportDiagnostic(
                        ImportDiagnosticSeverity.Fatal,
                        ImportDiagnosticCode.UnsupportedSource,
                        $"Unsupported import source '{request.Source.Broker}/{request.Source.Format}'.")
                }
            };
        }

        var result = new ImportResult();
        var instrumentCatalog = new Dictionary<InstrumentId, Instrument>();
        var files = ResolveFiles(request.Source.FilePath);
        if (files.Count == 0)
        {
            result.Diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Fatal,
                ImportDiagnosticCode.InputPathNotFound,
                $"No XML files found at '{request.Source.FilePath}'."));
            return result;
        }

        var importedEvents = new List<AccountEvent>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var xml = await File.ReadAllTextAsync(file, ct);
                var document = XDocument.Parse(xml);
                importedEvents.AddRange(ParseDocument(document, file, request.AccountId, instrumentCatalog, result.Diagnostics));
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    ImportDiagnosticCode.FileReadFailed,
                    $"Failed to read '{Path.GetFileName(file)}': {ex.Message}",
                    SourceReference: file));
            }
        }

        result.Instruments = instrumentCatalog.Values.OrderBy(x => x.Symbol).ThenBy(x => x.ISIN).ToList();
        var orderedEvents = importedEvents.OrderBy(x => x.OccurredAt).ToList();
        result.AccountEvents = CleanCancellations(orderedEvents, result.Diagnostics);
        return result;
    }

    private static List<string> ResolveFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return [inputPath];
        }

        if (Directory.Exists(inputPath))
        {
            return Directory.GetFiles(inputPath, "*.xml", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static List<AccountEvent> ParseDocument(
        XDocument document,
        string filePath,
        AccountId accountId,
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        List<ImportDiagnostic> diagnostics)
    {
        var result = new List<AccountEvent>();

        foreach (var trade in document.Descendants("Trade"))
        {
            if (ParseElement(trade, false, filePath, accountId, instrumentCatalog, diagnostics) is { } accountEvent)
            {
                result.Add(accountEvent);
            }
        }

        foreach (var cashTransaction in document.Descendants("CashTransaction"))
        {
            if (ParseElement(cashTransaction, true, filePath, accountId, instrumentCatalog, diagnostics) is { } accountEvent)
            {
                result.Add(accountEvent);
            }
        }

        return result;
    }

    private static AccountEvent? ParseElement(
        XElement element,
        bool isCash,
        string filePath,
        AccountId accountId,
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        List<ImportDiagnostic> diagnostics)
    {
        var transactionId = element.Attribute("transactionID")?.Value;
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Warning,
                ImportDiagnosticCode.InvalidRecord,
                "Skipped record without transactionID.",
                SourceReference: filePath));
            return null;
        }

        var type = element.Attribute("type")?.Value ?? string.Empty;
        var assetCategory = element.Attribute("assetCategory")?.Value;
        var assetClass = element.Attribute("assetClass")?.Value;
        var finalAssetClass = !string.IsNullOrWhiteSpace(assetCategory) ? assetCategory : assetClass ?? "UNKNOWN";
        var symbol = element.Attribute("symbol")?.Value ?? string.Empty;
        var isin = element.Attribute("isin")?.Value ?? string.Empty;
        var currency = element.Attribute("currency")?.Value ?? "EUR";
        var description = element.Attribute("description")?.Value ?? string.Empty;

        if (!ShouldImport(isCash, type, finalAssetClass, symbol, filePath, diagnostics))
        {
            return null;
        }

        var instrument = EnsureInstrument(instrumentCatalog, isCash, symbol, isin, currency, description);
        var occurredAt = ParseDateTimeOffset(element.Attribute("dateTime")?.Value
            ?? element.Attribute("tradeDate")?.Value
            ?? element.Attribute("reportDate")?.Value);

        var quantity = ParseDecimal(element.Attribute("quantity")?.Value);
        var price = ParseDecimal(element.Attribute("tradePrice")?.Value ?? element.Attribute("amount")?.Value);
        var fxRate = ParseDecimal(element.Attribute("fxRateToBase")?.Value ?? "1.0");
        var commission = ParseDecimal(element.Attribute("ibCommission")?.Value);
        var fees = new Money(Math.Abs(commission) * fxRate, Currency.EUR);

        if (isCash)
        {
            var grossAmount = new Money(price * fxRate, Currency.EUR);
            if (type.Contains("Dividends", StringComparison.OrdinalIgnoreCase))
            {
                return new CashIncomeEvent(
                    AccountEventId.NewId(),
                    accountId,
                    occurredAt,
                    EventType.Dividend,
                    "IBKR",
                    transactionId,
                    instrument.InstrumentId,
                    CashIncomeType.Dividend,
                    grossAmount,
                    new Money(0m, Currency.EUR),
                    fees);
            }

            if (type.Contains("Withholding Tax", StringComparison.OrdinalIgnoreCase))
            {
                return new WithholdingTaxEvent(
                    AccountEventId.NewId(),
                    accountId,
                    occurredAt,
                    "IBKR",
                    transactionId,
                    instrument.InstrumentId,
                    grossAmount);
            }

            return new CashIncomeEvent(
                AccountEventId.NewId(),
                accountId,
                occurredAt,
                EventType.Interest,
                "IBKR",
                transactionId,
                instrument.InstrumentId,
                CashIncomeType.Interest,
                grossAmount,
                new Money(0m, Currency.EUR),
                fees);
        }

        var buySell = element.Attribute("buySell")?.Value;
        if (description.Contains("(Ca.)", StringComparison.OrdinalIgnoreCase)
            || (buySell?.Contains("Ca.", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            transactionId = $"{transactionId}|CANCEL";
        }

        var side = buySell switch
        {
            not null when buySell.StartsWith("BUY", StringComparison.OrdinalIgnoreCase) => TradeSide.Buy,
            not null when buySell.StartsWith("SELL", StringComparison.OrdinalIgnoreCase) => TradeSide.Sell,
            _ => (TradeSide?)null
        };

        if (side is null)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Warning,
                ImportDiagnosticCode.InvalidRecord,
                $"Skipped unsupported trade side for '{symbol}'.",
                SourceReference: transactionId,
                Field: "buySell"));
            return null;
        }

        return new ExecutedTradeEvent(
            AccountEventId.NewId(),
            accountId,
            occurredAt,
            "IBKR",
            transactionId,
            instrument.InstrumentId,
            side.Value,
            new Quantity(Math.Abs(quantity)),
            new Money(price * fxRate, Currency.EUR),
            fees,
            new Money(0m, Currency.EUR));
    }

    private static Instrument EnsureInstrument(
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        bool isCash,
        string symbol,
        string isin,
        string currency,
        string description)
    {
        var normalizedIsin = isin.Trim();
        var normalizedSymbol = symbol.Trim();
        var effectiveSymbol = string.IsNullOrWhiteSpace(normalizedSymbol) ? currency.Trim().ToUpperInvariant() : normalizedSymbol;
        var identity = string.IsNullOrWhiteSpace(normalizedIsin) ? $"CASH:{effectiveSymbol}:{description}" : normalizedIsin;
        var instrumentId = CreateStableInstrumentId(identity);

        if (!instrumentCatalog.ContainsKey(instrumentId))
        {
            instrumentCatalog[instrumentId] = new Instrument(
                instrumentId,
                normalizedIsin,
                effectiveSymbol,
                string.IsNullOrWhiteSpace(description) ? effectiveSymbol : description,
                isCash && string.IsNullOrWhiteSpace(normalizedIsin) ? 0m : 0.30m);
        }

        return instrumentCatalog[instrumentId];
    }

    private static bool ShouldImport(
        bool isCash,
        string type,
        string assetClass,
        string symbol,
        string filePath,
        List<ImportDiagnostic> diagnostics)
    {
        if (isCash)
        {
            var isInterest = type.Contains("Interest", StringComparison.OrdinalIgnoreCase);
            var isDividend = type.Contains("Dividends", StringComparison.OrdinalIgnoreCase);
            var isWithholdingTax = type.Contains("Withholding Tax", StringComparison.OrdinalIgnoreCase);
            if (!isInterest && !isDividend && !isWithholdingTax)
            {
                return false;
            }

            if (isDividend && assetClass is not ("STK" or "FUND"))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Info,
                    ImportDiagnosticCode.IgnoredAsset,
                    $"Ignored cash dividend for unsupported asset class '{assetClass}'.",
                    SourceReference: filePath,
                    Field: symbol));
                return false;
            }

            return true;
        }

        if (assetClass == "CASH")
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Info,
                ImportDiagnosticCode.IgnoredAsset,
                $"Ignored forex cash trade '{symbol}'.",
                SourceReference: filePath,
                Field: symbol));
            return false;
        }

        if (LooksLikeForexPair(symbol))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Info,
                ImportDiagnosticCode.IgnoredAsset,
                $"Ignored forex trade '{symbol}'.",
                SourceReference: filePath,
                Field: symbol));
            return false;
        }

        if (assetClass is not ("STK" or "FUND"))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Info,
                ImportDiagnosticCode.IgnoredAsset,
                $"Ignored unsupported asset class '{assetClass}' for '{symbol}'.",
                SourceReference: filePath,
                Field: symbol));
            return false;
        }

        return true;
    }

    private static bool LooksLikeForexPair(string symbol)
    {
        if (!symbol.Contains('.') || symbol.Length != 7)
        {
            return false;
        }

        string[] currencies = ["EUR", "USD", "GBP", "CHF", "JPY"];
        return currencies.Any(symbol.Contains);
    }

    private static List<AccountEvent> CleanCancellations(List<AccountEvent> accountEvents, List<ImportDiagnostic> diagnostics)
    {
        var indicesToRemove = new HashSet<int>();
        for (var index = 0; index < accountEvents.Count; index++)
        {
            if (accountEvents[index] is not ExecutedTradeEvent tradeEvent
                || !tradeEvent.SourceReference.EndsWith("|CANCEL", StringComparison.Ordinal))
            {
                continue;
            }

            indicesToRemove.Add(index);
            var matchedOriginalIndex = -1;
            for (var candidateIndex = index - 1; candidateIndex >= 0; candidateIndex--)
            {
                if (indicesToRemove.Contains(candidateIndex)
                    || accountEvents[candidateIndex] is not ExecutedTradeEvent candidate)
                {
                    continue;
                }

                var sameSide = candidate.Side == tradeEvent.Side;
                var sameQuantity = candidate.Quantity.Value == tradeEvent.Quantity.Value;
                var sameAmount = Math.Abs(candidate.UnitPrice.Amount * candidate.Quantity.Value - tradeEvent.UnitPrice.Amount * tradeEvent.Quantity.Value) < 0.05m;
                if (candidate.InstrumentId == tradeEvent.InstrumentId && sameSide && sameQuantity && sameAmount)
                {
                    matchedOriginalIndex = candidateIndex;
                    break;
                }
            }

            if (matchedOriginalIndex >= 0)
            {
                indicesToRemove.Add(matchedOriginalIndex);
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Info,
                    ImportDiagnosticCode.CancellationRemoved,
                    "Removed cancellation pair.",
                    SourceReference: tradeEvent.SourceReference));
            }
        }

        return accountEvents.Where((_, index) => !indicesToRemove.Contains(index)).ToList();
    }

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Any, Culture, out var result) ? result : 0m;

    private static DateTimeOffset ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd;HHmmss", Culture, DateTimeStyles.AssumeUniversal, out var dateTime))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd", Culture, DateTimeStyles.AssumeUniversal, out var dateOnly))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateOnly.AddHours(23).AddMinutes(59).AddSeconds(59), DateTimeKind.Utc));
        }

        return DateTimeOffset.MinValue;
    }

    private static InstrumentId CreateStableInstrumentId(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return (InstrumentId)new Guid(bytes);
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Diagnostic;
using WealthIQ.Application.Import.Enumeration;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Ledger;

using CurrencyCode = WealthIQ.Domain.Enumeration.Currency;

namespace WealthIQ.Infrastructure.Ibkr.Import;

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

        var importedEntries = new List<PortfolioEntry>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var xml = await File.ReadAllTextAsync(file, ct);
                var document = XDocument.Parse(xml);
                importedEntries.AddRange(ParseDocument(document, file, request.AccountId, instrumentCatalog, result.Diagnostics));
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
        var orderedEntries = importedEntries.OrderBy(x => x.OccurredAt).ToList();
        result.PortfolioLedger = new PortfolioLedger(CleanCancellations(orderedEntries, result.Diagnostics), result.Instruments);
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

    private static List<PortfolioEntry> ParseDocument(
        XDocument document,
        string filePath,
        AccountId accountId,
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        List<ImportDiagnostic> diagnostics)
    {
        var result = new List<PortfolioEntry>();

        foreach (var trade in document.Descendants("Trade"))
        {
            if (ParseElement(trade, false, filePath, accountId, instrumentCatalog, diagnostics) is { } portfolioEntry)
            {
                result.Add(portfolioEntry);
            }
        }

        foreach (var cashTransaction in document.Descendants("CashTransaction"))
        {
            if (ParseElement(cashTransaction, true, filePath, accountId, instrumentCatalog, diagnostics) is { } portfolioEntry)
            {
                result.Add(portfolioEntry);
            }
        }

        return result;
    }

    private static PortfolioEntry? ParseElement(
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

        var dateAttr = element.Attribute("dateTime")
            ?? element.Attribute("tradeDate")
            ?? element.Attribute("reportDate");
        if (!TryParseDateTimeOffset(dateAttr?.Value, out var occurredAt))
        {
            var fieldName = dateAttr?.Name.LocalName ?? "dateTime";
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable date for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: fieldName));
            return null;
        }

        var effectiveDate = DateOnly.FromDateTime(occurredAt.UtcDateTime);

        // Cash records carry the value in "amount"; trades in "tradePrice". Both are required.
        var rawPrice = isCash ? element.Attribute("amount")?.Value : element.Attribute("tradePrice")?.Value;
        if (!TryParseDecimal(rawPrice, allowEmpty: false, out var price))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable {(isCash ? "amount" : "tradePrice")} for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: isCash ? "amount" : "tradePrice"));
            return null;
        }

        var quantity = 0m;
        if (!isCash && !TryParseDecimal(element.Attribute("quantity")?.Value, allowEmpty: false, out quantity))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Error,
                ImportDiagnosticCode.MalformedField,
                $"Missing or unparseable quantity for transaction '{transactionId}'.",
                SourceReference: transactionId,
                Field: "quantity"));
            return null;
        }

        // ibCommission is optional (cash records often omit it) → empty means zero.
        TryParseDecimal(element.Attribute("ibCommission")?.Value, allowEmpty: true, out var commission);
        var currencyCode = ParseCurrency(currency);
        var fees = new Money(Math.Abs(commission), currencyCode);
        var sourceProvenance = new SourceProvenance
        {
            SourceSystem = "IBKR",
            ImportFormat = "XML",
            SourceLocation = filePath,
            SourceRecordReference = transactionId,
            SourceSection = isCash ? "CashTransaction" : "Trade"
        };

        if (isCash)
        {
            var cashInstrument = EnsureCashInstrument(instrumentCatalog, currencyCode);
            var relatedInstrument = EnsureRelatedInstrument(instrumentCatalog, symbol, isin, currency, description);
            var grossAmount = new Money(price, currencyCode);
            if (type.Contains("Dividends", StringComparison.OrdinalIgnoreCase))
            {
                return new CashEntry(
                    PortfolioEntryId.NewId(),
                    accountId,
                    occurredAt,
                    effectiveDate,
                    sourceProvenance,
                    cashInstrument.InstrumentId,
                    CashFlowType.Dividend,
                    grossAmount,
                    fees,
                    new Money(0m, currencyCode),
                    relatedInstrument?.InstrumentId);
            }

            if (type.Contains("Withholding Tax", StringComparison.OrdinalIgnoreCase))
            {
                return new CashEntry(
                    PortfolioEntryId.NewId(),
                    accountId,
                    occurredAt,
                    effectiveDate,
                    sourceProvenance,
                    cashInstrument.InstrumentId,
                    CashFlowType.WithholdingTax,
                    grossAmount,
                    new Money(0m, currencyCode),
                    new Money(0m, currencyCode),
                    relatedInstrument?.InstrumentId);
            }

            return new CashEntry(
                PortfolioEntryId.NewId(),
                accountId,
                occurredAt,
                effectiveDate,
                sourceProvenance,
                cashInstrument.InstrumentId,
                CashFlowType.Interest,
                grossAmount,
                fees,
                new Money(0m, currencyCode));
        }

        var instrument = EnsureTradeInstrument(instrumentCatalog, symbol, isin, currency, description);

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

        return new TradeEntry(
            PortfolioEntryId.NewId(),
            accountId,
            occurredAt,
            effectiveDate,
            sourceProvenance with { SourceRecordReference = transactionId },
            instrument.InstrumentId,
            side.Value,
            new Quantity(Math.Abs(quantity)),
            new Money(price, currencyCode),
            fees,
            new Money(0m, currencyCode));
    }

    private static Instrument EnsureTradeInstrument(
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        string symbol,
        string isin,
        string currency,
        string description)
    {
        var normalizedIsin = isin.Trim();
        var normalizedSymbol = symbol.Trim();
        var effectiveSymbol = string.IsNullOrWhiteSpace(normalizedSymbol) ? currency.Trim().ToUpperInvariant() : normalizedSymbol;
        var identity = string.IsNullOrWhiteSpace(normalizedIsin) ? $"ASSET:{effectiveSymbol}:{description}" : normalizedIsin;
        var instrumentId = CreateStableInstrumentId(identity);

        if (!instrumentCatalog.ContainsKey(instrumentId))
        {
            instrumentCatalog[instrumentId] = new Instrument(
                instrumentId,
                normalizedIsin,
                effectiveSymbol,
                string.IsNullOrWhiteSpace(description) ? effectiveSymbol : description,
                string.IsNullOrWhiteSpace(normalizedIsin) ? 0m : 0.30m);
        }

        return instrumentCatalog[instrumentId];
    }

    private static Instrument? EnsureRelatedInstrument(
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        string symbol,
        string isin,
        string currency,
        string description)
    {
        if (string.IsNullOrWhiteSpace(symbol) && string.IsNullOrWhiteSpace(isin))
        {
            return null;
        }

        return EnsureTradeInstrument(instrumentCatalog, symbol, isin, currency, description);
    }

    private static Instrument EnsureCashInstrument(
        Dictionary<InstrumentId, Instrument> instrumentCatalog,
        CurrencyCode currency)
    {
        var symbol = currency.ToString();
        var instrumentId = CreateStableInstrumentId($"CASH:{symbol}");
        if (!instrumentCatalog.ContainsKey(instrumentId))
        {
            instrumentCatalog[instrumentId] = new Instrument(
                instrumentId,
                string.Empty,
                symbol,
                $"{symbol} cash",
                0m);
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

    private static List<PortfolioEntry> CleanCancellations(List<PortfolioEntry> portfolioEntries, List<ImportDiagnostic> diagnostics)
    {
        var indicesToRemove = new HashSet<int>();
        for (var index = 0; index < portfolioEntries.Count; index++)
        {
            if (portfolioEntries[index] is not TradeEntry tradeEntry
                || !tradeEntry.SourceProvenance.SourceRecordReference.EndsWith("|CANCEL", StringComparison.Ordinal))
            {
                continue;
            }

            indicesToRemove.Add(index);
            var matchedOriginalIndex = -1;
            for (var candidateIndex = index - 1; candidateIndex >= 0; candidateIndex--)
            {
                if (indicesToRemove.Contains(candidateIndex)
                    || portfolioEntries[candidateIndex] is not TradeEntry candidate)
                {
                    continue;
                }

                var sameSide = candidate.Side == tradeEntry.Side;
                var sameQuantity = candidate.Quantity.Value == tradeEntry.Quantity.Value;
                var sameAmount = Math.Abs(candidate.UnitPrice.Amount * candidate.Quantity.Value - tradeEntry.UnitPrice.Amount * tradeEntry.Quantity.Value) < 0.05m;
                if (candidate.InstrumentId == tradeEntry.InstrumentId && sameSide && sameQuantity && sameAmount)
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
                    SourceReference: tradeEntry.SourceProvenance.SourceRecordReference));
            }
        }

        return portfolioEntries.Where((_, index) => !indicesToRemove.Contains(index)).ToList();
    }

    private static bool TryParseDecimal(string? value, bool allowEmpty, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return allowEmpty;
        }

        return decimal.TryParse(value, NumberStyles.Any, Culture, out result);
    }

    private static bool TryParseDateTimeOffset(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd;HHmmss", Culture, DateTimeStyles.AssumeUniversal, out var dateTime))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            return true;
        }

        if (DateTime.TryParseExact(value, "yyyyMMdd", Culture, DateTimeStyles.AssumeUniversal, out var dateOnly))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(dateOnly.AddHours(23).AddMinutes(59).AddSeconds(59), DateTimeKind.Utc));
            return true;
        }

        return false;
    }

    private static InstrumentId CreateStableInstrumentId(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return (InstrumentId)new Guid(bytes);
    }

    private static CurrencyCode ParseCurrency(string currency)
    {
        if (Enum.TryParse<CurrencyCode>(currency, true, out var parsedCurrency))
        {
            return parsedCurrency;
        }

        throw new InvalidOperationException($"Unsupported currency '{currency}'.");
    }
}

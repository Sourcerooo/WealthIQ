using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WealthIQ.Application.Audit.Interface;
using WealthIQ.Application.Currency;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.MarketData;
using WealthIQ.Application.MarketData.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Infrastructure.Ibkr.Currency;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ibkr.MarketData;
using WealthIQ.Infrastructure.Ibkr.Tax;
using WealthIQ.Infrastructure.TradersPlace.Import;
using WealthIQ.Infrastructure.Ingest;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// --- Local data layout ---
// Defaults are repo-relative (ContentRootPath = src/WealthIQ.Web → repo root is two levels up).
// Optional config overrides: DataPaths:Root (the data/ folder) and DataPaths:Reference.
var defaultRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
var repoData = string.IsNullOrWhiteSpace(builder.Configuration["DataPaths:Root"])
    ? defaultRoot
    : Path.GetFullPath(builder.Configuration["DataPaths:Root"]!);
var referenceDir = string.IsNullOrWhiteSpace(builder.Configuration["DataPaths:Reference"])
    ? Path.Combine(repoData, "reference")
    : Path.GetFullPath(builder.Configuration["DataPaths:Reference"]!);
var appDataDir = Path.Combine(repoData, "app");
var auditDir = Path.Combine(appDataDir, "audit");
var dbPath = Path.Combine(appDataDir, "wealthiq.db");
Directory.CreateDirectory(auditDir);

var referenceDataSources = new ReferenceDataSources(
    Path.Combine(referenceDir, "basiszins.csv"),
    Path.Combine(referenceDir, "historical_prices.csv"),
    Path.Combine(referenceDir, "instruments.json"),
    Path.Combine(referenceDir, "listings.json"),
    Path.Combine(referenceDir, "fx_rates.csv"),
    Path.Combine(referenceDir, "tradersplace_dividend_aliases.csv"));
builder.Services.AddSingleton(referenceDataSources);

// --- Config options ---
var marketDataOptions = builder.Configuration.GetSection("MarketData").Get<HistoricalPriceProviderOptions>() ?? new HistoricalPriceProviderOptions();
var fxRateOptions = builder.Configuration.GetSection("FxRates").Get<FxRateProviderOptions>() ?? new FxRateProviderOptions();
var basiszinsOptions = builder.Configuration.GetSection("Basiszins").Get<BasisInterestRateSourceOptions>() ?? new BasisInterestRateSourceOptions();
builder.Services.AddSingleton(marketDataOptions);
builder.Services.AddSingleton(fxRateOptions);
builder.Services.AddSingleton(basiszinsOptions);

// --- Blazor + MudBlazor ---
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<WealthIQ.Web.Services.ThemePreferenceService>();
builder.Services.AddScoped<WealthIQ.Web.Services.ChartSelectionState>();

// --- HTTP client factory ---
builder.Services.AddHttpClient();

// --- Persistence ---
// Blazor Server: scoped == circuit-lifetime, so a single AddDbContext would be shared across
// overlapping UI operations. Register a factory and resolve a fresh, short-lived context per scope.
builder.Services.AddDbContextFactory<WealthIqDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<WealthIqDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<WealthIqDbContext>>().CreateDbContext());
// SqliteLedgerStore registered as both concrete and interface so SqliteImportStore (which takes the
// concrete type to share the same EF transaction) and ILedgerStore consumers both resolve the same instance.
builder.Services.AddScoped<SqliteLedgerStore>();
builder.Services.AddScoped<ILedgerStore>(sp => sp.GetRequiredService<SqliteLedgerStore>());
builder.Services.AddScoped<IImportStore, SqliteImportStore>();
builder.Services.AddScoped<IImportAuditStore, SqliteImportAuditStore>();
builder.Services.AddSingleton<IRawFileStore>(_ => new FileSystemRawFileStore(auditDir));

// --- Import ---
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IStatementImporter, IbkrStatementImporter>();
builder.Services.AddScoped<IStatementImporter, TradersPlaceStatementImporter>();
builder.Services.AddScoped<StatementImportPipeline>();

// --- Reference data ---
builder.Services.AddScoped<IReferenceDataSeeder, ReferenceDataSeeder>();
builder.Services.AddScoped<IBasisInterestRateProvider, DbBasisInterestRateProvider>();
builder.Services.AddScoped<IInstrumentProfileEnricher, DbInstrumentProfileEnricher>();
builder.Services.AddScoped<IFxRateLookup, DbFxRateLookup>();
builder.Services.AddScoped<IHistoricalPriceLookup, DbHistoricalPriceLookup>();
builder.Services.AddScoped<IInstrumentMarketDataMap, DbInstrumentMarketDataMap>();
builder.Services.AddScoped<IInstrumentPriceProvider, DerivedInstrumentPriceProvider>();

// --- Network providers (use named HttpClients via IHttpClientFactory) ---
builder.Services.AddScoped<IHistoricalPriceProvider>(sp =>
    new YahooHistoricalPriceProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Yahoo"),
        sp.GetRequiredService<HistoricalPriceProviderOptions>()));
builder.Services.AddScoped<IFxRateProvider>(sp =>
    new EcbFxRateProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("ECB"),
        sp.GetRequiredService<FxRateProviderOptions>()));
builder.Services.AddScoped<IBasisInterestRateSource>(sp =>
    new BmfBasisInterestRateSource(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("BMF"),
        sp.GetRequiredService<BasisInterestRateSourceOptions>()));

// --- Stores ---
builder.Services.AddScoped<IHistoricalPriceStore, DbHistoricalPriceStore>();
builder.Services.AddScoped<IFxRateStore, DbFxRateStore>();
builder.Services.AddScoped<IBasisInterestRateStore, DbBasisInterestRateStore>();

builder.Services.AddScoped<IDividendAliasMap, DbDividendAliasMap>();
builder.Services.AddScoped<IDividendAliasStore, DbDividendAliasStore>();
builder.Services.AddScoped<DividendAliasRefreshService>();

// --- Refresh services ---
builder.Services.AddScoped<HistoricalPriceRefreshService>();
builder.Services.AddScoped<FxRateRefreshService>();
builder.Services.AddScoped<BasisInterestRateRefreshService>();

// --- Admin, clear, and log services ---
builder.Services.AddScoped<IInstrumentReferenceAdmin, DbInstrumentReferenceAdmin>();
builder.Services.AddScoped<ILedgerClearService>(sp =>
    new DbLedgerClearService(
        sp.GetRequiredService<WealthIqDbContext>(),
        auditDirectory: auditDir));
builder.Services.AddScoped<IReferenceDataClearService, DbReferenceDataClearService>();
builder.Services.AddScoped<IDataRefreshLog, DbDataRefreshLog>();

// --- Tax replay ---
builder.Services.AddScoped<InstrumentCatalogBuilder>();
builder.Services.AddScoped<GermanTaxCalculator>();
builder.Services.AddScoped<AnnualTaxReportService>();

// --- Portfolio dashboard ---
builder.Services.AddScoped<WealthIQ.Application.Valuation.PortfolioValuationService>();
builder.Services.AddScoped<WealthIQ.Application.Dashboard.PortfolioDashboardService>();

var app = builder.Build();

// --- Startup: migrate + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WealthIqDbContext>();
    db.Database.Migrate();

    var seeder = scope.ServiceProvider.GetRequiredService<IReferenceDataSeeder>();
    await seeder.SeedIfEmptyAsync(referenceDataSources);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

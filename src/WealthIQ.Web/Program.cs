using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using WealthIQ.Application.Audit.Interface;
using WealthIQ.Application.Currency.Interface;
using WealthIQ.Application.Import;
using WealthIQ.Application.Import.Interface;
using WealthIQ.Application.Persistence.Interface;
using WealthIQ.Application.ReferenceData;
using WealthIQ.Application.ReferenceData.Interface;
using WealthIQ.Application.Tax;
using WealthIQ.Application.Tax.Interface;
using WealthIQ.Application.Tax.Report;
using WealthIQ.Infrastructure.Ibkr.Import;
using WealthIQ.Infrastructure.Ingest;
using WealthIQ.Infrastructure.Persistence;
using WealthIQ.Infrastructure.ReferenceData;
using WealthIQ.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// --- Local data layout ---
// ContentRootPath = src/WealthIQ.Web → repo root is two levels up.
var repoData = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
var referenceDir = Path.Combine(repoData, "reference");
var appDataDir = Path.Combine(repoData, "app");
var auditDir = Path.Combine(appDataDir, "audit");
var dbPath = Path.Combine(appDataDir, "wealthiq.db");
Directory.CreateDirectory(auditDir);

// --- Blazor + MudBlazor ---
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

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
builder.Services.AddScoped<StatementImportPipeline>();

// --- Reference data ---
builder.Services.AddScoped<IReferenceDataSeeder, ReferenceDataSeeder>();
builder.Services.AddScoped<IBasisInterestRateProvider, DbBasisInterestRateProvider>();
builder.Services.AddScoped<IYearEndPriceProvider, DbYearEndPriceProvider>();
builder.Services.AddScoped<IInstrumentProfileEnricher, DbInstrumentProfileEnricher>();
builder.Services.AddScoped<IFxRateLookup, DbFxRateLookup>();

// --- Tax replay ---
builder.Services.AddScoped<InstrumentCatalogBuilder>();
builder.Services.AddScoped<GermanTaxCalculator>();
builder.Services.AddScoped<AnnualTaxReportService>();

var app = builder.Build();

// --- Startup: migrate + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WealthIqDbContext>();
    db.Database.Migrate();

    var seeder = scope.ServiceProvider.GetRequiredService<IReferenceDataSeeder>();
    var sources = new ReferenceDataSources(
        Path.Combine(referenceDir, "basiszins.csv"),
        Path.Combine(referenceDir, "prices.csv"),
        Path.Combine(referenceDir, "instruments.json"),
        Path.Combine(referenceDir, "fx_rates.csv"));
    await seeder.SeedIfEmptyAsync(sources);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

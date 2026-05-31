using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` build the context without the Web host. The connection string here is only used by the
/// EF tooling at design time; the running app supplies its own SQLite path via DI (see WealthIQ.Web/Program.cs).
/// </summary>
public sealed class WealthIqDbContextFactory : IDesignTimeDbContextFactory<WealthIqDbContext>
{
    public WealthIqDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite("Data Source=wealthiq-design.db")
            .Options;
        return new WealthIqDbContext(options);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthIQ.Infrastructure.Persistence;

namespace WealthIQ.Tests.Infrastructure.Persistence;

/// <summary>
/// Creates a WealthIqDbContext backed by a private in-memory SQLite database.
/// The open connection must be kept alive for the DB to persist between contexts,
/// so the helper is disposable and owns the connection.
/// </summary>
public sealed class InMemorySqlite : IDisposable
{
    private readonly SqliteConnection _connection;

    public InMemorySqlite()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public WealthIqDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<WealthIqDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WealthIqDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}

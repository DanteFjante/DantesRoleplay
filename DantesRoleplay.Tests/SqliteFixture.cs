using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Each test gets its own in-memory SQLite database. The connection must stay open for the
/// lifetime of the test — closing it destroys the database.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteFixture()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public DantesRoleplayDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new DantesRoleplayDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}

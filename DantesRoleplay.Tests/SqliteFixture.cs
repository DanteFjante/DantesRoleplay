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

    /// <summary>
    /// Clones an already-populated database instead of building one.
    ///
    /// Setup that is identical for every test in a class — a schema, a registered application, an
    /// activated catalog — costs the same whether it is performed once or three hundred times, and
    /// performing it per test is how a suite quietly grows to half an hour. The copy is a page-level
    /// copy between two private in-memory databases, so each test still gets a database of its own
    /// and can write to it freely; nothing is shared once this returns.
    /// </summary>
    /// <remarks>
    /// Private, and reached through <see cref="CloneOf"/>: xunit rejects a class fixture with more
    /// than one public constructor, and this type is used as one.
    /// </remarks>
    private SqliteFixture(SqliteConnection template)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        template.BackupDatabase(_connection);
    }

    public static SqliteFixture CloneOf(SqliteConnection template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new SqliteFixture(template);
    }

    /// <summary>The open connection, for a caller that needs to clone this database into another.</summary>
    public SqliteConnection Connection => _connection;

    public DantesRoleplayDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new DantesRoleplayDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}

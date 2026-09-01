using System.Data;
using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.Ecs;

internal static class SqliteEcsConstraintTransaction
{
    /// <summary>
    /// Acquires SQLite's writer reservation before any constraint read. This makes the later
    /// validate-and-commit sequence serial with every other ECS writer instead of relying on a
    /// racy count followed by an insert.
    /// </summary>
    internal static async Task<IDbContextTransaction?> BeginIfNeededAsync(
        DantesRoleplayDbContext db,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        if (db.Database.GetDbConnection() is not SqliteConnection connection)
            return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false);
        return await db.Database.UseTransactionAsync(transaction, cancellationToken)
            ?? throw new InvalidOperationException("The ECS write transaction could not be enlisted.");
    }
}

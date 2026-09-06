using System.Data;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections;

/// <summary>Runs a compound projection read against one consistent SQLite snapshot.</summary>
public sealed class SqliteProjectionReadTransaction(DantesRoleplayDbContext db) : IProjectionReadTransaction
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (db.Database.CurrentTransaction is not null)
            return await read(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var result = await read(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

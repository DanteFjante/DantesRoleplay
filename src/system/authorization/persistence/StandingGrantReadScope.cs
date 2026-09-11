using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.Authorization;

/// <summary>One deferred read snapshot when no caller transaction exists; never replaces an owner's transaction.</summary>
internal sealed class StandingGrantReadScope(
    DantesRoleplayDbContext db, SqliteTransaction transaction, IDbContextTransaction enlistment) : IAsyncDisposable
{
    internal static async Task<StandingGrantReadScope?> EnterAsync(DantesRoleplayDbContext db,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) return null;
        await db.Database.OpenConnectionAsync(cancellationToken);
        SqliteTransaction? transaction = null;
        try
        {
            transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
            var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken)
                ?? throw new InvalidOperationException("The owner read transaction could not be enlisted.");
            return new(db, transaction, enlistment);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await enlistment.DisposeAsync(); }
        finally
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}

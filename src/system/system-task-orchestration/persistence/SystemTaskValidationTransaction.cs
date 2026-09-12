using System.Data;
using DantesRoleplay.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>Owns one short SQLite boundary; no caller transaction can escape as an admitted task or call.</summary>
internal sealed class SystemTaskValidationTransaction : IAsyncDisposable
{
    private readonly DantesRoleplayDbContext _db;
    private readonly IDbContextTransaction _enlistment;
    private readonly bool _opened;
    private SystemTaskValidationTransaction(DantesRoleplayDbContext db, SqliteConnection connection,
        SqliteTransaction transaction, IDbContextTransaction enlistment, bool opened, TimeProvider time)
    { _db = db; Connection = connection; Transaction = transaction; _enlistment = enlistment; _opened = opened;
        Store = new(connection.ConnectionString, time); }
    internal SqliteConnection Connection { get; }
    internal SqliteTransaction Transaction { get; }
    internal SqliteSystemTaskLifecycleStore Store { get; }

    internal static async Task<SystemTaskValidationTransaction> OpenAsync(DantesRoleplayDbContext db,
        TimeProvider time, bool write, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Validation requires its own transaction boundary.");
        if (db.Database.GetDbConnection() is not SqliteConnection connection)
            throw new InvalidOperationException("Validation requires SQLite storage.");
        var opened = connection.State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(cancellationToken);
        SqliteTransaction? transaction = null;
        try
        {
            transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: !write);
            var enlistment = await db.Database.UseTransactionAsync(transaction, cancellationToken)
                ?? throw new InvalidOperationException("The validation transaction could not be enlisted.");
            return new(db, connection, transaction, enlistment, opened, time);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (opened) await db.Database.CloseConnectionAsync();
            throw;
        }
    }
    internal Task CommitAsync() => Transaction.CommitAsync(CancellationToken.None);
    public async ValueTask DisposeAsync()
    {
        try { await _enlistment.DisposeAsync(); }
        finally
        {
            try { await Transaction.DisposeAsync(); }
            finally { if (_opened) await _db.Database.CloseConnectionAsync(); }
        }
    }
}

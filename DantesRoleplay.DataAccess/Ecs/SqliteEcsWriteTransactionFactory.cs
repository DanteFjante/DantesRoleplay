using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.Ecs;

public sealed class SqliteEcsWriteTransactionFactory(DantesRoleplayDbContext db)
    : IEcsWriteTransactionFactory
{
    public async Task<IEcsWriteTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("An ECS migration transaction is already active.");
        return new Transaction(db, await db.Database.BeginTransactionAsync(cancellationToken));
    }

    private sealed class Transaction(
        DantesRoleplayDbContext db,
        IDbContextTransaction transaction) : IEcsWriteTransaction
    {
        private bool _completed;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return;
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed) await RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
        }
    }
}

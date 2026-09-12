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
        return new Transaction(this, db, await db.Database.BeginTransactionAsync(cancellationToken));
    }

    public bool OwnsCurrent(IEcsWriteTransaction transaction) => transaction is Transaction candidate &&
        ReferenceEquals(candidate.Origin, this) && !candidate.Completed && !candidate.Disposed &&
        ReferenceEquals(db.Database.CurrentTransaction, candidate.Native);

    private sealed class Transaction(
        SqliteEcsWriteTransactionFactory origin,
        DantesRoleplayDbContext db,
        IDbContextTransaction transaction) : IEcsWriteTransaction
    {
        private bool _completed;
        private bool _disposed;
        public SqliteEcsWriteTransactionFactory Origin { get; } = origin;
        public IDbContextTransaction Native { get; } = transaction;
        public bool Completed => _completed;
        public bool Disposed => _disposed;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await Native.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return;
            await Native.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (!_completed) await RollbackAsync(CancellationToken.None);
            }
            finally { await Native.DisposeAsync(); }
        }
    }
}

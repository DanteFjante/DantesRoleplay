using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class EcsWriteTransactionFactoryTests
{
    [Fact]
    public async Task Ownership_is_exact_to_the_live_origin_wrapper()
    {
        var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var first = new SqliteEcsWriteTransactionFactory(db);
        var second = new SqliteEcsWriteTransactionFactory(db);
        await using var transaction = await first.BeginAsync();
        Assert.True(first.OwnsCurrent(transaction));
        Assert.False(second.OwnsCurrent(transaction));
        await transaction.RollbackAsync();
        Assert.False(first.OwnsCurrent(transaction));
    }

    [Fact]
    public async Task Disposed_and_arbitrary_wrappers_are_never_owned()
    {
        var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var factory = new SqliteEcsWriteTransactionFactory(db);
        var transaction = await factory.BeginAsync();
        await transaction.DisposeAsync();
        Assert.False(factory.OwnsCurrent(transaction));
        Assert.False(factory.OwnsCurrent(new Arbitrary()));
        Assert.False(((IEcsWriteTransactionFactory)new UnsupportedFactory()).OwnsCurrent(new Arbitrary()));
    }

    [Fact]
    public async Task Other_context_completed_and_replaced_transactions_are_never_owned()
    {
        var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        await using var otherDb = fixture.Open();
        var factory = new SqliteEcsWriteTransactionFactory(db);
        var other = new SqliteEcsWriteTransactionFactory(otherDb);
        var previous = await factory.BeginAsync();
        Assert.False(other.OwnsCurrent(previous));
        await previous.CommitAsync();
        Assert.False(factory.OwnsCurrent(previous));
        await previous.DisposeAsync();
        await using var replacement = await factory.BeginAsync();
        Assert.True(factory.OwnsCurrent(replacement));
        Assert.False(factory.OwnsCurrent(previous));
    }

    [Fact]
    public async Task Begin_reserves_the_SQLite_writer_before_any_mutation()
    {
        var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        await using var peer = new SqliteConnection(fixture.Connection);
        await peer.OpenAsync();
        var factory = new SqliteEcsWriteTransactionFactory(db);
        await using var transaction = await factory.BeginAsync();
        // No writes have occurred. A deferred first transaction would let this reservation succeed.
        var busy = Assert.Throws<SqliteException>(() => peer.BeginTransaction(deferred: false));
        Assert.Equal(5, busy.SqliteErrorCode);
        Assert.True(factory.OwnsCurrent(transaction));
        await transaction.RollbackAsync();
        await using var available = peer.BeginTransaction(deferred: false);
        await available.RollbackAsync();
    }

    private sealed class UnsupportedFactory : IEcsWriteTransactionFactory
    {
        public Task<IEcsWriteTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IEcsWriteTransaction>(new Arbitrary());
    }

    private sealed class Arbitrary : IEcsWriteTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record Fixture(string Connection)
    {
        public DantesRoleplayDbContext Open() => new(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(Connection).Options);
        public static async Task<Fixture> CreateAsync()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException();
            var directory = Path.Combine(root.FullName, ".tmp", "ecs-transaction-factory", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "kernel.db"), Pooling = false, DefaultTimeout = 1 }.ToString();
            await using var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).Options);
            await db.Database.MigrateAsync();
            return new(connection);
        }
    }
}

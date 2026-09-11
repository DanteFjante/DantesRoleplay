using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Real SQLite mutation/receipt checks; issuer membership is an explicitly isolated policy fixture.</summary>
public sealed class SqliteStandingGrantAdministrationTests
{
    [Fact]
    public async Task Issue_replays_one_receipt_while_changed_payload_and_stale_other_command_conflict()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var (store, issuer, request) = Setup(db);
        var first = await store.MutateAsync(Operator, request, "issue");
        var replay = await store.MutateAsync(Operator, request, "issue");
        var conflict = await store.MutateAsync(Operator, request with { MaximumOperations = 3 }, "issue");
        var stale = await store.MutateAsync(Operator, request, "other-command");
        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.False(first.Receipt!.EffectDetailsAvailable);
        Assert.Equal("STANDING_GRANT_COMMAND_CONFLICT", conflict.Code);
        Assert.Equal("STANDING_GRANT_CAS_MISMATCH", stale.Code);
        Assert.Single(await db.Set<StandingGrantRevisionRecord>().ToArrayAsync());
        Assert.Single(await db.Operations.ToArrayAsync());
        Assert.Equal(4, issuer.Calls);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Replace_and_terminal_revoke_preserve_history_and_cannot_change_revocation_bindings()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var (store, _, request) = Setup(db);
        Assert.Equal(InteractionInvocationResultTag.Committed, (await store.MutateAsync(Operator, request, "issue")).Tag);
        var replacement = request with { Mutation = StandingGrantIssuerMutation.Replace, ExpectedCurrentRevision = 1, MaximumOperations = 3 };
        Assert.Equal(InteractionInvocationResultTag.Committed, (await store.MutateAsync(Operator, replacement, "replace")).Tag);
        var revoke = replacement with { Mutation = StandingGrantIssuerMutation.Revoke, ExpectedCurrentRevision = 2 };
        var changed = await store.MutateAsync(Operator, revoke with { PrincipalReference = "principal." + new string('c', 64) }, "bad-revoke");
        Assert.Equal("STANDING_GRANT_REVOKE_BINDINGS_CHANGED", changed.Code);
        // Reconstructed equivalent collection instances must compare semantically.
        revoke = revoke with { Definitions = new(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.runtime", false, ["procedure"])]) };
        var result = await store.MutateAsync(Operator, revoke, "revoke");
        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.Equal(result.Receipt, (await store.MutateAsync(Operator, revoke, "revoke")).Receipt);
        Assert.Equal("STANDING_GRANT_TERMINAL", (await store.MutateAsync(Operator,
            replacement with { ExpectedCurrentRevision = 3 }, "revive")).Code);
        var history = await db.Set<StandingGrantRevisionRecord>().OrderBy(value => value.Revision).ToArrayAsync();
        Assert.Equal(3, history.Length);
        Assert.False(history[0].Revoked);
        Assert.True(history[2].Revoked);
        Assert.Equal(history[1].PermissionsJson, history[2].PermissionsJson);
        Assert.Equal(history[1].PrincipalReference, history[2].PrincipalReference);
        Assert.Equal(3, (await db.Set<StandingGrantCurrentRecord>().AsNoTracking().SingleAsync()).Revision);
        Assert.Equal(3, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Verified_invited_identity_and_replay_after_issuer_revocation_get_no_authority()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var (store, issuer, request) = Setup(db);
        var invited = TrustedPrincipalContext.VerifiedPrincipal(request.PrincipalReference, "test");
        Assert.Equal("ISSUER_DENIED", (await store.MutateAsync(invited, request, "guest")).Code);
        Assert.Empty(await db.Operations.ToArrayAsync());
        Assert.Equal(InteractionInvocationResultTag.Committed, (await store.MutateAsync(Operator, request, "issue")).Tag);
        issuer.Enabled = false;
        Assert.Equal("ISSUER_DENIED", (await store.MutateAsync(Operator, request, "issue")).Code);
        Assert.Single(await db.Operations.ToArrayAsync());
        Assert.Single(await db.Set<StandingGrantRevisionRecord>().ToArrayAsync());
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Failure_updating_current_pointer_rolls_back_new_revision_and_audit_then_same_command_can_retry()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var (store, _, request) = Setup(db);
        await store.MutateAsync(Operator, request, "issue");
        var replacement = request with { Mutation = StandingGrantIssuerMutation.Replace, ExpectedCurrentRevision = 1, MaximumOperations = 3 };
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fixture_reject_grant_pointer BEFORE UPDATE ON system_standing_grant_current
            BEGIN SELECT RAISE(ABORT, 'fixture pointer failure'); END;
            """);
        var failed = await store.MutateAsync(Operator, replacement, "replace");
        Assert.Equal(InteractionInvocationResultTag.Unavailable, failed.Tag);
        Assert.Single(await db.Set<StandingGrantRevisionRecord>().AsNoTracking().ToArrayAsync());
        Assert.Single(await db.Operations.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, (await db.Set<StandingGrantCurrentRecord>().AsNoTracking().SingleAsync()).Revision);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Null(db.Database.CurrentTransaction);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fixture_reject_grant_pointer;");
        Assert.Equal(InteractionInvocationResultTag.Committed, (await store.MutateAsync(Operator, replacement, "replace")).Tag);
        Assert.Equal(2, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Outer_transaction_and_unrelated_pending_writes_are_preserved_and_rejected()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var (store, issuer, request) = Setup(db);
        await using (var outer = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("STANDING_GRANT_OUTER_TRANSACTION", (await store.MutateAsync(Operator, request, "outer")).Code);
            Assert.Same(outer, db.Database.CurrentTransaction);
        }
        var pending = new Operation { Id = "unrelated", Tool = "fixture", Timestamp = DateTime.UtcNow };
        db.Add(pending);
        Assert.Equal("STANDING_GRANT_CONTEXT_HAS_PENDING_WRITES", (await store.MutateAsync(Operator, request, "pending")).Code);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        Assert.Equal(0, issuer.Calls);
        Assert.Empty(await db.Operations.AsNoTracking().ToArrayAsync());
    }

    private static readonly TrustedPrincipalContext Operator = TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test");
    private static (SqliteStandingGrantAdministration Store, IssuerFixture Issuer, StandingGrantMutationRequest Request) Setup(DantesRoleplayDbContext db)
    {
        var app = ApplicationIdentifier.Parse("demo");
        new SqliteApplicationRegistry(db).Register(new(app, "Demo", "Generic grant administration fixture.", []));
        var issuer = new IssuerFixture(db);
        return (new(db, issuer, new OperationLog(db)), issuer,
            new(StandingGrantIssuerMutation.Issue, "authors", 0, "principal." + new string('b', 64), app,
                StandingGrantScope.Application, null, [StandingGrantCapability.Author, StandingGrantCapability.Read],
                new(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.runtime", false, ["procedure"])]),
                [], 2, DateTime.UtcNow.AddHours(1)));
    }

    private sealed class IssuerFixture(DantesRoleplayDbContext db) : IStandingGrantIssuerPolicy
    {
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public Task<StandingGrantIssuerDecision> EvaluateAsync(TrustedPrincipalContext issuer, StandingGrantIssuerRequirement requirement,
            string commandId, CancellationToken cancellationToken = default)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Matches("^[0-9a-f]{32}$", requirement.Revision.IssuedByOperationId);
            Calls++;
            var allowed = Enabled && issuer.PrincipalId == Operator.PrincipalId;
            return Task.FromResult(new StandingGrantIssuerDecision(allowed, allowed ? "ISSUER_ALLOWED" : "ISSUER_DENIED",
                new(issuer.PrincipalId, issuer.AuthenticationMethod, "grant.issue", "installation", commandId, allowed,
                    allowed ? "ISSUER_ALLOWED" : "ISSUER_DENIED")));
        }
    }
}

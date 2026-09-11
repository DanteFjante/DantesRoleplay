using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class SqliteStandingGrantPolicyTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("demo");
    private static readonly StandingGrantDefinitionTarget Target = new(
        "demo.rules.item", "mechanic", Application, "demo.rules", "isolatedpolicyunitboundary", 1, Hash);

    [Fact]
    public async Task Current_read_application_grant_is_allowed()
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(capabilities: [StandingGrantCapability.Read]);

        var host = Host();
        var decision = await fixture.Policy.EvaluateAsync(host, Requirement(StandingGrantCapability.Read));

        Assert.True(decision.Allowed);
        Assert.Equal("STANDING_GRANT_ALLOWED", decision.Code);
        Assert.Equal(1, host.Budget.RemainingOperations);
        Assert.Null(fixture.Db.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("numeric")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    public async Task Stored_permissions_cannot_use_numeric_capabilities_duplicate_keys_or_extra_authority(string invalid)
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(capabilities: [StandingGrantCapability.Read]);
        var grant = await fixture.Db.Set<StandingGrantRevisionRecord>().SingleAsync();
        grant.PermissionsJson = invalid switch
        {
            "numeric" => grant.PermissionsJson.Replace("\"read\"", "\"4\""),
            "duplicate" => grant.PermissionsJson.Replace("\"capabilities\":", "\"capabilities\":[\"activate\"],\"capabilities\":"),
            _ => grant.PermissionsJson.Replace("\"capabilities\":", "\"operator\":true,\"capabilities\":")
        };
        await fixture.Db.SaveChangesAsync();
        var decision = await fixture.Policy.EvaluateAsync(Host(), Requirement(StandingGrantCapability.Read));
        Assert.False(decision.Allowed);
        Assert.Equal("STANDING_GRANT_INVALID", decision.Code);
    }

    [Fact]
    public async Task Old_grant_reference_is_denied_after_current_revision_is_revoked_even_when_old_row_is_tracked()
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(capabilities: [StandingGrantCapability.Read]);
        _ = await fixture.Db.Set<StandingGrantRevisionRecord>().SingleAsync(row => row.GrantReference == "grant@1");
        fixture.Db.Add(GrantRecord(2, "grant@2", revoked: true, capabilities: [StandingGrantCapability.Read]));
        var current = await fixture.Db.Set<StandingGrantCurrentRecord>().SingleAsync();
        current.Revision = 2;
        await fixture.Db.SaveChangesAsync();

        var decision = await fixture.Policy.EvaluateAsync(Host(), Requirement(StandingGrantCapability.Read));

        Assert.False(decision.Allowed);
        Assert.Equal("STANDING_GRANT_NOT_CURRENT", decision.Code);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("principal")]
    [InlineData("expired")]
    [InlineData("target")]
    public async Task Current_grant_denies_scope_principal_expiry_and_target_mismatches(string mismatch)
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(capabilities: [StandingGrantCapability.Read], expiresAtUtc: mismatch == "expired" ? DateTime.UtcNow.AddMinutes(-1) : null);
        var host = mismatch == "principal" ? Host(principal: "principal.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb") : Host();
        var requirement = mismatch == "scope"
            ? Requirement(StandingGrantCapability.Read, StandingGrantScope.StateSpace)
            : mismatch == "target"
                ? Requirement(StandingGrantCapability.Read, target: Target with { ContentFingerprint = new string('B', 64) })
                : Requirement(StandingGrantCapability.Read);

        var decision = await fixture.Policy.EvaluateAsync(host, requirement);

        Assert.False(decision.Allowed);
        Assert.Equal(mismatch == "target" ? "STANDING_GRANT_TARGET_DENIED" : "STANDING_GRANT_DENIED", decision.Code);
    }

    [Fact]
    public async Task Mutation_requires_callers_sqlite_transaction_and_is_allowed_inside_it()
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(capabilities: [StandingGrantCapability.Author], effectKinds: ["component.set"]);
        var requirement = Requirement(StandingGrantCapability.Author, effectKinds: ["component.set"]);

        var outside = await fixture.Policy.EvaluateAsync(Host(), requirement);
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();
        var inside = await fixture.Policy.EvaluateAsync(Host(), requirement);

        Assert.False(outside.Allowed);
        Assert.Equal("STANDING_GRANT_TRANSACTION_REQUIRED", outside.Code);
        Assert.True(inside.Allowed);
        Assert.Equal("STANDING_GRANT_ALLOWED", inside.Code);
    }

    [Fact]
    public async Task Budget_ceiling_deadline_and_final_reserved_allowance_are_evaluated_correctly()
    {
        await using var fixture = await PolicyFixture.CreateAsync();
        await fixture.SeedAsync(
            capabilities: [StandingGrantCapability.Execute], scope: StandingGrantScope.StateSpace,
            maximumOperations: 1, effectKinds: ["component.set"]);
        var requirement = Requirement(StandingGrantCapability.Execute, StandingGrantScope.StateSpace, ["component.set"]);
        var overBudget = Host(maximumOperations: 2);
        var overDeadline = Host(deadlineUtc: DateTime.UtcNow.AddHours(2));
        var reservedFinalBudget = new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1));
        Assert.True(reservedFinalBudget.TryConsumeOperation());
        Assert.Equal(0, reservedFinalBudget.RemainingOperations);
        await using var transaction = await fixture.Db.Database.BeginTransactionAsync();

        var budgetDecision = await fixture.Policy.EvaluateAsync(overBudget, requirement);
        var deadlineDecision = await fixture.Policy.EvaluateAsync(overDeadline, requirement);
        var finalDecision = await fixture.Policy.EvaluateAsync(Host(budget: reservedFinalBudget), requirement);

        Assert.Equal("STANDING_GRANT_DENIED", budgetDecision.Code);
        Assert.Equal("STANDING_GRANT_DENIED", deadlineDecision.Code);
        Assert.True(finalDecision.Allowed);
        Assert.Equal("STANDING_GRANT_ALLOWED", finalDecision.Code);
    }

    private static InteractionInvocationHost Host(string principal = Principal, int maximumOperations = 1,
        DateTime? deadlineUtc = null, InteractionInvocationBudget? budget = null) => new(
        TrustedPrincipalContext.VerifiedPrincipal(principal, "test"), new ApplicationRevision(Application, 1, Hash, []),
        "state", "grant@1", "command", "state@1", InteractionExecutionProfile.Atomic,
        budget ?? new InteractionInvocationBudget(maximumOperations, deadlineUtc ?? DateTime.UtcNow.AddMinutes(1)));

    private static StandingGrantRequirement Requirement(StandingGrantCapability capability,
        StandingGrantScope scope = StandingGrantScope.Application, IReadOnlyList<string>? effectKinds = null,
        StandingGrantDefinitionTarget? target = null) => new(capability, scope, [target ?? Target], effectKinds ?? []);

    private static StandingGrantRevisionRecord GrantRecord(int revision, string reference, bool revoked,
        IReadOnlyList<StandingGrantCapability> capabilities, StandingGrantScope scope = StandingGrantScope.Application,
        int maximumOperations = 1, IReadOnlyList<string>? effectKinds = null, DateTime? expiresAtUtc = null) => new()
    {
        GrantId = "grant", Revision = revision, GrantReference = reference, PrincipalReference = Principal,
        ApplicationId = Application.Value, Scope = scope == StandingGrantScope.Application ? "application" : "stateSpace",
        StateSpaceId = scope == StandingGrantScope.Application ? null : "state", ContentFingerprint = Hash,
        MaximumOperations = maximumOperations, ExpiresAtUtc = expiresAtUtc ?? DateTime.UtcNow.AddMinutes(30),
        Revoked = revoked, IssuedByOperationId = "operation",
        PermissionsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            capabilities = capabilities.Select(value => value.ToString().ToLowerInvariant()),
            definitions = new { mode = "exactIds", exactIds = new[] { Target.DefinitionId }, applicationOwnedNamespaces = Array.Empty<object>() },
            effectKinds = effectKinds ?? Array.Empty<string>()
        })
    };

    private sealed class ExactTargetResolver : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> RevalidateAsync(InteractionInvocationHost host,
            StandingGrantDefinitionTarget target, CancellationToken cancellationToken = default) =>
            ResolveAsync(host, new(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint), cancellationToken);

        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(selection == new StandingGrantDefinitionReference(Target.DefinitionId, Target.Kind, Target.Revision, Target.ContentFingerprint)
                ? new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available, "isolatedpolicyunitboundary", Target)
                : new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Denied, "isolatedpolicyunitboundary", null));

        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            DantesRoleplay.ApplicationActivation.ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable, "isolatedpolicyunitboundary", null));
    }

    private sealed class PolicyFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private PolicyFixture(SqliteConnection connection, DantesRoleplayDbContext db)
        {
            this.connection = connection;
            Db = db;
            Policy = new SqliteStandingGrantPolicy(db, new ExactTargetResolver());
        }

        public DantesRoleplayDbContext Db { get; }
        public SqliteStandingGrantPolicy Policy { get; }

        public static async Task<PolicyFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new PolicyFixture(connection, db);
        }

        public async Task SeedAsync(IReadOnlyList<StandingGrantCapability> capabilities,
            StandingGrantScope scope = StandingGrantScope.Application, int maximumOperations = 1,
            IReadOnlyList<string>? effectKinds = null, DateTime? expiresAtUtc = null)
        {
            Db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" });
            Db.Add(GrantRecord(1, "grant@1", false, capabilities, scope, maximumOperations, effectKinds, expiresAtUtc));
            Db.Add(new StandingGrantCurrentRecord { GrantId = "grant", Revision = 1 });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

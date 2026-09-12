using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.Tests;

public sealed class ApplicationAuthoringModelTests
{
    [Fact]
    public async Task Proposed_model_enforces_grant_budget_and_valid_evidence_requirements()
    {
        await using var connection = new SqliteConnection("Filename=:memory:"); await connection.OpenAsync();
        await using var db = new ProposalContext(connection); await db.Database.EnsureCreatedAsync();
        db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        await db.SaveChangesAsync();
        db.Add(new StandingGrantRevisionRecord { GrantId = "grant", Revision = 1, GrantReference = "grant@1", PrincipalReference = "principal", ApplicationId = "app", Scope = "stateSpace", StateSpaceId = "space", PermissionsJson = EmptyPermissions, ContentFingerprint = Hash, MaximumOperations = 17, ExpiresAtUtc = DateTime.UtcNow, IssuedByOperationId = "operation" });
        var grantFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Contains("CK_system_grant_budget", grantFailure.InnerException!.Message); db.ChangeTracker.Clear();
        db.Add(new ApplicationRegistryRecord { Id = "app", DisplayName = "App", Description = "Test" });
        db.Add(new ApplicationRevisionRecord { ApplicationId = "app", Revision = 1, Fingerprint = Hash, CreatedAtUtc = DateTime.UtcNow });
        db.Add(new ApplicationCandidateRevisionRecord { ApplicationId = "app", CandidateId = "candidate", Revision = 1, ApplicationRevision = 1, ContentFingerprint = Hash, Origin = "runtime", NewImplementationReason = "needed", AuthorGrantReference = "grant@1", SourceOperationId = "operation", CanonicalCommandFingerprint = Hash });
        await db.SaveChangesAsync();
        db.Add(new Operation { Id = "validation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new ApplicationCandidateValidationRecord { OperationId = "validation", ApplicationId = "app", CandidateId = "candidate", Revision = 1, CandidateFingerprint = Hash, GrantReference = "grant@1", DependencyFingerprint = Hash, Outcome = "valid", PreparationVersion = "v1", ManualPacketResultFingerprint = Hash, CanonicalCommandFingerprint = Hash, DependenciesJson = "[]", DiagnosticsJson = "[]", AlternativesJson = "[]" });
        var validationFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Contains("CK_system_candidate_validation_valid_evidence", validationFailure.InnerException!.Message); db.ChangeTracker.Clear();
        db.Add(new Operation { Id = "validation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new ApplicationCandidateValidationRecord { OperationId = "validation", ApplicationId = "app", CandidateId = "candidate", Revision = 1, CandidateFingerprint = Hash, GrantReference = "grant@1", DependencyFingerprint = Hash, Outcome = "valid", PreparationVersion = "v1", ManualPacketResultFingerprint = Hash, CanonicalCommandFingerprint = Hash, DependenciesJson = "[]", DependenciesComplete = true, DependencyEvidenceReference = "dependencies", PreparedEvidenceReference = "prepared", ReuseEvidenceReference = "reuse", DiagnosticsJson = "[]", AlternativesJson = "[]" });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("application", null, true)]
    [InlineData("application", "space", false)]
    [InlineData("stateSpace", "space", true)]
    [InlineData("stateSpace", null, false)]
    public async Task Grant_scope_pairs_with_state_space_exactly(string scope, string? stateSpaceId, bool accepted)
    {
        await using var connection = new SqliteConnection("Filename=:memory:"); await connection.OpenAsync();
        await using var db = new ProposalContext(connection); await db.Database.EnsureCreatedAsync();
        db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" }); await db.SaveChangesAsync();
        db.Add(new StandingGrantRevisionRecord { GrantId = "grant", Revision = 1, GrantReference = "grant@1", PrincipalReference = "principal", ApplicationId = "app", Scope = scope, StateSpaceId = stateSpaceId, PermissionsJson = EmptyPermissions, ContentFingerprint = Hash, MaximumOperations = 1, ExpiresAtUtc = DateTime.UtcNow, IssuedByOperationId = "operation" });
        if (accepted) await db.SaveChangesAsync();
        else
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CK_system_grant_scope", failure.InnerException!.Message);
        }
    }

    [Theory]
    [InlineData("exactIds", "[]", "[]", true)]
    [InlineData("applicationOwned", "[]", "[]", false)]
    [InlineData("applicationOwned", "[]", "[{\"namespaceId\":\"app.custom\",\"includeDescendants\":false,\"definitionKinds\":[\"mechanic\"]}]", true)]
    [InlineData("applicationOwned", "[\"app.custom.old\"]", "[{\"namespaceId\":\"app.custom\",\"includeDescendants\":false,\"definitionKinds\":[\"mechanic\"]}]", false)]
    [InlineData("anything", "[]", "[]", false)]
    public async Task Proposed_grant_requires_explicit_exclusive_definition_mode(
        string mode, string exactIds, string boundaries, bool accepted)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = new ProposalContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        await db.SaveChangesAsync();
        var permissions = JsonSerializer.Serialize(new
        {
            capabilities = new[] { "author", "activate" }, effectKinds = Array.Empty<string>(),
            definitions = new
            {
                mode, exactIds = JsonSerializer.Deserialize<JsonElement>(exactIds),
                applicationOwnedNamespaces = JsonSerializer.Deserialize<JsonElement>(boundaries)
            }
        });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = "grant", Revision = 1, GrantReference = "grant@1", PrincipalReference = "principal",
            ApplicationId = "app", Scope = "application", StateSpaceId = null, PermissionsJson = permissions,
            ContentFingerprint = Hash, MaximumOperations = 1, ExpiresAtUtc = DateTime.UtcNow,
            IssuedByOperationId = "operation"
        });
        if (accepted) await db.SaveChangesAsync();
        else
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CK_system_grant_definition_mode", failure.InnerException!.Message);
        }
    }

    [Theory]
    [InlineData("preparation")]
    [InlineData("manual")]
    [InlineData("command")]
    [InlineData("incomplete")]
    [InlineData("object")]
    [InlineData("too-many")]
    [InlineData("too-large")]
    public async Task Valid_validation_rejects_missing_pins_or_unbounded_dependencies(string invalid)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = new ProposalContext(connection);
        await SeedCandidate(db);
        var row = Validation();
        switch (invalid)
        {
            case "preparation": row.PreparationVersion = null; break;
            case "manual": row.ManualPacketResultFingerprint = null; break;
            case "command": row.CanonicalCommandFingerprint = ""; break;
            case "incomplete": row.DependenciesComplete = false; break;
            case "object": row.DependenciesJson = "{}"; break;
            case "too-many": row.DependenciesJson = JsonSerializer.Serialize(Enumerable.Repeat(new { definitionId = "app.rules.item", revision = 1, contentFingerprint = Hash }, 65)); break;
            case "too-large": row.DependenciesJson = JsonSerializer.Serialize(new[] { new string('x', 65536) }); break;
        }
        db.Add(row);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_system_candidate_validation", failure.InnerException!.Message);
    }

    [Fact]
    public async Task Validation_retains_distinct_exact_pins_across_contexts_and_unavailable_needs_no_invented_success()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var expected = Validation();
        await using (var db = new ProposalContext(connection))
        {
            await SeedCandidate(db);
            db.Add(expected);
            await db.SaveChangesAsync();
        }
        await using (var db = new ProposalContext(connection))
        {
            var saved = await db.Set<ApplicationCandidateValidationRecord>().SingleAsync();
            Assert.Equal(expected.PreparationVersion, saved.PreparationVersion);
            Assert.Equal(expected.CandidateFingerprint, saved.CandidateFingerprint);
            Assert.Equal(expected.ManualPacketResultFingerprint, saved.ManualPacketResultFingerprint);
            Assert.Equal(expected.CanonicalCommandFingerprint, saved.CanonicalCommandFingerprint);
            Assert.Equal(expected.DependencyFingerprint, saved.DependencyFingerprint);
            Assert.Equal(expected.DependenciesJson, saved.DependenciesJson);
            Assert.True(saved.DependenciesComplete);
            db.Add(new Operation { Id = "unavailable", Timestamp = DateTime.UtcNow, Tool = "test" });
            var unavailable = Validation();
            unavailable.OperationId = "unavailable";
            unavailable.Outcome = "unavailable";
            unavailable.PreparationVersion = null;
            unavailable.ManualPacketResultFingerprint = null;
            unavailable.PreparedEvidenceReference = null;
            unavailable.ReuseEvidenceReference = null;
            unavailable.DependencyEvidenceReference = null;
            unavailable.DependenciesComplete = false;
            db.Add(unavailable);
            await db.SaveChangesAsync();
        }
        await using (var db = new ProposalContext(connection))
        {
            var saved = await db.Set<ApplicationCandidateValidationRecord>().SingleAsync(x => x.OperationId == "unavailable");
            Assert.Equal("unavailable", saved.Outcome);
            Assert.Null(saved.PreparationVersion);
            Assert.Null(saved.ManualPacketResultFingerprint);
            Assert.False(saved.DependenciesComplete);
        }
    }

    private static ApplicationCandidateValidationRecord Validation() => new()
    {
        OperationId = "validation", ApplicationId = "app", CandidateId = "candidate", Revision = 1,
        CandidateFingerprint = Hash, GrantReference = "grant@1", DependencyFingerprint = new string('B', 64),
        PreparationVersion = "reviewed-preparation-v2", ManualPacketResultFingerprint = new string('C', 64),
        CanonicalCommandFingerprint = new string('D', 64), DependenciesComplete = true,
        DependenciesJson = JsonSerializer.Serialize(new[] { new { definitionId = "app.rules.item", revision = 3, contentFingerprint = new string('E', 64) } }),
        DependencyEvidenceReference = "dependencies", PreparedEvidenceReference = "prepared", ReuseEvidenceReference = "reuse",
        Outcome = "valid", DiagnosticsJson = "[]", AlternativesJson = "[]"
    };

    private static async Task SeedCandidate(ProposalContext db)
    {
        await db.Database.EnsureCreatedAsync();
        db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new Operation { Id = "validation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new ApplicationRegistryRecord { Id = "app", DisplayName = "App", Description = "Test" });
        db.Add(new ApplicationRevisionRecord { ApplicationId = "app", Revision = 1, Fingerprint = Hash, CreatedAtUtc = DateTime.UtcNow });
        db.Add(new ApplicationCandidateRevisionRecord
        {
            ApplicationId = "app", CandidateId = "candidate", Revision = 1, ApplicationRevision = 1,
            ContentFingerprint = Hash, Origin = "runtime", NewImplementationReason = "needed",
            AuthorGrantReference = "grant@1", SourceOperationId = "operation", CanonicalCommandFingerprint = Hash
        });
        await db.SaveChangesAsync();
    }

    private const string EmptyPermissions = """
        {"capabilities":[],"definitions":{"mode":"exactIds","exactIds":[],"applicationOwnedNamespaces":[]},"effectKinds":[]}
        """;
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private sealed class ProposalContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Operation>(e => { e.HasKey(x => x.Id); });
            b.Entity<ApplicationRegistryRecord>(e => { e.HasKey(x => x.Id); });
            b.Entity<ApplicationRevisionRecord>(e => { e.HasKey(x => new { x.ApplicationId, x.Revision }); });
            b.Entity<ApplicationActivationDocumentIdentityRecord>(e => { e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.ApplicationId, x.Id }); });
            b.Entity<ApplicationActivationDocumentEvidenceRecord>(e => e.HasKey(x => new { x.IdentityId, x.EvidenceVersion }));
            b.Entity<ApplicationActivationReceiptRecord>(e => e.HasKey(x => x.OperationId));
            ApplicationAuthoringModelConfiguration.Configure(b);
            StandingGrantModelConfiguration.Configure(b);
            InformationHistoryModelConfiguration.Configure(b);
        }
    }
}

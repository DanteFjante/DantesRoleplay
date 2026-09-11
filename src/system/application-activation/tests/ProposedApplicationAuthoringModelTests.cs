using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class ProposedApplicationAuthoringModelTests
{
    [Fact]
    public void Production_model_does_not_include_review_only_rows()
    {
        using var fixture = new SqliteFixture(); using var db = fixture.CreateContext();
        Assert.Null(db.Model.FindEntityType(typeof(ApplicationCandidateRevisionRecord)));
        Assert.Null(db.Model.FindEntityType(typeof(StandingGrantRevisionRecord)));
        Assert.Null(db.Model.FindEntityType(typeof(InformationContentRevisionRecord)));
    }

    [Fact]
    public async Task Proposed_model_enforces_grant_budget_and_valid_evidence_requirements()
    {
        await using var connection = new SqliteConnection("Filename=:memory:"); await connection.OpenAsync();
        await using var db = new ProposalContext(connection); await db.Database.EnsureCreatedAsync();
        db.Add(new Operation { Id = "operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        await db.SaveChangesAsync();
        db.Add(new StandingGrantRevisionRecord { GrantId = "grant", Revision = 1, GrantReference = "grant@1", PrincipalReference = "principal", ApplicationId = "app", Scope = "stateSpace", StateSpaceId = "space", PermissionsJson = "[]", ContentFingerprint = Hash, MaximumOperations = 17, ExpiresAtUtc = DateTime.UtcNow, IssuedByOperationId = "operation" });
        var grantFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Contains("CK_proposed_grant_budget", grantFailure.InnerException!.Message); db.ChangeTracker.Clear();
        db.Add(new ApplicationRegistryRecord { Id = "app", DisplayName = "App", Description = "Test" });
        db.Add(new ApplicationRevisionRecord { ApplicationId = "app", Revision = 1, Fingerprint = Hash, CreatedAtUtc = DateTime.UtcNow });
        db.Add(new ApplicationCandidateRevisionRecord { ApplicationId = "app", CandidateId = "candidate", Revision = 1, ApplicationRevision = 1, ContentFingerprint = Hash, Origin = "runtime", NewImplementationReason = "needed", AuthorGrantReference = "grant@1", SourceOperationId = "operation", CanonicalCommandFingerprint = Hash });
        await db.SaveChangesAsync();
        db.Add(new Operation { Id = "validation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new ApplicationCandidateValidationRecord { OperationId = "validation", ApplicationId = "app", CandidateId = "candidate", Revision = 1, CandidateFingerprint = Hash, GrantReference = "grant@1", DependencyFingerprint = Hash, Outcome = "valid", DiagnosticsJson = "[]", AlternativesJson = "[]" });
        var validationFailure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Contains("CK_proposed_candidate_validation_valid_evidence", validationFailure.InnerException!.Message); db.ChangeTracker.Clear();
        db.Add(new Operation { Id = "validation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new ApplicationCandidateValidationRecord { OperationId = "validation", ApplicationId = "app", CandidateId = "candidate", Revision = 1, CandidateFingerprint = Hash, GrantReference = "grant@1", DependencyFingerprint = Hash, Outcome = "valid", PreparedEvidenceReference = "prepared", ReuseEvidenceReference = "reuse", DiagnosticsJson = "[]", AlternativesJson = "[]" });
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
        db.Add(new StandingGrantRevisionRecord { GrantId = "grant", Revision = 1, GrantReference = "grant@1", PrincipalReference = "principal", ApplicationId = "app", Scope = scope, StateSpaceId = stateSpaceId, PermissionsJson = "[]", ContentFingerprint = Hash, MaximumOperations = 1, ExpiresAtUtc = DateTime.UtcNow, IssuedByOperationId = "operation" });
        if (accepted) await db.SaveChangesAsync();
        else
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CK_proposed_grant_scope", failure.InnerException!.Message);
        }
    }

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
            ProposedApplicationAuthoringModel.Configure(b);
            ProposedStandingGrantModel.Configure(b);
            ProposedInformationHistoryModel.Configure(b);
        }
    }
}

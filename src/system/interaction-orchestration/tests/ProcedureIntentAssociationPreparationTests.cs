using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>Real retained-row reads with test-only model/grant/source mappings, not production grant or migration acceptance.</summary>
public sealed class ProcedureIntentAssociationPreparationTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public async Task Reads_retained_bytes_and_requires_both_permissions_again_before_return(
        bool read, bool author, bool revoke, bool expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var modelContext = new ReadModel(connection);
        await modelContext.Database.EnsureCreatedAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>().UseSqlite(connection).UseModel(modelContext.Model).Options;
        await using var db = new DantesRoleplayDbContext(options);
        var applications = new InMemoryApplicationRegistry();
        var app = ApplicationIdentifier.Parse("sample-app");
        var revision = applications.Register(new(app, "Sample", "Fixture application", []));
        const string text = "---\nid: procedure.inspect\ncategory: tools\nname: Inspect\nstatus: active\ncustom: preserve\n---\n\n## Description\nInspect scope.\n\n## Instructions\nRead only.\n\n## Matches\nold phrase\n\n## Constraints\nNo changes.\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        const string path = "content/procedures/inspect.md";
        var document = new ActivatedApplicationDocument("file:" + path, "catalog", SourceTrust.Trusted, 0,
            path, "text/markdown", Hash(text), bytes.Length, true);
        var row = new ApplicationCandidateRevisionRecord
        {
            ApplicationId = app.Value, CandidateId = new string('a', 32), Revision = 1, ApplicationRevision = revision.Revision,
            ContentFingerprint = new string('0', 64), ExpectedActiveFingerprint = Hash("active"), Origin = "runtime",
            NewImplementationReason = "Add alternate wording.", AuthorGrantReference = "grant.1", SourceOperationId = "operation.1",
            CanonicalCommandFingerprint = Hash("command")
        };
        row.ContentFingerprint = ApplicationCandidateRetainedReader.ContentFingerprint(row, revision, [new(document, bytes)]);
        db.Add(row);
        db.Add(new ApplicationActivationDocumentIdentityRecord { Id = 1, ApplicationId = app.Value, LogicalIdentity = document.LogicalIdentity });
        db.Add(new ApplicationActivationDocumentEvidenceRecord { IdentityId = 1, EvidenceVersion = 1, SourceId = "catalog",
            Trust = (int)SourceTrust.Trusted, RelativePath = path, MediaType = document.MediaType,
            ContentFingerprint = document.ContentFingerprint, Length = bytes.Length, IsText = true, RetainedBytes = bytes });
        db.Add(new ApplicationCandidateDocumentRecord { ApplicationId = app.Value, CandidateId = row.CandidateId, Revision = 1, Ordinal = 0, IdentityId = 1, EvidenceVersion = 1 });
        await db.SaveChangesAsync();
        var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(app, document,
            new Dictionary<string, ActivatedApplicationDocument> { [path] = document },
            new Dictionary<string, byte[]> { [path] = bytes })!;
        var policy = new Permissions(read, author, revoke);
        var preparation = new ProcedureIntentAssociationPreparation(new(db, applications), new Targets(), policy, new Changes(app));
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            revision, "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.ReadOnly,
            new(1, DateTime.UtcNow.AddMinutes(1)));
        var candidate = new ApplicationCandidateReference(app, row.CandidateId, 1, row.ContentFingerprint);
        var result = await preparation.PrepareAsync(host, candidate,
            new(record.QualifiedId, record.Kind, record.Version, record.ContentFingerprint), [" show   scope ", "SHOW scope", "inspect area"]);
        Assert.Equal(expected, result.Document is not null);
        Assert.Equal(0, host.Budget.RemainingOperations);
        if (expected)
        {
            Assert.Equal("INTENT_REPLACEMENT_PREPARED", result.Code);
            Assert.Equal((document.LogicalIdentity, document.SourceId, path, document.MediaType),
                (result.Document!.LogicalIdentity, result.Document.SourceId, result.Document.RelativePath, result.Document.MediaType));
            Assert.Contains("## Matches\nshow scope\ninspect area\n", result.Document.Text);
            Assert.Contains("custom: preserve", result.Document.Text);
            Assert.Equal(4, policy.Calls);
        }
        Assert.Equal(bytes, (await db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking().SingleAsync()).RetainedBytes);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Missing_retained_candidate_returns_no_document()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var app = ApplicationIdentifier.Parse("sample-app");
        var apps = new InMemoryApplicationRegistry();
        var revision = apps.Register(new(app, "Sample", "Fixture", []));
        var host = new InteractionInvocationHost(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), revision,
            "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));
        var result = await new ProcedureIntentAssociationPreparation(new(db, apps), new Targets(), new Permissions(true, true, false), new Changes(app))
            .PrepareAsync(host, new(app, new string('a', 32), 1, Hash("candidate")),
                new("sample-app.procedure.inspect", "procedure", 1, Hash("content")), ["inspect"]);
        Assert.Equal("INTENT_PREPARATION_UNAVAILABLE", result.Code);
        Assert.Null(result.Document);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class ReadModel(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ApplicationCandidateRevisionRecord>().HasKey(value => new { value.ApplicationId, value.CandidateId, value.Revision });
            model.Entity<ApplicationCandidateDocumentRecord>().HasKey(value => new { value.ApplicationId, value.CandidateId, value.Revision, value.Ordinal });
            model.Entity<ApplicationActivationDocumentIdentityRecord>().HasKey(value => value.Id);
            model.Entity<ApplicationActivationDocumentEvidenceRecord>().HasKey(value => new { value.IdentityId, value.EvidenceVersion });
        }
    }
    private sealed class Targets : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host, StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host, ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) => Task.FromResult(new StandingGrantTargetResolution(
                StandingGrantTargetResolutionStatus.Available, "FIXTURE", new(selection.DefinitionId, selection.Kind,
                    host.ApplicationRevision.ApplicationId, "sample-app.procedure", "fixture.owner", selection.Revision, selection.ContentFingerprint)));
    }
    private sealed class Permissions(bool read, bool author, bool revoke) : IStandingGrantPolicy
    {
        internal int Calls;
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host, StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            Calls++;
            var allowed = (requirement.Capability == StandingGrantCapability.Read ? read : author) && !(revoke && Calls > 2);
            var grant = new StandingGrantRevision(host.GrantReference, "fixture", 1, Hash("grant"), host.Principal.PrincipalId,
                host.ApplicationRevision.ApplicationId, StandingGrantScope.Application, null, [StandingGrantCapability.Read, StandingGrantCapability.Author],
                new(StandingGrantDefinitionMode.ExactIds, requirement.Definitions.Select(value => value.DefinitionId).ToArray(), []), [], 16,
                DateTime.UtcNow.AddHours(1), false, "fixture.issuance");
            return Task.FromResult(new StandingGrantDecision(allowed, "FIXTURE", allowed ? grant : null,
                new(host.Principal.PrincipalId, "fixture", "read", "application", host.CommandId, allowed, "fixture")));
        }
    }
    private sealed class Changes(ApplicationIdentifier app) : IApplicationDefinitionChangeReader
    {
        public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier id) => new(app, 1, Hash("active"), "operation.1", DateTime.UnixEpoch,
            new([], []), new(Hash("dependencies"), "fixture", true), new("rebuildable", false, false));
        public ApplicationDefinitionChange? RevisionChange(ApplicationIdentifier id, int revision) => CurrentChange(id);
        public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(ApplicationIdentifier id, int revision, int limit) => [];
    }
}

using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task One_file_authoring_and_recovery_preserve_a_large_generation_and_inspect_only_changed_content()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        for (var index = 0; index < 220; index++)
            File.WriteAllText(Path.Combine(root, "content", "procedures", $"unchanged-{index}.md"),
                ProcedureText("Unchanged fixture.").Replace("demo.runtime.inspect", $"demo.runtime.unchanged-{index}", StringComparison.Ordinal),
                new UTF8Encoding(false));
        await ActivateAsync(setup);
        var historical = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        var write = await service.WriteCandidateAsync(ApplicationHost(setup, "large-write", InteractionExecutionProfile.Atomic),
            Request(historical.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var metadata = await new ApplicationCandidateRetainedReader(db, setup.Applications)
            .ReadMetadataAsync(Application, row.CandidateId, row.Revision);
        Assert.Equal(221, metadata!.Documents.Count);
        var inspection = await service.InspectAsync(ApplicationHost(setup, "large-inspect"), new(null, 0, write.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Completed, inspection.Tag);
        using var inspected = JsonDocument.Parse(inspection.DataJson!);
        Assert.Equal(1, inspected.RootElement.GetProperty("documents").GetArrayLength());
        Assert.DoesNotContain("Unchanged fixture", inspection.DataJson);

        WriteProcedure("A later active change.");
        await ActivateAsync(setup, historical.ActivationFingerprint);
        var current = setup.Activation.Current(Application)!;
        var recovery = await service.RecoverAsync(ApplicationHost(setup, "large-recovery", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);
        Assert.Equal(InteractionInvocationResultTag.Committed, recovery.Tag);
        var recovered = await service.InspectAsync(ApplicationHost(setup, "large-recovery-inspect"), new(null, 0, recovery.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Completed, recovered.Tag);
        using var recoveredJson = JsonDocument.Parse(recovered.DataJson!);
        Assert.Equal(1, recoveredJson.RootElement.GetProperty("documents").GetArrayLength());
        Assert.Contains("Inspect it.", recovered.DataJson);
        Assert.Equal(current.ActivationFingerprint, setup.Activation.Current(Application)!.ActivationFingerprint);
    }

    [Fact]
    public async Task Cold_candidate_write_inspect_and_validate_never_read_an_unchanged_base_blob()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        for (var index = 0; index < 220; index++)
            File.WriteAllText(Path.Combine(root, "content", "procedures", $"cold-{index}.md"),
                ProcedureText("Unchanged cold fixture.").Replace("demo.runtime.inspect", $"demo.runtime.cold-{index}", StringComparison.Ordinal),
                new UTF8Encoding(false));
        await ActivateAsync(setup);
        var active = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate]);

        var writeEvidence = new ChangedOnlyEvidence(setup.Activation, "file:" + RelativePath);
        var write = await CandidateService(db, setup, writeEvidence).WriteCandidateAsync(
            ApplicationHost(setup, "cold-candidate-write", InteractionExecutionProfile.Atomic), Request(active.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);

        var inspectEvidence = new ChangedOnlyEvidence(setup.Activation, "file:" + RelativePath);
        var inspection = await CandidateService(db, setup, inspectEvidence).InspectAsync(
            ApplicationHost(setup, "cold-candidate-inspect"), new(row.CandidateId, row.Revision));
        var validationEvidence = new ChangedOnlyEvidence(setup.Activation, "file:" + RelativePath);
        var validation = await CandidateService(db, setup, validationEvidence).ValidateAsync(
            ApplicationHost(setup, "cold-candidate-validate", InteractionExecutionProfile.Atomic), candidate);

        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, inspection.Tag);
        Assert.Equal(InteractionInvocationResultTag.Committed, validation.Tag);
        Assert.Equal(0, writeEvidence.UnchangedReads);
        Assert.Equal(0, inspectEvidence.UnchangedReads);
        Assert.Equal(0, validationEvidence.UnchangedReads);
    }

    [Fact]
    public async Task Candidate_owner_proof_refuses_unchanged_targets_and_ambiguous_changed_ids()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        File.WriteAllText(Path.Combine(root, "content", "procedures", "unchanged-target.md"),
            ProcedureText("Unchanged target.").Replace("demo.runtime.inspect", "demo.runtime.unchanged", StringComparison.Ordinal),
            new UTF8Encoding(false));
        await ActivateAsync(setup);
        var active = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var service = Service(db, setup);
        var written = await service.WriteCandidateAsync(ApplicationHost(setup, "candidate-unchanged-write", InteractionExecutionProfile.Atomic),
            Request(active.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var unchangedDocument = active.Winners.Single(value => value.RelativePath == "content/procedures/unchanged-target.md");
        var unchanged = await setup.Resolver.ResolveCandidateReferenceAsync(ApplicationHost(setup, "candidate-unchanged-target"), candidate,
            new("demo.runtime.unchanged", CatalogNamespaceKinds.Procedure, 1, unchangedDocument.ContentFingerprint));

        var duplicate = await service.WriteCandidateAsync(ApplicationHost(setup, "candidate-duplicate-id", InteractionExecutionProfile.Atomic),
            Request(active.ActivationFingerprint) with
            {
                Documents = [
                    new("file:" + RelativePath, "catalog", RelativePath, "text/markdown", ProcedureText("First changed copy.")),
                    new("file:content/procedures/duplicate.md", "catalog", "content/procedures/duplicate.md", "text/markdown", ProcedureText("Second changed copy."))
                ]
            });

        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        Assert.Equal(StandingGrantTargetResolutionStatus.Unavailable, unchanged.Status);
        Assert.Equal("STANDING_GRANT_DEFINITION_UNAVAILABLE", unchanged.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, duplicate.Tag);
        Assert.Equal("STANDING_GRANT_DEFINITION_AMBIGUOUS", duplicate.Code);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    private static SqliteApplicationAuthoringService CandidateService(DantesRoleplayDbContext db, SetupState setup,
        ChangedOnlyEvidence evidence)
    {
        var catalog = new ActivatedApplicationCatalogMaterializer(setup.Applications, evidence, setup.Sources, setup.Roots, setup.Extensions);
        var targets = new SqliteStandingGrantTargetResolver(db, setup.Applications, evidence, evidence, setup.Sources,
            setup.Extensions, setup.Namespaces, catalog);
        return new(db, setup.Applications, evidence, evidence, setup.Sources, new SqliteStandingGrantPolicy(db, targets),
            targets, new OperationLog(db));
    }

    private sealed class ChangedOnlyEvidence(ApplicationActivationService inner, string changedIdentity) :
        IApplicationActivationReader, IActivatedApplicationEvidenceReader
    {
        public int UnchangedReads { get; private set; }

        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) => inner.Current(applicationId);

        public ActiveApplicationManifest? ReadRevision(ApplicationIdentifier applicationId, int activationRevision) =>
            inner.ReadRevision(applicationId, activationRevision);

        public ActivatedApplicationDocumentEvidence? ReadDocumentEvidence(ApplicationIdentifier applicationId,
            int activationRevision, string logicalIdentity)
        {
            if (logicalIdentity != changedIdentity)
            {
                UnchangedReads++;
                throw new ApplicationActivationException("UNEXPECTED_UNCHANGED_BASE_BLOB_READ",
                    "The cold candidate path attempted to read an unchanged retained base document.");
            }
            return inner.ReadDocumentEvidence(applicationId, activationRevision, logicalIdentity);
        }
    }
}

using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests : IDisposable
{
    private const string RelativePath = "content/procedures/inspect.md";
    private readonly SqliteFixture fixture = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), $"grant-target-resolver-{Guid.NewGuid():N}");
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("demo");

    [Fact]
    public async Task Current_procedure_resolves_from_the_active_retained_generation_with_its_normalized_catalog_hash()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);

        var current = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var target = Assert.IsType<StandingGrantDefinitionTarget>(current.Target);
        var exact = await setup.Resolver.ResolveAsync(Host(setup), Selection(target));
        var catalogRecord = Assert.Single(new ActivatedApplicationCatalogMaterializer(
            setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
            .Build(Application).Records);

        Assert.Equal(StandingGrantTargetResolutionStatus.Available, current.Status);
        Assert.Equal("demo.runtime.inspect", target.DefinitionId);
        Assert.Equal(Application, target.OwnerApplicationId);
        Assert.Equal("demo.runtime", target.NamespaceId);
        Assert.Equal(1, target.Revision);
        Assert.Equal(catalogRecord.ContentFingerprint, target.ContentFingerprint);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, exact.Status);
        Assert.Equal(target, exact.Target);
    }

    [Fact]
    public async Task Deleted_source_file_still_resolves_the_same_retained_target_and_proof()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var before = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        File.Delete(Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var after = await setup.Resolver.ResolveAsync(Host(setup), Selection(before));

        Assert.Equal(StandingGrantTargetResolutionStatus.Available, after.Status);
        Assert.Equal(before, after.Target);
    }

    [Fact]
    public async Task Incorrect_catalog_hash_is_denied()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var current = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);

        var result = await setup.Resolver.ResolveAsync(Host(setup), Selection(current) with { ContentFingerprint = new string('B', 64) });

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal("STANDING_GRANT_DEFINITION_STALE", result.Code);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unreviewed")]
    [InlineData("retired")]
    public async Task Namespace_drift_and_source_retirement_reject_a_previously_active_target(string drift)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var target = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        switch (drift)
        {
            case "disabled": setup.Namespaces.SetEnabled("demo.runtime", false); break;
            case "unreviewed": setup.Namespaces.SetReview("demo.runtime", CatalogNamespaceReviewStatuses.NeedsReview, "Review changed."); break;
            case "retired": setup.Sources.Retire(Application, "catalog", "Fixture source retired."); break;
        }

        var result = await setup.Resolver.ResolveAsync(Host(setup), Selection(target));

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal(drift == "retired" ? "STANDING_GRANT_SOURCE_DRIFT" : "STANDING_GRANT_NAMESPACE_UNREVIEWED", result.Code);
    }

    [Fact]
    public async Task Corrupt_retained_bytes_never_materialize_a_target()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var target = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync();
        evidence.RetainedBytes = Encoding.UTF8.GetBytes("corrupt retained evidence");
        await db.SaveChangesAsync();

        var result = await setup.Resolver.ResolveAsync(Host(setup), Selection(target));

        Assert.NotEqual(StandingGrantTargetResolutionStatus.Available, result.Status);
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal("STANDING_GRANT_RETAINED_BYTES_CORRUPT", result.Code);
    }

    [Fact]
    public async Task Declared_extension_source_is_unavailable_for_a_standing_grant()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db, extension: true);
        await ActivateAsync(setup);

        var result = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);

        Assert.Equal(StandingGrantTargetResolutionStatus.Unavailable, result.Status);
        Assert.Equal("STANDING_GRANT_BORROWED_DEFINITION_UNAVAILABLE", result.Code);
    }

    [Fact]
    public async Task Raw_local_id_is_not_an_alias_for_the_materialized_qualified_id()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);

        var qualified = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var local = await setup.Resolver.ResolveCurrentAsync(Host(setup), "runtime.inspect", CatalogNamespaceKinds.Procedure);

        Assert.Equal(StandingGrantTargetResolutionStatus.Available, qualified.Status);
        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, local.Status);
        Assert.Equal("STANDING_GRANT_DEFINITION_SCOPE", local.Code);
    }

    [Fact]
    public async Task Absent_durable_candidate_is_unavailable_even_when_its_snapshot_shape_looks_owner_materialized()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        var document = new ActivatedApplicationDocument("file:" + RelativePath, "catalog", SourceTrust.Trusted, 0,
            RelativePath, "text/markdown", new string('A', 64), 1, true);
        var candidate = new ApplicationCandidateSnapshot(
            new(Application, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1, new string('A', 64)), 1, new string('A', 64),
            null, "runtime", null, "Needed.", "grant@1", "operation", [new(document, [(byte)'x'])], []);
        var selection = new StandingGrantDefinitionReference("demo.runtime.inspect", CatalogNamespaceKinds.Procedure, 1, new string('A', 64));

        var result = await setup.Resolver.ResolveCandidateAsync(Host(setup), candidate, selection);

        Assert.Equal(StandingGrantTargetResolutionStatus.Unavailable, result.Status);
        Assert.Equal("STANDING_GRANT_CANDIDATE_UNAVAILABLE", result.Code);
    }

    [Fact]
    public async Task Historical_origin_resolves_its_exact_retained_generation_after_active_content_changes_and_source_deletion()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var old = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var oldTarget = Assert.IsType<StandingGrantDefinitionTarget>(old.Target);
        var origin = Assert.IsType<StandingGrantActivationOrigin>(old.CurrentActivation);
        WriteProcedure("Inspect the historical runtime definition.");
        await ActivateAsync(setup, origin.ActivationFingerprint);

        var current = await setup.Resolver.ResolveAsync(Host(setup), Selection(oldTarget));
        File.Delete(Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var retained = await setup.Resolver.ResolveRetainedAsync(Host(setup), origin, Selection(oldTarget));

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, current.Status);
        Assert.Equal("STANDING_GRANT_DEFINITION_STALE", current.Code);
        var target = Assert.IsType<StandingGrantDefinitionTarget>(retained.Target);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, retained.Status);
        Assert.Equal(oldTarget.DefinitionId, target.DefinitionId);
        Assert.Equal(origin, target.RetainedActivation);
        Assert.Null(retained.CurrentActivation);
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("unknown")]
    public async Task Forged_or_unknown_historical_origins_are_not_backfilled(string state)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var current = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var target = Assert.IsType<StandingGrantDefinitionTarget>(current.Target);
        var origin = Assert.IsType<StandingGrantActivationOrigin>(current.CurrentActivation);
        origin = state == "forged"
            ? origin with { ActivationFingerprint = new string('B', 64) }
            : origin with { ActivationRevision = 99 };

        var result = await setup.Resolver.ResolveRetainedAsync(Host(setup), origin, Selection(target));

        Assert.Equal(state == "forged" ? StandingGrantTargetResolutionStatus.Denied : StandingGrantTargetResolutionStatus.Unavailable, result.Status);
        Assert.Equal(state == "forged" ? "STANDING_GRANT_RETAINED_ORIGIN_STALE" : "STANDING_GRANT_RETAINED_GENERATION_UNAVAILABLE", result.Code);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("source")]
    public async Task Historical_origin_rechecks_namespace_and_source_retirement(string drift)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var current = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var target = Assert.IsType<StandingGrantDefinitionTarget>(current.Target);
        var origin = Assert.IsType<StandingGrantActivationOrigin>(current.CurrentActivation);
        if (drift == "namespace")
            setup.Namespaces.SetReview("demo.runtime", CatalogNamespaceReviewStatuses.NeedsReview, "Historical review withdrawn.");
        else
            setup.Sources.Retire(Application, "catalog", "Historical source retired.");

        var result = await setup.Resolver.ResolveRetainedAsync(Host(setup), origin, Selection(target));

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal(drift == "namespace" ? "STANDING_GRANT_NAMESPACE_UNREVIEWED" : "STANDING_GRANT_SOURCE_DRIFT", result.Code);
    }

    [Fact]
    public async Task Durable_candidate_ignores_spoofed_snapshot_content_and_survives_source_file_deletion()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var target = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        var candidate = await SeedCandidateAsync(db, setup);
        File.Delete(Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        var result = await setup.Resolver.ResolveCandidateAsync(Host(setup), candidate, Selection(target));

        var resolved = Assert.IsType<StandingGrantDefinitionTarget>(result.Target);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, result.Status);
        Assert.Equal(target.DefinitionId, resolved.DefinitionId);
        Assert.Equal(candidate.Candidate, resolved.Candidate);
    }

    [Fact]
    public async Task Candidate_fingerprint_tampering_is_not_accepted_from_a_snapshot()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var target = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        var candidate = await SeedCandidateAsync(db, setup);
        (await db.Set<ApplicationCandidateRevisionRecord>().SingleAsync()).NewImplementationReason = "tampered";
        await db.SaveChangesAsync();

        var result = await setup.Resolver.ResolveCandidateAsync(Host(setup), candidate, Selection(target));

        Assert.Equal(StandingGrantTargetResolutionStatus.Unavailable, result.Status);
        Assert.Equal("STANDING_GRANT_CANDIDATE_OWNER_UNAVAILABLE", result.Code);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("source")]
    public async Task Candidate_rechecks_namespace_and_source_registration_drift(string drift)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var target = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure)).Target);
        var candidate = await SeedCandidateAsync(db, setup);
        if (drift == "namespace")
            setup.Namespaces.SetEnabled("demo.runtime", false);
        else
            setup.Sources.Retire(Application, "catalog", "Fixture source retired.");

        var result = await setup.Resolver.ResolveCandidateAsync(Host(setup), candidate, Selection(target));

        Assert.Equal(StandingGrantTargetResolutionStatus.Denied, result.Status);
        Assert.Equal(drift == "namespace" ? "STANDING_GRANT_NAMESPACE_UNREVIEWED" : "STANDING_GRANT_SOURCE_DRIFT", result.Code);
    }

    [Fact]
    public async Task Candidate_authoring_creates_inspects_and_replays_without_activation()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var active = setup.Activation.Current(Application)!;
        var service = Service(db, setup);
        var request = Request(active.ActivationFingerprint);

        var created = await service.WriteCandidateAsync(Host(setup, "write", InteractionExecutionProfile.Atomic), request);
        Assert.Equal(InteractionInvocationResultTag.Committed, created.Tag);
        Assert.NotNull(created.Receipt);
        var stored = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var inspected = await service.InspectAsync(Host(setup, "inspect"), new(stored.CandidateId, stored.Revision));
        var replay = await service.WriteCandidateAsync(Host(setup, "write", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        Assert.Equal(InteractionInvocationResultTag.Committed, replay.Tag);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Single(await db.Operations.Where(x => x.Tool == "application-candidate").ToArrayAsync());
        Assert.Equal(active.ActivationFingerprint, setup.Activation.Current(Application)!.ActivationFingerprint);
    }

    [Fact]
    public async Task Candidate_created_from_a_commit_receipt_is_inspectable_by_its_source_operation_with_current_read_authority()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup);

        var created = await service.WriteCandidateAsync(Host(setup, "receipt-inspect", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.NotNull(created.Receipt);
        var receipt = created.Receipt!;
        var inspected = await service.InspectAsync(Host(setup, "receipt-read"), new(null, 0, receipt.OperationId));

        Assert.Equal(InteractionInvocationResultTag.Committed, created.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        Assert.NotNull(inspected.DataJson);
        Assert.Contains(receipt.OperationId, inspected.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_receipt_lookup_rejects_a_mixed_candidate_and_operation_selector()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        var created = await service.WriteCandidateAsync(Host(setup, "mixed-selector", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var stored = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());

        Assert.NotNull(created.Receipt);
        var inspected = await service.InspectAsync(Host(setup, "mixed-read"),
            new(stored.CandidateId, stored.Revision, created.Receipt!.OperationId));

        Assert.Equal(InteractionInvocationResultTag.Failed, inspected.Tag);
        Assert.Equal("INVALID_PAYLOAD", inspected.Code);
        Assert.Null(inspected.DataJson);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("read-absent")]
    public async Task Candidate_receipt_lookup_denies_content_when_current_read_authority_is_revoked_or_absent(string state)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        var created = await service.WriteCandidateAsync(Host(setup, "receipt-" + state, InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.NotNull(created.Receipt);
        var receipt = created.Receipt!;
        if (state == "revoked")
            await RevokeGrantAsync(db);
        else
            await ReplaceGrantCapabilitiesAsync(db, [StandingGrantCapability.Author]);

        var inspected = await service.InspectAsync(Host(setup, "receipt-denied-" + state,
            grantReference: "grant@2"), new(null, 0, receipt.OperationId));

        Assert.Equal(InteractionInvocationResultTag.Failed, inspected.Tag);
        Assert.Equal("STANDING_GRANT_DENIED", inspected.Code);
        Assert.Null(inspected.DataJson);
    }

    [Fact]
    public async Task Candidate_receipt_from_another_application_cannot_disclose_candidate_content()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        var created = await service.WriteCandidateAsync(Host(setup, "demo-receipt", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.NotNull(created.Receipt);
        var receipt = created.Receipt!;
        var other = ApplicationIdentifier.Parse("other");
        setup.Applications.Register(new(other, "Other", "Other fixture application.", []));
        var otherHost = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new ApplicationRevision(other, 1, new string('B', 64), []), "state", "grant@1", "other-receipt-read", "state@1",
            InteractionExecutionProfile.ReadOnly, new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

        var inspected = await service.InspectAsync(otherHost, new(null, 0, receipt.OperationId));

        Assert.Equal(InteractionInvocationResultTag.Failed, inspected.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_NOT_FOUND", inspected.Code);
        Assert.Null(inspected.DataJson);
    }

    [Theory]
    [InlineData("null-request")]
    [InlineData("null-documents")]
    [InlineData("maximum-revision")]
    [InlineData("blank-reason")]
    [InlineData("runtime-sync-evidence")]
    public async Task Invalid_candidate_write_payloads_leave_no_candidate_or_audit(string invalid)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var fingerprint = setup.Activation.Current(Application)!.ActivationFingerprint;
        ApplicationCandidateWriteRequest? request = invalid switch
        {
            "null-request" => null,
            "null-documents" => Request(fingerprint) with { Documents = null! },
            "maximum-revision" => Request(fingerprint) with
            {
                CandidateId = CandidateId("maximum-revision"),
                ExpectedCandidateRevision = int.MaxValue
            },
            "blank-reason" => Request(fingerprint) with { NewImplementationReason = "   " },
            "runtime-sync-evidence" => Request(fingerprint) with { SynchronizationEvidenceReference = "export@1" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        var result = await Service(db, setup).WriteCandidateAsync(
            Host(setup, "invalid-" + invalid, InteractionExecutionProfile.Atomic), request!);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVALID_PAYLOAD", result.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Operations.Where(x => x.Tool == "application-candidate").ToArrayAsync());
    }

    [Fact]
    public async Task Denied_candidate_authoring_leaves_no_candidate_or_audit()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Read]);
        var result = await Service(db, setup).WriteCandidateAsync(Host(setup, "denied", InteractionExecutionProfile.Atomic), Request(setup.Activation.Current(Application)!.ActivationFingerprint));

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Operations.Where(x => x.Tool == "application-candidate").ToArrayAsync());
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Null(db.Database.CurrentTransaction);
        var absent = await Service(db, setup).InspectAsync(Host(setup), new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 1));
        Assert.Equal(InteractionInvocationResultTag.Failed, absent.Tag);
        Assert.Null(absent.DataJson);
    }

    [Fact]
    public async Task Committed_candidate_replay_survives_a_later_active_generation()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup); await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var request = Request(setup.Activation.Current(Application)!.ActivationFingerprint);
        var service = Service(db, setup);
        var first = await service.WriteCandidateAsync(Host(setup, "stale-replay", InteractionExecutionProfile.Atomic), request);
        WriteProcedure("A later active generation.");
        await ActivateAsync(setup, setup.Activation.Current(Application)!.ActivationFingerprint);

        var replay = await service.WriteCandidateAsync(Host(setup, "stale-replay", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Changed_payload_conflicts_even_after_the_active_generation_changes()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup); await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var request = Request(setup.Activation.Current(Application)!.ActivationFingerprint);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "payload-conflict", InteractionExecutionProfile.Atomic), request);
        WriteProcedure("A later active generation.");
        await ActivateAsync(setup, setup.Activation.Current(Application)!.ActivationFingerprint);

        var conflict = await service.WriteCandidateAsync(Host(setup, "payload-conflict", InteractionExecutionProfile.Atomic), request with { NewImplementationReason = "Different payload." });

        Assert.Equal(InteractionInvocationResultTag.Failed, conflict.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_COMMAND_CONFLICT", conflict.Code);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task New_procedure_under_registered_source_glob_is_retained_and_a_sibling_escape_is_unavailable()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup); await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var service = Service(db, setup); var fingerprint = setup.Activation.Current(Application)!.ActivationFingerprint;
        var allowed = await service.WriteCandidateAsync(Host(setup, "new-path", InteractionExecutionProfile.Atomic), NewProcedureRequest(fingerprint, "content/procedures/new.md", "demo.runtime.new"));
        var stored = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var readable = await service.InspectAsync(Host(setup, "new-read"), new(stored.CandidateId, 1));
        var escaped = await service.WriteCandidateAsync(Host(setup, "escape", InteractionExecutionProfile.Atomic), NewProcedureRequest(fingerprint, "outside/escape.md", "demo.runtime.escape"));

        Assert.Equal(InteractionInvocationResultTag.Committed, allowed.Tag);
        Assert.Equal(InteractionInvocationResultTag.Completed, readable.Tag);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, escaped.Tag);
        Assert.Single(await db.Operations.Where(x => x.Tool == "application-candidate").ToArrayAsync());
    }

    [Fact]
    public async Task Candidate_revision_preserves_prior_retained_bytes_and_rejects_stale_revision()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup); await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var fingerprint = setup.Activation.Current(Application)!.ActivationFingerprint; var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "rev-one", InteractionExecutionProfile.Atomic), Request(fingerprint));
        var first = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var old = (await new ApplicationCandidateRetainedReader(db, setup.Applications).ReadAsync(Application, first.CandidateId, 1))!.Documents.Single().RetainedBytes;
        var secondRequest = Request(fingerprint) with { CandidateId = first.CandidateId, ExpectedCandidateRevision = 1, Documents = [new("file:" + RelativePath, "catalog", RelativePath, "text/markdown", ProcedureText("Revision two."))] };
        var second = await service.WriteCandidateAsync(Host(setup, "rev-two", InteractionExecutionProfile.Atomic), secondRequest);
        var stale = await service.WriteCandidateAsync(Host(setup, "rev-three", InteractionExecutionProfile.Atomic), secondRequest);
        var retainedOld = (await new ApplicationCandidateRetainedReader(db, setup.Applications).ReadAsync(Application, first.CandidateId, 1))!.Documents.Single().RetainedBytes;

        Assert.Equal(InteractionInvocationResultTag.Committed, second.Tag);
        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_CAS_MISMATCH", stale.Code);
        Assert.Equal(old, retainedOld);
        Assert.Equal(2, await db.Set<ApplicationCandidateRevisionRecord>().CountAsync());
    }

    [Fact]
    public async Task Revoked_current_grant_denies_a_later_candidate_replay()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup); await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var request = Request(setup.Activation.Current(Application)!.ActivationFingerprint); var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(Host(setup, "revoked-replay", InteractionExecutionProfile.Atomic), request);
        await RevokeGrantAsync(db);

        var replay = await service.WriteCandidateAsync(Host(setup, "revoked-replay", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Failed, replay.Tag);
        Assert.Equal("STANDING_GRANT_NOT_CURRENT", replay.Code);
    }

    private SetupState Setup(DantesRoleplayDbContext db, bool extension = false)
    {
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(Application, "Demo", "Standing grant resolver fixture.", []));
        var sources = new SqliteSourceRegistry(db);
        sources.Register(new(Application, "catalog", "fixture-root", "content/**/*", SourceTrust.Trusted, 0, "fixture-catalog"));
        var extensions = new SqliteApplicationExtensionRegistry(db, sources);
        if (extension)
            extensions.Register(new(Application, "fixture-extension", "Fixture extension", "Fixture source is declared as an extension.",
                ApplicationExtensionClassifications.Homebrew, ["catalog"], ["demo.runtime"], [], [], [], false));
        var namespaces = new SqliteCatalogNamespaceRegistry(db);
        namespaces.Register(new CatalogNamespaceRegistration("demo", "human-domain-label", "Demo root namespace.", [CatalogNamespaceKinds.Procedure],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        namespaces.Register(new CatalogNamespaceRegistration("demo.runtime", "human-domain-label", "Demo runtime namespace.", [CatalogNamespaceKinds.Procedure],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed, ReviewNote: "Reviewed fixture."));
        WriteProcedure();
        var roots = new Root(root);
        var previews = new ApplicationPreviewService(applications, sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()), new SourceOverlayResolver());
        var activation = new ApplicationActivationService(db, previews, extensions, sources, roots,
            new ProjectionImpactService(applications, new SqliteProjectionImpactSnapshotReader(db)), new OperationLog(db));
        return new(applications, sources, extensions, namespaces, roots, activation,
            new SqliteStandingGrantTargetResolver(db, applications, activation, activation, sources, extensions, namespaces,
                new ActivatedApplicationCatalogMaterializer(applications, activation, sources, roots, extensions)
                    .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority())));
    }

    private async Task ActivateAsync(SetupState setup, string? expectedActiveFingerprint = null)
    {
        var preview = await new ApplicationPreviewService(setup.Applications, setup.Sources,
            new RegisteredSourceScanner(setup.Sources, new Root(root), new LocalDocumentScanner()), new SourceOverlayResolver())
            .PreviewAsync(Application);
        Assert.True(preview.IsValid, string.Join(';', preview.Problems.Select(problem => problem.Code)));
        var request = new ApplicationActivationRequest(Application, preview.PreviewFingerprint, expectedActiveFingerprint);
        var context = new ApplicationActivationContext(Guid.NewGuid().ToString("N"), "Activate resolver fixture.", ["procedure.system.use"],
            new AuthorizationAuditEvidence("principal." + new string('a', 64), "test", "modify", "system.private-host",
                "resolver-fixture", true, "PRIVATE_OPERATOR_ALLOWED"));
        await setup.Activation.PreviewAsync(request, context);
        _ = await setup.Activation.ActivateAsync(request, context);
    }

    private static async Task<ApplicationCandidateSnapshot> SeedCandidateAsync(
        DantesRoleplayDbContext db, SetupState setup)
    {
        var activation = setup.Activation.Current(Application)!;
        var documents = activation.Winners.Select(document => new ApplicationCandidateDocument(document,
            setup.Activation.ReadDocumentEvidence(Application, activation.ActivationRevision, document.LogicalIdentity)!
                .RetainedBytes!)).ToArray();
        var retained = new ApplicationRetainedDocumentStore(db);
        var links = await retained.RetainAsync(Application, activation.Winners,
            documents.ToDictionary(value => value.Document.LogicalIdentity, value => value.RetainedBytes, StringComparer.Ordinal),
            CancellationToken.None);
        const string candidateId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        db.Add(new Operation { Id = "candidate-source-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        var row = new ApplicationCandidateRevisionRecord
        {
            ApplicationId = Application.Value,
            CandidateId = candidateId,
            Revision = 1,
            ApplicationRevision = activation.ApplicationRevision,
            ContentFingerprint = new string('0', 64),
            Origin = "runtime",
            NewImplementationReason = "Fixture candidate.",
            AuthorGrantReference = "grant@1",
            SourceOperationId = "candidate-source-operation",
            CanonicalCommandFingerprint = new string('A', 64)
        };
        row.ContentFingerprint = ApplicationCandidateRetainedReader.ContentFingerprint(row,
            setup.Applications.Get(Application, activation.ApplicationRevision)!, documents);
        db.Add(row);
        foreach (var link in links)
            db.Add(new ApplicationCandidateDocumentRecord
            {
                ApplicationId = Application.Value,
                CandidateId = candidateId,
                Revision = 1,
                Ordinal = link.Ordinal,
                IdentityId = link.IdentityId,
                EvidenceVersion = link.EvidenceVersion
            });
        await db.SaveChangesAsync();

        var spoofed = documents[0] with { Document = documents[0].Document with { ContentFingerprint = new string('B', 64) } };
        return new(new(Application, candidateId, 1, row.ContentFingerprint), 999, new string('B', 64),
            new string('B', 64), "spoofed", "spoofed", "Spoofed reason.", "spoofed", "spoofed", [spoofed],
            [new("demo.runtime.inspect", 99, new string('B', 64))]);
    }

    private void WriteProcedure(string instruction = "Inspect it.")
    {
        var path = Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            ---
            id: demo.runtime.inspect
            category: runtime.inspect
            name: Inspect runtime
            governs: query(kind: "runtime.inspect")
            status: active
            ---

            ## Description
            Inspect the active runtime definition.

            ## Instructions
            1. {{instruction}}

            ## Constraints
            - Preserve it.
            """, new UTF8Encoding(false));
    }

    private static InteractionInvocationHost Host(SetupState setup, string command = "command", InteractionExecutionProfile profile = InteractionExecutionProfile.ReadOnly,
        string grantReference = "grant@1") => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []), "state", grantReference, command, "state@1",
        profile, new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

    private static string CandidateId(string command) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
        "dantes-roleplay/application-candidate/candidate/v1\nprincipal." + new string('a', 64) + "\ndemo\n" + command)))[..32].ToLowerInvariant();

    private static ApplicationCandidateWriteRequest Request(string fingerprint) => new(null, 0, fingerprint, "runtime", null,
        "Fixture change.", [new("file:" + RelativePath, "catalog", RelativePath, "text/markdown", ProcedureText("Changed by candidate."))]);

    private static ApplicationCandidateWriteRequest NewProcedureRequest(string fingerprint, string path, string id) => new(null, 0, fingerprint, "runtime", null,
        "New fixture procedure.", [new("file:" + path, "catalog", path, "text/markdown", ProcedureText("New procedure.").Replace("demo.runtime.inspect", id, StringComparison.Ordinal))]);

    private static string ProcedureText(string instruction) => $$"""
        ---
        id: demo.runtime.inspect
        category: runtime.inspect
        name: Inspect runtime
        governs: query(kind: "runtime.inspect")
        status: active
        ---

        ## Description
        Inspect the active runtime definition.

        ## Instructions
        1. {{instruction}}

        ## Constraints
        - Preserve it.
        """;

    private static SqliteApplicationAuthoringService Service(DantesRoleplayDbContext db, SetupState setup)
    {
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        return new(db, setup.Applications, setup.Activation, setup.Activation, setup.Sources, policy, setup.Resolver, new OperationLog(db));
    }

    private static async Task SeedGrantAsync(DantesRoleplayDbContext db, IReadOnlyList<StandingGrantCapability> capabilities)
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, new string('0', 64), "principal." + new string('a', 64), Application,
            StandingGrantScope.Application, null, capabilities,
            new(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.runtime", true, [CatalogNamespaceKinds.Procedure])]), [], 1, DateTime.UtcNow.AddMinutes(10), false, "grant-operation");
        db.Add(new Operation { Id = "grant-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant), ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc, Revoked = false, IssuedByOperationId = "grant-operation" });
        db.Add(new StandingGrantCurrentRecord { GrantId = "grant", Revision = 1 });
        await db.SaveChangesAsync();
    }

    private static async Task RevokeGrantAsync(DantesRoleplayDbContext db)
    {
        var prior = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>().SingleAsync(x => x.GrantId == "grant"));
        var revoked = prior with { Revision = 2, GrantReference = "grant@2", Revoked = true, IssuedByOperationId = "grant-revoke" };
        db.Add(new Operation { Id = "grant-revoke", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = revoked.GrantId, Revision = revoked.Revision, GrantReference = revoked.GrantReference,
            PrincipalReference = revoked.PrincipalReference, ApplicationId = revoked.ApplicationId.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(revoked), ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(revoked),
            MaximumOperations = revoked.MaximumOperations, ExpiresAtUtc = revoked.ExpiresAtUtc, Revoked = true, IssuedByOperationId = "grant-revoke" });
        (await db.Set<StandingGrantCurrentRecord>().SingleAsync(x => x.GrantId == "grant")).Revision = 2;
        await db.SaveChangesAsync();
    }

    private static async Task ReplaceGrantCapabilitiesAsync(
        DantesRoleplayDbContext db, IReadOnlyList<StandingGrantCapability> capabilities)
    {
        var prior = SqliteStandingGrantPolicy.Parse(await db.Set<StandingGrantRevisionRecord>()
            .SingleAsync(x => x.GrantId == "grant" && x.Revision == 1));
        var replacement = prior with
        {
            Revision = 2,
            GrantReference = "grant@2",
            Capabilities = capabilities,
            IssuedByOperationId = "grant-capability-replace"
        };
        db.Add(new Operation { Id = replacement.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord { GrantId = replacement.GrantId, Revision = replacement.Revision,
            GrantReference = replacement.GrantReference, PrincipalReference = replacement.PrincipalReference,
            ApplicationId = replacement.ApplicationId.Value, Scope = "application", StateSpaceId = null,
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(replacement),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(replacement),
            MaximumOperations = replacement.MaximumOperations, ExpiresAtUtc = replacement.ExpiresAtUtc,
            Revoked = false, IssuedByOperationId = replacement.IssuedByOperationId });
        (await db.Set<StandingGrantCurrentRecord>().SingleAsync(x => x.GrantId == "grant")).Revision = 2;
        await db.SaveChangesAsync();
    }

    private static StandingGrantDefinitionReference Selection(StandingGrantDefinitionTarget target) =>
        new(target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint);

    public void Dispose()
    {
        fixture.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed record SetupState(
        SqliteApplicationRegistry Applications,
        SqliteSourceRegistry Sources,
        SqliteApplicationExtensionRegistry Extensions,
        SqliteCatalogNamespaceRegistry Namespaces,
        Root Roots,
        ApplicationActivationService Activation,
        SqliteStandingGrantTargetResolver Resolver);

    private sealed class Root(string value) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = value;
            return allowedRootId == "fixture-root";
        }
    }
}

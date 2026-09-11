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

    private static InteractionInvocationHost Host(SetupState setup) => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new ApplicationRevision(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []), "state", "grant@1", "command", "state@1",
        InteractionExecutionProfile.ReadOnly, new InteractionInvocationBudget(1, DateTime.UtcNow.AddMinutes(1)));

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

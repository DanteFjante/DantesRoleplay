using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class TrailSurvivalApplicationSeamTests
{
    private const string ApplicationId = "trail-survival";
    private const string SourceId = "trail-survival-core";
    private const string SourceGlob = "catalog/applications/trail-survival/**/*";
    private const string ProcedurePath =
        "catalog/applications/trail-survival/procedures/application/procedure.trail-survival.about.md";
    private const string DndProcedurePath =
        "catalog/procedures/game/core/world/procedure.game.core.world.time.md";

    [Fact]
    public async Task Real_source_previews_activates_materializes_replays_and_binds_isolated_state()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var application = DantesRoleplay.Applications.ApplicationIdentifier.Parse(ApplicationId);
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(
            application,
            "Trail Survival",
            "Original customizable single-player trail-survival application.",
            []));
        Assert.Empty(revision.BaseApplications);

        var sources = new SqliteSourceRegistry(db);
        sources.Register(new(
            application,
            SourceId,
            "workspace",
            SourceGlob,
            SourceTrust.Trusted,
            0,
            "trail-survival-core-catalog"));
        var roots = new WorkspaceRoot(RepositoryRoot());
        var previewService = new ApplicationPreviewService(
            applications,
            sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
            new SourceOverlayResolver());

        var firstPreview = await previewService.PreviewAsync(application);
        var repeatedPreview = await previewService.PreviewAsync(application);
        Assert.True(firstPreview.IsValid);
        Assert.Equal(firstPreview.PreviewFingerprint, repeatedPreview.PreviewFingerprint);
        Assert.Equal(firstPreview.Winners, repeatedPreview.Winners);
        var winner = Assert.Single(firstPreview.Winners, value => value.RelativePath == ProcedurePath);
        Assert.Equal(ProcedurePath, winner.RelativePath);
        Assert.Equal(SourceId, winner.SourceId);
        Assert.DoesNotContain("dnd2024", winner.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/components/", winner.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/mechanics/", winner.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/world/", winner.RelativePath, StringComparison.Ordinal);

        var authoredPath = Path.Combine(
            RepositoryRoot(),
            ProcedurePath.Replace('/', Path.DirectorySeparatorChar));
        var procedure = ProcedureFile.Parse(await File.ReadAllTextAsync(authoredPath), ProcedurePath);
        Assert.Equal("procedure.trail-survival.about", procedure.Id);
        Assert.Equal("trail-survival.application", procedure.Category);

        var activationService = new ApplicationActivationService(
            db,
            previewService,
            new EmptyImpact(application),
            new OperationLog(db));
        var activationRequest = new ApplicationActivationRequest(
            application,
            firstPreview.PreviewFingerprint,
            null);
        var activationContext = Context("0123456789abcdef0123456789abcdef");
        var dryRun = await activationService.PreviewAsync(activationRequest, activationContext);
        Assert.Equal("would-activate", dryRun.Outcome);
        var activated = await activationService.ActivateAsync(activationRequest, activationContext);
        var replay = await activationService.ActivateAsync(activationRequest, activationContext);
        Assert.Equal("activated", activated.Outcome);
        Assert.Equal(activated.OperationId, replay.OperationId);
        Assert.Equal(activated.Activation.ActivationFingerprint, replay.Activation.ActivationFingerprint);
        Assert.Equal(1, replay.Activation.ActivationRevision);
        Assert.Contains(replay.Activation.Winners, value => value.RelativePath == ProcedurePath);

        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications,
            activationService,
            sources,
            roots);
        var manifest = materializer.Build(application);
        var record = Assert.Single(manifest.Records, value =>
            value.QualifiedId == "trail-survival.procedure.trail-survival.about");
        Assert.Equal("procedure", record.Kind);
        Assert.Equal("trail-survival.procedure.trail-survival.about", record.QualifiedId);
        Assert.Equal(SourceId, record.SourceId);
        Assert.Equal(ProcedurePath, record.SourceLogicalPath);
        var navigator = new InMemoryCatalogNavigator(
            manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes(
                "trail-survival-catalog-cursor-test-key")));
        var inspected = navigator.Inspect(new(
            application,
            ApplicationId,
            record.QualifiedId));
        Assert.Contains("declares no playable scenario", inspected.ContentJson, StringComparison.Ordinal);

        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        var stateSpace = stateSpaces.Create(new(
            "trail-survival-seam",
            revision,
            activated.Activation.ActivationFingerprint));
        Assert.Equal(ApplicationId, stateSpace.ApplicationRevision.ApplicationId.Value);
        Assert.Equal(
            activated.Activation.ActivationFingerprint,
            stateSpaces.Get("trail-survival-seam")!.ManifestFingerprint);
        Assert.Single(stateSpaces.ListPage(application, null, 100).StateSpaces);

        var otherApplication = DantesRoleplay.Applications.ApplicationIdentifier.Parse("dnd2024");
        applications.Register(new(otherApplication, "D&D 2024", "Isolation fixture only.", []));
        Assert.Empty(stateSpaces.ListPage(otherApplication, null, 100).StateSpaces);
    }

    [Fact]
    public async Task Unknown_allowed_root_cannot_activate_or_create_state()
    {
        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var application = DantesRoleplay.Applications.ApplicationIdentifier.Parse(ApplicationId);
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(
            application,
            "Trail Survival",
            "Original customizable single-player trail-survival application.",
            []));
        var sources = new SqliteSourceRegistry(db);
        sources.Register(new(
            application,
            SourceId,
            "missing-workspace",
            SourceGlob,
            SourceTrust.Trusted,
            0,
            "trail-survival-core-catalog"));
        var previewService = new ApplicationPreviewService(
            applications,
            sources,
            new RegisteredSourceScanner(
                sources,
                new EmptyAllowedSourceRootResolver(),
                new LocalDocumentScanner()),
            new SourceOverlayResolver());

        var preview = await previewService.PreviewAsync(application);
        Assert.False(preview.IsValid);
        Assert.Equal("SOURCE_ROOT_UNKNOWN", Assert.Single(preview.Problems).Code);
        Assert.Empty(preview.Winners);

        var activationService = new ApplicationActivationService(
            db,
            previewService,
            new EmptyImpact(application),
            new OperationLog(db));
        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            activationService.PreviewAsync(
                new(application, preview.PreviewFingerprint, null),
                Context("1123456789abcdef0123456789abcdef")));
        Assert.Equal("PREVIEW_INVALID", exception.Code);
        Assert.Null(activationService.Current(application));
        Assert.Empty(new SqliteStateSpaceRegistry(db, applications)
            .ListPage(application, null, 100).StateSpaces);
    }

    [Fact]
    public async Task Zero_application_host_and_two_independent_applications_remain_isolated()
    {
        using (var emptyFixture = new SqliteFixture())
        await using (var emptyDb = emptyFixture.CreateContext())
        {
            Assert.Empty(new SqliteApplicationRegistry(emptyDb).ListPage(null, 100).Applications);
        }

        using var fixture = new SqliteFixture();
        await using var db = fixture.CreateContext();
        var trail = ApplicationIdentifier.Parse(ApplicationId);
        var dnd = ApplicationIdentifier.Parse("dnd2024");
        var applications = new SqliteApplicationRegistry(db);
        var trailRevision = applications.Register(new(
            trail,
            "Trail Survival",
            "Original customizable single-player trail-survival application.",
            []));
        var dndRevision = applications.Register(new(
            dnd,
            "D&D 2024",
            "Independent application isolation witness.",
            []));
        Assert.Empty(trailRevision.BaseApplications);
        Assert.Empty(dndRevision.BaseApplications);
        Assert.Equal(["dnd2024", ApplicationId], applications.ListPage(null, 100).Applications
            .Select(value => value.Id.Value));

        var sources = new SqliteSourceRegistry(db);
        sources.Register(new(
            trail,
            SourceId,
            "workspace",
            SourceGlob,
            SourceTrust.Trusted,
            0,
            "trail-survival-core-catalog"));
        sources.Register(new(
            dnd,
            "dnd2024-world-time",
            "workspace",
            DndProcedurePath,
            SourceTrust.Trusted,
            0,
            "dnd2024-world-time-catalog"));
        var roots = new WorkspaceRoot(RepositoryRoot());
        var previewService = new ApplicationPreviewService(
            applications,
            sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
            new SourceOverlayResolver());
        var trailPreview = await previewService.PreviewAsync(trail);
        var dndPreview = await previewService.PreviewAsync(dnd);
        Assert.True(trailPreview.IsValid);
        Assert.True(dndPreview.IsValid);
        Assert.Contains(trailPreview.Winners, value => value.RelativePath == ProcedurePath);
        Assert.Equal(DndProcedurePath, Assert.Single(dndPreview.Winners).RelativePath);

        var activationService = new ApplicationActivationService(
            db,
            previewService,
            new EmptyImpact(trail, dnd),
            new OperationLog(db));
        var trailActivated = await ActivateAsync(
            activationService,
            trail,
            trailPreview,
            "2123456789abcdef0123456789abcdef");
        var dndActivated = await ActivateAsync(
            activationService,
            dnd,
            dndPreview,
            "3123456789abcdef0123456789abcdef");

        var materializer = new ActivatedApplicationCatalogMaterializer(
            applications,
            activationService,
            sources,
            roots);
        var trailManifest = materializer.Build(trail);
        var dndManifest = materializer.Build(dnd);
        var trailRecord = Assert.Single(trailManifest.Records, value =>
            value.QualifiedId == "trail-survival.procedure.trail-survival.about");
        var dndRecord = Assert.Single(dndManifest.Records);
        Assert.Equal("trail-survival.procedure.trail-survival.about", trailRecord.QualifiedId);
        Assert.Equal("dnd2024.procedure.game.core.world.time", dndRecord.QualifiedId);
        Assert.Equal(ProcedurePath, trailRecord.SourceLogicalPath);
        Assert.Equal(DndProcedurePath, dndRecord.SourceLogicalPath);

        var trailNavigator = new InMemoryCatalogNavigator(
            trailManifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes(
                "trail-survival-two-application-cursor-key")));
        var dndNavigator = new InMemoryCatalogNavigator(
            dndManifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes(
                "dnd2024-two-application-cursor-key")));
        Assert.Throws<ArgumentException>(() => trailNavigator.ListCollections(dnd));
        Assert.Throws<ArgumentException>(() => dndNavigator.ListCollections(trail));
        Assert.Throws<ArgumentException>(() => trailNavigator.Inspect(new(
            trail,
            ApplicationId,
            dndRecord.QualifiedId)));
        Assert.Throws<ArgumentException>(() => dndNavigator.Inspect(new(
            dnd,
            "dnd2024",
            trailRecord.QualifiedId)));

        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new(
            "trail-survival-isolation",
            trailRevision,
            trailActivated.Activation.ActivationFingerprint));
        stateSpaces.Create(new(
            "dnd2024-isolation",
            dndRevision,
            dndActivated.Activation.ActivationFingerprint));
        Assert.Equal(
            ["trail-survival-isolation"],
            stateSpaces.ListPage(trail, null, 100).StateSpaces.Select(value => value.StateSpaceId));
        Assert.Equal(
            ["dnd2024-isolation"],
            stateSpaces.ListPage(dnd, null, 100).StateSpaces.Select(value => value.StateSpaceId));
        Assert.Empty(await db.Set<ApplicationEcsEntityRecord>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<ApplicationEcsComponentRecord>().AsNoTracking().ToListAsync());
    }

    private static async Task<ApplicationActivationReceipt> ActivateAsync(
        ApplicationActivationService service,
        ApplicationIdentifier application,
        ApplicationPreviewResult preview,
        string requestToken)
    {
        var request = new ApplicationActivationRequest(
            application,
            preview.PreviewFingerprint,
            null);
        Assert.Equal("would-activate", (await service.PreviewAsync(request, Context(requestToken))).Outcome);
        return await service.ActivateAsync(request, Context(requestToken));
    }

    private static ApplicationActivationContext Context(string requestToken) => new(
        requestToken,
        "Activate the exact Trail Survival application source in disposable test state.",
        ["procedure.system.use"],
        new AuthorizationAuditEvidence(
            "principal." + new string('a', 64),
            "test",
            "modify",
            "system.private-host",
            "trail-survival-seam",
            true,
            "PRIVATE_OPERATOR_ALLOWED"));

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DANTES_ROLEPLAY_TEST_REPOSITORY_ROOT");
        foreach (var start in new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                     .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class WorkspaceRoot(string repositoryRoot) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = allowedRootId == "workspace" ? repositoryRoot : "";
            return canonicalPath.Length > 0;
        }
    }

    private sealed class EmptyImpact(params ApplicationIdentifier[] applications) : IProjectionImpactService
    {
        private readonly HashSet<ApplicationIdentifier> _applications = applications.ToHashSet();

        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true)
        {
            Assert.Contains(applicationId, _applications);
            Assert.Null(rootId);
            return new(
                applicationId,
                new string('F', 64),
                null,
                transitive,
                [],
                [],
                []);
        }
    }
}

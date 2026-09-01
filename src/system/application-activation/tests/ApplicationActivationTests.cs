using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation.Tests;

public sealed class ApplicationActivationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Exact_dry_run_activation_and_replay_retain_one_redacted_manifest()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "activation-app");
        var preview = new MutablePreview(Result(app, 'F'));
        var service = Service(db, preview);
        var request = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, null);
        var context = Context("0123456789abcdef0123456789abcdef");

        var noDryRun = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.ActivateAsync(request, context));
        Assert.Equal("DRY_RUN_REQUIRED", noDryRun.Code);
        Assert.Null(service.Current(app));

        var dryRun = await service.PreviewAsync(request, context);
        Assert.Equal("would-activate", dryRun.Outcome);
        Assert.Null(service.Current(app));
        var activated = await service.ActivateAsync(request, context);
        preview.Result = Result(app, 'A');
        var replay = await service.ActivateAsync(request, context);

        Assert.Equal("activated", activated.Outcome);
        Assert.Equal(activated.OperationId, replay.OperationId);
        Assert.Equal(activated.Outcome, replay.Outcome);
        Assert.Equal(activated.Activation.ActivationFingerprint, replay.Activation.ActivationFingerprint);
        Assert.Equal(activated.Activation.Sources, replay.Activation.Sources);
        Assert.Equal(activated.Activation.Winners, replay.Activation.Winners);
        Assert.Equal(1, activated.Activation.ActivationRevision);
        Assert.False(activated.Activation.DependencyCoverageComplete);
        Assert.Equal("catalog/entry.json", Assert.Single(activated.Activation.Winners).RelativePath);
        Assert.Equal(activated.Activation.ActivationFingerprint,
            service.Current(app)!.ActivationFingerprint);
        Assert.Equal(2, await db.Operations.CountAsync());

        var conflict = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.ActivateAsync(request with { PreviewFingerprint = new string('A', 64) }, context));
        Assert.Equal("REQUEST_TOKEN_CONFLICT", conflict.Code);

        preview.Result = Result(app, 'F');
        var unchangedRequest = request with
        {
            ExpectedActiveFingerprint = activated.Activation.ActivationFingerprint
        };
        var unchangedContext = Context("8123456789abcdef0123456789abcdef");
        Assert.Equal("unchanged", (await service.PreviewAsync(unchangedRequest, unchangedContext)).Outcome);
        var unchanged = await service.ActivateAsync(unchangedRequest, unchangedContext);
        Assert.Equal("unchanged", unchanged.Outcome);
        Assert.Equal(1, unchanged.Activation.ActivationRevision);
        Assert.Equal(4, await db.Operations.CountAsync());
    }

    [Fact]
    public async Task Source_profile_selection_is_canonical_replay_identity_and_rejects_ambiguous_lists()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "profile-activation");
        var preview = new MutablePreview(Result(app, 'F'));
        var service = Service(db, preview);
        var context = Context("9123456789abcdef0123456789abcdef");
        var request = new ApplicationActivationRequest(
            app, preview.Result.PreviewFingerprint, null, ["dnd2024-core", "extension.optional"]);

        await service.PreviewAsync(request, context);
        var activated = await service.ActivateAsync(request, context);
        var replay = await service.ActivateAsync(request with
        {
            SourceIds = ["extension.optional", "dnd2024-core"]
        }, context);

        Assert.Equal("activated", activated.Outcome);
        Assert.Equal(activated.OperationId, replay.OperationId);
        Assert.Equal(activated.Activation.ActivationFingerprint,
            replay.Activation.ActivationFingerprint);
        var conflict = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.ActivateAsync(request with { SourceIds = ["dnd2024-core"] }, context));
        var duplicate = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.PreviewAsync(request with { SourceIds = ["dnd2024-core", "dnd2024-core"] },
                Context("a123456789abcdef0123456789abcdef")));
        Assert.Equal("REQUEST_TOKEN_CONFLICT", conflict.Code);
        Assert.Equal("INVALID_PAYLOAD", duplicate.Code);
    }

    [Fact]
    public async Task Reviewed_base_sources_can_be_activated_with_an_explicit_extension_set()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "mixed-profile-activation");
        var preview = new MutablePreview(Result(app, 'F'));
        var service = Service(db, preview);
        var request = new ApplicationActivationRequest(
            app, preview.Result.PreviewFingerprint, null, ["reviewed-core"])
        {
            ExtensionIds = []
        };

        var dryRun = await service.PreviewAsync(
            request, Context("b123456789abcdef0123456789abcdef"));

        Assert.Equal("would-activate", dryRun.Outcome);
        Assert.Equal(1, preview.MixedPreviewCount);
    }

    [Fact]
    public async Task Preview_drift_or_stale_active_expectation_changes_nothing()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "stale-activation");
        var preview = new MutablePreview(Result(app, 'E'));
        var service = Service(db, preview);
        var initialRequest = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, null);
        var initialContext = Context("1123456789abcdef0123456789abcdef");
        await service.PreviewAsync(initialRequest, initialContext);
        preview.Result = Result(app, 'D');

        var drift = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.ActivateAsync(initialRequest, initialContext));
        Assert.Equal("PREVIEW_STALE", drift.Code);
        Assert.Null(service.Current(app));

        var currentRequest = initialRequest with { PreviewFingerprint = preview.Result.PreviewFingerprint };
        var currentContext = Context("2123456789abcdef0123456789abcdef");
        await service.PreviewAsync(currentRequest, currentContext);
        var first = await service.ActivateAsync(currentRequest, currentContext);
        preview.Result = Result(app, 'C');
        var staleRequest = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, null);
        var staleContext = Context("3123456789abcdef0123456789abcdef");

        var stale = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.PreviewAsync(staleRequest, staleContext));
        Assert.Equal("ACTIVATION_STALE", stale.Code);
        Assert.Equal(first.Activation.ActivationFingerprint, service.Current(app)!.ActivationFingerprint);
    }

    [Fact]
    public async Task Exact_current_expectation_appends_revision_and_audit_failure_rolls_back()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "revision-activation");
        var preview = new MutablePreview(Result(app, 'A'));
        var service = Service(db, preview);
        var firstRequest = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, null);
        var firstContext = Context("4123456789abcdef0123456789abcdef");
        await service.PreviewAsync(firstRequest, firstContext);
        var first = await service.ActivateAsync(firstRequest, firstContext);

        preview.Result = Result(app, 'B');
        var secondRequest = new ApplicationActivationRequest(
            app, preview.Result.PreviewFingerprint, first.Activation.ActivationFingerprint);
        var secondContext = Context("5123456789abcdef0123456789abcdef");
        await service.PreviewAsync(secondRequest, secondContext);
        var failing = new ApplicationActivationService(db, preview, new StaticImpact(app), new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.ActivateAsync(secondRequest, secondContext));
        Assert.Equal(first.Activation.ActivationFingerprint, service.Current(app)!.ActivationFingerprint);
        Assert.Null(await new OperationLog(db).GetAsync(secondContext.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());

        var second = await service.ActivateAsync(secondRequest, secondContext);
        Assert.Equal(2, second.Activation.ActivationRevision);
        Assert.NotEqual(first.Activation.ActivationFingerprint, second.Activation.ActivationFingerprint);
    }

    [Fact]
    public async Task Dependency_graph_drift_after_dry_run_requires_a_new_exact_dry_run()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "dependency-drift");
        var preview = new MutablePreview(Result(app, 'F'));
        var impacts = new MutableImpact(app, new string('A', 64));
        var service = new ApplicationActivationService(db, preview, impacts, new OperationLog(db));
        var request = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, null);
        var context = Context("7123456789abcdef0123456789abcdef");
        await service.PreviewAsync(request, context);
        impacts.Fingerprint = new string('B', 64);

        var stale = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            service.ActivateAsync(request, context));

        Assert.Equal("DRY_RUN_STALE", stale.Code);
        Assert.Null(service.Current(app));
        var refreshed = await service.PreviewAsync(request, context);
        Assert.Equal("would-activate", refreshed.Outcome);
        Assert.Equal("activated", (await service.ActivateAsync(request, context)).Outcome);
    }

    [Fact]
    public async Task Invalid_candidate_is_never_activated()
    {
        await using var db = _fixture.CreateContext();
        var app = Register(db, "invalid-activation");
        var result = Result(app, 'F') with
        {
            IsValid = false,
            Problems = [new("SOURCE_OVERLAY_CONFLICT", "", "file:entry.json", "Closed conflict.")]
        };
        var service = Service(db, new MutablePreview(result));

        var exception = await Assert.ThrowsAsync<ApplicationActivationException>(() => service.PreviewAsync(
            new(app, result.PreviewFingerprint, null), Context("6123456789abcdef0123456789abcdef")));

        Assert.Equal("PREVIEW_INVALID", exception.Code);
        Assert.Null(service.Current(app));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    public void Dispose() => _fixture.Dispose();

    private static ApplicationIdentifier Register(DantesRoleplayDbContext db, string id)
    {
        var app = ApplicationIdentifier.Parse(id);
        new SqliteApplicationRegistry(db).Register(new(app, id, "Neutral activation fixture.", []));
        return app;
    }

    private static ApplicationActivationService Service(
        DantesRoleplayDbContext db,
        IApplicationPreviewService preview) => new(
            db, preview, new StaticImpact(preview is MutablePreview value
                ? value.Result.ApplicationId : ApplicationIdentifier.Parse("fallback")), new OperationLog(db));

    private static ApplicationActivationContext Context(string token) => new(
        token,
        "Activate an exact neutral application overlay.",
        ["procedure.system.use"],
        new("principal." + new string('a', 64), "test", "modify", "system.private-host",
            "activation-test", true, "PRIVATE_OPERATOR_ALLOWED"));

    private static ApplicationPreviewResult Result(ApplicationIdentifier app, char previewHash) => new(
        app, 1, new string('A', 64), new string('B', 64), new string('C', 64),
        new string(previewHash, 64), true,
        [new("catalog", new string('D', 64), 1, 0)],
        [new("file:catalog/entry.json", "catalog", SourceTrust.Trusted, 10,
            "catalog/entry.json", "application/json", new string('E', 64), 12, true)],
        [], []);

    private sealed class MutablePreview(ApplicationPreviewResult result) : IApplicationPreviewService
    {
        public ApplicationPreviewResult Result { get; set; } = result;
        public int MixedPreviewCount { get; private set; }
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> baseSourceIds,
            IReadOnlyList<string> extensionIds,
            CancellationToken cancellationToken = default)
        {
            MixedPreviewCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StaticImpact(ApplicationIdentifier app) : IProjectionImpactService
    {
        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true) => new(
                app, new string('F', 64), null, transitive, [], [], []);
    }

    private sealed class MutableImpact(ApplicationIdentifier app, string fingerprint) : IProjectionImpactService
    {
        public string Fingerprint { get; set; } = fingerprint;
        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true) => new(app, Fingerprint, null, transitive, [], [], []);
    }

    private sealed class FailingOperationLog : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Operation?>(null);
        public Task<Operation> RecordAsync(
            string tool, string summary, bool success, string intent = "", string subject = "",
            IEnumerable<string>? proceduresCited = null, string error = "",
            bool consumesReadEvidence = false, CancellationToken cancellationToken = default,
            string mechanicId = "", int? mechanicVersion = null, long? seed = null,
            string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            throw new InvalidOperationException("Injected audit failure.");
        public Task<IReadOnlyList<Operation>> RecentAsync(
            int limit = 20, bool failuresOnly = false, string? tool = null, string? subject = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Operation>>([]);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}

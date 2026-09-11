using System.Text;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation.Tests;

/// <summary>Recovery through the real activation, source, dependency and SQLite owners.</summary>
public sealed class ApplicationActivationRecoveryTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), $"activation-recovery-{Guid.NewGuid():N}");
    private readonly ApplicationIdentifier app = ApplicationIdentifier.Parse("activation-recovery");
    private const string PathName = "content/entry.json";

    public ApplicationActivationRecoveryTests()
    {
        using var db = fixture.CreateContext();
        new SqliteApplicationRegistry(db).Register(new(app, "Recovery", "Generic activation recovery fixture.", []));
        new SqliteSourceRegistry(db).Register(new(app, "catalog", "fixture-root", "content/**/*",
            SourceTrust.Trusted, 0, "fixture-catalog"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Missing_or_corrupt_retained_content_cannot_be_confirmed_or_reused(bool restore, bool missing)
    {
        await using var db = fixture.CreateContext();
        var setup = Services(db);
        Write("{\"value\":1}");
        var first = await Activate(setup, null);
        var current = first;
        if (restore)
        {
            Write("{\"value\":2}");
            current = await Activate(setup, first.Activation.ActivationFingerprint);
            Write("{\"value\":1}");
        }

        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>()
            .SingleAsync(value => value.ContentFingerprint == first.Activation.Winners.Single().ContentFingerprint);
        evidence.RetainedBytes = missing ? null : Encoding.UTF8.GetBytes("corrupted");
        await db.SaveChangesAsync();
        var request = await Request(setup, current.Activation.ActivationFingerprint);
        var context = Context();
        await setup.Activation.PreviewAsync(request, context);
        var operationCount = await db.Operations.CountAsync();

        var error = await Assert.ThrowsAsync<ApplicationActivationException>(() =>
            setup.Activation.ActivateAsync(request, context));

        Assert.Equal(missing ? "ACTIVATION_EVIDENCE_MISSING" : "ACTIVATION_EVIDENCE_CORRUPT", error.Code);
        Assert.Equal(current.Activation.ActivationRevision, setup.Activation.Current(app)!.ActivationRevision);
        Assert.Equal(operationCount, await db.Operations.CountAsync());
        Assert.False(await db.Set<ApplicationActivationReceiptRecord>().AnyAsync(value => value.OperationId == context.RequestToken));
        Assert.Equal(restore ? 2 : 1, await db.Set<ApplicationActivationRevisionRecord>().CountAsync());
        Assert.Equal(restore ? 2 : 1, await db.Set<ApplicationActivationDocumentEvidenceRecord>().CountAsync());
        var preserved = await db.Set<ApplicationActivationDocumentEvidenceRecord>().AsNoTracking()
            .SingleAsync(value => value.ContentFingerprint == first.Activation.Winners.Single().ContentFingerprint);
        Assert.Equal(evidence.RetainedBytes, preserved.RetainedBytes);
        if (restore)
            Assert.Equal(Encoding.UTF8.GetBytes("{\"value\":2}"), setup.Activation.ReadDocumentEvidence(
                app, current.Activation.ActivationRevision, "file:" + PathName)!.RetainedBytes);
    }

    [Fact]
    public async Task Restart_replays_one_receipt_and_restores_prior_bytes_as_a_new_generation()
    {
        ApplicationActivationReceipt first;
        ApplicationActivationRequest firstRequest;
        var firstContext = Context();
        await using (var db = fixture.CreateContext())
        {
            var setup = Services(db);
            Write("{\"value\":1}");
            firstRequest = await Request(setup, null);
            await setup.Activation.PreviewAsync(firstRequest, firstContext);
            first = await setup.Activation.ActivateAsync(firstRequest, firstContext);
            Write("{\"value\":2}");
            await Activate(setup, first.Activation.ActivationFingerprint);
        }

        await using var reopened = fixture.CreateContext();
        var restarted = Services(reopened);
        File.Delete(Path.Combine(root, PathName));
        var replay = await restarted.Activation.ActivateAsync(firstRequest, firstContext);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(1, replay.Activation.ActivationRevision);
        Assert.Equal(2, restarted.Activation.Current(app)!.ActivationRevision);
        Assert.Equal(2, await reopened.Set<ApplicationActivationReceiptRecord>().CountAsync());

        var retained = restarted.Activation.ReadDocumentEvidence(app, 1, "file:" + PathName)!;
        Write(Encoding.UTF8.GetString(retained.RetainedBytes!));
        var restored = await Activate(restarted, restarted.Activation.Current(app)!.ActivationFingerprint);
        Assert.Equal(3, restored.Activation.ActivationRevision);
        Assert.Equal(first.Activation.ActivationFingerprint, restored.Activation.ActivationFingerprint);
        Assert.NotEqual(first.OperationId, restored.OperationId);
        var changes = restarted.Activation.ChangesAfter(app, 0, 10);
        Assert.Equal([1, 2, 3], changes.Select(value => value.Revision));
        Assert.Equal(restored.OperationId, changes[2].SourceOperationId);
        Assert.All(changes, value => Assert.False(value.DerivedIndex.IsActivationGate));
        Assert.Equal(2, await reopened.Set<ApplicationActivationDocumentEvidenceRecord>().CountAsync());
    }

    [Fact]
    public async Task Source_edit_after_preview_remains_inert_and_requires_a_fresh_preview()
    {
        await using var db = fixture.CreateContext();
        var setup = Services(db);
        Write("{\"value\":1}");
        var first = await Activate(setup, null);
        Write("{\"value\":2}");
        var request = await Request(setup, first.Activation.ActivationFingerprint);
        var context = Context();
        await setup.Activation.PreviewAsync(request, context);
        Write("{\"value\":3}");

        var error = await Assert.ThrowsAsync<ApplicationActivationException>(() => setup.Activation.ActivateAsync(request, context));

        Assert.Equal("PREVIEW_STALE", error.Code);
        Assert.Single(setup.Activation.ChangesAfter(app, 0, 10));
        Assert.Equal(first.Activation.ActivationFingerprint, setup.Activation.Current(app)!.ActivationFingerprint);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"value\":1}"), setup.Activation.ReadDocumentEvidence(app, 1, "file:" + PathName)!.RetainedBytes);
        Assert.False(await db.Set<ApplicationActivationReceiptRecord>().AnyAsync(value => value.OperationId == context.RequestToken));
    }

    private (ApplicationPreviewService Preview, ApplicationActivationService Activation) Services(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var roots = new Root(root);
        var previews = new ApplicationPreviewService(applications, sources,
            new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()), new SourceOverlayResolver());
        return (previews, new ApplicationActivationService(db, previews, new SqliteApplicationExtensionRegistry(db, sources),
            sources, roots, new ProjectionImpactService(applications, new SqliteProjectionImpactSnapshotReader(db)), new OperationLog(db)));
    }

    private async Task<ApplicationActivationRequest> Request(
        (ApplicationPreviewService Preview, ApplicationActivationService Activation) setup, string? expected)
    {
        var preview = await setup.Preview.PreviewAsync(app);
        Assert.True(preview.IsValid);
        return new(app, preview.PreviewFingerprint, expected);
    }

    private async Task<ApplicationActivationReceipt> Activate(
        (ApplicationPreviewService Preview, ApplicationActivationService Activation) setup, string? expected)
    {
        var request = await Request(setup, expected);
        var context = Context();
        await setup.Activation.PreviewAsync(request, context);
        return await setup.Activation.ActivateAsync(request, context);
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(Path.Combine(root, "content"));
        File.WriteAllText(Path.Combine(root, PathName), content, new UTF8Encoding(false));
    }

    private static ApplicationActivationContext Context() => new(Guid.NewGuid().ToString("N"),
        "Verify activation recovery.", ["procedure.system.use"],
        new AuthorizationAuditEvidence("principal." + new string('a', 64), "test", "modify",
            "system.private-host", "activation-recovery-test", true, "PRIVATE_OPERATOR_ALLOWED"));

    private sealed class Root(string path) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = path;
            return allowedRootId == "fixture-root";
        }
    }

    public void Dispose()
    {
        fixture.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

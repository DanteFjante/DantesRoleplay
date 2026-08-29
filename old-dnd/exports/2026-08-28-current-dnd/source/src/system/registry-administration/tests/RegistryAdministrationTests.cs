using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Operations;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.RegistryAdministration.Tests;

public sealed class RegistryAdministrationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Dry_run_commit_and_identical_replay_share_one_immutable_receipt()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var service = new RegistryAdministrationService(
            db, applications, new SqliteSourceRegistry(db), new OperationLog(db));
        var registration = new ApplicationRegistration(
            ApplicationIdentifier.Parse("fixture-app"), "Fixture", "Neutral fixture.", []);
        var context = Context("0123456789abcdef0123456789abcdef", expected: null);

        var noPreview = await Assert.ThrowsAsync<RegistryAdministrationException>(() =>
            service.RegisterApplicationAsync(registration, context));
        Assert.Equal("DRY_RUN_REQUIRED", noPreview.Code);
        Assert.Null(applications.Get(registration.Id));

        var preview = await service.PreviewApplicationAsync(registration, context);
        Assert.Equal("would-register", preview.Outcome);
        Assert.Null(applications.Get(registration.Id));
        Assert.Single(await db.Operations.AsNoTracking().ToListAsync());

        var written = await service.RegisterApplicationAsync(registration, context);
        var replay = await service.RegisterApplicationAsync(registration, context);

        Assert.Equal("registered", written.Outcome);
        Assert.Equal(written, replay);
        Assert.Equal(context.RequestToken, written.OperationId);
        Assert.Equal(written.Fingerprint, applications.Get(registration.Id)!.Fingerprint);
        Assert.Single(await db.Operations.AsNoTracking().Where(operation => operation.Id == context.RequestToken).ToListAsync());
        Assert.Equal(2, await db.Operations.AsNoTracking().CountAsync());
        Assert.Single(applications.List(100));
    }

    [Fact]
    public async Task Source_registration_requires_exact_expectation_and_rejects_token_reuse()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var sources = new SqliteSourceRegistry(db);
        var service = new RegistryAdministrationService(db, applications, sources, new OperationLog(db));
        var app = ApplicationIdentifier.Parse("fixture-app");
        applications.Register(new(app, "Fixture", "", []));
        var source = new SourceRegistration(app, "core", "workspace", "catalog/**/*.json",
            SourceTrust.Trusted, 10, "catalog");
        var context = Context("1123456789abcdef0123456789abcdef", expected: null);

        await service.PreviewSourceAsync(source, context);
        var receipt = await service.RegisterSourceAsync(source, context);
        Assert.Equal("registered", receipt.Outcome);
        Assert.Equal(SourceRegistrationFingerprint.Compute(source), receipt.Fingerprint);
        Assert.Equal(receipt, await service.RegisterSourceAsync(source, context));

        var tokenConflict = await Assert.ThrowsAsync<RegistryAdministrationException>(() =>
            service.RegisterSourceAsync(source with { LogicalIdentity = "other" }, context));
        Assert.Equal("REQUEST_TOKEN_CONFLICT", tokenConflict.Code);

        var stale = await Assert.ThrowsAsync<RegistryAdministrationException>(() =>
            service.PreviewSourceAsync(source, Context("2123456789abcdef0123456789abcdef", expected: null)));
        Assert.Equal("REGISTRY_STALE", stale.Code);

        var confirmation = await service.PreviewSourceAsync(source,
            Context("3123456789abcdef0123456789abcdef", receipt.Fingerprint));
        Assert.Equal("unchanged", confirmation.Outcome);
        Assert.Single(sources.For(app));
        Assert.Equal(3, await db.Operations.AsNoTracking().CountAsync(operation => operation.Tool == "commit"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_registration_and_clears_pending_entities()
    {
        await using var db = _fixture.CreateContext();
        var applications = new SqliteApplicationRegistry(db);
        var registration = new ApplicationRegistration(
            ApplicationIdentifier.Parse("rollback-app"), "Rollback", "", []);
        var sources = new SqliteSourceRegistry(db);
        var context = Context("4123456789abcdef0123456789abcdef", expected: null);
        var previewService = new RegistryAdministrationService(db, applications, sources, new OperationLog(db));
        await previewService.PreviewApplicationAsync(registration, context);
        var service = new RegistryAdministrationService(db, applications, sources, new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterApplicationAsync(
            registration, context));

        Assert.Null(applications.Get(registration.Id));
        Assert.Single(await db.Operations.AsNoTracking().ToListAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    public void Dispose() => _fixture.Dispose();

    private static RegistryAdministrationContext Context(string token, string? expected) => new(
        token,
        expected,
        "Register a neutral test application source.",
        ["procedure.system.use"],
        new AuthorizationAuditEvidence(
            "principal." + new string('a', 64), "test", "modify", "system.private-host",
            "registry-test", true, "PRIVATE_OPERATOR_ALLOWED"));

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

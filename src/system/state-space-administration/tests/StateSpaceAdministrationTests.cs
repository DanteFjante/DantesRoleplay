using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.StateSpaceAdministration.Tests;

public sealed class StateSpaceAdministrationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Exact_dry_run_creates_empty_binding_and_replays_after_active_drift()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-create", 'A');
        var request = Request("space-one", setup.App, setup.Active.ActivationFingerprint);
        var context = Context("0123456789abcdef0123456789abcdef");

        var required = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.CreateAsync(request, context));
        Assert.Equal("DRY_RUN_REQUIRED", required.Code);
        Assert.Null(setup.Service.Get("space-one"));

        var preview = await setup.Service.PreviewCreateAsync(request, context);
        var created = await setup.Service.CreateAsync(request, context);
        setup.Preview.Result = Result(setup.App, 'B', setup.Applications.Get(setup.App)!.Fingerprint);
        var changed = await ActivateAsync(db, setup.App, setup.Preview, setup.Active.ActivationFingerprint,
            "1123456789abcdef0123456789abcdef");
        var replay = await setup.Service.CreateAsync(request, context);

        Assert.Equal("would-create", preview.Outcome);
        Assert.Equal("created", created.Outcome);
        Assert.Equal(created, replay);
        Assert.NotEqual(created.Binding.ActiveFingerprint, changed.ActivationFingerprint);
        Assert.Equal(setup.App, created.Binding.ApplicationId);
        Assert.NotNull(created.Binding.CreatedAtUtc);
        Assert.Equal(created.Binding, setup.Service.Get("space-one"));
        Assert.Single(setup.Service.List(setup.App, 50));
        Assert.Equal(0, await ScalarAsync(db, "SELECT COUNT(*) FROM system_ecs_entity"));
        Assert.Equal(0, await ScalarAsync(db, "SELECT COUNT(*) FROM system_ecs_component"));

        var conflict = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.CreateAsync(request with { StateSpaceId = "space-two" }, context));
        Assert.Equal("REQUEST_TOKEN_CONFLICT", conflict.Code);
    }

    [Fact]
    public async Task Activation_drift_after_dry_run_rejects_without_creation()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-stale", 'C');
        var request = Request("stale-space", setup.App, setup.Active.ActivationFingerprint);
        var context = Context("2123456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(request, context);
        setup.Preview.Result = Result(setup.App, 'D', setup.Applications.Get(setup.App)!.Fingerprint);
        await ActivateAsync(db, setup.App, setup.Preview, setup.Active.ActivationFingerprint,
            "3123456789abcdef0123456789abcdef");

        var stale = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.CreateAsync(request, context));

        Assert.Equal("ACTIVATION_STALE", stale.Code);
        Assert.Null(setup.Service.Get("stale-space"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
    }

    [Fact]
    public async Task Existing_id_and_nonnull_creation_expectation_are_rejected()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-absence", 'E');
        var first = Request("reserved-space", setup.App, setup.Active.ActivationFingerprint);
        var firstContext = Context("4123456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(first, firstContext);
        await setup.Service.CreateAsync(first, firstContext);

        var duplicate = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.PreviewCreateAsync(first, Context("5123456789abcdef0123456789abcdef")));
        var expectation = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.PreviewCreateAsync(first with { StateSpaceId = "new-space", ExpectedFingerprint = new string('A', 64) },
                Context("6123456789abcdef0123456789abcdef")));

        Assert.Equal("STATE_SPACE_EXISTS", duplicate.Code);
        Assert.Equal("STATE_SPACE_EXPECTED_ABSENT", expectation.Code);
        Assert.Single(setup.Service.List(setup.App, 50));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_state_space_and_success_operation()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-rollback", 'F');
        var request = Request("rollback-space", setup.App, setup.Active.ActivationFingerprint);
        var context = Context("7123456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(request, context);
        var failing = new StateSpaceAdministrationService(
            db, setup.Applications, setup.Activations,
            new SqliteStateSpaceRegistry(db, setup.Applications), setup.Types, setup.Schemas,
            new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateAsync(request, context));

        Assert.Null(setup.Service.Get("rollback-space"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Empty_space_upgrades_twice_and_every_create_or_upgrade_token_replays_history()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-upgrade", 'A');
        var createRequest = Request("upgrade-space", setup.App, setup.Active.ActivationFingerprint);
        var createContext = Context("8123456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(createRequest, createContext);
        var created = await setup.Service.CreateAsync(createRequest, createContext);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM system_state_space_binding_revision WHERE StateSpaceId = 'upgrade-space'");

        var activeTwo = await NextActivationAsync(db, setup, 'B', "9123456789abcdef0123456789abcdef");
        var firstRequest = new StateSpaceUpgradeRequest("upgrade-space", setup.App,
            activeTwo.ActivationFingerprint, created.Binding.BindingFingerprint);
        var firstContext = UpgradeContext("aa23456789abcdef0123456789abcdef");
        var noDryRun = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.UpgradeAsync(firstRequest, firstContext));
        Assert.Equal("DRY_RUN_REQUIRED", noDryRun.Code);

        var preview = await setup.Service.PreviewUpgradeAsync(firstRequest, firstContext);
        var first = await setup.Service.UpgradeAsync(firstRequest, firstContext);
        var firstReplay = await setup.Service.UpgradeAsync(firstRequest, firstContext);
        var createReplay = await setup.Service.CreateAsync(createRequest, createContext);

        Assert.Equal("would-upgrade", preview.Outcome);
        Assert.Equal("upgraded", first.Outcome);
        Assert.Equal(first, firstReplay);
        Assert.Equal(created, createReplay);
        Assert.Equal(2, first.Binding.BindingRevision);
        Assert.Equal("empty-state-compatible", first.Compatibility.Code);
        Assert.Equal(0, first.Compatibility.EntityCount);
        Assert.Equal(0, first.Compatibility.ComponentCount);
        Assert.False(first.Compatibility.DependencyCoverageComplete);

        var activeThree = await NextActivationAsync(db, setup, 'C', "b123456789abcdef0123456789abcdef");
        var secondRequest = new StateSpaceUpgradeRequest("upgrade-space", setup.App,
            activeThree.ActivationFingerprint, first.Binding.BindingFingerprint);
        var secondContext = UpgradeContext("c123456789abcdef0123456789abcdef");
        await setup.Service.PreviewUpgradeAsync(secondRequest, secondContext);
        var second = await setup.Service.UpgradeAsync(secondRequest, secondContext);

        Assert.Equal(3, second.Binding.BindingRevision);
        Assert.Equal(first, await setup.Service.UpgradeAsync(firstRequest, firstContext));
        Assert.Equal(created, await setup.Service.CreateAsync(createRequest, createContext));
        Assert.Equal(second.Binding, setup.Service.Get("upgrade-space"));
        Assert.Equal(3, await ScalarAsync(db,
            "SELECT COUNT(*) FROM system_state_space_binding_revision WHERE StateSpaceId = 'upgrade-space'"));
    }

    [Fact]
    public async Task Compatible_populated_space_rebinds_without_changing_its_component()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-nonempty", 'D');
        var create = Request("nonempty-space", setup.App, setup.Active.ActivationFingerprint);
        var createContext = Context("dd23456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(create, createContext);
        var created = await setup.Service.CreateAsync(create, createContext);
        var store = new SqliteEntityComponentStore(db,
            setup.Types, setup.Schemas);
        await store.CreateEntityAsync("nonempty-space", "entity-one", "Entity One");
        var type = setup.Types.Define(new ComponentTypeDefinition(setup.App, "space-nonempty.note",
            "{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}},\"additionalProperties\":false}"));
        var reference = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
        var component = await store.AddComponentAsync(new EcsComponentWrite("nonempty-space", "entity-one",
            reference, "{\"name\":\"kept\"}", 0));
        var target = await NextActivationAsync(db, setup, 'E', "e123456789abcdef0123456789abcdef");
        var request = new StateSpaceUpgradeRequest("nonempty-space", setup.App,
            target.ActivationFingerprint, created.Binding.BindingFingerprint);
        var context = UpgradeContext("f123456789abcdef0123456789abcdef");

        var preview = await setup.Service.PreviewUpgradeAsync(request, context);
        var rebound = await setup.Service.UpgradeAsync(request, context);

        Assert.Equal("populated-state-compatible-rebind", preview.Compatibility.Code);
        Assert.Equal(rebound, await setup.Service.UpgradeAsync(request, context));
        Assert.NotEqual(created.Binding, rebound.Binding);
        Assert.Equal(component, await store.GetComponentAsync("nonempty-space", "entity-one", type.QualifiedId));
        Assert.Equal(2, await ScalarAsync(db,
            "SELECT COUNT(*) FROM system_state_space_binding_revision WHERE StateSpaceId = 'nonempty-space'"));
    }

    [Fact]
    public async Task Incompatible_populated_component_requires_migration_without_binding_change()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-incompatible", 'D');
        var create = Request("incompatible-space", setup.App, setup.Active.ActivationFingerprint);
        var createContext = Context("de23456789abcdef0123456789abcdef");
        await setup.Service.PreviewCreateAsync(create, createContext);
        var created = await setup.Service.CreateAsync(create, createContext);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO system_ecs_entity (StateSpaceId, Id, Name, Revision, CreatedAtUtc) VALUES ('incompatible-space', 'entity-one', 'Entity One', 1, CURRENT_TIMESTAMP)");
        var registered = setup.Types.Define(new ComponentTypeDefinition(setup.App, "space-incompatible.note",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));
        var staleHash = new string('A', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO system_ecs_component (StateSpaceId, EntityId, QualifiedTypeId, TypeVersion, SchemaHash, Data, Revision, CreatedAtUtc, UpdatedAtUtc) VALUES ('incompatible-space', 'entity-one', {registered.QualifiedId}, {registered.Version}, {staleHash}, '{{}}', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
        var target = await NextActivationAsync(db, setup, 'E', "ef23456789abcdef0123456789abcdef");
        var request = new StateSpaceUpgradeRequest("incompatible-space", setup.App,
            target.ActivationFingerprint, created.Binding.BindingFingerprint);

        var failure = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.PreviewUpgradeAsync(request, UpgradeContext("ff23456789abcdef0123456789abcdef")));

        Assert.Equal("MIGRATION_REQUIRED", failure.Code);
        Assert.Equal(created.Binding, setup.Service.Get("incompatible-space"));
    }

    [Fact]
    public async Task Target_activation_drift_after_dry_run_rejects_without_binding_change()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-upgrade-stale", 'A');
        var create = Request("stale-upgrade-space", setup.App, setup.Active.ActivationFingerprint);
        var createContext = Context("0123456789abcdef0123456789abcdea");
        await setup.Service.PreviewCreateAsync(create, createContext);
        var created = await setup.Service.CreateAsync(create, createContext);
        var target = await NextActivationAsync(db, setup, 'B', "1123456789abcdef0123456789abcdea");
        var request = new StateSpaceUpgradeRequest("stale-upgrade-space", setup.App,
            target.ActivationFingerprint, created.Binding.BindingFingerprint);
        var context = UpgradeContext("2123456789abcdef0123456789abcdea");
        await setup.Service.PreviewUpgradeAsync(request, context);
        await NextActivationAsync(db, setup, 'C', "3123456789abcdef0123456789abcdea");

        var stale = await Assert.ThrowsAsync<StateSpaceAdministrationException>(() =>
            setup.Service.UpgradeAsync(request, context));

        Assert.Equal("ACTIVATION_STALE", stale.Code);
        Assert.Equal(created.Binding, setup.Service.Get("stale-upgrade-space"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
    }

    [Fact]
    public async Task Upgrade_audit_failure_rolls_back_current_binding_and_history()
    {
        await using var db = _fixture.CreateContext();
        var setup = await SetupAsync(db, "space-upgrade-rollback", 'D');
        var create = Request("upgrade-rollback-space", setup.App, setup.Active.ActivationFingerprint);
        var createContext = Context("4123456789abcdef0123456789abcdea");
        await setup.Service.PreviewCreateAsync(create, createContext);
        var created = await setup.Service.CreateAsync(create, createContext);
        var target = await NextActivationAsync(db, setup, 'E', "5123456789abcdef0123456789abcdea");
        var request = new StateSpaceUpgradeRequest("upgrade-rollback-space", setup.App,
            target.ActivationFingerprint, created.Binding.BindingFingerprint);
        var context = UpgradeContext("6123456789abcdef0123456789abcdea");
        await setup.Service.PreviewUpgradeAsync(request, context);
        var failing = new StateSpaceAdministrationService(db, setup.Applications, setup.Activations,
            new SqliteStateSpaceRegistry(db, setup.Applications), setup.Types, setup.Schemas,
            new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.UpgradeAsync(request, context));

        Assert.Equal(created.Binding, setup.Service.Get("upgrade-rollback-space"));
        Assert.Equal(1, await ScalarAsync(db,
            "SELECT COUNT(*) FROM system_state_space_binding_revision WHERE StateSpaceId = 'upgrade-rollback-space'"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    public void Dispose() => _fixture.Dispose();

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db, string id, char hash)
    {
        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse(id);
        applications.Register(new(app, id, "Neutral state-space fixture.", []));
        var preview = new MutablePreview(Result(app, hash, applications.Get(app)!.Fingerprint));
        var activations = new ApplicationActivationService(db, preview, new StaticImpact(app), new OperationLog(db));
        var active = await ActivateAsync(db, app, preview, null,
            hash + "123456789abcdef0123456789abcdef");
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var service = new StateSpaceAdministrationService(
            db, applications, activations, new SqliteStateSpaceRegistry(db, applications), types, schemas,
            new OperationLog(db));
        return new(app, applications, preview, activations, active, types, schemas, service);
    }

    private static async Task<ActiveApplicationManifest> ActivateAsync(
        DantesRoleplayDbContext db,
        ApplicationIdentifier app,
        MutablePreview preview,
        string? expected,
        string token)
    {
        var service = new ApplicationActivationService(db, preview, new StaticImpact(app), new OperationLog(db));
        var request = new ApplicationActivationRequest(app, preview.Result.PreviewFingerprint, expected);
        var context = new ApplicationActivationContext(token.ToLowerInvariant(), "Activate fixture.",
            ["procedure.system.use"], Evidence());
        await service.PreviewAsync(request, context);
        return (await service.ActivateAsync(request, context)).Activation;
    }

    private static StateSpaceCreationRequest Request(
        string stateSpaceId,
        ApplicationIdentifier app,
        string activeFingerprint) => new(stateSpaceId, app, activeFingerprint, null);

    private static StateSpaceCreationContext Context(string token) => new(
        token, "Create an empty neutral state space.", ["procedure.system.use"], Evidence());

    private static StateSpaceUpgradeContext UpgradeContext(string token) => new(
        token, "Upgrade an empty neutral state space.", ["procedure.system.use"], Evidence());

    private static async Task<ActiveApplicationManifest> NextActivationAsync(
        DantesRoleplayDbContext db,
        Setup setup,
        char hash,
        string token)
    {
        setup.Preview.Result = Result(setup.App, hash, setup.Applications.Get(setup.App)!.Fingerprint);
        return await ActivateAsync(db, setup.App, setup.Preview,
            setup.Activations.Current(setup.App)!.ActivationFingerprint, token);
    }

    private static AuthorizationAuditEvidence Evidence() => new(
        "principal." + new string('a', 64), "test", "modify", "system.private-host",
        "state-space-test", true, "PRIVATE_OPERATOR_ALLOWED");

    private static ApplicationPreviewResult Result(
        ApplicationIdentifier app,
        char previewHash,
        string applicationFingerprint) => new(
        app, 1, applicationFingerprint, new string('B', 64), new string('C', 64),
        new string(previewHash, 64), true,
        [new("catalog", new string('D', 64), 1, 0)],
        [new("file:catalog/entry.json", "catalog", SourceTrust.Trusted, 10,
            "catalog/entry.json", "application/json", new string('E', 64), 12, true)], [], []);

    private static async Task<long> ScalarAsync(DantesRoleplayDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record Setup(
        ApplicationIdentifier App,
        SqliteApplicationRegistry Applications,
        MutablePreview Preview,
        ApplicationActivationService Activations,
        ActiveApplicationManifest Active,
        SqliteComponentTypeRegistry Types,
        BoundedJsonSchemaValidator Schemas,
        StateSpaceAdministrationService Service);

    private sealed class MutablePreview(ApplicationPreviewResult result) : IApplicationPreviewService
    {
        public ApplicationPreviewResult Result { get; set; } = result;
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);
        public Task<ApplicationPreviewResult> PreviewAsync(
            ApplicationIdentifier applicationId,
            IReadOnlyList<string> sourceIds,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }

    private sealed class StaticImpact(ApplicationIdentifier app) : IProjectionImpactService
    {
        public ProjectionImpactReport Analyze(
            ApplicationIdentifier applicationId,
            string? rootId = null,
            bool transitive = true) => new(app, new string('F', 64), null, transitive, [], [], []);
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

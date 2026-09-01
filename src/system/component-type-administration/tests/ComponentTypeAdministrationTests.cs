using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DantesRoleplay.ComponentTypeAdministration.Tests;

public sealed class ComponentTypeAdministrationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Registration_requires_exact_preview_appends_versions_replays_and_refuses_rollback()
    {
        using var db = _fixture.CreateContext();
        var setup = Setup(db, "fixture-app");
        var first = new ComponentTypeRegistrationRequest(setup.App, "fixture-app.note",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"note\":{\"type\":\"string\",\"maxLength\":80}}}");
        var firstContext = Context("0123456789abcdef0123456789abcdef", null);

        var required = await Assert.ThrowsAsync<ComponentTypeAdministrationException>(() =>
            setup.Service.RegisterAsync(first, firstContext));
        Assert.Equal("DRY_RUN_REQUIRED", required.Code);
        var preview = await setup.Service.PreviewAsync(first, firstContext);
        var registered = await setup.Service.RegisterAsync(first, firstContext);
        var replay = await setup.Service.RegisterAsync(first, firstContext);

        var second = first with { SchemaJson = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"note\":{\"type\":\"string\",\"maxLength\":120}}}" };
        var secondContext = Context("1123456789abcdef0123456789abcdef", registered.ComponentType.SchemaHash);
        await setup.Service.PreviewAsync(second, secondContext);
        var updated = await setup.Service.RegisterAsync(second, secondContext);
        var retired = await Assert.ThrowsAsync<ComponentTypeAdministrationException>(() =>
            setup.Service.PreviewAsync(first, Context("2123456789abcdef0123456789abcdef", updated.ComponentType.SchemaHash)));

        Assert.Equal("would-register", preview.Outcome);
        Assert.Equal("registered", registered.Outcome);
        Assert.Equal(1, registered.ComponentType.Version);
        Assert.Equal(registered, replay);
        Assert.Equal(2, updated.ComponentType.Version);
        Assert.Equal("SCHEMA_RETIRED", retired.Code);
        Assert.Equal(updated.ComponentType, setup.Types.GetLatest("fixture-app.note"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_staged_component_type_version()
    {
        await using var db = _fixture.CreateContext();
        var setup = Setup(db, "rollback-app");
        var request = new ComponentTypeRegistrationRequest(setup.App, "rollback-app.note", "true");
        var context = Context("3123456789abcdef0123456789abcdef", null);
        await setup.Service.PreviewAsync(request, context);
        var failing = new ComponentTypeAdministrationService(db, setup.Types,
            new BoundedJsonSchemaValidator(), new FailingOperationLog());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RegisterAsync(request, context));

        Assert.Null(setup.Types.GetLatest("rollback-app.note"));
        Assert.Null(await new OperationLog(db).GetAsync(context.RequestToken));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Legacy_stats_schema_preserves_both_fixtures_and_only_the_object_root_boundary()
    {
        var validator = new BoundedJsonSchemaValidator();
        var catalog = Path.Combine(RepositoryRoot(), "catalog");
        var schema = File.ReadAllText(CatalogLayout.ToFileSystemPath(
            catalog, CatalogLayout.ComponentSchema("fixture.legacy.stats")));
        var compilation = validator.Compile(schema);

        Assert.True(compilation.IsAccepted);
        Assert.Equal(SystemJsonSchemaProfile.Version1Id, compilation.ProfileId);
        foreach (var entityId in new[] { "fixture.legacy.homer", "fixture.legacy.orban" })
        {
            using var entity = JsonDocument.Parse(File.ReadAllText(CatalogLayout.ToFileSystemPath(
                catalog, CatalogLayout.Entity(entityId))));
            var stats = entity.RootElement.GetProperty("components")
                .GetProperty("fixture.legacy.stats").GetRawText();
            Assert.Equal(SchemaValueStatus.Valid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, stats).Status);
        }

        Assert.Equal(SchemaValueStatus.Valid,
            validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, "{}").Status);
        Assert.All(new[] { "[]", "\"stats\"", "1", "true", "null" }, value =>
            Assert.Equal(SchemaValueStatus.Invalid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, value).Status));
    }

    public void Dispose() => _fixture.Dispose();

    private static Fixture Setup(DantesRoleplayDbContext db, string appId)
    {
        var app = ApplicationIdentifier.Parse(appId);
        new SqliteApplicationRegistry(db).Register(new(app, appId, "Neutral component-type fixture.", []));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        return new Fixture(app, types, new ComponentTypeAdministrationService(db, types,
            new BoundedJsonSchemaValidator(), new OperationLog(db)));
    }

    private static ComponentTypeAdministrationContext Context(string token, string? expectedSchemaHash) => new(
        token, expectedSchemaHash, "Register a neutral component type.", ["procedure.system.use"],
        new AuthorizationAuditEvidence("principal." + new string('a', 64), "test", "modify",
            "system.private-host", "component-type-test", true, "PRIVATE_OPERATOR_ALLOWED"));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record Fixture(ApplicationIdentifier App, SqliteComponentTypeRegistry Types,
        ComponentTypeAdministrationService Service);

    private sealed class FailingOperationLog : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Operation?>(null);
        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "", string subject = "",
            IEnumerable<string>? proceduresCited = null, string error = "", bool consumesReadEvidence = false,
            CancellationToken cancellationToken = default, string mechanicId = "", int? mechanicVersion = null,
            long? seed = null, string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            throw new InvalidOperationException("Injected audit failure.");
        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false, string? tool = null,
            string? subject = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Operation>>([]);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

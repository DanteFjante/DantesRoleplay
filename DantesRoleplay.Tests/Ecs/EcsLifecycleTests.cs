using DantesRoleplay.Applications;
using DantesRoleplay.AI;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.Tests;
using System.Text.Json;

namespace DantesRoleplay.Ecs.Tests;

public sealed class EcsLifecycleTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Disabled_component_types_leave_exact_history_available_but_disappear_from_discovery()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Application, "fixture-app.accidental", "true"));

        var disabled = await setup.Lifecycle.SetComponentTypeEnabledAsync(type.QualifiedId, false);

        Assert.False(disabled.IsEnabled);
        Assert.Null(setup.Types.GetLatest(type.QualifiedId));
        Assert.Empty(setup.Types.ListLatestPage(setup.Application, null, 100).ComponentTypes);
        Assert.NotNull(setup.Types.Get(type.QualifiedId, type.Version));
        Assert.Throws<InvalidOperationException>(() => setup.Types.Define(
            new(setup.Application, type.QualifiedId, "false")));

        var enabled = await setup.Lifecycle.SetComponentTypeEnabledAsync(type.QualifiedId, true);
        Assert.True(enabled.IsEnabled);
        Assert.Equal(type.QualifiedId, setup.Types.GetLatest(type.QualifiedId)!.QualifiedId);
    }

    [Fact]
    public async Task Unused_component_type_can_be_renamed_and_disabled_type_can_be_purged()
    {
        var setup = Setup();
        setup.Types.Define(new(setup.Application, "fixture-app.wrong-place", "true"));
        setup.Types.Define(new(setup.Application, "fixture-app.wrong-place", "false"));

        var renamed = await setup.Lifecycle.RenameComponentTypeAsync(
            "fixture-app.wrong-place", "fixture-app.correct-place");

        Assert.Equal("fixture-app.correct-place", renamed.QualifiedTypeId);
        Assert.Equal(2, renamed.LatestVersion);
        Assert.Null(await setup.Lifecycle.GetComponentTypeAsync("fixture-app.wrong-place"));
        Assert.NotNull(setup.Types.Get("fixture-app.correct-place", 1));
        Assert.NotNull(setup.Types.Get("fixture-app.correct-place", 2));

        await setup.Lifecycle.SetComponentTypeEnabledAsync("fixture-app.correct-place", false);
        Assert.True(await setup.Lifecycle.DeleteComponentTypeAsync("fixture-app.correct-place"));
        Assert.Null(await setup.Lifecycle.GetComponentTypeAsync("fixture-app.correct-place"));
    }

    [Fact]
    public async Task Component_type_rename_moves_live_components_and_retires_the_old_identity()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Application, "fixture-app.used", "true"));
        await setup.Entities.CreateEntityAsync("space", "actor", "Actor");
        var reference = new EcsComponentReference(type.QualifiedId, type.Version, type.SchemaHash);
        await setup.Entities.AddComponentAsync(new("space", "actor", reference, "{}", 0));

        var view = await setup.Lifecycle.GetComponentTypeAsync(type.QualifiedId);
        Assert.Equal(1, Assert.Single(view!.References, value => value.Kind == "components").Count);
        var moved = await setup.Lifecycle.RenameComponentTypeAsync(
            type.QualifiedId, "fixture-app.moved");

        Assert.Equal("fixture-app.moved", moved.QualifiedTypeId);
        Assert.Equal("fixture-app.moved", Assert.Single(
            (await setup.Entities.ListComponentsAsync("space", "actor", null, 100)).Components)
            .Type.QualifiedTypeId);
        Assert.Null(await setup.Entities.GetComponentAsync("space", "actor", type.QualifiedId));
        Assert.NotNull(await setup.Entities.GetComponentAsync("space", "actor", "fixture-app.moved"));
        var retired = await setup.Lifecycle.GetComponentTypeAsync(type.QualifiedId);
        Assert.False(retired!.IsEnabled);
        Assert.Empty(retired.References);
        Assert.True(await setup.Lifecycle.DeleteComponentTypeAsync(type.QualifiedId));
    }

    [Fact]
    public async Task Component_type_migration_targets_an_existing_base_contract_and_validates_rewrites()
    {
        var setup = SetupWithBase();
        var source = setup.Types.Define(new(setup.Application, "fixture-app.legacy-note",
            "{\"type\":\"object\",\"required\":[\"text\"],\"properties\":{\"text\":{\"type\":\"string\"}},\"additionalProperties\":false}"));
        var target = setup.Types.Define(new(setup.BaseApplication, "fixture-base.note",
            "{\"type\":\"object\",\"required\":[\"message\"],\"properties\":{\"message\":{\"type\":\"string\"}},\"additionalProperties\":false}"));
        await setup.Entities.CreateEntityAsync("space", "actor", "Actor");
        await setup.Entities.AddComponentAsync(new("space", "actor",
            new(source.QualifiedId, source.Version, source.SchemaHash), "{\"text\":\"kept\"}", 0));

        var invalid = await Assert.ThrowsAsync<EcsLifecycleException>(() =>
            setup.Lifecycle.MigrateComponentTypeAsync(source.QualifiedId, target.QualifiedId));
        Assert.Equal("COMPONENT_TYPE_MIGRATION_VALUE_INVALID", invalid.Code);

        var migrated = await setup.Lifecycle.MigrateComponentTypeAsync(
            source.QualifiedId, target.QualifiedId,
            [new("space", "actor", 1, "{\"message\":\"kept\"}")]);

        Assert.Equal(1, migrated.MigratedComponents);
        Assert.Equal(1, migrated.RewrittenValues);
        Assert.Equal(["space"], migrated.StateSpaceIds);
        Assert.False((await setup.Lifecycle.GetComponentTypeAsync(source.QualifiedId))!.IsEnabled);
        var component = Assert.Single((await setup.Entities.ListComponentsAsync("space", "actor", null, 100)).Components);
        Assert.Equal(target.QualifiedId, component.Type.QualifiedTypeId);
        Assert.Equal(target.Version, component.Type.TypeVersion);
        Assert.Equal(target.SchemaHash, component.Type.SchemaHash);
        Assert.Equal("{\"message\":\"kept\"}", component.ValueJson);
        Assert.Equal(2, component.Revision);
    }

    [Fact]
    public async Task Relationship_kind_migration_targets_a_base_owned_kind_transactionally()
    {
        var setup = SetupWithBase();
        await setup.Entities.CreateEntityAsync("space", "from", "From");
        await setup.Entities.CreateEntityAsync("space", "to", "To");
        var edges = new SqliteStateSpaceEdgeStore(setup.Db, setup.StateSpaces);
        await edges.SetRelationshipAsync(
            "space", "from", "to", "fixture-app.legacy-related-to", "{}", 0);

        var migrated = await setup.Lifecycle.MigrateRelationshipKindAsync(
            "fixture-app.legacy-related-to", "fixture-base.related-to");

        Assert.Equal(1, migrated.MigratedRelationships);
        Assert.Equal(["space"], migrated.StateSpaceIds);
        Assert.Null(await edges.GetRelationshipAsync(
            "space", "from", "to", "fixture-app.legacy-related-to"));
        var relationship = await edges.GetRelationshipAsync(
            "space", "from", "to", "fixture-base.related-to");
        Assert.NotNull(relationship);
        Assert.Equal(2, relationship.Revision);
    }

    [Fact]
    public async Task Entity_name_status_identity_and_permanent_deletion_are_explicit()
    {
        var setup = Setup();
        var created = await setup.Entities.CreateEntityAsync("space", "mistake", "Old name");

        var edited = await setup.Lifecycle.UpdateEntityAsync(
            "space", "mistake", "corrected", "Correct name", created.Revision);
        Assert.Equal("corrected", edited.Entity.EntityId);
        Assert.Equal("Correct name", edited.Entity.Name);
        Assert.Null(await setup.Lifecycle.GetEntityAsync("space", "mistake"));

        var disabled = await setup.Lifecycle.SetEntityEnabledAsync(
            "space", "corrected", false, edited.Entity.Revision);
        Assert.False(disabled.IsEnabled);
        Assert.Null(await setup.Entities.GetEntityAsync("space", "corrected"));
        Assert.Empty((await setup.Entities.ListEntitiesAsync("space", null, 100)).Entities);

        var restored = await setup.Lifecycle.SetEntityEnabledAsync(
            "space", "corrected", true, disabled.Entity.Revision);
        Assert.True(restored.IsEnabled);
        Assert.NotNull(await setup.Entities.GetEntityAsync("space", "corrected"));

        var disabledAgain = await setup.Lifecycle.SetEntityEnabledAsync(
            "space", "corrected", false, restored.Entity.Revision);
        Assert.True(await setup.Lifecycle.DeleteEntityPermanentlyAsync("space", "corrected"));
        Assert.Null(await setup.Lifecycle.GetEntityAsync("space", "corrected"));
        Assert.True(disabledAgain.Entity.Revision > created.Revision);
    }

    [Fact]
    public async Task Referenced_entity_reports_blockers_and_cannot_be_renamed_or_purged()
    {
        var setup = Setup();
        var entity = await setup.Entities.CreateEntityAsync("space", "actor", "Actor");
        var type = setup.Types.Define(new(setup.Application, "fixture-app.marker", "true"));
        await setup.Entities.AddComponentAsync(new(
            "space", "actor", new(type.QualifiedId, type.Version, type.SchemaHash), "1", 0));

        var view = await setup.Lifecycle.GetEntityAsync("space", "actor");
        Assert.Equal(1, Assert.Single(view!.References, value => value.Kind == "components").Count);
        var rename = await Assert.ThrowsAsync<EcsLifecycleException>(() => setup.Lifecycle.UpdateEntityAsync(
            "space", "actor", "renamed", "Actor", entity.Revision));
        Assert.Equal("ENTITY_IN_USE", rename.Code);

        var disabled = await setup.Lifecycle.SetEntityEnabledAsync("space", "actor", false, entity.Revision);
        var purge = await Assert.ThrowsAsync<EcsLifecycleException>(() =>
            setup.Lifecycle.DeleteEntityPermanentlyAsync("space", "actor"));
        Assert.Equal("ENTITY_IN_USE", purge.Code);
        Assert.False(disabled.IsEnabled);
    }

    [Fact]
    public async Task Local_ai_receives_direct_lifecycle_reads_and_confirmed_writes()
    {
        var setup = Setup();
        setup.Types.Define(new(setup.Application, "fixture-app.mistake", "true"));
        var source = new EcsLifecycleAiToolSource(setup.Lifecycle);
        var deniedTools = source.CreateTools(AiContext(null));
        Assert.Equal(5, deniedTools.Count);

        var inspect = Assert.Single(deniedTools, value =>
            value.Definition.Name == "ecs_inspect_component_type");
        var inspected = await inspect.InvokeAsync(Call(inspect.Definition.Name,
            """{"qualifiedTypeId":"fixture-app.mistake"}"""));
        Assert.True(inspected.Ok);
        Assert.Contains("fixture-app.mistake", inspected.Content, StringComparison.Ordinal);

        var denied = Assert.Single(deniedTools, value =>
            value.Definition.Name == "ecs_manage_component_type");
        var deniedResult = await denied.InvokeAsync(Call(denied.Definition.Name,
            """{"action":"rename","qualifiedTypeId":"fixture-app.mistake","correctedQualifiedTypeId":"fixture-app.correct"}"""));
        Assert.False(deniedResult.Ok);
        Assert.Equal("AI_TOOL_CONFIRMATION_REQUIRED", deniedResult.ErrorCode);

        var approved = Assert.Single(source.CreateTools(AiContext(new ApproveAll())), value =>
            value.Definition.Name == "ecs_manage_component_type");
        var renamed = await approved.InvokeAsync(Call(approved.Definition.Name,
            """{"action":"rename","qualifiedTypeId":"fixture-app.mistake","correctedQualifiedTypeId":"fixture-app.correct"}"""));
        Assert.True(renamed.Ok);
        Assert.NotNull(await setup.Lifecycle.GetComponentTypeAsync("fixture-app.correct"));
    }

    private SetupResult Setup()
    {
        var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("fixture-app");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(application, "Fixture", "", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("space", revision, Manifest));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        return new(
            db,
            application,
            application,
            stateSpaces,
            types,
            new SqliteEntityComponentStore(db, types, schemas),
            new SqliteEcsLifecycleStore(db));
    }

    private SetupResult SetupWithBase()
    {
        var db = _fixture.CreateContext();
        var application = ApplicationIdentifier.Parse("fixture-app");
        var baseApplication = ApplicationIdentifier.Parse("fixture-base");
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(baseApplication, "Fixture Base", "", []));
        var revision = applications.Register(new(application, "Fixture", "", [baseApplication]));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("space", revision, Manifest));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        return new(db, application, baseApplication, stateSpaces, types,
            new SqliteEntityComponentStore(db, types, schemas),
            new SqliteEcsLifecycleStore(db, schemas: schemas));
    }

    public void Dispose() => _fixture.Dispose();

    private static SystemAiToolSourceContext AiContext(IAiToolApprovalGate? approval) => new(
        new("fixture-agent", "Fixture", "A test agent."),
        new("fixture", "model", [new(AiMessageRole.User, "test")]),
        new(TrustedPrincipalContext.Unauthenticated("test"), "test", "test"),
        null,
        approval,
        () => []);

    private static AiToolInvocation Call(string name, string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return new("call", name, document.RootElement.Clone(), AiRequestKind.Task);
    }

    private sealed class ApproveAll : IAiToolApprovalGate
    {
        public Task<bool> ConfirmAsync(AiToolApprovalRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed record SetupResult(
        DantesRoleplayDbContext Db,
        ApplicationIdentifier Application,
        ApplicationIdentifier BaseApplication,
        SqliteStateSpaceRegistry StateSpaces,
        SqliteComponentTypeRegistry Types,
        SqliteEntityComponentStore Entities,
        SqliteEcsLifecycleStore Lifecycle);
}

using System.Text.Json;
using System.Data.Common;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DantesRoleplay.Projections.Tests;

public sealed class FieldBasedProjectionMaterializationTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    private const string Space = "field-read-space";

    [Fact]
    public async Task T09_display_uses_attached_registered_version_not_highest_registered_or_declared_version()
    {
        var setup = Setup();
        var first = setup.Types.Define(new(setup.Owner, "field-read.profile",
            """{"type":"object","properties":{"title":{"type":"string"}}}"""));
        var second = setup.Types.Define(new(setup.Owner, first.QualifiedId,
            """{"type":"object","properties":{"title":{"type":"string"},"note":{"type":"string"}}}"""));
        setup.Types.Define(new(setup.Owner, first.QualifiedId,
            """{"type":"object","properties":{"future":{"type":"boolean"}}}"""));
        var definition = Define(setup, first, [new("profile", "", "/profile")]);
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(second), """{"title":"Current","note":"Added"}""", 0));
        var request = Request(definition) with { Purpose = ProjectionReadPurpose.Display };

        var result = await setup.Materializer.MaterializeAsync(request);
        using var json = JsonDocument.Parse(result.OutputJson);
        Assert.Equal("Current", json.RootElement.GetProperty("profile").GetProperty("title").GetString());
        Assert.Equal("Added", json.RootElement.GetProperty("profile").GetProperty("note").GetString());
        Assert.Equal(Ref(second), Assert.Single(result.SourceRevisions).Type);
        Assert.Equal(1, result.SourceRevisions[0].Revision);
        Assert.Equal("value", Assert.Single(result.Fields).Availability);
        var observed = Assert.Single(result.ObservedSources);
        Assert.Equal(["profile"], observed.InputPath);
        Assert.Equal(Ref(first), observed.DeclaredComponent);
        Assert.Equal(Ref(second), observed.ActualComponent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            request with { Purpose = ProjectionReadPurpose.Exact }));
    }

    [Fact]
    public async Task T11_absent_optional_path_null_and_absent_component_have_distinct_evidence()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Owner, "field-read.profile",
            """{"type":"object","properties":{"title":{"type":"string"},"note":{"type":["string","null"]},"missing":{"type":"string"}}}"""));
        var definition = Define(setup, type,
            [new("profile", "/title", "/title"), new("profile", "/note", "/note"), new("profile", "/missing", "/missing")],
            requiredSource: false);
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        var request = Request(definition) with { Purpose = ProjectionReadPurpose.Display };
        var absent = await setup.Materializer.MaterializeAsync(request);
        Assert.Equal("{}", absent.OutputJson);
        Assert.Equal(0, Assert.Single(absent.SourceRevisions).Revision);
        Assert.All(absent.Fields, value => Assert.Equal("absent-source", value.Availability));

        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(type), """{"title":"Shown","note":null}""", 0));
        var present = await setup.Materializer.MaterializeAsync(request);
        using var json = JsonDocument.Parse(present.OutputJson);
        Assert.Equal("Shown", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("note").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("missing", out _));
        Assert.Equal("null", present.Fields.Single(value => value.TargetPointer == "/note").Availability);
        Assert.Equal("absent-path", present.Fields.Single(value => value.TargetPointer == "/missing").Availability);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            request with { Purpose = ProjectionReadPurpose.Exact }));
    }

    [Fact]
    public async Task T11_required_source_and_roles_still_fail_and_executable_snapshots_refuse_display()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Owner, "field-read.profile", """{"type":"object"}"""));
        var definition = Define(setup, type, [new("profile", "", "/profile")]);
        var request = Request(definition) with { Purpose = ProjectionReadPurpose.Display };
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            request with { RoleEntityIds = new Dictionary<string, string>() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeSnapshotAsync(
            request, setup.Owner, []));
    }

    [Fact]
    public async Task T11_optional_root_mapping_has_honest_absence_evidence_and_exact_execution_still_fails()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Owner, "field-read.profile",
            """{"type":"object","properties":{"details":{"type":"object"}}}"""));
        var definition = Define(setup, type, [new("profile", "/details", "")], requiredSource: false);
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        var request = Request(definition) with { Purpose = ProjectionReadPurpose.Display };
        var absentSource = await setup.Materializer.MaterializeAsync(request);
        Assert.Equal("{}", absentSource.OutputJson);
        Assert.Equal("absent-source", Assert.Single(absentSource.Fields).Availability);
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(type), "{}", 0));
        var absentPath = await setup.Materializer.MaterializeAsync(request);
        Assert.Equal("{}", absentPath.OutputJson);
        Assert.Equal("absent-path", Assert.Single(absentPath.Fields).Availability);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            request with { Purpose = ProjectionReadPurpose.Exact }));
    }

    [Fact]
    public async Task T23_legacy_object_display_request_does_not_change_exact_semantics()
    {
        var setup = Setup();
        var first = setup.Types.Define(new(setup.Owner, "field-read.profile", """{"type":"object"}"""));
        var second = setup.Types.Define(new(setup.Owner, first.QualifiedId, """{"type":"object","maxProperties":5}"""));
        var definition = Define(setup, first, [new("profile", "", "/profile")], fieldBased: false);
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(second), "{}", 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            Request(definition) with { Purpose = ProjectionReadPurpose.Display }));
    }

    [Fact]
    public async Task T24_field_based_values_keep_declared_output_bounds()
    {
        var setup = Setup();
        var type = setup.Types.Define(new(setup.Owner, "field-read.profile", """{"type":"object"}"""));
        var definition = Define(setup, type, [new("profile", "", "/profile")], outputBytes: 64);
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(type),
            JsonSerializer.Serialize(new { note = new string('x', 100) }), 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Materializer.MaterializeAsync(
            Request(definition) with { Purpose = ProjectionReadPurpose.Display }));
    }

    [Fact]
    public void T12_read_purpose_is_not_a_deserializable_client_option()
    {
        var json = """{"StateSpaceId":"s","Projection":{"QualifiedId":"field-read.view","Version":1,"ContentHash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"},"RoleEntityIds":{},"Purpose":1}""";
        Assert.Equal(ProjectionReadPurpose.Exact, JsonSerializer.Deserialize<ProjectionMaterializationRequest>(json)!.Purpose);
        Assert.DoesNotContain("Purpose", JsonSerializer.Serialize(new ProjectionMaterializationRequest("s",
            new("field-read.view", 1, new string('A', 64)), new Dictionary<string, string>())
            { Purpose = ProjectionReadPurpose.Display }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T09_T20_current_collection_keeps_host_identity_paging_and_actual_revision_evidence(bool optimized)
    {
        var setup = Setup();
        var rootType = setup.Types.Define(new(setup.Owner, "field-read.profile",
            """{"type":"object","properties":{"title":{"type":"string"}}}"""));
        var oldItem = setup.Types.Define(new(setup.Owner, "field-read.item",
            """{"type":"object","properties":{"summary":{"type":"string"}}}"""));
        var itemType = setup.Types.Define(new(setup.Owner, oldItem.QualifiedId,
            """{"type":"object","properties":{"summary":{"type":"string"},"id":{"type":"string"},"newField":{"type":"boolean"}}}"""));
        var laterItemType = setup.Types.Define(new(setup.Owner, oldItem.QualifiedId,
            """{"type":"object","properties":{"summary":{"type":"string"},"id":{"type":"string"},"newField":{"type":"boolean"},"later":{"type":"string"}}}"""));
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(rootType), """{"title":"Directory"}""", 0));
        var edges = new SqliteStateSpaceEdgeStore(setup.Db, setup.Spaces);
        foreach (var id in new[] { "item.a", "item.b" })
        {
            await setup.Store.CreateEntityAsync(Space, id, id);
            await setup.Store.AddComponentAsync(new(Space, id, id == "item.a" ? Ref(itemType) : Ref(laterItemType),
                """{"summary":"Shown","id":"wrong-target","newField":true}""", 0));
            await edges.SetRelationshipAsync(Space, "actor", id, "field-read.includes", "{}", 0);
        }
        var contract = new ApplicationObjectContractRequest(
            [new("actor", true), new("item", false)], [new("profile", true)],
            [new("items", "field-read.includes", "actor", "item", "many", "/items", [new("to", Ref(oldItem))], [])], [],
            [new("items", "items", 1, 2, [new("/name", "asc")], "source-revision-bound")
            { Metadata = new("/page/totalCount", "/page/complete", "/page/nextCursor") }],
            new(1, 10, 32768, 12), new(["dm"], []), null)
        { ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId };
        var definition = setup.Registry.Define(new(setup.Owner, "field-read.directory",
            RegisteredApplicationObjectContract.TransportSchemaJson, [new("profile", "actor", Ref(rootType))], [],
            [new("profile", "/title", "/title")], contract, 1));
        var materializer = new ProjectionCollectionMaterializer(setup.Registry, setup.Materializer, edges,
            setup.Store, setup.Store, setup.Schemas, new SqliteProjectionReadTransaction(setup.Db),
            optimized ? new SqliteProjectionCollectionEndpointSelector(setup.Db) : null, setup.Types);
        var request = new ProjectionCollectionMaterializationRequest(Space, definition.Reference,
            new Dictionary<string, string> { ["actor"] = "actor" }, "items", "dm")
        { Purpose = ProjectionReadPurpose.Display };
        var result = await materializer.MaterializeAsync(request);
        using var json = JsonDocument.Parse(result.OutputJson);
        Assert.Equal("item.a", json.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.True(json.RootElement.GetProperty("items")[0].GetProperty("newField").GetBoolean());
        var page = json.RootElement.GetProperty("page");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.False(result.Complete);
        var cursor = page.GetProperty("nextCursor").GetString();
        Assert.Contains(result.SourceRevisions, value => value.EntityId == "item.a" && value.Type == Ref(itemType));
        Assert.Contains(result.SourceRevisions, value => value.EntityId == "item.b" && value.Type == Ref(laterItemType));
        Assert.Contains(result.ObservedSources, value => value.InputPath.SequenceEqual(
                ["collection", "items", "items", "to"])
            && value.DeclaredComponent == Ref(oldItem) && value.ActualComponent == Ref(itemType));
        Assert.DoesNotContain(result.ObservedSources, value => value.ActualComponent == Ref(laterItemType));
        var second = await materializer.MaterializeAsync(request with { Cursor = cursor });
        Assert.True(second.Complete);
        Assert.Contains(second.ObservedSources, value => value.ActualComponent == Ref(laterItemType));
        Assert.DoesNotContain(second.ObservedSources, value => value.ActualComponent == Ref(itemType));
        await setup.Store.SetComponentAsync(new(Space, "item.b", Ref(laterItemType), """{"summary":"Changed"}""", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(request with { Cursor = cursor }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => materializer.MaterializeAsync(request with { Perspective = "player" }));
    }

    [Fact]
    public async Task T09_T24_mixed_version_collection_batches_registered_identities_within_query_budget()
    {
        const int itemCount = 100;
        var setup = Setup();
        var rootType = setup.Types.Define(new(setup.Owner, "field-read.profile",
            """{"type":"object","properties":{"title":{"type":"string"}}}"""));
        var oldItem = setup.Types.Define(new(setup.Owner, "field-read.item",
            """{"type":"object","properties":{"summary":{"type":"string"}}}"""));
        await setup.Store.CreateEntityAsync(Space, "actor", "Actor");
        await setup.Store.AddComponentAsync(new(Space, "actor", Ref(rootType), """{"title":"Directory"}""", 0));
        var edges = new SqliteStateSpaceEdgeStore(setup.Db, setup.Spaces);
        for (var index = 0; index < itemCount; index++)
        {
            var itemType = setup.Types.Define(new(setup.Owner, oldItem.QualifiedId,
                JsonSerializer.Serialize(new { type = "object", properties = new Dictionary<string, object>
                    { ["summary"] = new { type = "string" }, [$"extra{index}"] = new { type = "boolean" } } })));
            var id = $"item.{index:D3}";
            await setup.Store.CreateEntityAsync(Space, id, id);
            await setup.Store.AddComponentAsync(new(Space, id, Ref(itemType), """{"summary":"Shown"}""", 0));
            await edges.SetRelationshipAsync(Space, "actor", id, "field-read.includes", "{}", 0);
        }
        var contract = new ApplicationObjectContractRequest(
            [new("actor", true), new("item", false)], [new("profile", true)],
            [new("items", "field-read.includes", "actor", "item", "many", "/items", [new("to", Ref(oldItem))], [])], [],
            [new("items", "items", 10, 10, [new("/name", "asc")], "source-revision-bound")
            { Metadata = new("/totalCount", "/complete", "/nextCursor") }],
            new(1, itemCount, 32768, 12), new(["dm"], []), null)
        { ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId };
        var definition = setup.Registry.Define(new(setup.Owner, "field-read.directory",
            RegisteredApplicationObjectContract.TransportSchemaJson, [new("profile", "actor", Ref(rootType))], [],
            [new("profile", "/title", "/title")], contract, 1));

        var counter = new CommandCounter();
        await using var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Connection).AddInterceptors(counter).Options);
        var spaces = new SqliteStateSpaceRegistry(db, new SqliteApplicationRegistry(db));
        var types = new SqliteComponentTypeRegistry(db, setup.Schemas);
        var store = new SqliteEntityComponentStore(db, types, setup.Schemas);
        var registry = new SqliteProjectionDefinitionRegistry(db, types, setup.Schemas);
        var root = new ProjectionMaterializer(registry, store, spaces, setup.Schemas,
            snapshots: new SqliteProjectionSourceSnapshotReader(db, spaces, store), componentTypes: types);
        var materializer = new ProjectionCollectionMaterializer(registry, root,
            new SqliteStateSpaceEdgeStore(db, spaces), store, store, setup.Schemas,
            new SqliteProjectionReadTransaction(db), new SqliteProjectionCollectionEndpointSelector(db), types);
        Assert.NotNull(registry.Get(definition.QualifiedId, definition.Version));
        var request = new ProjectionCollectionMaterializationRequest(Space, definition.Reference,
            new Dictionary<string, string> { ["actor"] = "actor" }, "items", "dm")
        { Purpose = ProjectionReadPurpose.Display };
        string? cursor = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < itemCount / 10; page++)
        {
            counter.Commands.Clear();
            var result = await materializer.MaterializeAsync(request with { Cursor = cursor });
            Assert.True(counter.Commands.Count <= contract.Limits.SqlQueries,
                $"Mixed-version page used {counter.Commands.Count} queries:\n" + string.Join('\n', counter.Commands));
            Assert.Single(counter.Commands, command => command.Contains("system_component_type_version", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(result.OutputJson);
            Assert.Equal(itemCount, json.RootElement.GetProperty("totalCount").GetInt32());
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                Assert.Equal("Shown", item.GetProperty("summary").GetString());
                Assert.True(ids.Add(item.GetProperty("id").GetString()!));
            }
            cursor = json.RootElement.GetProperty("nextCursor").GetString();
            Assert.Equal(page == 9, result.Complete);
        }
        Assert.Null(cursor);
        Assert.Equal(itemCount, ids.Count);
    }

    [Fact]
    public void T09_T24_registered_identity_batch_uses_exact_pairs_and_excludes_schema_payloads()
    {
        var setup = Setup();
        var first = setup.Types.Define(new(setup.Owner, "field-read.profile", """{"type":"object"}"""));
        var second = setup.Types.Define(new(setup.Owner, first.QualifiedId, """{"type":"object","maxProperties":5}"""));
        var counter = new CommandCounter();
        using var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Connection).AddInterceptors(counter).Options);
        var types = new SqliteComponentTypeRegistry(db, setup.Schemas);
        var actual = types.ReadIdentities([Ref(first), Ref(first), new(first.QualifiedId, 999, new string('A', 64))]);
        var identity = Assert.Single(actual);
        Assert.Equal(first.Version, identity.Version);
        Assert.Equal(first.SchemaHash, identity.SchemaHash);
        Assert.NotEqual(second.SchemaHash, identity.SchemaHash);
        var sql = Assert.Single(counter.Commands);
        Assert.DoesNotContain("SchemaJson", sql, StringComparison.Ordinal);
        Assert.Empty(types.ReadIdentities([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => types.ReadIdentities(Enumerable.Repeat(Ref(first), 513).ToArray()));
        Assert.Throws<ArgumentException>(() => types.ReadIdentities([new(first.QualifiedId, 0, first.SchemaHash)]));
        Assert.Single(counter.Commands);
    }

    [Fact]
    public void Authorized_schema_batch_is_exact_bounded_and_returns_registered_versions()
    {
        var setup = Setup();
        var first = setup.Types.Define(new(setup.Owner, "field-read.batch-schema",
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"));
        var second = setup.Types.Define(new(setup.Owner, first.QualifiedId,
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"note\":{\"type\":\"string\"}}}"));
        var counter = new CommandCounter();
        using var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(fixture.Connection).AddInterceptors(counter).Options);
        IApplicationComponentTypeVersionReader versions = new SqliteComponentTypeRegistry(db, setup.Schemas);

        var actual = versions.ReadVersions([Ref(first), Ref(second), Ref(first)]);

        Assert.Equal(2, actual.Count);
        Assert.Single(counter.Commands);
        Assert.Contains(actual, value => value.Version == second.Version && value.SchemaJson == second.SchemaJson);
        Assert.Empty(versions.ReadVersions([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => versions.ReadVersions(
            Enumerable.Repeat(Ref(first), 513).ToArray()));
        Assert.Single(counter.Commands);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static ProjectionMaterializationRequest Request(RegisteredProjectionDefinition definition) =>
        new(Space, definition.Reference, new Dictionary<string, string> { ["actor"] = "actor" });

    private static RegisteredProjectionDefinition Define(SetupState setup, RegisteredComponentTypeVersion type,
        IReadOnlyList<StructuralProjectionMapping> mappings, bool requiredSource = true, bool fieldBased = true,
        int outputBytes = 32768) => setup.Registry.Define(new(setup.Owner, "field-read.view",
            RegisteredApplicationObjectContract.TransportSchemaJson, [new("profile", "actor", Ref(type))], [], mappings,
            new ApplicationObjectContractRequest([new("actor", true)], [new("profile", requiredSource)], [], [], [],
                new(1, 10, outputBytes, 12), new(["dm"], []), null)
            { ProfileId = fieldBased ? RegisteredApplicationObjectContract.FieldBasedContractProfileId
                : RegisteredApplicationObjectContract.ContractProfileId }, 1));

    private SetupState Setup()
    {
        var db = fixture.CreateContext();
        var owner = ApplicationIdentifier.Parse("field-read");
        var apps = new SqliteApplicationRegistry(db);
        var revision = apps.Register(new(owner, "Field reads", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, apps);
        spaces.Create(new(Space, revision, new string('A', 64)));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var store = new SqliteEntityComponentStore(db, types, schemas);
        var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas);
        return new(db, owner, spaces, schemas, types, store, registry,
            new(registry, store, spaces, schemas, snapshots: new SqliteProjectionSourceSnapshotReader(db, spaces, store),
                componentTypes: types));
    }

    private sealed record SetupState(DantesRoleplayDbContext Db, ApplicationIdentifier Owner,
        SqliteStateSpaceRegistry Spaces, BoundedJsonSchemaValidator Schemas, SqliteComponentTypeRegistry Types,
        SqliteEntityComponentStore Store, SqliteProjectionDefinitionRegistry Registry, ProjectionMaterializer Materializer);
    private static EcsComponentReference Ref(RegisteredComponentTypeVersion value) => new(value.QualifiedId, value.Version, value.SchemaHash);
    public void Dispose() => fixture.Dispose();
}

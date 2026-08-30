using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using DantesRoleplay.World;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Effects;
using DantesRoleplay.Operations;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationMechanicExecutionTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Application_projection_is_byte_identical_to_legacy_projection_for_declared_graph()
    {
        await using var db = _fixture.CreateContext();
        var now = DateTime.UtcNow;
        db.ComponentDefinitions.AddRange(Definition("alpha", now), Definition("link", now), Definition("child", now));
        db.Entities.AddRange(Entity("actor", "Actor", now), Entity("container", "Container", now),
            Entity("child", "Child", now), Entity("target", "Target", now), Entity("reference", "Reference", now));
        db.Components.AddRange(
            Component("actor", "alpha", "{\"value\":1}", now),
            Component("actor", "link", "{\"targetRef\":{\"entityId\":\"reference\"}}", now),
            Component("child", "child", "{\"value\":2}", now),
            Component("reference", "alpha", "{\"value\":3}", now),
            Component("target", "alpha", "{\"value\":4}", now));
        db.Containments.AddRange(
            new Containment { ContainerId = "container", ContainedId = "actor", Slot = "inside", CreatedAt = now },
            new Containment { ContainerId = "actor", ContainedId = "child", Slot = "held", CreatedAt = now });
        db.Relationships.Add(new Relationship
        {
            FromEntityId = "actor", ToEntityId = "target", Kind = "knows", Data = "{\"strength\":2}", CreatedAt = now
        });
        await db.SaveChangesAsync();

        var applications = new SqliteApplicationRegistry(db);
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = applications.Register(new(app, "Fixture", "Projection fixture.", []));
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("space", revision, new string('A', 64)));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var alpha = types.Define(new(app, "fixture.alpha", "{}"));
        var link = types.Define(new(app, "fixture.link", "{}"));
        var child = types.Define(new(app, "fixture.child", "{}"));
        var store = new SqliteEntityComponentStore(db, types, schemas);
        foreach (var value in new[] { ("actor", "Actor"), ("container", "Container"), ("child", "Child"), ("target", "Target"), ("reference", "Reference") })
            await store.CreateEntityAsync("space", value.Item1, value.Item2);
        await store.AddComponentAsync(Write("actor", alpha, "{\"value\":1}"));
        await store.AddComponentAsync(Write("actor", link,
            "{\"targetRef\":{\"entityId\":\"reference\"}}"));
        await store.AddComponentAsync(Write("child", child, "{\"value\":2}"));
        await store.AddComponentAsync(Write("reference", alpha, "{\"value\":3}"));
        await store.AddComponentAsync(Write("target", alpha, "{\"value\":4}"));
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        await edges.MoveContainmentAsync("space", "actor", "container", "inside", 0);
        await edges.MoveContainmentAsync("space", "child", "actor", "held", 0);
        await edges.SetRelationshipAsync("space", "actor", "target", "fixture.knows", "{\"strength\":2}", 0);

        var requirements = new MechanicRequirements
        {
            Roles = new Dictionary<string, RoleRequirement>
            {
                ["subject"] = new(["alpha", "link"], IncludeContents: true,
                    IncludeRelationships: true, ContentsDepth: 2, ContentComponentIds: ["child"],
                    ComponentReferences: [new("link", "targetRef", ["alpha"])],
                    RelationshipComponents: [new("knows", "outgoing", ["alpha"])])
            }
        };
        var roles = new Dictionary<string, string> { ["subject"] = "actor" };
        var legacy = await new ProjectionResolver(db).ResolveAsync(requirements, roles, "{\"x\":1}", 42);
        var mapping = new ApplicationMechanicProjectionMapping(new Dictionary<string, EcsComponentReference>
        {
            ["alpha"] = Reference(alpha), ["link"] = Reference(link), ["child"] = Reference(child)
        }, new Dictionary<string, string> { ["knows"] = "fixture.knows" });
        var application = await new ApplicationMechanicProjectionResolver(db, stateSpaces)
            .ResolveAsync("space", app, requirements, mapping, roles, "{\"x\":1}", 42);

        Assert.True(legacy.Ok, string.Join("; ", legacy.Problems));
        Assert.True(application.Ok, string.Join("; ", application.Problems));
        Assert.Equal("space", application.Projection!.StateSpaceId);
        Assert.Equal(JsonSerializer.Serialize(legacy.Projection), JsonSerializer.Serialize(application.Projection with { StateSpaceId = string.Empty }));
        Assert.Equal(1, application.Projection.ComponentRevisions["actor"]["alpha"]);
        Assert.Equal(1, application.Projection.ComponentRevisions["child"]["child"]);
        Assert.Equal(1, application.Projection.ComponentRevisions["reference"]["alpha"]);
        Assert.Equal(1, application.Projection.ComponentRevisions["target"]["alpha"]);
        var related = Assert.Single(application.Projection.Roles["subject"].Related!);
        Assert.Equal("target", related.Id);
        Assert.Equal(["alpha"], related.Components.Keys);
        Assert.Equal(["child"], application.Projection.ContainmentRevisions["actor"]
            .Select(value => value.EntityId));
        Assert.Empty(application.Projection.ContainmentRevisions["child"]);
    }

    [Fact]
    public async Task Exact_catalog_evaluator_invokes_all_fourteen_ratified_mechanics_with_parity()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var files = RatifiedMechanics();
        Assert.Equal(14, files.Length);
        var records = files.Select(file =>
        {
            var content = JsonSerializer.Serialize(new { requirements = file.Requirements, source = file.Source });
            return new CatalogRecordDefinition(app.Value, "mechanic", app.Value + "." + file.Id,
                file.Name, SingleLine(file.Description), [], [], "mechanics", "active", 1, content,
                Hash(content), "catalog", file.Id + ".md");
        }).ToArray();
        var manifest = CatalogNavigationManifest.Create(app, new string('A', 64), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Fixture mechanics.")],
            [new(app.Value, "", "Fixture", "Fixture mechanics.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)], records);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("application-execution-test-cursor-key-32-bytes")));
        var provider = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            { [app] = navigator });
        var projection = new MechanicProjection { Input = "{}", Seed = 4242 };
        var resolver = new StaticResolver(projection);
        var engine = new JintMechanicEngine();
        var evaluator = new ApplicationMechanicEvaluator(provider, resolver, engine);

        foreach (var (file, record) in files.Zip(records))
        {
            var expected = await engine.RunAsync(file.Source, projection, ExecutionLimits.Default);
            var actual = await evaluator.EvaluateAsync(new("space", app, record.QualifiedId,
                record.ContentFingerprint, new(new Dictionary<string, EcsComponentReference>(),
                    new Dictionary<string, string>()), new Dictionary<string, string>(), "{}", 4242));
            Assert.True(actual.Evaluated, string.Join("; ", actual.Problems));
            Assert.Equal(Comparable(expected), Comparable(actual.Run!));
        }
    }

    [Fact]
    public async Task Composed_child_snapshots_are_merged_into_the_root_authority_envelope()
    {
        var app = ApplicationIdentifier.Parse("snapshot");
        var childContent = JsonSerializer.Serialize(new
        {
            requirements = "{\"roles\":{\"subject\":{\"components\":[\"state\"]}}}",
            source = "return {data:{ok:true},effects:[],events:[],notifications:[]};"
        });
        var parentContent = JsonSerializer.Serialize(new
        {
            requirements = "{\"roles\":{\"container\":{\"components\":[]}},\"children\":{\"state\":{\"mechanicId\":\"mechanic.child\",\"roleBindings\":{\"subject\":\"container\"},\"inheritInput\":false,\"input\":\"{}\"}}}",
            source = "return {data:{ok:true},effects:[],events:[],notifications:[]};"
        });
        var child = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.child",
            "Child", "Child.", [], [], "mechanics", "active", 1, childContent, Hash(childContent),
            "source", "mechanics/child.md");
        var parent = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.parent",
            "Parent", "Parent.", [], [], "mechanics", "active", 1, parentContent, Hash(parentContent),
            "source", "mechanics/parent.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("snapshot-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Snapshot", "Snapshot catalog.")],
            [new(app.Value, "", "Snapshot", "Snapshot catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)],
            [child, parent]);
        var provider = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("snapshot-merge-test-cursor-key-32")))
        });
        var result = await new ApplicationMechanicEvaluator(
            provider, new CompositionSnapshotResolver(), new JintMechanicEngine()).EvaluateAsync(new(
            "space", app, parent.QualifiedId, parent.ContentFingerprint,
            new(new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>()),
            new Dictionary<string, string> { ["container"] = "entity" }, "{}", 42));

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        Assert.Equal(7, result.Projection!.ComponentRevisions["entity"]["state"]);
        Assert.Equal([new ContainmentRevision("item", "slot", 3)],
            result.Projection.ContainmentRevisions["entity"]);
    }

    [Fact]
    public async Task Host_derives_and_projects_immutable_child_operation_identity()
    {
        var app = ApplicationIdentifier.Parse("identity");
        var childContent = JsonSerializer.Serialize(new
        {
            requirements = "{}",
            source = "return {data:{execution:ctx.execution},effects:[],events:[],notifications:[]};"
        });
        var parentContent = JsonSerializer.Serialize(new
        {
            requirements = "{\"children\":{\"child\":{\"mechanicId\":\"mechanic.child\",\"roleBindings\":{},\"inheritInput\":false,\"input\":\"{}\"}}}",
            source = "var rejected=0;try{ctx.execution.operationId='forged';}catch(error){rejected++;}return {data:{execution:ctx.execution,child:ctx.children.child[0].execution,childData:JSON.parse(ctx.children.child[0].output.data).execution,rejected:rejected,caller:ctx.input.execution.operationId},effects:[],events:[],notifications:[]};"
        });
        var child = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.child",
            "Child", "Child.", [], [], "mechanics", "active", 1, childContent, Hash(childContent),
            "source", "mechanics/child.md");
        var parent = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.parent",
            "Parent", "Parent.", [], [], "mechanics", "active", 1, parentContent, Hash(parentContent),
            "source", "mechanics/parent.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("identity-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Identity", "Identity catalog.")],
            [new(app.Value, "", "Identity", "Identity catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)],
            [child, parent]);
        var provider = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("identity-test-cursor-key-32-bytes")))
        });
        var rootId = "0123456789abcdef0123456789abcdef";
        var execution = new MechanicExecutionContext(rootId, rootId, null, 0);
        var result = await new ApplicationMechanicEvaluator(
            provider, new PassThroughResolver(), new JintMechanicEngine()).EvaluateAsync(new(
            "space", app, parent.QualifiedId, parent.ContentFingerprint,
            new(new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>()),
            new Dictionary<string, string>(),
            "{\"execution\":{\"operationId\":\"caller-forged\"}}", 42, execution));

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        var childExecution = Assert.Single(result.Projection!.Children["child"]).Execution;
        Assert.NotNull(childExecution);
        var canonical = string.Join('\n', "mechanic-child-operation-v1", rootId, rootId, "0",
            child.QualifiedId, child.ContentFingerprint);
        var expectedChildId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32]
            .ToLowerInvariant();
        Assert.Equal(new MechanicExecutionContext(rootId, expectedChildId, rootId, 0), childExecution);

        using var data = JsonDocument.Parse(result.Run!.Output.Data);
        var root = data.RootElement;
        Assert.Equal(rootId, root.GetProperty("execution").GetProperty("operationId").GetString());
        Assert.Equal(expectedChildId, root.GetProperty("child").GetProperty("operationId").GetString());
        Assert.Equal(expectedChildId, root.GetProperty("childData").GetProperty("operationId").GetString());
        Assert.Equal(1, root.GetProperty("rejected").GetInt32());
        Assert.Equal("caller-forged", root.GetProperty("caller").GetString());
    }

    [Fact]
    public async Task Exact_application_action_applies_to_bound_state_once_and_replays_by_identity()
    {
        await using var db = _fixture.CreateContext();
        var app = ApplicationIdentifier.Parse("fixture-action");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(app, "Fixture action", "", []));
        var activationFingerprint = Hash("fixture-action-activation");
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("action-space", revision, activationFingerprint));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var createdState = types.Define(new(app, "fixture-action.created-state", "{}"));
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        var operations = new OperationLog(db);
        var effectApplier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges);
        var content = JsonSerializer.Serialize(new
        {
            id = "mechanic.create",
            requirements = "{\"roles\":{\"declarations\":{\"components\":[\"created-state\"]}}}",
            source = "return { effects: [] };"
        });
        var record = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.create",
            "Create", "Create a fixture.", [], [], "mechanics", "active", 1, content, Hash(content),
            "source", "mechanics/create.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("fixture-action-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Fixture catalog.")],
            [new(app.Value, "", "Fixture", "Fixture catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Fixture mechanics.", CatalogDescriptionStatus.Authored)],
            [record]);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("application-action-test-cursor-key-32")))
        });
        var activation = new StaticActivation(new(app, 1, revision.Revision, revision.Fingerprint,
            Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
            "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow));
        var evaluator = new StaticEvaluator(new ApplicationMechanicEvaluationResult(record.QualifiedId,
            record.ContentFingerprint, new MechanicProjection(), new MechanicRunResult
            {
                Ok = true,
                Seed = 42,
                Output = new MechanicOutput
                {
                    Effects =
                    [
                        new Effect { Type = EffectType.EntityCreate, EntityId = "created", Name = "Created" },
                        new Effect
                        {
                            Type = EffectType.ComponentAdd,
                            EntityId = "created",
                            DefinitionId = "created-state",
                            Data = "{\"ready\":true}"
                        }
                    ],
                    Narration = "A fixture appears."
                }
            }, []));
        var runner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
            evaluator, effectApplier, operations);
        var request = new ApplicationActionExecutionRequest("action-space", app, record.QualifiedId,
            record.ContentFingerprint, new Dictionary<string, string>(), "{}", 42,
            new("0123456789abcdef0123456789abcdef", new string('A', 64)));

        var first = await runner.RunAsync(request);
        var replay = await runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(new MechanicExecutionContext(
            request.ExecutionIdentity.OperationId,
            request.ExecutionIdentity.OperationId,
            null,
            0), evaluator.LastRequest!.Execution);
        Assert.Equal("A fixture appears.", first.Narration);
        Assert.Equal(2, first.AppliedEffectCount);
        Assert.NotNull(await entities.GetEntityAsync("action-space", "created"));
        var component = await entities.GetComponentAsync(
            "action-space", "created", createdState.QualifiedId);
        Assert.NotNull(component);
        Assert.Equal("{\"ready\":true}", component.ValueJson);
        Assert.Single((await entities.ListEntitiesAsync("action-space", null, 10)).Entities);
    }

    public void Dispose() => _fixture.Dispose();

    private static MechanicFile[] RatifiedMechanics()
    {
        var root = RepositoryRoot();
        return new[] { "mechanics/game/core", "mechanics/check", "mechanics/change" }
            .SelectMany(relative => Directory.EnumerateFiles(Path.Combine(root, "catalog", relative.Replace('/', Path.DirectorySeparatorChar)), "*.md", SearchOption.AllDirectories))
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(path => MechanicFile.Parse(File.ReadAllText(path), Path.GetRelativePath(Path.Combine(root, "catalog"), path).Replace('\\', '/'), File.ReadAllText(Path.ChangeExtension(path, ".js"))))
            .ToArray();
    }

    private static string Comparable(MechanicRunResult value) => JsonSerializer.Serialize(new
    {
        value.Ok, value.Output, value.Error, value.LimitHit, value.Log, value.Seed
    });
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string SingleLine(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static ComponentDefinition Definition(string id, DateTime now) => new() { Id = id, Name = id, Description = id, Schema = "{}", CreatedAt = now, UpdatedAt = now };
    private static Entity Entity(string id, string name, DateTime now) => new() { Id = id, Name = name, CreatedAt = now };
    private static Component Component(string entity, string definition, string data, DateTime now) => new() { EntityId = entity, DefinitionId = definition, Data = data, Revision = 1, CreatedAt = now, UpdatedAt = now };
    private static EcsComponentWrite Write(string entity, RegisteredComponentTypeVersion type, string data) => new("space", entity, Reference(type), data, 0);
    private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) => new(type.QualifiedId, type.Version, type.SchemaHash);
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private sealed class StaticResolver(MechanicProjection projection) : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default) => Task.FromResult(new ProjectionResult(projection, []));
    }

    private sealed class CompositionSnapshotResolver : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default)
        {
            var projection = new MechanicProjection { Input = inputJson, Seed = seed };
            if (requirements.Roles.ContainsKey("container"))
                projection.Roles["container"] = new("entity", "Entity", new Dictionary<string, string>());
            if (requirements.Roles.ContainsKey("subject"))
            {
                projection.Roles["subject"] = new("entity", "Entity",
                    new Dictionary<string, string> { ["state"] = "{\"value\":1}" });
                projection.ComponentRevisions["entity"] = new(StringComparer.Ordinal) { ["state"] = 7 };
                projection.ContainmentRevisions["entity"] = [new("item", "slot", 3)];
            }
            return Task.FromResult(new ProjectionResult(projection, []));
        }
    }

    private sealed class PassThroughResolver : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default) => Task.FromResult(new ProjectionResult(
                new MechanicProjection { StateSpaceId = stateSpaceId, Input = inputJson, Seed = seed }, []));
    }

    private sealed class StaticActivation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == value.ApplicationId ? value : null;
    }

    private sealed class StaticEvaluator(ApplicationMechanicEvaluationResult value) : IApplicationMechanicEvaluator
    {
        public ApplicationMechanicEvaluationRequest? LastRequest { get; private set; }

        public Task<ApplicationMechanicEvaluationResult> EvaluateAsync(ApplicationMechanicEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(value);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
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
            Entity("child", "Child", now), Entity("target", "Target", now), Entity("reference", "Reference", now),
            Entity("identity", "Identity only", now));
        db.Components.AddRange(
            Component("actor", "alpha", "{\"value\":1}", now),
            Component("actor", "link", "{\"targetRef\":{\"entityId\":\"reference\"},\"nested\":{\"targets\":[{\"entity\":{\"entityId\":\"identity\"}}]}}", now),
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
        var beta = types.Define(new(app, "fixture.beta", "{}"));
        var link = types.Define(new(app, "fixture.link", "{}"));
        var child = types.Define(new(app, "fixture.child", "{}"));
        var store = new SqliteEntityComponentStore(db, types, schemas);
        foreach (var value in new[] { ("actor", "Actor"), ("container", "Container"), ("child", "Child"), ("target", "Target"), ("reference", "Reference"), ("identity", "Identity only") })
            await store.CreateEntityAsync("space", value.Item1, value.Item2);
        await store.AddComponentAsync(Write("actor", alpha, "{\"value\":1}"));
        await store.AddComponentAsync(Write("actor", link,
            "{\"targetRef\":{\"entityId\":\"reference\"},\"nested\":{\"targets\":[{\"entity\":{\"entityId\":\"identity\"}}]}}"));
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
                    ComponentReferences:
                    [
                        new("link", "targetRef", ["alpha"], ["beta"]),
                        new("link", "nested.targets[].entity", [])
                    ],
                    RelationshipComponents: [new("knows", "outgoing", ["alpha"])])
            }
        };
        var roles = new Dictionary<string, string> { ["subject"] = "actor" };
        var legacy = await new ProjectionResolver(db).ResolveAsync(requirements, roles, "{\"x\":1}", 42);
        var mapping = new ApplicationMechanicProjectionMapping(new Dictionary<string, EcsComponentReference>
        {
            ["alpha"] = Reference(alpha), ["beta"] = Reference(beta),
            ["link"] = Reference(link), ["child"] = Reference(child)
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
        Assert.DoesNotContain("beta", application.Projection.References["reference"].Components.Keys);
        Assert.Equal("Reference", application.Projection.References["reference"].Name);
        Assert.Equal("Identity only", application.Projection.References["identity"].Name);
        Assert.Empty(application.Projection.References["identity"].Components);
        Assert.Equal(1, application.Projection.ComponentRevisions["target"]["alpha"]);
        var related = Assert.Single(application.Projection.Roles["subject"].Related!);
        Assert.Equal("target", related.Id);
        Assert.Equal(["alpha"], related.Components.Keys);
        Assert.Equal(["child"], application.Projection.ContainmentRevisions["actor"]
            .Select(value => value.EntityId));
        Assert.Empty(application.Projection.ContainmentRevisions["child"]);
    }

    [Fact]
    public async Task Exact_catalog_evaluator_invokes_all_ratified_mechanics_with_parity()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var files = RatifiedMechanics();
        Assert.Equal(23, files.Length);
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
    public async Task Exact_application_composition_carries_foreach_child_effects_and_rejects_stale_pins()
    {
        var app = ApplicationIdentifier.Parse("composite");
        var childContent = JsonSerializer.Serialize(new
        {
            requirements = "{}",
            source = "return {effects:[{type:'entity.create',entityId:ctx.input.entityId,name:ctx.input.name}],data:{entityId:ctx.input.entityId}};"
        });
        var child = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.child",
            "Child", "Child.", [], [], "mechanics", "active", 1, childContent, Hash(childContent),
            "source", "mechanics/child.md");
        string ParentContent(string fingerprint) => JsonSerializer.Serialize(new
        {
            requirements = $$$$"""{"children":{"items":{"mechanicId":"mechanic.child","mechanicVersion":1,"contentFingerprint":"{{{{fingerprint}}}}","roleBindings":{},"after":["zFirst"],"inheritInput":false,"forEachInputProperty":"items"},"zFirst":{"mechanicId":"mechanic.child","mechanicVersion":1,"contentFingerprint":"{{{{fingerprint}}}}","roleBindings":{},"inheritInput":false,"input":"{\"entityId\":\"first\",\"name\":\"First\"}"}}}""",
            source = "return {effects:[{type:'entity.create',entityId:'parent',name:'Parent'}],data:{childCount:ctx.children.items.length}};"
        });
        var validContent = ParentContent(child.ContentFingerprint);
        var staleContent = ParentContent(Hash("stale-child"));
        var valid = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.valid",
            "Valid", "Valid.", [], [], "mechanics", "active", 1, validContent, Hash(validContent),
            "source", "mechanics/valid.md");
        var stale = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.stale",
            "Stale", "Stale.", [], [], "mechanics", "active", 1, staleContent, Hash(staleContent),
            "source", "mechanics/stale.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("composite-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Composite", "Composite catalog.")],
            [new(app.Value, "", "Composite", "Composite catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)],
            [child, valid, stale]);
        var provider = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("composite-test-cursor-signing-key-32")))
        });
        var evaluator = new ApplicationMechanicEvaluator(provider, new PassThroughResolver(), new JintMechanicEngine());
        var input = "{\"items\":[{\"entityId\":\"child-a\",\"name\":\"A\"},{\"entityId\":\"child-b\",\"name\":\"B\"}]}";
        var mapping = new ApplicationMechanicProjectionMapping(new Dictionary<string, EcsComponentReference>(),
            new Dictionary<string, string>());

        var result = await evaluator.EvaluateAsync(new("space", app, valid.QualifiedId,
            valid.ContentFingerprint, mapping, new Dictionary<string, string>(), input, 42));
        var staleResult = await evaluator.EvaluateAsync(new("space", app, stale.QualifiedId,
            stale.ContentFingerprint, mapping, new Dictionary<string, string>(), input, 42));

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        Assert.Equal(["first", "child-a", "child-b"], result.Proposal.Effects.Select(value => value.EntityId));
        Assert.Equal("parent", Assert.Single(result.Run!.Output.Effects).EntityId);
        Assert.Equal(2, result.Projection!.Children["items"].Count);
        Assert.False(staleResult.Evaluated);
        Assert.Contains("CHILD_STALE", Assert.Single(staleResult.Problems));
    }

    [Fact]
    public async Task Furnished_location_composite_is_found_by_one_intent_and_pins_every_primitive()
    {
        var files = (await CatalogReader.ReadAsync(Path.Combine(RepositoryRoot(), "catalog"))).Mechanics;
        var composite = Assert.Single(files, value =>
            value.Id == "mechanic.game.core.world.location.register-furnished");
        var content = CatalogMechanicContent(composite);
        var app = ApplicationIdentifier.Parse("fixture");
        var record = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + "." + composite.Id,
            composite.Name, SingleLine(composite.Description), [], composite.Matches.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            "mechanics", "active", 1, content, Hash(content), "catalog", "mechanics/register-furnished.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("furnished-location-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Fixture catalog.")],
            [new(app.Value, "", "Fixture", "Fixture catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)], [record]);
        var navigator = new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes("furnished-location-search-key-32-bytes")));

        var hit = Assert.Single(navigator.Search(new(app, "register a furnished location")).Records);
        Assert.Equal(record.QualifiedId, hit.Record.QualifiedId);
        var requirements = MechanicRequirements.Parse(composite.Requirements);
        Assert.Equal(7, requirements.Children.Count);
        Assert.All(requirements.Children.Where(value => value.Key != "shell"),
            child => Assert.Contains("shell", child.Value.After));
        Assert.All(requirements.Children.Values, child =>
        {
            Assert.Equal(1, child.MechanicVersion);
            var target = Assert.Single(files, value => value.Id == child.MechanicId);
            Assert.Equal(Hash(CatalogMechanicContent(target)), child.ContentFingerprint);
        });
        Assert.Equal(JsonValueKind.Object, requirements.InputSchema!.Value.ValueKind);
    }

    [Fact]
    public async Task Furnished_location_composite_evaluates_the_complete_real_child_graph_in_creation_order()
    {
        var files = (await CatalogReader.ReadAsync(Path.Combine(RepositoryRoot(), "catalog"))).Mechanics;
        var wanted = files.Where(value => value.Id.StartsWith(
                "mechanic.game.core.world.location.", StringComparison.Ordinal)
            && value.Id is not "mechanic.game.core.world.location.move").ToArray();
        Assert.Equal(9, wanted.Length);
        var app = ApplicationIdentifier.Parse("fixture");
        var records = wanted.Select(file =>
        {
            var content = CatalogMechanicContent(file);
            return new CatalogRecordDefinition(app.Value, "mechanic", app.Value + "." + file.Id,
                file.Name, SingleLine(file.Description), [], file.Matches.Split('\n',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                "mechanics", "active", 1, content, Hash(content), "catalog",
                "mechanics/" + file.Id.Split('.')[^1] + ".md");
        }).ToArray();
        var manifest = CatalogNavigationManifest.Create(app, Hash("real-furnished-location-catalog"),
            "catalog-lexical-v1", [new(app.Value, "Fixture", "Fixture catalog.")],
            [new(app.Value, "", "Fixture", "Fixture catalog.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)], records);
        var provider = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("real-furnished-location-key-32-bytes")))
        });
        var composite = Assert.Single(records, value =>
            value.QualifiedId.EndsWith(".register-furnished", StringComparison.Ordinal));
        var input = JsonSerializer.Serialize(new
        {
            worldId = "world.fixture",
            parentId = "location.fixture.parent",
            location = new
            {
                locationId = "location.fixture.workshop", name = "Workshop", kind = "interior",
                status = "active", summary = "A complete reviewed workshop.", visibility = "party"
            },
            furnishings = new[]
            {
                new
                {
                    definition = new
                    {
                        furnishingId = "furnishing.fixture.table", name = "Drafting table",
                        status = "active", summary = "A reviewed drafting table.", visibility = "party"
                    },
                    placement = new
                    {
                        locationId = "location.fixture.workshop", locationName = "Workshop",
                        locationStatus = "active", furnishingId = "furnishing.fixture.table",
                        furnishingName = "Drafting table", furnishingStatus = "active"
                    }
                }
            },
            connections = new[]
            {
                new
                {
                    locationId = "location.fixture.workshop", locationName = "Workshop",
                    locationStatus = "active", targetLocationId = "location.fixture.hall"
                }
            },
            relevantFacts = new[]
            {
                new
                {
                    locationId = "location.fixture.workshop", locationName = "Workshop",
                    knowledgeId = "fact.fixture.charter"
                }
            },
            media = new[]
            {
                new
                {
                    locationId = "location.fixture.workshop", locationName = "Workshop",
                    locationStatus = "active",
                    attachments = new[]
                    {
                        new
                        {
                            role = "setting", visibility = new[] { "player", "dm" },
                            sha256 = new string('a', 64), mimeType = "image/png", width = 1200,
                            height = 675, alt = "The workshop.", caption = "Workshop", order = 0,
                            provenance = new
                            {
                                kind = "original", credit = "Fixture", source = "Finalized receipt",
                                reviewedOn = "2026-09-03", version = 1
                            }
                        }
                    }
                }
            }
        });
        var evaluator = new ApplicationMechanicEvaluator(provider,
            new FurnishedCompositeResolver(), new JintMechanicEngine());
        var result = await evaluator.EvaluateAsync(new("space", app, composite.QualifiedId,
            composite.ContentFingerprint,
            new(new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>()),
            new Dictionary<string, string>
            {
                ["world"] = "world.fixture", ["parent"] = "location.fixture.parent"
            }, input, 42));

        Assert.True(result.Ok, result.Run?.Error ?? string.Join("; ", result.Problems));
        Assert.Equal(9, result.Proposal.Effects.Count);
        Assert.Equal(EffectType.EntityCreate, result.Proposal.Effects[0].Type);
        Assert.Equal("location.fixture.workshop", result.Proposal.Effects[0].EntityId);
        Assert.Equal(EffectType.ComponentAdd, result.Proposal.Effects[1].Type);
        Assert.Equal("game.core.world.location", result.Proposal.Effects[1].DefinitionId);
        Assert.Equal(7, result.Projection!.Children.Count);
        Assert.Empty(result.Run!.Output.Effects);
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
            }, [])
        {
            Proposal = new CompositionProposal(
                [new Effect { Type = EffectType.EntityCreate, EntityId = "created", Name = "Created" }],
                [], [])
        });
        var runner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
            new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges),
            evaluator, effectApplier, operations);
        var request = new ApplicationActionExecutionRequest("action-space", app, record.QualifiedId,
            record.Version, record.ContentFingerprint, new Dictionary<string, string>(), "{}", 42,
            new("0123456789abcdef0123456789abcdef", new string('A', 64)));
        var staleVersion = await runner.RunAsync(request with
        {
            MechanicVersion = request.MechanicVersion + 1,
            ExecutionIdentity = new("1123456789abcdef0123456789abcdef", new string('B', 64))
        });
        var staleFingerprint = await runner.RunAsync(request with
        {
            ContentFingerprint = Hash("changed-mechanic"),
            ExecutionIdentity = new("2123456789abcdef0123456789abcdef", new string('C', 64))
        });

        var first = await runner.RunAsync(request);
        var replay = await runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Stale, staleVersion.Disposition);
        Assert.Equal("MECHANIC_STALE", Assert.Single(staleVersion.Problems).Code);
        Assert.Equal(ApplicationActionExecutionDisposition.Stale, staleFingerprint.Disposition);
        Assert.Equal("MECHANIC_STALE", Assert.Single(staleFingerprint.Problems).Code);
        Assert.Equal(new MechanicExecutionContext(
            request.ExecutionIdentity.OperationId,
            request.ExecutionIdentity.OperationId,
            null,
            0), evaluator.LastRequest!.Execution);
        Assert.Equal("A fixture appears.", first.Narration);
        Assert.Equal(2, first.AppliedEffectCount);
        Assert.Equal(["created"], first.AffectedEntityIds);
        Assert.Equal(2, first.EffectReceipts.Count);
        Assert.NotNull(await entities.GetEntityAsync("action-space", "created"));
        var component = await entities.GetComponentAsync(
            "action-space", "created", createdState.QualifiedId);
        Assert.NotNull(component);
        Assert.Equal("{\"ready\":true}", component.ValueJson);
        Assert.Single((await entities.ListEntitiesAsync("action-space", null, 10)).Entities);

        var rejectedEvaluator = new StaticEvaluator(new ApplicationMechanicEvaluationResult(
            record.QualifiedId, record.ContentFingerprint, new MechanicProjection(),
            new MechanicRunResult
            {
                Ok = true,
                Seed = 43,
                Output = new MechanicOutput
                {
                    Effects =
                    [
                        new Effect
                        {
                            Type = EffectType.ContainmentMove,
                            EntityId = "rolled-back",
                            ToEntityId = "missing-parent",
                            Slot = "inside"
                        }
                    ]
                }
            }, [])
        {
            Proposal = new CompositionProposal(
                [new Effect { Type = EffectType.EntityCreate, EntityId = "rolled-back", Name = "Rolled back" }],
                [], [])
        });
        var rejectedRunner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
            new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges),
            rejectedEvaluator, effectApplier, operations);
        var rejected = await rejectedRunner.RunAsync(request with
        {
            Seed = 43,
            ExecutionIdentity = new("3123456789abcdef0123456789abcdef", new string('D', 64))
        });

        Assert.Equal(ApplicationActionExecutionDisposition.Stale, rejected.Disposition);
        Assert.Null(await entities.GetEntityAsync("action-space", "rolled-back"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("membership")]
    [InlineData("payload")]
    public async Task Authorized_linked_sources_require_exact_membership_and_target_mappings(string? missing)
    {
        await using var db = _fixture.CreateContext();
        var app = ApplicationIdentifier.Parse("fixture-linked");
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(app, "Linked fixture", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("linked-space", revision, Hash("linked-activation")));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        foreach (var id in new[] { "link", "inline", "membership", "payload" }.Where(id => id != missing))
            types.Define(new(app, app.Value + "." + id, "{}"));
        var manifest = CatalogNavigationManifest.Create(app, Hash("linked-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Linked fixture.")],
            [new(app.Value, "", "Fixture", "Linked fixture.", CatalogDescriptionStatus.Authored)], []);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("linked-source-test-cursor-key-32-bytes")))
        });
        var requirements = new MechanicRequirements
        {
            AuthorizedContext = new()
            {
                SourceSets = new()
                {
                    Selection = new() { DefinitionLinkComponentId = "link" },
                    Activities = new("inline", "activities", ["selected-item", "selected-definition"])
                    {
                        Linked = new() { ComponentId = "membership", Field = "records", TargetComponentIds = ["payload"] }
                    }
                }
            }
        };
        var resolver = new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types,
            new SqliteStateSpaceEdgeStore(db, spaces));
        var result = await resolver.ResolveAsync("linked-space", app, "fixture-linked.read", requirements);
        if (missing is not null)
        {
            Assert.False(result.Resolved);
            Assert.Equal("COMPONENT_MAPPING_MISSING", Assert.Single(result.Problems).Code);
        }
        else
        {
            Assert.True(result.Resolved);
            Assert.Equal(new[] { "inline", "link", "membership", "payload" }, result.Mapping!.Components.Keys.Order());
            foreach (var (id, reference) in result.Mapping.Components)
                Assert.Equal(types.GetLatest(app.Value + "." + id)!.SchemaHash, reference.SchemaHash);
        }
    }

    public void Dispose() => _fixture.Dispose();

    private static MechanicFile[] RatifiedMechanics()
    {
        var root = RepositoryRoot();
        return CatalogReader.ReadAsync(Path.Combine(root, "catalog")).GetAwaiter().GetResult().Mechanics
            .Where(value => new[] { "game.core", "check", "change" }.Any(category =>
                value.Category == category || value.Category.StartsWith(category + ".", StringComparison.Ordinal)))
            .Where(value => MechanicRequirements.Parse(value.Requirements).Children.Count == 0)
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Comparable(MechanicRunResult value) => JsonSerializer.Serialize(new
    {
        value.Ok, value.Output, value.Error, value.LimitHit, value.Log, value.Seed
    });
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string CatalogMechanicContent(MechanicFile file) => JsonSerializer.Serialize(new
    {
        id = file.Id,
        category = file.Category,
        name = file.Name,
        description = file.Description,
        matches = file.Matches,
        requirements = file.Requirements,
        source = file.Source,
        scope = file.Scope,
        status = file.Status.ToString().ToLowerInvariant()
    });
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

    private sealed class FurnishedCompositeResolver : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default)
        {
            var projection = new MechanicProjection { StateSpaceId = stateSpaceId, Input = inputJson, Seed = seed };
            foreach (var (role, entityId) in roleAssignments)
            {
                projection.Roles[role] = role switch
                {
                    "world" => new(entityId, "World", new Dictionary<string, string>
                    {
                        ["game.core.world.root"] = "{\"status\":\"active\",\"summary\":\"Fixture world.\",\"visibility\":\"party\"}"
                    }, Contains: []),
                    "parent" => Location(entityId, "Parent", "region"),
                    "right" => Location(entityId, "Hall", "interior"),
                    "knowledge" => new(entityId, "Workshop charter", new Dictionary<string, string>
                    {
                        ["game.core.world.knowledge.classification"] = "{\"subjectKind\":\"location\",\"sensitivity\":\"open\"}",
                        ["game.core.world.fact"] = "{\"status\":\"active\",\"summary\":\"The workshop has a charter.\",\"provenance\":\"Fixture.\",\"visibility\":\"party\"}"
                    }, Relationships:
                    [
                        new(entityId, "world.fixture", "game.core.world.knowledge.in-world", "{}")
                    ]),
                    _ => new(entityId, entityId, new Dictionary<string, string>())
                };
            }
            return Task.FromResult(new ProjectionResult(projection, []));
        }

        private static EntityProjection Location(string id, string name, string kind) => new(
            id, name, new Dictionary<string, string>
            {
                ["game.core.world.location"] = JsonSerializer.Serialize(new
                {
                    kind, status = "active", summary = "Fixture location.", visibility = "party"
                })
            }, Relationships: []);
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

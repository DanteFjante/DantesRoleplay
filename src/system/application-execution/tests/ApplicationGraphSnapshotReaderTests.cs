using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationGraphSnapshotReaderTests : IDisposable
{
    private readonly SqliteFixture fixture = new();

    [Fact]
    public void Graph_declarations_allow_topology_only_steps_and_reject_unbounded_or_null_shapes()
    {
        var topologyOnly = new GraphSnapshotRequirement
        {
            RootRole = "selection",
            ComponentIds = ["marker"],
            Steps = [new()
            {
                Id = "neighbors", From = "root", RelationshipKinds = ["knows"],
                Direction = "outgoing", ComponentIds = []
            }]
        };
        Assert.True(topologyOnly.Valid());
        var unbounded = topologyOnly with { MaxSteps = ProjectionLimits.MaxGraphSteps + 1 };
        Assert.False(unbounded.Valid());
        var nullSteps = topologyOnly with { Steps = null! };
        Assert.False(nullSteps.Valid());
        var nullKinds = topologyOnly with
        {
            Steps = [topologyOnly.Steps[0] with { RelationshipKinds = null! }]
        };
        Assert.False(nullKinds.Valid());
    }

    [Fact]
    public async Task Mixed_role_and_graph_reads_share_owned_snapshot_and_leave_no_transaction_open()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var probe = new TransactionProbeGraphReader(db);
        var resolver = new ApplicationMechanicProjectionResolver(db, setup.Spaces, probe,
            new SqliteProjectionReadTransaction(db));
        var requirements = new MechanicRequirements
        {
            Roles = new Dictionary<string, RoleRequirement>
            {
                ["selection"] = new(["marker"])
            },
            GraphSnapshots = new()
            {
                ["party"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"]
                }
            }
        };

        var result = await resolver.ResolveAsync("space", setup.Application, requirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" }, "{}", 1);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.True(probe.SawActiveTransaction);
        Assert.NotNull(result.Projection!.Roles["selection"]);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Mixed_role_and_graph_reads_reuse_caller_transaction_without_committing_it()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var probe = new TransactionProbeGraphReader(db);
        var resolver = new ApplicationMechanicProjectionResolver(db, setup.Spaces, probe,
            new SqliteProjectionReadTransaction(db));
        var requirements = new MechanicRequirements
        {
            Roles = new Dictionary<string, RoleRequirement>
            {
                ["selection"] = new(["marker"])
            },
            GraphSnapshots = new()
            {
                ["party"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"]
                }
            }
        };

        await using var callerTransaction = await db.Database.BeginTransactionAsync();
        var result = await resolver.ResolveAsync("space", setup.Application, requirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" }, "{}", 1);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.True(probe.SawActiveTransaction);
        Assert.Same(callerTransaction, db.Database.CurrentTransaction);
        await callerTransaction.RollbackAsync();
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Explicit_paths_include_parsed_components_edges_containment_and_empty_collection_evidence()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var requirements = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["party"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection",
                    ComponentIds = ["marker"],
                    Steps =
                    [
                        new()
                        {
                            Id = "world",
                            From = "root",
                            RelationshipKinds = ["knows"],
                            Direction = "outgoing",
                            ComponentIds = ["marker"],
                            IncludeEdgeData = true
                        },
                        new()
                        {
                            Id = "ancestors",
                            From = "world",
                            Containment = "ancestors",
                            ComponentIds = ["marker"],
                            MaxDepth = 2
                        },
                        new()
                        {
                            Id = "empty",
                            From = "root",
                            RelationshipKinds = ["missing-kind"],
                            Direction = "outgoing",
                            ComponentIds = ["marker"]
                        },
                        new()
                        {
                            Id = "either",
                            From = "root",
                            RelationshipKinds = ["knows"],
                            Direction = "either",
                            ComponentIds = ["marker"]
                        }
                    ]
                }
            }
        };

        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, requirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });

        Assert.True(result.Ok, $"Problems: {string.Join("; ", result.Problems)}");
        var graph = Assert.Single(result.Snapshots).Value;
        Assert.Equal("selection", graph.Root.Id);
        Assert.True(graph.Steps.Count == 4);
        Assert.Equal("child", Assert.Single(graph.Steps["world"].Nodes).Id);
        Assert.Equal(7, graph.Steps["world"].Nodes[0].Components["fixture-graph.marker"].GetProperty("value").GetInt32());
        Assert.DoesNotContain("fixture-graph.extra", graph.Steps["world"].Nodes[0].Components.Keys);
        Assert.Equal(1, graph.Steps["world"].Nodes[0].ComponentRevisions["fixture-graph.marker"].Revision);
        Assert.Equal(2, graph.Steps["world"].Edges[0].Data!.Value.GetProperty("weight").GetInt32());
        Assert.Contains(graph.Steps["ancestors"].Nodes, value => value.Id == "container");
        Assert.Contains(graph.Steps["ancestors"].Nodes, value => value.Id == "grandparent");
        Assert.Contains(graph.Containment, value => value.ContainedEntityId == "child" && value.Slot == "inside");
        Assert.Equal(4, graph.Coverage.Steps);
        Assert.Equal(5, graph.Coverage.Nodes);
        Assert.Equal(3, graph.Coverage.Edges);
        Assert.Equal(4, graph.Coverage.Components);
        Assert.Equal(2, graph.Coverage.Depth);
        Assert.True(graph.Coverage.Bytes > 0);
        Assert.Empty(graph.Steps["empty"].Nodes);
        Assert.True(graph.Steps["either"].Edges.Count == 2);
        Assert.Null(graph.Page);
        Assert.Contains(result.Evidence, value => value.EntityId == "selection"
            && value.QualifiedKind == "fixture-graph.knows" && !value.Incoming);
        Assert.Contains(result.Evidence, value => value.EntityId == "selection"
            && value.QualifiedKind == "fixture-graph.knows" && value.Incoming);
        Assert.Contains(result.Evidence, value => value.EntityId == "selection"
            && value.QualifiedKind == "fixture-graph.missing-kind"
            && !value.Incoming && value.Relationships.Count == 0);
        Assert.NotEmpty(graph.SourceRevisionFingerprint);

        var run = await new JintMechanicEngine().RunAsync(
            "return {data:{worldNodes:ctx.graphSnapshots.party.steps.world.nodes.length," +
            "edgeData:ctx.graphSnapshots.party.steps.world.edges[0].data.weight}};",
            new MechanicProjection { GraphSnapshots = new() { ["party"] = graph } },
            ExecutionLimits.Default);
        Assert.True(run.Ok, run.Error);
        using var runData = JsonDocument.Parse(run.Output.Data);
        Assert.Equal(1, runData.RootElement.GetProperty("worldNodes").GetInt32());
        Assert.Equal(2, runData.RootElement.GetProperty("edgeData").GetInt32());
    }

    [Fact]
    public async Task Paged_graph_keeps_one_full_source_fingerprint_and_rebuilds_only_the_selected_closure()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedPagingAsync(db);
        var reader = new ApplicationGraphSnapshotReader(db, setup.Spaces);
        var roles = new Dictionary<string, string> { ["selection"] = "selection" };

        var first = await reader.ReadAsync("space", setup.Application, PagingRequirements(), setup.Mapping,
            roles, "{}");
        Assert.True(first.Ok, string.Join("; ", first.Problems));
        var firstGraph = first.Snapshots["paged"];
        Assert.Equal(new[] { "alpha" }, firstGraph.Steps["items"].Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "alpha-detail" }, firstGraph.Steps["details"].Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "alpha" }, firstGraph.Steps["admissions"].Nodes.Select(node => node.Id));
        Assert.All(firstGraph.Steps["admissions"].Edges,
            edge => Assert.Equal("alpha", edge.ToEntityId));
        Assert.Equal(new[] { "container", "grandparent" },
            firstGraph.Steps["ancestors-one"].Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "container", "grandparent" },
            firstGraph.Steps["ancestors-two"].Nodes.Select(node => node.Id));
        Assert.NotNull(firstGraph.Page);
        Assert.Equal(0, firstGraph.Page!.Offset);
        Assert.Equal(3, firstGraph.Page.TotalCount);
        Assert.Equal(1, firstGraph.Page.PageSize);
        Assert.Equal("1", firstGraph.Page.NextCursor);
        Assert.Equal(3, Assert.Single(first.Evidence,
            value => value.QualifiedKind == "fixture-graph.admission").Relationships.Count);

        var second = await reader.ReadAsync("space", setup.Application, PagingRequirements(), setup.Mapping,
            roles, "{\"cursor\":\"1\"}");
        var third = await reader.ReadAsync("space", setup.Application, PagingRequirements(), setup.Mapping,
            roles, "{\"cursor\":\"2\"}");
        Assert.True(second.Ok, string.Join("; ", second.Problems));
        Assert.True(third.Ok, string.Join("; ", third.Problems));
        Assert.Equal(new[] { "child" }, second.Snapshots["paged"].Steps["items"].Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "omega" }, third.Snapshots["paged"].Steps["items"].Nodes.Select(node => node.Id));
        Assert.Null(third.Snapshots["paged"].Page!.NextCursor);
        Assert.Equal(firstGraph.SourceRevisionFingerprint, second.Snapshots["paged"].SourceRevisionFingerprint);
        Assert.Equal(firstGraph.SourceRevisionFingerprint, third.Snapshots["paged"].SourceRevisionFingerprint);

        var marker = setup.Mapping.Components["marker"];
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var store = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        await store.MergeComponentAsync(new EcsComponentWrite("space", "omega", marker,
            "{\"value\":222}", 1));
        var afterOffPageComponent = await reader.ReadAsync("space", setup.Application, PagingRequirements(),
            setup.Mapping, roles, "{}");
        Assert.NotEqual(firstGraph.SourceRevisionFingerprint,
            afterOffPageComponent.Snapshots["paged"].SourceRevisionFingerprint);
        Assert.Equal(new[] { "alpha" },
            afterOffPageComponent.Snapshots["paged"].Steps["items"].Nodes.Select(node => node.Id));

        var edgeStore = new SqliteStateSpaceEdgeStore(db, setup.Spaces);
        await edgeStore.SetRelationshipAsync(
            "space", "omega", "omega-detail", "fixture-graph.detail", "{\"changed\":true}", 1);
        var afterOffPageEdge = await reader.ReadAsync("space", setup.Application, PagingRequirements(),
            setup.Mapping, roles, "{}");
        Assert.NotEqual(afterOffPageComponent.Snapshots["paged"].SourceRevisionFingerprint,
            afterOffPageEdge.Snapshots["paged"].SourceRevisionFingerprint);
        Assert.Equal(new[] { "alpha-detail" },
            afterOffPageEdge.Snapshots["paged"].Steps["details"].Nodes.Select(node => node.Id));

        await edgeStore.SetRelationshipAsync(
            "space", "selection", "omega", "fixture-graph.admission", "{\"state\":\"withdrawn\"}", 1);
        var afterOffPageAdmission = await reader.ReadAsync("space", setup.Application, PagingRequirements(),
            setup.Mapping, roles, "{}");
        Assert.NotEqual(afterOffPageEdge.Snapshots["paged"].SourceRevisionFingerprint,
            afterOffPageAdmission.Snapshots["paged"].SourceRevisionFingerprint);
        Assert.All(afterOffPageAdmission.Snapshots["paged"].Steps["admissions"].Edges,
            edge => Assert.Equal("alpha", edge.ToEntityId));
    }

    [Fact]
    public async Task Graph_page_reads_are_exactly_state_scoped()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedPagingAsync(db);
        var reader = new ApplicationGraphSnapshotReader(db, setup.Spaces);
        var roles = new Dictionary<string, string> { ["selection"] = "selection" };
        var baseline = await reader.ReadAsync("space", setup.Application, PagingRequirements(), setup.Mapping,
            roles, "{}");
        Assert.True(baseline.Ok, string.Join("; ", baseline.Problems));
        var source = setup.Spaces.Get("space")!;
        setup.Spaces.Create(new("other-space", source.ApplicationRevision, new string('B', 64)));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var store = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        var marker = setup.Mapping.Components["marker"];
        await store.CreateEntityAsync("other-space", "selection", "Foreign selection");
        await store.CreateEntityAsync("other-space", "alpha", "Foreign alpha");
        await store.AddComponentAsync(new EcsComponentWrite("other-space", "selection", marker,
            "{\"value\":900}", 0));
        await store.AddComponentAsync(new EcsComponentWrite("other-space", "alpha", marker,
            "{\"value\":901}", 0));
        await new SqliteStateSpaceEdgeStore(db, setup.Spaces).SetRelationshipAsync(
            "other-space", "selection", "alpha", "fixture-graph.knows", "{\"foreign\":true}", 0);

        var result = await reader.ReadAsync("space", setup.Application, PagingRequirements(), setup.Mapping,
            roles, "{}");

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        var graph = result.Snapshots["paged"];
        Assert.Equal(baseline.Snapshots["paged"].SourceRevisionFingerprint,
            graph.SourceRevisionFingerprint);
        Assert.Equal("Selection", graph.Root.Name);
        Assert.Equal(1, graph.Root.Components[marker.QualifiedTypeId].GetProperty("value").GetInt32());
        Assert.Equal("Alpha", Assert.Single(graph.Steps["items"].Nodes).Name);
        Assert.Equal(11, graph.Steps["items"].Nodes[0].Components[marker.QualifiedTypeId]
            .GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"cursor\"")]
    [InlineData("{")]
    [InlineData("{\"cursor\":null}")]
    [InlineData("{\"cursor\":1}")]
    [InlineData("{\"cursor\":\"\"}")]
    [InlineData("{\"cursor\":\"0\"}")]
    [InlineData("{\"cursor\":\"01\"}")]
    [InlineData("{\"cursor\":\"-1\"}")]
    [InlineData("{\"cursor\":\"3\"}")]
    [InlineData("{\"cursor\":\"99999999999\"}")]
    public async Task Graph_page_rejects_noncanonical_or_out_of_range_cursors(string input)
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedPagingAsync(db);

        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, PagingRequirements(), setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" }, input);

        Assert.False(result.Ok);
        Assert.Contains("GRAPH_PAGE_INPUT_INVALID", string.Join("; ", result.Problems));
        Assert.DoesNotContain("paged", result.Snapshots.Keys);
    }

    [Fact]
    public async Task Positive_cursor_over_an_empty_page_source_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var empty = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["paged"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"],
                    Page = new() { StepId = "items", CursorInput = "cursor", PageSize = 1 },
                    Steps = [new()
                    {
                        Id = "items", From = "root", RelationshipKinds = ["missing-kind"],
                        Direction = "outgoing", ComponentIds = ["marker"]
                    }]
                }
            }
        };

        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, empty, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" }, "{\"cursor\":\"1\"}");

        Assert.False(result.Ok);
        Assert.Contains("GRAPH_PAGE_INPUT_INVALID", string.Join("; ", result.Problems));
    }

    [Fact]
    public void Graph_page_declarations_reject_unbounded_ambiguous_or_missing_shapes()
    {
        var declaration = PagingRequirements().GraphSnapshots["paged"];

        Assert.False((declaration with { Page = declaration.Page! with { PageSize = 0 } }).Valid());
        Assert.False((declaration with
        {
            Page = declaration.Page! with { PageSize = ProjectionLimits.MaxGraphPageSize + 1 }
        }).Valid());
        Assert.False((declaration with { Page = declaration.Page! with { CursorInput = "" } }).Valid());
        Assert.False((declaration with { Page = declaration.Page! with { StepId = "missing" } }).Valid());
        Assert.False((declaration with
        {
            Steps = declaration.Steps.Select(step => step.Id == "items"
                ? step with { Direction = "either" } : step).ToArray()
        }).Valid());
        Assert.False((declaration with
        {
            Steps = declaration.Steps.Select(step => step.Id == "admissions"
                ? step with { Direction = "either" } : step).ToArray()
        }).Valid());
    }

    [Fact]
    public async Task Graph_mechanics_are_read_only_even_when_javascript_proposes_effects()
    {
        var app = ApplicationIdentifier.Parse("fixture-graph");
        const string requirements = "{\"graphSnapshots\":{\"party\":{\"rootRole\":\"selection\",\"componentIds\":[\"marker\"],\"steps\":[]}}}";
        const string source = "return {effects:[{type:'component.merge',entityId:'selection',definitionId:'fixture-graph.marker',data:'{}'}]};";
        var content = JsonSerializer.Serialize(new { requirements, source });
        var record = new CatalogRecordDefinition(app.Value, "mechanic", "fixture-graph.mechanic.graph",
            "Graph mechanic", "Graph mechanic.", [], [], "mechanics", "active", 1, content,
            Hash(content), "catalog", "mechanics/graph.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("graph-engine-test"), "catalog-lexical-v1",
            [new(app.Value, "Fixture", "Graph fixture.")],
            [new(app.Value, "", "Fixture", "Graph fixture.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)], [record]);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("graph-engine-test-cursor-key-32-bytes")))
        });
        var projection = new MechanicProjection
        {
            GraphSnapshots = new()
            {
                ["party"] = new MechanicGraphSnapshot(
                    new("selection", "Selection", new Dictionary<string, JsonElement>(),
                        new Dictionary<string, MechanicGraphComponentRevision>(), 1),
                    new Dictionary<string, MechanicGraphStepSnapshot>(), [], true,
                    new GraphSnapshotCoverage(0, 1, 0, 0, 0, 0), "fingerprint")
            }
        };
        var evaluator = new ApplicationMechanicEvaluator(catalogs, new StaticProjectionResolver(projection),
            new JintMechanicEngine());
        var result = await evaluator.EvaluateAsync(new("space", app, record.QualifiedId,
            record.ContentFingerprint, new(new Dictionary<string, EcsComponentReference>(),
                new Dictionary<string, string>()), new Dictionary<string, string>(), "{}", 1));

        Assert.False(result.Ok);
        Assert.Contains("READ_MODEL_OUTPUT_UNSAFE", result.Problems);
    }

    [Fact]
    public async Task Required_graph_caps_fail_closed_without_relaxing_edge_validation()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var requirements = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["capped"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection",
                    ComponentIds = ["marker"],
                    MaxEntities = 10,
                    MaxEdges = 1,
                    Steps =
                    [new()
                    {
                        Id = "world",
                        From = "root",
                        RelationshipKinds = ["knows"],
                        Direction = "either",
                        ComponentIds = ["marker"]
                    }, new()
                    {
                        Id = "after",
                        From = "world",
                        RelationshipKinds = ["knows"],
                        Direction = "outgoing",
                        ComponentIds = ["marker"]
                    }]
                }
            }
        };

        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, requirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });

        Assert.False(result.Ok);
        Assert.Contains("GRAPH_EDGE_LIMIT", string.Join("; ", result.Problems));
        Assert.False(result.Snapshots["capped"].Complete);
        Assert.False(result.Snapshots["capped"].Steps["after"].Complete);

        var entityCapped = requirements with
        {
            GraphSnapshots = new()
            {
                ["capped"] = requirements.GraphSnapshots["capped"] with
                {
                    MaxEntities = 1, MaxEdges = ProjectionLimits.MaxGraphEdges,
                    Steps = [requirements.GraphSnapshots["capped"].Steps[0] with { Direction = "outgoing" }]
                }
            }
        };
        var entityResult = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, entityCapped, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.Contains("GRAPH_ENTITY_LIMIT", string.Join("; ", entityResult.Problems));
    }

    [Fact]
    public async Task Cumulative_selected_edge_payloads_fail_even_when_each_candidate_is_individually_bounded()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var longData = $"{{\"value\":\"{new string('x', 180)}\"}}";
        var edges = new SqliteStateSpaceEdgeStore(db, setup.Spaces);
        await edges.SetRelationshipAsync("space", "selection", "child", "fixture-graph.dual", longData, 0);
        await edges.SetRelationshipAsync("space", "selection", "container", "fixture-graph.dual", longData, 0);
        var mapping = new ApplicationMechanicProjectionMapping(setup.Mapping.Components,
            setup.Mapping.Relationships.Concat(new[] { new KeyValuePair<string, string>("dual", "fixture-graph.dual") })
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
        var requirements = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["dual"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], MaxBytes = 512,
                    Steps = [new()
                    {
                        Id = "edges", From = "root", RelationshipKinds = ["dual"],
                        Direction = "outgoing", ComponentIds = ["marker"], IncludeEdgeData = true
                    }]
                }
            }
        };
        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, requirements, mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.False(result.Ok);
        Assert.Contains("GRAPH_BYTE_LIMIT", string.Join("; ", result.Problems));
    }

    [Fact]
    public async Task Metadata_overflow_with_incomplete_graph_never_returns_an_oversized_snapshot()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        db.Set<ApplicationEcsEntityRecord>().Add(new ApplicationEcsEntityRecord
        {
            StateSpaceId = "space", Id = "empty-root", Name = "Empty root", Revision = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var requirements = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["metadata"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], MaxBytes = 1,
                    RequireComplete = false
                }
            }
        };

        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, requirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "empty-root" });

        Assert.False(result.Ok);
        Assert.Contains("GRAPH_BYTE_LIMIT", string.Join("; ", result.Problems));
        Assert.DoesNotContain("metadata", result.Snapshots.Keys);
    }

    [Fact]
    public async Task Deleted_endpoint_is_filtered_before_take_so_a_later_live_edge_remains_visible()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        db.Set<ApplicationEcsEntityRecord>().Add(new ApplicationEcsEntityRecord
        {
            StateSpaceId = "space", Id = "deleted", Name = "Deleted", Revision = 1,
            CreatedAtUtc = DateTime.UtcNow, DeletedAtUtc = DateTime.UtcNow
        });
        db.Set<ApplicationEcsRelationshipRecord>().Add(new ApplicationEcsRelationshipRecord
        {
            StateSpaceId = "space", FromEntityId = "selection", ToEntityId = "deleted",
            QualifiedKind = "fixture-graph.filtered", Data = "{}", Revision = 1
        });
        db.Set<ApplicationEcsRelationshipRecord>().Add(new ApplicationEcsRelationshipRecord
        {
            StateSpaceId = "space", FromEntityId = "selection", ToEntityId = "child",
            QualifiedKind = "fixture-graph.filtered", Data = "{}", Revision = 2
        });
        await db.SaveChangesAsync();
        var mapping = new ApplicationMechanicProjectionMapping(setup.Mapping.Components,
            setup.Mapping.Relationships.Concat(new[] { new KeyValuePair<string, string>("filtered", "fixture-graph.filtered") })
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
        var requirements = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["filtered"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], MaxEdges = 1,
                    Steps = [new()
                    {
                        Id = "edges", From = "root", RelationshipKinds = ["filtered"],
                        Direction = "outgoing", ComponentIds = ["marker"]
                    }]
                }
            }
        };
        var result = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, requirements, mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.Equal("child", Assert.Single(result.Snapshots["filtered"].Steps["edges"].Nodes).Id);
    }

    [Fact]
    public async Task Missing_root_role_fails_closed_and_empty_collection_fingerprint_changes_after_new_edge()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var missing = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["missing"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"]
                }
            }
        };
        var missingResult = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync(
            "space", setup.Application, missing, setup.Mapping, new Dictionary<string, string>());
        Assert.False(missingResult.Ok);
        Assert.Contains("GRAPH_ROOT_UNAVAILABLE", string.Join("; ", missingResult.Problems));

        var empty = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["empty"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], Steps =
                    [new()
                    {
                        Id = "edges", From = "root", RelationshipKinds = ["missing-kind"],
                        Direction = "outgoing", ComponentIds = ["marker"]
                    }]
                }
            }
        };
        var reader = new ApplicationGraphSnapshotReader(db, setup.Spaces);
        var first = await reader.ReadAsync("space", setup.Application, empty, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        var firstFingerprint = first.Snapshots["empty"].SourceRevisionFingerprint;
        await new SqliteStateSpaceEdgeStore(db, setup.Spaces).SetRelationshipAsync(
            "space", "selection", "container", "fixture-graph.missing-kind", "{}", 0);
        var second = await reader.ReadAsync("space", setup.Application, empty, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.NotEqual(firstFingerprint, second.Snapshots["empty"].SourceRevisionFingerprint);
        Assert.Single(second.Snapshots["empty"].Steps["edges"].Edges);
        Assert.Single(second.Evidence.Single(value => value.QualifiedKind == "fixture-graph.missing-kind").Relationships);
    }

    [Fact]
    public async Task Containment_depth_cycles_and_selected_edge_bytes_fail_closed()
    {
        await using var db = fixture.CreateContext();
        var setup = await SeedAsync(db);
        var ancestry = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["limited"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], MaxDepth = 1, Steps =
                    [new()
                    {
                        Id = "world", From = "root", RelationshipKinds = ["knows"],
                        Direction = "outgoing", ComponentIds = ["marker"]
                    }, new()
                    {
                        Id = "ancestors", From = "world", Containment = "ancestors",
                        ComponentIds = ["marker"], MaxDepth = 1
                    }]
                }
            }
        };
        var reader = new ApplicationGraphSnapshotReader(db, setup.Spaces);
        var limited = await reader.ReadAsync("space", setup.Application, ancestry, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.False(limited.Ok);
        Assert.Contains("CONTAINMENT_DEPTH_LIMIT", string.Join("; ", limited.Problems));

        db.Set<ApplicationEcsContainmentRecord>().Add(new ApplicationEcsContainmentRecord
        {
            StateSpaceId = "space", ContainedEntityId = "grandparent", ContainerEntityId = "child",
            Slot = "cycle", Revision = 1
        });
        await db.SaveChangesAsync();
        var cycleDeclaration = ancestry.GraphSnapshots["limited"] with
        {
            MaxDepth = 8,
            Steps = ancestry.GraphSnapshots["limited"].Steps
                .Select(step => step.Containment is null ? step : step with { MaxDepth = 8 }).ToArray()
        };
        var cycleRequirements = new MechanicRequirements
        {
            GraphSnapshots = new() { ["limited"] = cycleDeclaration }
        };
        var cycle = await new ApplicationGraphSnapshotReader(db, setup.Spaces).ReadAsync("space", setup.Application, cycleRequirements, setup.Mapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.Contains("CONTAINMENT_CYCLE", string.Join("; ", cycle.Problems));

        await new SqliteStateSpaceEdgeStore(db, setup.Spaces).SetRelationshipAsync(
            "space", "selection", "container", "fixture-graph.huge",
            $"{{\"value\":\"{new string('x', 300)}\"}}", 0);
        var hugeMapping = new ApplicationMechanicProjectionMapping(setup.Mapping.Components,
            setup.Mapping.Relationships.Concat(new[] { new KeyValuePair<string, string>("huge", "fixture-graph.huge") })
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));
        var huge = new MechanicRequirements
        {
            GraphSnapshots = new()
            {
                ["huge"] = new GraphSnapshotRequirement
                {
                    RootRole = "selection", ComponentIds = ["marker"], MaxBytes = 128, Steps =
                    [new()
                    {
                        Id = "huge-edge", From = "root", RelationshipKinds = ["huge"],
                        Direction = "outgoing", ComponentIds = ["marker"], IncludeEdgeData = true
                    }]
                }
            }
        };
        var hugeResult = await reader.ReadAsync("space", setup.Application, huge, hugeMapping,
            new Dictionary<string, string> { ["selection"] = "selection" });
        Assert.False(hugeResult.Ok);
        Assert.Contains("GRAPH_BYTE_LIMIT", string.Join("; ", hugeResult.Problems));
    }

    private static MechanicRequirements PagingRequirements() => new()
    {
        GraphSnapshots = new()
        {
            ["paged"] = new GraphSnapshotRequirement
            {
                RootRole = "selection",
                ComponentIds = ["marker"],
                Page = new() { StepId = "items", CursorInput = "cursor", PageSize = 1 },
                Steps =
                [
                    new()
                    {
                        Id = "items", From = "root", RelationshipKinds = ["knows"],
                        Direction = "outgoing", ComponentIds = ["marker"], IncludeEdgeData = true
                    },
                    new()
                    {
                        Id = "details", From = "items", RelationshipKinds = ["detail"],
                        Direction = "outgoing", ComponentIds = ["marker"]
                    },
                    new()
                    {
                        Id = "ancestors-one", From = "items", Containment = "ancestors",
                        ComponentIds = ["marker"], MaxDepth = 2
                    },
                    new()
                    {
                        Id = "ancestors-two", From = "items", Containment = "ancestors",
                        ComponentIds = ["marker"], MaxDepth = 2
                    },
                    new()
                    {
                        Id = "admissions", From = "root", RelationshipKinds = ["admission"],
                        Direction = "outgoing", ComponentIds = ["marker"], IncludeEdgeData = true,
                        FilterEndpointStep = "items"
                    }
                ]
            }
        }
    };

    private static async Task<GraphSetup> SeedPagingAsync(DantesRoleplayDbContext db)
    {
        var setup = await SeedAsync(db);
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var store = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        var marker = setup.Mapping.Components["marker"];
        foreach (var (id, name, value) in new[]
        {
            ("alpha", "Alpha", 11), ("omega", "Omega", 22),
            ("alpha-detail", "Alpha detail", 31), ("child-detail", "Child detail", 32),
            ("omega-detail", "Omega detail", 33)
        })
        {
            await store.CreateEntityAsync("space", id, name);
            await store.AddComponentAsync(new EcsComponentWrite("space", id, marker,
                JsonSerializer.Serialize(new { value }), 0));
        }

        var edges = new SqliteStateSpaceEdgeStore(db, setup.Spaces);
        await edges.SetRelationshipAsync("space", "selection", "alpha", "fixture-graph.knows", "{}", 0);
        await edges.SetRelationshipAsync("space", "selection", "omega", "fixture-graph.knows", "{}", 0);
        foreach (var (item, detail) in new[]
        {
            ("alpha", "alpha-detail"), ("child", "child-detail"), ("omega", "omega-detail")
        })
            await edges.SetRelationshipAsync("space", item, detail, "fixture-graph.detail", "{}", 0);
        foreach (var item in new[] { "alpha", "child", "omega" })
            await edges.SetRelationshipAsync("space", "selection", item, "fixture-graph.admission",
                "{\"state\":\"known\"}", 0);
        await edges.MoveContainmentAsync("space", "alpha", "container", "inside", 0);

        var relationships = setup.Mapping.Relationships
            .Concat(new[]
            {
                new KeyValuePair<string, string>("detail", "fixture-graph.detail"),
                new KeyValuePair<string, string>("admission", "fixture-graph.admission")
            }).ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
        return setup with { Mapping = new(setup.Mapping.Components, relationships) };
    }

    private static async Task<GraphSetup> SeedAsync(DantesRoleplayDbContext db)
    {
        var applications = new SqliteApplicationRegistry(db);
        var application = ApplicationIdentifier.Parse("fixture-graph");
        var revision = applications.Register(new(application, "Graph fixture", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications);
        spaces.Create(new("space", revision, new string('A', 64)));
        var types = new SqliteComponentTypeRegistry(db, new BoundedJsonSchemaValidator());
        var marker = types.Define(new(application, "fixture-graph.marker", "{}"));
        var extra = types.Define(new(application, "fixture-graph.extra", "{}"));
        var store = new SqliteEntityComponentStore(db, types, new BoundedJsonSchemaValidator());
        await store.CreateEntityAsync("space", "selection", "Selection");
        await store.CreateEntityAsync("space", "child", "Child");
        await store.CreateEntityAsync("space", "container", "Container");
        await store.CreateEntityAsync("space", "grandparent", "Grandparent");
        await store.AddComponentAsync(new EcsComponentWrite("space", "selection",
            new(marker.QualifiedId, marker.Version, marker.SchemaHash), "{\"value\":1}", 0));
        await store.AddComponentAsync(new EcsComponentWrite("space", "child",
            new(marker.QualifiedId, marker.Version, marker.SchemaHash), "{\"value\":7}", 0));
        await store.AddComponentAsync(new EcsComponentWrite("space", "child",
            new(extra.QualifiedId, extra.Version, extra.SchemaHash), "{\"hidden\":true}", 0));
        await store.AddComponentAsync(new EcsComponentWrite("space", "container",
            new(marker.QualifiedId, marker.Version, marker.SchemaHash), "{\"value\":9}", 0));
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        await edges.SetRelationshipAsync("space", "selection", "child", "fixture-graph.knows",
            "{\"weight\":2}", 0);
        await edges.SetRelationshipAsync("space", "child", "selection", "fixture-graph.knows",
            "{\"weight\":3}", 0);
        await edges.MoveContainmentAsync("space", "child", "container", "inside", 0);
        await edges.MoveContainmentAsync("space", "container", "grandparent", "outer", 0);
        return new(application, spaces, new(
            new Dictionary<string, EcsComponentReference>
            {
                ["marker"] = new(marker.QualifiedId, marker.Version, marker.SchemaHash)
            }, new Dictionary<string, string>
            {
                ["knows"] = "fixture-graph.knows",
                ["missing-kind"] = "fixture-graph.missing-kind"
            }));
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Dispose() => fixture.Dispose();

    private sealed record GraphSetup(ApplicationIdentifier Application, SqliteStateSpaceRegistry Spaces,
        ApplicationMechanicProjectionMapping Mapping);

    private sealed class TransactionProbeGraphReader(DantesRoleplayDbContext db) : IApplicationGraphSnapshotReader
    {
        public bool SawActiveTransaction { get; private set; }

        public Task<ApplicationGraphSnapshotReadResult> ReadAsync(
            string stateSpaceId,
            ApplicationIdentifier applicationId,
            MechanicRequirements requirements,
            ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments,
            string inputJson = "{}",
            CancellationToken cancellationToken = default)
        {
            SawActiveTransaction = db.Database.CurrentTransaction is not null;
            return Task.FromResult(new ApplicationGraphSnapshotReadResult(
                new Dictionary<string, MechanicGraphSnapshot>(StringComparer.Ordinal), [], []));
        }
    }

    private sealed class StaticProjectionResolver(MechanicProjection projection) : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default) => Task.FromResult(new ProjectionResult(projection, []));
    }
}

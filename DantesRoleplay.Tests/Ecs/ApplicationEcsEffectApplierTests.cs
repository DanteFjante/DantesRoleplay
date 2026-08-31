using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.EcsEffects.Tests;

public sealed class ApplicationEcsEffectApplierTests : IDisposable
{
    private static readonly string Manifest = new('A', 64);
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Create_then_add_commits_with_one_success_audit()
    {
        var setup = Setup();
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Intent = "Create a fixture with generic state.",
            Effects =
            [
                new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "fixture", Name = "Fixture" },
                new() { Type = ApplicationEcsEffectType.ComponentAdd, EntityId = "fixture", ComponentType = setup.Type, DataJson = "{\"value\":1}", ExpectedRevision = 0 }
            ]
        });

        Assert.True(result.Applied); Assert.True(result.Valid); Assert.Equal(2, result.Receipts.Count);
        Assert.Equal("{\"value\":1}", (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!.ValueJson);
        var audit = Assert.Single(await setup.Db.Operations.AsNoTracking().Where(x => x.Id == result.OperationId).ToListAsync());
        Assert.True(audit.Success); Assert.Equal("system.ecs.effects", audit.Tool); Assert.True(audit.ConsumedReadEvidence);
    }

    [Fact]
    public async Task Deterministic_execution_identity_replays_without_applying_twice()
    {
        var setup = Setup();
        var identity = new ApplicationEcsExecutionIdentity(
            "0123456789abcdef0123456789abcdef", new string('B', 64));
        var batch = new ApplicationEcsEffectBatch
        {
            StateSpaceId = "effect-space",
            ExecutionIdentity = identity,
            Effects = [new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "once", Name = "Once" }]
        };

        var first = await setup.Applier.ApplyAsync(batch);
        var replay = await setup.Applier.ApplyAsync(batch);

        Assert.True(first.Applied);
        Assert.False(first.Replayed);
        Assert.False(replay.Applied);
        Assert.True(replay.Replayed);
        Assert.Equal(identity.OperationId, replay.OperationId);
        Assert.Single(await setup.Db.Operations.AsNoTracking().Where(value => value.Id == identity.OperationId).ToListAsync());
        Assert.Single((await setup.Store.ListEntitiesAsync("effect-space", null, 10)).Entities);
    }

    [Fact]
    public async Task Deterministic_operation_id_cannot_be_rebound_to_another_request()
    {
        var setup = Setup();
        var operationId = "fedcba9876543210fedcba9876543210";
        var first = new ApplicationEcsEffectBatch
        {
            StateSpaceId = "effect-space",
            ExecutionIdentity = new(operationId, new string('C', 64)),
            Effects = [new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "first", Name = "First" }]
        };
        var conflicting = first with
        {
            ExecutionIdentity = new(operationId, new string('D', 64)),
            Effects = [new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "second", Name = "Second" }]
        };

        Assert.True((await setup.Applier.ApplyAsync(first)).Applied);
        var result = await setup.Applier.ApplyAsync(conflicting);

        Assert.False(result.Applied);
        Assert.Equal("OPERATION_ID_CONFLICT", Assert.Single(result.Problems).Code);
        Assert.Null(await setup.Store.GetEntityAsync("effect-space", "second"));
    }

    [Fact]
    public async Task Late_stale_revision_rolls_back_earlier_effect_and_records_failure_only()
    {
        var setup = Setup(); await SeedAsync(setup);
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects =
            [
                Set(setup.Type, "{\"value\":2}", 1),
                Set(setup.Type, "{\"value\":3}", 1)
            ]
        });

        Assert.False(result.Applied); Assert.Equal("REVISION_STALE", Assert.Single(result.Problems).Code);
        var current = (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!;
        Assert.Equal(1, current.Revision); Assert.Equal("{\"value\":1}", current.ValueJson);
        var audit = Assert.Single(await setup.Db.Operations.AsNoTracking().Where(x => x.Id == result.OperationId).ToListAsync());
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task Dry_run_executes_real_path_then_rolls_back_and_records_non_consuming_audit()
    {
        var setup = Setup(); await SeedAsync(setup);
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            ProceduresUsed = ["procedure.fixture"],
            Effects = [Set(setup.Type, "{\"value\":2}", 1)]
        }, dryRun: true);

        Assert.False(result.Applied); Assert.True(result.DryRun); Assert.True(result.Valid);
        Assert.Equal(2, Assert.Single(result.Receipts).Revision);
        var current = (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!;
        Assert.Equal(1, current.Revision); Assert.Equal("{\"value\":1}", current.ValueJson);
        var audit = Assert.Single(await setup.Db.Operations.AsNoTracking().Where(x => x.Id == result.OperationId).ToListAsync());
        Assert.True(audit.Success); Assert.False(audit.ConsumedReadEvidence);
    }

    [Fact]
    public async Task Cross_application_component_contract_is_rejected_without_state_change()
    {
        var setup = Setup(); await setup.Store.CreateEntityAsync("effect-space", "fixture", "Fixture");
        var other = ApplicationIdentifier.Parse("other-effects");
        new SqliteApplicationRegistry(setup.Db).Register(new(other, "Other", "", []));
        var otherType = new SqliteComponentTypeRegistry(setup.Db, new BoundedJsonSchemaValidator()).Define(new(other, "other-effects.value", "{\"type\":\"object\"}"));
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [new() { Type = ApplicationEcsEffectType.ComponentAdd, EntityId = "fixture", ComponentType = new(otherType.QualifiedId, otherType.Version, otherType.SchemaHash), DataJson = "{}" }]
        });

        Assert.False(result.Applied); Assert.NotEmpty(result.Problems);
        Assert.Null(await setup.Store.GetComponentAsync("effect-space", "fixture", otherType.QualifiedId));
    }

    [Fact]
    public async Task Consecutive_revision_bound_updates_commit_in_authored_order()
    {
        var setup = Setup(); await SeedAsync(setup);
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects =
            [
                Set(setup.Type, "{\"value\":2}", 1),
                new() { Type = ApplicationEcsEffectType.ComponentMerge, EntityId = "fixture", ComponentType = setup.Type, DataJson = "{\"extra\":3}", ExpectedRevision = 2 }
            ]
        });

        Assert.True(result.Applied);
        Assert.Equal((int?)2, result.Receipts[0].Revision);
        Assert.Equal((int?)3, result.Receipts[1].Revision);
        var current = (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!;
        Assert.Equal(3, current.Revision);
        Assert.Equal("{\"value\":2,\"extra\":3}", current.ValueJson);
    }

    [Fact]
    public async Task Removal_and_deletion_receipts_distinguish_hard_removal_from_tombstone()
    {
        var setup = Setup(); await SeedAsync(setup);
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects =
            [
                new() { Type = ApplicationEcsEffectType.ComponentRemove, EntityId = "fixture", ComponentType = setup.Type, ExpectedRevision = 1 },
                new() { Type = ApplicationEcsEffectType.EntityDelete, EntityId = "fixture", ExpectedRevision = 1 }
            ]
        });

        Assert.True(result.Applied);
        Assert.Null(result.Receipts[0].Revision);
        Assert.Equal((int?)1, result.Receipts[0].RemovedRevision);
        Assert.Equal((int?)2, result.Receipts[1].Revision);
        Assert.Null(result.Receipts[1].RemovedRevision);
        Assert.Null(await setup.Store.GetEntityAsync("effect-space", "fixture"));
        Assert.Empty(await setup.Db.Set<ApplicationEcsComponentRecord>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Malformed_and_oversized_batches_are_typed_and_audited_without_mutation()
    {
        var setup = Setup();
        var missingEffects = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = null!
        });
        var tooManyEffects = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = Enumerable.Range(0, ApplicationEcsEffectValidation.MaximumEffects + 1)
                .Select(index => new ApplicationEcsEffect { Type = ApplicationEcsEffectType.EntityCreate, EntityId = $"entity-{index}", Name = "Fixture" })
                .ToArray()
        });
        var missingProcedures = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ProceduresUsed = null!
        });

        Assert.Equal("EFFECTS_REQUIRED", Assert.Single(missingEffects.Problems).Code);
        Assert.Equal("EFFECT_LIMIT", Assert.Single(tooManyEffects.Problems).Code);
        Assert.Equal("PROCEDURES_REQUIRED", Assert.Single(missingProcedures.Problems).Code);
        Assert.Equal(3, await setup.Db.Operations.AsNoTracking().CountAsync());
        Assert.Empty(await setup.Db.Set<ApplicationEcsEntityRecord>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public void Audit_metadata_is_bounded_by_closed_batch_validation()
    {
        var intent = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            Intent = new string('I', ApplicationEcsEffectValidation.MaximumIntentLength + 1)
        });
        var procedureCount = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ProceduresUsed = Enumerable.Range(0, ApplicationEcsEffectValidation.MaximumProcedures + 1)
                .Select(index => $"procedure.{index}").ToArray()
        });
        var procedureLength = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ProceduresUsed = [new string('P', ApplicationEcsEffectValidation.MaximumProcedureIdLength + 1)]
        });

        Assert.Equal("INTENT_LIMIT", Assert.Single(intent).Code);
        Assert.Equal("PROCEDURE_LIMIT", Assert.Single(procedureCount).Code);
        Assert.Equal("PROCEDURE_INVALID", Assert.Single(procedureLength).Code);
    }

    [Fact]
    public async Task Schema_invalid_and_deleted_entity_requests_are_typed_and_leave_state_unchanged()
    {
        var setup = Setup(); await SeedAsync(setup);
        var invalid = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [Set(setup.Type, "{\"value\":\"not-an-integer\"}", 1)]
        });

        Assert.Equal("VALIDATION_FAILED", Assert.Single(invalid.Problems).Code);
        Assert.Equal(1, (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!.Revision);

        Assert.True(await setup.Store.DeleteEntityAsync("effect-space", "fixture", 1));
        var deleted = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [Set(setup.Type, "{\"value\":2}", 1)]
        });

        Assert.Equal("REFERENCE_UNKNOWN", Assert.Single(deleted.Problems).Code);
        Assert.Equal(2, await setup.Db.Operations.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Exact_component_hash_mismatch_is_rejected_without_state_change()
    {
        var setup = Setup(); await SeedAsync(setup);
        var wrongHash = setup.Type with { SchemaHash = new string('B', 64) };
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [Set(wrongHash, "{\"value\":2}", 1)]
        });

        Assert.Equal("REFERENCE_UNKNOWN", Assert.Single(result.Problems).Code);
        var current = (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!;
        Assert.Equal(1, current.Revision);
        Assert.Equal("{\"value\":1}", current.ValueJson);
    }

    [Fact]
    public async Task Cancellation_rolls_back_and_records_a_typed_failure()
    {
        var setup = Setup(); await SeedAsync(setup);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [Set(setup.Type, "{\"value\":2}", 1)]
        }, cancellationToken: cancellation.Token);

        Assert.Equal("CANCELLED", Assert.Single(result.Problems).Code);
        Assert.Equal(1, (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!.Revision);
        Assert.False((await setup.Db.Operations.AsNoTracking().SingleAsync(x => x.Id == result.OperationId)).Success);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_and_clears_tracked_mutations_before_propagating()
    {
        var setup = Setup(); await SeedAsync(setup);
        var applier = new ApplicationEcsEffectApplier(setup.Db, setup.Store, setup.StateSpaces, new FailingOperationLog(setup.Db));

        await Assert.ThrowsAsync<AuditUnavailableException>(() => applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [Set(setup.Type, "{\"value\":2}", 1)]
        }));

        await setup.Db.SaveChangesAsync();
        var current = (await setup.Store.GetComponentAsync("effect-space", "fixture", setup.Type.QualifiedTypeId))!;
        Assert.Equal(1, current.Revision);
        Assert.Equal("{\"value\":1}", current.ValueJson);
        Assert.Empty(await setup.Db.Operations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Edge_effects_commit_in_order_and_a_late_stale_edge_rolls_back_the_batch()
    {
        var setup = Setup();
        foreach (var id in new[] { "child", "first", "second" })
            await setup.Store.CreateEntityAsync("effect-space", id, id);

        var created = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects =
            [
                new() { Type = ApplicationEcsEffectType.ContainmentMove, EntityId = "child", TargetEntityId = "first" },
                new()
                {
                    Type = ApplicationEcsEffectType.RelationshipSet,
                    EntityId = "first",
                    TargetEntityId = "second",
                    QualifiedRelationshipKind = "fixture-effects.knows",
                    DataJson = "[1,true]"
                }
            ]
        });

        Assert.True(created.Applied);
        Assert.Equal("first", (await setup.Edges.GetContainmentAsync("effect-space", "child"))!.ContainerEntityId);
        Assert.Equal("[1,true]", (await setup.Edges.GetRelationshipAsync(
            "effect-space", "first", "second", "fixture-effects.knows"))!.DataJson);

        var rejected = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects =
            [
                new()
                {
                    Type = ApplicationEcsEffectType.ContainmentMove,
                    EntityId = "child",
                    TargetEntityId = "second",
                    ExpectedRevision = 1
                },
                new()
                {
                    Type = ApplicationEcsEffectType.RelationshipSet,
                    EntityId = "first",
                    TargetEntityId = "second",
                    QualifiedRelationshipKind = "fixture-effects.knows",
                    DataJson = "{}",
                    ExpectedRevision = 0
                }
            ]
        });

        Assert.False(rejected.Applied);
        Assert.Equal("REVISION_STALE", Assert.Single(rejected.Problems).Code);
        Assert.Equal("first", (await setup.Edges.GetContainmentAsync("effect-space", "child"))!.ContainerEntityId);
    }

    [Fact]
    public void Containment_expectation_shape_is_bounded_and_closed()
    {
        var tooMany = Enumerable.Range(0, ApplicationEcsEffectValidation.MaximumContainmentExpectations + 1)
            .Select(index => new ApplicationEcsContainmentExpectation("container-" + index, []))
            .ToArray();
        var excessive = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ContainmentExpectations = tooMany
        });
        Assert.Contains(excessive, problem => problem.Code == "CONTAINMENT_EXPECTATION_LIMIT");

        var malformed = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ContainmentExpectations =
            [
                new("container", [new("child", "slot", 0), new("child", "slot", 1)]),
                new("container", [])
            ]
        });
        Assert.Contains(malformed, problem => problem.Code == "CONTAINMENT_EXPECTATION_INVALID");
    }

    [Fact]
    public void Exact_containment_edge_expectation_shape_is_bounded_and_closed()
    {
        var tooMany = Enumerable.Range(0, ApplicationEcsEffectValidation.MaximumContainmentEdgeExpectations + 1)
            .Select(index => new ApplicationEcsContainmentEdgeExpectation(
                "child-" + index, "container", "slot", 1))
            .ToArray();
        var excessive = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ContainmentEdgeExpectations = tooMany
        });
        Assert.Contains(excessive, problem => problem.Code == "CONTAINMENT_EDGE_EXPECTATION_LIMIT");

        var malformed = ApplicationEcsEffectValidation.Validate(new()
        {
            StateSpaceId = "effect-space",
            Effects = [],
            ContainmentEdgeExpectations =
            [
                new("child", "container", "slot", 0),
                new("child", "other", "slot", 1)
            ]
        });
        Assert.Contains(malformed, problem => problem.Code == "CONTAINMENT_EDGE_EXPECTATION_INVALID");
    }

    [Fact]
    public async Task Stale_containment_snapshot_rejects_the_whole_effect_transaction()
    {
        var setup = Setup();
        foreach (var id in new[] { "child", "container" })
            await setup.Store.CreateEntityAsync("effect-space", id, id);
        var observed = await setup.Edges.MoveContainmentAsync(
            "effect-space", "child", "container", "participant", 0);
        _ = await setup.Edges.MoveContainmentAsync(
            "effect-space", "child", "container", "changed", observed.Revision);

        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "must-not-exist", Name = "Rejected" }],
            ContainmentExpectations =
            [
                new("container", [new("child", "participant", observed.Revision)])
            ]
        });

        Assert.False(result.Applied);
        Assert.Equal("REVISION_STALE", Assert.Single(result.Problems).Code);
        Assert.Null(await setup.Store.GetEntityAsync("effect-space", "must-not-exist"));
    }

    [Fact]
    public async Task Stale_exact_containment_edge_rejects_the_whole_effect_transaction()
    {
        var setup = Setup();
        foreach (var id in new[] { "child", "container", "other" })
            await setup.Store.CreateEntityAsync("effect-space", id, id);
        var observed = await setup.Edges.MoveContainmentAsync(
            "effect-space", "child", "container", "participant", 0);
        _ = await setup.Edges.MoveContainmentAsync(
            "effect-space", "child", "other", "changed", observed.Revision);

        var result = await setup.Applier.ApplyAsync(new()
        {
            StateSpaceId = "effect-space",
            Effects = [new() { Type = ApplicationEcsEffectType.EntityCreate, EntityId = "must-not-exist-exact", Name = "Rejected" }],
            ContainmentEdgeExpectations =
            [
                new("child", "container", "participant", observed.Revision)
            ]
        });

        Assert.False(result.Applied);
        Assert.Equal("REVISION_STALE", Assert.Single(result.Problems).Code);
        Assert.Null(await setup.Store.GetEntityAsync("effect-space", "must-not-exist-exact"));
    }

    private SetupResult Setup()
    {
        var db = _fixture.CreateContext(); var app = ApplicationIdentifier.Parse("fixture-effects");
        var applications = new SqliteApplicationRegistry(db); var revision = applications.Register(new(app, "Fixture effects", "", []));
        var spaces = new SqliteStateSpaceRegistry(db, applications); spaces.Create(new("effect-space", revision, Manifest));
        var schemas = new BoundedJsonSchemaValidator(); var types = new SqliteComponentTypeRegistry(db, schemas);
        var registered = types.Define(new(app, "fixture-effects.value", "{\"type\":\"object\",\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
        var type = new EcsComponentReference(registered.QualifiedId, registered.Version, registered.SchemaHash);
        var store = new SqliteEntityComponentStore(db, types, schemas);
        var edges = new SqliteStateSpaceEdgeStore(db, spaces);
        return new(db, store, type, spaces, edges,
            new ApplicationEcsEffectApplier(db, store, spaces, new OperationLog(db), edges));
    }

    private static async Task SeedAsync(SetupResult setup)
    {
        await setup.Store.CreateEntityAsync("effect-space", "fixture", "Fixture");
        await setup.Store.AddComponentAsync(new("effect-space", "fixture", setup.Type, "{\"value\":1}", 0));
    }

    private static ApplicationEcsEffect Set(EcsComponentReference type, string data, int revision) =>
        new() { Type = ApplicationEcsEffectType.ComponentSet, EntityId = "fixture", ComponentType = type, DataJson = data, ExpectedRevision = revision };

    public void Dispose() => _fixture.Dispose();
    private sealed record SetupResult(DantesRoleplayDbContext Db, SqliteEntityComponentStore Store,
        EcsComponentReference Type, IStateSpaceRegistry StateSpaces, IStateSpaceEdgeStore Edges,
        ApplicationEcsEffectApplier Applier);

    private sealed class AuditUnavailableException : Exception;

    private sealed class FailingOperationLog(DantesRoleplayDbContext db) : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Operation?>(null);

        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "", string subject = "", IEnumerable<string>? proceduresCited = null, string error = "", bool consumesReadEvidence = false, CancellationToken cancellationToken = default, string mechanicId = "", int? mechanicVersion = null, long? seed = null, string projectionJson = "", string guardEvidenceJson = "", string id = "")
        {
            db.Operations.Add(new Operation { Id = id, Tool = tool, Summary = summary, Success = success });
            return Task.FromException<Operation>(new AuditUnavailableException());
        }

        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false, string? tool = null, string? subject = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Operation>>([]);

        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

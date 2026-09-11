using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections.Tests;

public sealed class ApplicationObjectWriteTests : IDisposable
{
    private readonly Fixture fixture = new();

    [Fact]
    public async Task T13_T18_field_based_objects_keep_hidden_fields_atomic_validation_and_revision_authority()
    {
        using var scoped = new Fixture(fieldBased: true);
        var before = await scoped.ReadAsync();
        var request = scoped.Request(before.SourceRevisionFingerprint, "field-based-save", "{\"premise\":\"Reviewed\"}");
        var saved = await scoped.Writer.WriteAsync(request);
        var replayed = await scoped.Writer.WriteAsync(request);
        Assert.True(saved.Applied);
        Assert.True(replayed.Replayed);
        Assert.Equal(2, (await scoped.ComponentAsync(scoped.Primary)).Revision);
        Assert.Equal("retained-private-field", Json((await scoped.ComponentAsync(scoped.Primary)).ValueJson)
            .GetProperty("hidden").GetString());
        Assert.Equal("OBJECT_WRITE_SOURCE_STALE", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
            scoped.Writer.WriteAsync(scoped.Request(before.SourceRevisionFingerprint, "stale", "{\"premise\":\"Stale\"}")))).Code);
        Assert.Equal("OBJECT_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
            scoped.Writer.WriteAsync(scoped.Request(saved.SourceRevisionFingerprint, "denied", "{}")
                with { Perspective = "player" }))).Code);
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
            scoped.Writer.WriteAsync(scoped.Request(saved.SourceRevisionFingerprint, "undeclared", "{\"hidden\":\"Forged\"}")))).Code);
        Assert.Equal("OBJECT_WRITE_REJECTED", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
            scoped.Writer.WriteAsync(scoped.Request(saved.SourceRevisionFingerprint, "rollback",
                "{\"premise\":\"Must roll back\",\"secondary\":\"rejected\"}")))).Code);
        Assert.Equal("Reviewed", Json((await scoped.ComponentAsync(scoped.Primary)).ValueJson).GetProperty("premise").GetString());
        Assert.Equal(2, (await scoped.ComponentAsync(scoped.Primary)).Revision);
        Assert.Equal(1, (await scoped.ComponentAsync(scoped.Secondary)).Revision);
    }

    [Fact]
    public async Task Scalar_save_preserves_hidden_and_partial_fields_and_replays_once()
    {
        var before = await fixture.ReadAsync();
        var request = fixture.Request(before.SourceRevisionFingerprint,
            "save-premise", "{\"premise\":\"A newly reviewed premise.\"}");

        var written = await fixture.Writer.WriteAsync(request);
        var replayed = await fixture.Writer.WriteAsync(request);

        Assert.True(written.Applied);
        Assert.False(written.NoOp);
        Assert.NotEqual(before.SourceRevisionFingerprint, written.SourceRevisionFingerprint);
        Assert.Equal("A newly reviewed premise.", Json(written.OutputJson).GetProperty("premise").GetString());
        var component = await fixture.ComponentAsync(fixture.Primary);
        var value = Json(component.ValueJson);
        Assert.Equal("retained-private-field", value.GetProperty("hidden").GetString());
        Assert.Equal("Retained note", value.GetProperty("note").GetString());
        Assert.Equal(2, component.Revision);
        Assert.True(replayed.Replayed);
        Assert.False(replayed.Applied);
        Assert.Equal(written.OperationId, replayed.OperationId);
        Assert.Equal(2, (await fixture.ComponentAsync(fixture.Primary)).Revision);
        Assert.Single(await fixture.DeliveryRowsAsync());
    }

    [Fact]
    public async Task Mapped_two_component_save_notifies_its_registered_object_once_and_not_an_irrelevant_object()
    {
        var before = await fixture.ReadAsync();
        var saved = await fixture.Writer.WriteAsync(fixture.Request(before.SourceRevisionFingerprint,
            "two-component-change", "{\"premise\":\"Reviewed twice\",\"secondary\":\"changed\"}"));

        Assert.True(saved.Applied);
        Assert.Equal(2, saved.Receipts.Count);
        var deliveries = await fixture.DeliveryRowsAsync();
        Assert.DoesNotContain(deliveries, value => value.ObjectQualifiedId == "write-object.irrelevant");
        var delivery = Assert.Single(deliveries);
        Assert.Equal(ApplicationObjectChangeContract.ObjectScope, delivery.Scope);
        Assert.Equal("write-object.summary", delivery.ObjectQualifiedId);
        Assert.Equal(1, delivery.ObjectVersion);
        Assert.Equal("registered-dependency", delivery.Reason);
    }

    [Fact]
    public async Task Omission_is_a_no_op_while_explicit_clear_is_persisted_as_null()
    {
        var before = await fixture.ReadAsync();
        var omitted = await fixture.Writer.WriteAsync(fixture.Request(
            before.SourceRevisionFingerprint, "omit-fields", "{}"));

        Assert.True(omitted.NoOp);
        Assert.False(omitted.Applied);
        Assert.Empty(await fixture.DeliveryRowsAsync());
        Assert.Equal("Retained note", Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("note").GetString());

        var cleared = await fixture.Writer.WriteAsync(fixture.Request(
            omitted.SourceRevisionFingerprint, "clear-note", "{\"note\":null}"));

        Assert.True(cleared.Applied);
        Assert.Equal(JsonValueKind.Null, Json(cleared.OutputJson).GetProperty("note").ValueKind);
        Assert.Equal(JsonValueKind.Null, Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task Undeclared_and_unauthorized_edits_fail_without_mutation()
    {
        var before = await fixture.ReadAsync();
        var calculated = fixture.Request(before.SourceRevisionFingerprint,
            "calculated", "{\"calculated\":\"forged\"}");
        var player = fixture.Request(before.SourceRevisionFingerprint,
            "player", "{\"premise\":\"Forbidden\"}") with { Perspective = "player" };

        var calculatedFailure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(
            () => fixture.Writer.WriteAsync(calculated));
        var playerFailure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(
            () => fixture.Writer.WriteAsync(player));

        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID", calculatedFailure.Code);
        Assert.Equal("OBJECT_WRITE_FORBIDDEN", playerFailure.Code);
        Assert.Equal(1, (await fixture.ComponentAsync(fixture.Primary)).Revision);
    }

    [Fact]
    public async Task Stale_source_and_conflicting_idempotency_keys_fail_closed()
    {
        var before = await fixture.ReadAsync();
        var first = fixture.Request(before.SourceRevisionFingerprint,
            "stable-key", "{\"premise\":\"First edit\"}");
        var written = await fixture.Writer.WriteAsync(first);
        var stale = fixture.Request(before.SourceRevisionFingerprint,
            "stale-key", "{\"premise\":\"Stale edit\"}");
        var conflict = fixture.Request(before.SourceRevisionFingerprint,
            "stable-key", "{\"premise\":\"Different edit\"}");

        Assert.Equal("OBJECT_WRITE_SOURCE_STALE", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(
            () => fixture.Writer.WriteAsync(stale))).Code);
        Assert.Equal("OBJECT_WRITE_IDEMPOTENCY_CONFLICT", (await Assert.ThrowsAsync<ApplicationObjectWriteException>(
            () => fixture.Writer.WriteAsync(conflict))).Code);
        Assert.Equal("First edit", Json(written.OutputJson).GetProperty("premise").GetString());
    }

    [Fact]
    public async Task Late_component_failure_rolls_back_every_mapped_field()
    {
        var before = await fixture.ReadAsync();
        var request = fixture.Request(before.SourceRevisionFingerprint, "atomic-failure",
            "{\"premise\":\"Must roll back\",\"secondary\":\"rejected\"}");

        var failure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(
            () => fixture.Writer.WriteAsync(request));

        Assert.Equal("OBJECT_WRITE_REJECTED", failure.Code);
        Assert.Equal("Original premise", Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("premise").GetString());
        Assert.Equal("accepted", Json((await fixture.ComponentAsync(fixture.Secondary)).ValueJson)
            .GetProperty("detail").GetString());
        Assert.Equal(1, (await fixture.ComponentAsync(fixture.Primary)).Revision);
        Assert.Equal(1, (await fixture.ComponentAsync(fixture.Secondary)).Revision);
        Assert.Empty(await fixture.DeliveryRowsAsync());
    }

    [Fact]
    public async Task Declared_relationship_add_and_remove_use_typed_graph_effects()
    {
        var before = await fixture.ReadAsync();
        var added = await fixture.Writer.WriteAsync(fixture.Request(
            before.SourceRevisionFingerprint, "add-member", "{}",
            [new("/members", "relationship.add", Fixture.MemberTwo, 0)]));

        Assert.Equal(2, Json(added.OutputJson).GetProperty("members").GetArrayLength());
        Assert.NotNull(await fixture.Edges.GetRelationshipAsync(
            Fixture.StateSpace, Fixture.Subject, Fixture.MemberTwo, Fixture.MemberKind));

        var removed = await fixture.Writer.WriteAsync(fixture.Request(
            added.SourceRevisionFingerprint, "remove-member", "{}",
            [new("/members", "relationship.remove", Fixture.MemberOne, 1)]));

        var member = Assert.Single(Json(removed.OutputJson).GetProperty("members").EnumerateArray());
        Assert.Equal(Fixture.MemberTwo, member.GetProperty("id").GetString());
        Assert.Null(await fixture.Edges.GetRelationshipAsync(
            Fixture.StateSpace, Fixture.Subject, Fixture.MemberOne, Fixture.MemberKind));
    }

    [Theory]
    [InlineData("written-component")]
    [InlineData("read-only-component")]
    [InlineData("removed-component")]
    [InlineData("absent-root-component")]
    [InlineData("absent-endpoint-component")]
    [InlineData("added-edge")]
    [InlineData("removed-edge")]
    [InlineData("revised-edge")]
    [InlineData("empty-collection")]
    [InlineData("renamed-entity")]
    public async Task Concurrent_snapshot_changes_reject_the_entire_object_write(string change)
    {
        using var scoped = new Fixture(optionalSecondary: true);
        if (change == "absent-root-component")
            await scoped.Entities.RemoveComponentAsync(Fixture.StateSpace, Fixture.Subject, scoped.Secondary, 1);
        if (change == "empty-collection")
            await scoped.Edges.RemoveRelationshipAsync(Fixture.StateSpace, Fixture.Subject, Fixture.MemberOne, Fixture.MemberKind, 1);
        var before = await scoped.ReadAsync();
        scoped.BeforeApply = () => scoped.ChangeSnapshotAsync(change);

        var failure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(() => scoped.Writer.WriteAsync(
            scoped.Request(before.SourceRevisionFingerprint, "race", "{\"premise\":\"Must not commit\"}")));

        Assert.Equal("OBJECT_WRITE_SOURCE_STALE", failure.Code);
        Assert.Equal(change == "written-component" ? "Concurrent edit" : "Original premise",
            Json((await scoped.ComponentAsync(scoped.Primary)).ValueJson).GetProperty("premise").GetString());
        Assert.False((await scoped.AuditAsync()).Success);
    }

    [Fact]
    public async Task Successful_replay_does_not_revalidate_an_obsolete_snapshot_or_execute_again()
    {
        var before = await fixture.ReadAsync();
        var request = fixture.Request(before.SourceRevisionFingerprint, "replay-after-change", "{\"premise\":\"Saved\"}");
        var first = await fixture.Writer.WriteAsync(request);
        await fixture.ChangeSnapshotAsync("read-only-component");
        fixture.BeforeApply = () => throw new InvalidOperationException("Replay must not apply again.");
        var replay = await fixture.Writer.WriteAsync(request);
        Assert.True(replay.Replayed);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(2, (await fixture.ComponentAsync(fixture.Primary)).Revision);
    }

    [Fact]
    public async Task Full_object_mode_preserves_omissions_and_replays_the_exact_submission_once()
    {
        var before = await fixture.ReadAsync();
        var request = fixture.Request(before.SourceRevisionFingerprint, "full-object-save", "{}") with
        {
            SubmissionMode = "object",
            SubmittedObjectJson = "{\"premise\":\"Submitted premise\"}"
        };

        var saved = await fixture.Writer.WriteAsync(request);
        var replayed = await fixture.Writer.WriteAsync(request);

        Assert.True(saved.Applied);
        Assert.True(replayed.Replayed);
        Assert.Equal(saved.OperationId, replayed.OperationId);
        var component = Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson);
        Assert.Equal("Submitted premise", component.GetProperty("premise").GetString());
        Assert.Equal("Retained note", component.GetProperty("note").GetString());
        Assert.Equal("retained-private-field", component.GetProperty("hidden").GetString());
        Assert.Equal(1, (await fixture.ComponentAsync(fixture.Secondary)).Revision);
        Assert.Single(await fixture.DeliveryRowsAsync());

        var eventSummary = Assert.Single(await fixture.Ledger.FindAsync(rootOperationId: saved.OperationId));
        Assert.Equal("world.component.replaced", eventSummary.TypeId);
        Assert.Equal(new EventSourceContext("write-object", Fixture.StateSpace), eventSummary.Source);
        var eventDetail = await fixture.Ledger.GetAsync(eventSummary.Id);
        Assert.NotNull(eventDetail?.ComponentSnapshot);
        Assert.Equal(saved.OperationId, eventDetail.RootOperationId);
        Assert.Equal(Fixture.Subject, eventDetail.ComponentSnapshot.EntityId);
        Assert.Equal(fixture.Primary.QualifiedTypeId, eventDetail.ComponentSnapshot.QualifiedTypeId);
        Assert.Equal(1, eventDetail.ComponentSnapshot.BeforeRevision);
        Assert.Equal(2, eventDetail.ComponentSnapshot.AfterRevision);
        var beforeEvent = Json(eventDetail.ComponentSnapshot.BeforeJson!);
        var afterEvent = Json(eventDetail.ComponentSnapshot.AfterJson!);
        Assert.Equal("Original premise", beforeEvent.GetProperty("premise").GetString());
        Assert.Equal("Submitted premise", afterEvent.GetProperty("premise").GetString());
        Assert.Equal("Retained note", afterEvent.GetProperty("note").GetString());
        Assert.Equal("retained-private-field", afterEvent.GetProperty("hidden").GetString());

        var stale = request with
        {
            IdempotencyKey = "full-object-stale",
            SubmittedObjectJson = "{\"premise\":\"Must not persist\"}"
        };
        Assert.Equal("OBJECT_WRITE_SOURCE_STALE",
            (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
                fixture.Writer.WriteAsync(stale))).Code);
        Assert.Single(await fixture.Ledger.FindAsync(rootOperationId: saved.OperationId));
        Assert.Single(await fixture.Ledger.FindAsync());
        Assert.Equal("Submitted premise", Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("premise").GetString());

        var changesModeConflict = fixture.Request(before.SourceRevisionFingerprint,
            "full-object-save", "{\"premise\":\"Submitted premise\"}");
        Assert.Equal("OBJECT_WRITE_IDEMPOTENCY_CONFLICT",
            (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
                fixture.Writer.WriteAsync(changesModeConflict))).Code);
    }

    [Fact]
    public async Task Full_object_mode_rejects_read_only_and_schema_invalid_changes_without_mutation()
    {
        var before = await fixture.ReadAsync();
        var readOnly = fixture.Request(before.SourceRevisionFingerprint, "full-readonly", "{}") with
        {
            SubmissionMode = "object",
            SubmittedObjectJson = "{\"title\":\"Forged\"}"
        };
        var invalid = fixture.Request(before.SourceRevisionFingerprint, "full-invalid", "{}") with
        {
            SubmissionMode = "object",
            SubmittedObjectJson = "{\"premise\":\"\"}"
        };

        Assert.Equal("OBJECT_WRITE_READ_ONLY_CHANGED",
            (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
                fixture.Writer.WriteAsync(readOnly))).Code);
        Assert.Equal("OBJECT_WRITE_REQUEST_INVALID",
            (await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
                fixture.Writer.WriteAsync(invalid))).Code);
        Assert.Equal("Original premise", Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("premise").GetString());
        Assert.Empty(await fixture.DeliveryRowsAsync());
        Assert.Empty(await fixture.Ledger.FindAsync());
    }

    [Fact]
    public async Task Full_object_mode_late_component_failure_rolls_back_every_derived_change()
    {
        var before = await fixture.ReadAsync();
        var request = fixture.Request(before.SourceRevisionFingerprint, "full-rollback", "{}") with
        {
            SubmissionMode = "object",
            SubmittedObjectJson = "{\"premise\":\"Must roll back\",\"secondary\":\"rejected\"}"
        };

        var failure = await Assert.ThrowsAsync<ApplicationObjectWriteException>(() =>
            fixture.Writer.WriteAsync(request));

        Assert.Equal("OBJECT_WRITE_REJECTED", failure.Code);
        Assert.Equal("Original premise", Json((await fixture.ComponentAsync(fixture.Primary)).ValueJson)
            .GetProperty("premise").GetString());
        Assert.Equal("accepted", Json((await fixture.ComponentAsync(fixture.Secondary)).ValueJson)
            .GetProperty("detail").GetString());
        Assert.Empty(await fixture.DeliveryRowsAsync());
        Assert.Empty(await fixture.Ledger.FindAsync());
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    public void Dispose() => fixture.Dispose();

    private sealed class BeforeApplyApplier(IApplicationEcsEffectApplier inner, Func<Task> beforeApply) : IApplicationEcsEffectApplier
    {
        public async Task<ApplicationEcsEffectResult> ApplyAsync(ApplicationEcsEffectBatch batch, bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            await beforeApply();
            return await inner.ApplyAsync(batch, dryRun, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private const string Application = "write-object";
        public const string StateSpace = "write-object-space";
        public const string Subject = "subject.fixture";
        public const string MemberOne = "member.one";
        public const string MemberTwo = "member.two";
        public const string MemberKind = "write-object.member";
        private readonly SqliteFixture database = new();
        private readonly DantesRoleplayDbContext db;
        private readonly ProjectionReference projection;
        private readonly IProjectionCollectionMaterializer materializer;
        public readonly EcsComponentReference Primary;
        public readonly EcsComponentReference Secondary;
        public readonly EcsComponentReference Member;
        public readonly IEntityComponentStore Entities;
        public readonly IStateSpaceEdgeStore Edges;
        public readonly IApplicationObjectWriteService Writer;
        public readonly IEventLedger Ledger;
        public readonly ApplicationObjectDependencyIndexCache DependencyIndices = new();
        public Func<Task>? BeforeApply { get; set; }

        public Fixture(bool optionalSecondary = false, bool fieldBased = false)
        {
            db = database.CreateContext();
            var owner = ApplicationIdentifier.Parse(Application);
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(owner, "Write fixture", "", []));
            var transactions = new SqliteProjectionReadTransaction(db);
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications, transactions);
            stateSpaces.Create(new(StateSpace, revision, new('A', 64)));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            Primary = Ref(types.Define(new(owner, "write-object.primary", PrimarySchema)));
            Secondary = Ref(types.Define(new(owner, "write-object.secondary", SecondarySchema)));
            var member = Member = Ref(types.Define(new(owner, "write-object.member-state", MemberSchema)));
            Entities = new SqliteEntityComponentStore(db, types, schemas);
            Edges = new SqliteStateSpaceEdgeStore(db, stateSpaces, transactions);
            SeedAsync(member).GetAwaiter().GetResult();

            var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications,
                DependencyIndices);
            var definition = Definition(owner, Primary, Secondary, member, optionalSecondary);
            if (fieldBased)
                definition = definition with
                {
                    OutputSchemaJson = RegisteredApplicationObjectContract.TransportSchemaJson,
                    ObjectContract = definition.ObjectContract! with
                    {
                        ProfileId = RegisteredApplicationObjectContract.FieldBasedContractProfileId,
                        Collections = definition.ObjectContract!.Collections.Select(collection => collection with
                        { Metadata = new("/totalCount", "/complete", "/nextCursor") }).ToArray()
                    }
                };
            projection = registry.Define(definition).Reference;
            registry.Define(IrrelevantDefinition(owner, member));
            var source = new SqliteProjectionSourceSnapshotReader(db, stateSpaces, Entities);
            var root = new ProjectionMaterializer(registry, Entities, stateSpaces, schemas,
                new ProjectionPlanCache(), source);
            materializer = new ProjectionCollectionMaterializer(registry, root,
                (IRelationshipCollectionReader)Edges, (IEntityBatchReadStore)Entities,
                Entities, schemas, transactions);
            var eventTypes = new EventTypeStore(db);
            eventTypes.WriteAsync(new()
            {
                Id = "world.component.replaced", Category = "fixture", Name = "Component replaced",
                Scope = "", Status = EventTypeStatus.Active, PayloadSchema = StructuralEventSchema
            }).GetAwaiter().GetResult();
            Ledger = new EventLedger(db);
            var structuralEvents = new ApplicationStructuralEventTransactionParticipant(
                stateSpaces, Ledger, eventTypes, schemas);
            var applier = new ApplicationEcsEffectApplier(
                db, Entities, stateSpaces, new OperationLog(db), Edges,
                [new ApplicationObjectChangeTransactionParticipant(db, stateSpaces, DependencyIndices)],
                eventSources: [structuralEvents]);
            Writer = new ApplicationObjectWriteService(
                registry, materializer, Entities, new BeforeApplyApplier(applier, () => BeforeApply?.Invoke() ?? Task.CompletedTask),
                new OperationLog(db), schemas);
        }

        public Task<ProjectionCollectionMaterializationResult> ReadAsync() => materializer.MaterializeAsync(new(
            StateSpace, projection, new Dictionary<string, string> { ["subject"] = Subject },
            "members", "dm"));

        public ApplicationObjectWriteRequest Request(
            string expectedSourceRevision,
            string idempotencyKey,
            string changes,
            IReadOnlyList<ApplicationObjectRelationshipEdit>? relationships = null) => new(
                StateSpace, ApplicationIdentifier.Parse(Application), projection,
                new Dictionary<string, string> { ["subject"] = Subject }, "members", "dm",
                idempotencyKey, expectedSourceRevision, changes, relationships ?? []);

        public async Task<EcsComponentView> ComponentAsync(EcsComponentReference type) =>
            (await Entities.GetComponentAsync(StateSpace, Subject, type.QualifiedTypeId))!;

        public async Task<Operation> AuditAsync() => await db.Operations.AsNoTracking().SingleAsync();

        public async Task<IReadOnlyList<ChangeDelivery>> DeliveryRowsAsync() =>
            await db.Set<ApplicationObjectChangeRecord>().AsNoTracking()
                .Where(value => value.Scope != ApplicationObjectChangeContract.NoChangeScope)
                .OrderBy(value => value.Cursor)
                .Select(value => new ChangeDelivery(value.Scope, value.ObjectQualifiedId,
                    value.ObjectVersion, value.Reason))
                .ToArrayAsync();

        public async Task ChangeSnapshotAsync(string change)
        {
            Assert.Null(db.Database.CurrentTransaction);
            switch (change)
            {
                case "written-component":
                    await Entities.MergeComponentAsync(new(StateSpace, Subject, Primary, "{\"premise\":\"Concurrent edit\"}", 1));
                    break;
                case "read-only-component":
                    await Entities.SetComponentAsync(new(StateSpace, MemberOne, Member, "{\"status\":\"changed\"}", 1));
                    break;
                case "removed-component":
                    await Entities.RemoveComponentAsync(StateSpace, MemberOne, Member, 1);
                    break;
                case "absent-root-component":
                    await Entities.AddComponentAsync(new(StateSpace, Subject, Secondary, "{\"detail\":\"accepted\"}", 0));
                    break;
                case "absent-endpoint-component":
                    await Entities.AddComponentAsync(new(StateSpace, MemberOne, Secondary, "{\"detail\":\"accepted\"}", 0));
                    break;
                case "added-edge":
                case "empty-collection":
                    await Edges.SetRelationshipAsync(StateSpace, Subject, MemberTwo, MemberKind, "{}", 0);
                    break;
                case "removed-edge":
                    await Edges.RemoveRelationshipAsync(StateSpace, Subject, MemberOne, MemberKind, 1);
                    break;
                case "revised-edge":
                    await Edges.SetRelationshipAsync(StateSpace, Subject, MemberOne, MemberKind, "{\"changed\":true}", 1);
                    break;
                case "renamed-entity":
                    await db.Set<ApplicationEcsEntityRecord>().Where(value => value.StateSpaceId == StateSpace && value.Id == MemberOne)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.Name, "Renamed")
                            .SetProperty(value => value.Revision, value => value.Revision + 1));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        private async Task SeedAsync(EcsComponentReference member)
        {
            await Entities.CreateEntityAsync(StateSpace, Subject, "Subject");
            await Entities.CreateEntityAsync(StateSpace, MemberOne, "Member one");
            await Entities.CreateEntityAsync(StateSpace, MemberTwo, "Member two");
            await Entities.AddComponentAsync(new(StateSpace, Subject, Primary,
                "{\"title\":\"Fixture\",\"premise\":\"Original premise\",\"note\":\"Retained note\",\"hidden\":\"retained-private-field\"}", 0));
            await Entities.AddComponentAsync(new(StateSpace, Subject, Secondary,
                "{\"detail\":\"accepted\"}", 0));
            await Entities.AddComponentAsync(new(StateSpace, MemberOne, member,
                "{\"status\":\"active\"}", 0));
            await Entities.AddComponentAsync(new(StateSpace, MemberTwo, member,
                "{\"status\":\"active\"}", 0));
            await Edges.SetRelationshipAsync(
                StateSpace, Subject, MemberOne, MemberKind, "{}", 0);
        }

        private static ProjectionDefinitionRequest Definition(
            ApplicationIdentifier owner,
            EcsComponentReference primary,
            EcsComponentReference secondary,
            EcsComponentReference member,
            bool optionalSecondary) => new(
                owner, "write-object.summary", OutputSchema,
                [new("primary", "subject", primary), new("secondary", "subject", secondary)], [],
                [
                    new("primary", "/title", "/title"),
                    new("primary", "/premise", "/premise"),
                    new("primary", "/note", "/note"),
                    new("secondary", "/detail", "/secondary")
                ],
                new(
                    [new("subject", true), new("member", false)],
                    [new("primary", true), new("secondary", !optionalSecondary)],
                    [new("members", MemberKind, "subject", "member", "many", "/members",
                        [new("to", member)], [new("to", secondary)])],
                    [],
                    [new("members", "members", 20, 20, [new("/name", "asc")],
                        "source-revision-bound")],
                    new(2, 20, 65_536, 12),
                    new(["player", "dm"], ["dm"]),
                    new(EditSchema,
                        ["set", "clear", "relationship.add", "relationship.remove"],
                        [
                            new("/premise", ["set"]),
                            new("/note", ["set", "clear"]),
                            new("/secondary", ["set"]),
                            new("/members", ["relationship.add", "relationship.remove"])
                        ])),
                1);

        private static ProjectionDefinitionRequest IrrelevantDefinition(
            ApplicationIdentifier owner, EcsComponentReference member) => new(
                owner, "write-object.irrelevant", IrrelevantOutputSchema,
                [new("member", "subject", member)], [],
                [new("member", "/status", "/status")],
                new([new("subject", true)], [new("member", true)], [], [], [],
                    new(1, 1, 1_024, 4), new(["dm"], []), null), 1);

        private static EcsComponentReference Ref(RegisteredComponentTypeVersion type) =>
            new(type.QualifiedId, type.Version, type.SchemaHash);

        private const string PrimarySchema = """
        {"type":"object","additionalProperties":false,"required":["title","premise","note","hidden"],"properties":{"title":{"type":"string"},"premise":{"type":"string","minLength":1},"note":{"type":["string","null"]},"hidden":{"type":"string"}}}
        """;
        private const string SecondarySchema = """
        {"type":"object","additionalProperties":false,"required":["detail"],"properties":{"detail":{"enum":["accepted","changed"]}}}
        """;
        private const string MemberSchema = """
        {"type":"object","additionalProperties":false,"required":["status"],"properties":{"status":{"type":"string"}}}
        """;
        private const string OutputSchema = """
        {"type":"object","required":["title","premise","note","members","totalCount","complete","nextCursor"],"additionalProperties":false,"properties":{"title":{"type":"string"},"premise":{"type":"string"},"note":{"type":["string","null"]},"secondary":{"type":"string"},"members":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","name","status"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"status":{"type":"string"}}}},"totalCount":{"type":"integer"},"complete":{"type":"boolean"},"nextCursor":{"type":["string","null"]}}}
        """;
        private const string EditSchema = """
        {"type":"object","additionalProperties":false,"properties":{"premise":{"type":"string","minLength":1},"note":{"type":["string","null"]},"secondary":{"type":"string"}}}
        """;
        private const string IrrelevantOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["status"],"properties":{"status":{"type":"string"}}}
        """;
        private const string StructuralEventSchema = """
        {"type":"object","additionalProperties":false,"required":["effectIndex","entityId","definitionId","before","after"],"properties":{"effectIndex":{"type":"integer"},"entityId":{"type":"string"},"definitionId":{"type":"string"},"before":{},"after":{}}}
        """;

        public sealed record ChangeDelivery(string Scope, string? ObjectQualifiedId,
            int? ObjectVersion, string Reason);

        public void Dispose()
        {
            db.Dispose();
            database.Dispose();
        }
    }
}

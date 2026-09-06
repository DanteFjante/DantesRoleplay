using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Projections.Tests;

public sealed class ApplicationObjectChangeTests : IDisposable
{
    private readonly Fixture fixture = new();

    [Fact]
    public async Task Component_and_membership_changes_stage_exact_durable_objects_without_source_ids()
    {
        var component = await fixture.Store.GetComponentAsync(Fixture.Space, Fixture.Subject,
            fixture.Source.QualifiedTypeId);
        var changed = await fixture.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = Fixture.Space,
            Effects =
            [
                new()
                {
                    Type = ApplicationEcsEffectType.ComponentSet,
                    EntityId = Fixture.Subject,
                    ComponentType = fixture.Source,
                    DataJson = "{\"name\":\"changed\"}",
                    ExpectedRevision = component!.Revision
                },
                new()
                {
                    Type = ApplicationEcsEffectType.RelationshipSet,
                    EntityId = Fixture.Subject,
                    TargetEntityId = Fixture.Member,
                    QualifiedRelationshipKind = Fixture.Relationship,
                    DataJson = "{}",
                    ExpectedRevision = 0
                }
            ]
        });

        Assert.True(changed.Applied);
        var rows = await fixture.ReadRowsAsync();
        var row = Assert.Single(rows);
        Assert.Equal("change-test.summary", row.ObjectId);
        Assert.Equal(1, row.ObjectVersion);
        Assert.Equal("[\"dm\"]", row.Perspectives);
        Assert.DoesNotContain(Fixture.Subject, row.Joined, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Source.QualifiedTypeId, row.Joined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrelated_component_change_does_not_stage_world_invalidation()
    {
        var component = await fixture.Store.GetComponentAsync(Fixture.Space, Fixture.Subject,
            fixture.Unrelated.QualifiedTypeId);

        var result = await fixture.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = Fixture.Space,
            Effects = [new()
            {
                Type = ApplicationEcsEffectType.ComponentSet,
                EntityId = Fixture.Subject,
                ComponentType = fixture.Unrelated,
                DataJson = "{\"value\":2}",
                ExpectedRevision = component!.Revision
            }]
        });

        Assert.True(result.Applied);
        var marker = Assert.Single(await fixture.ReadRowsAsync());
        Assert.Equal(ApplicationObjectChangeContract.NoChangeScope, marker.Scope);
        Assert.Null(marker.ObjectId);
        Assert.Equal("[]", marker.Perspectives);
    }

    [Fact]
    public async Task Late_failure_rolls_back_staged_change_evidence_with_the_write()
    {
        var component = await fixture.Store.GetComponentAsync(Fixture.Space, Fixture.Subject,
            fixture.Source.QualifiedTypeId);
        var applier = fixture.CreateApplier([
            fixture.Participant,
            new RejectingParticipant()
        ]);

        var result = await applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = Fixture.Space,
            Effects = [new()
            {
                Type = ApplicationEcsEffectType.ComponentSet,
                EntityId = Fixture.Subject,
                ComponentType = fixture.Source,
                DataJson = "{\"name\":\"rolled back\"}",
                ExpectedRevision = component!.Revision
            }]
        });

        Assert.False(result.Applied);
        Assert.Empty(await fixture.ReadRowsAsync());
        var current = await fixture.Store.GetComponentAsync(Fixture.Space, Fixture.Subject,
            fixture.Source.QualifiedTypeId);
        Assert.Equal("{\"name\":\"original\"}", current!.ValueJson);
    }

    [Fact]
    public async Task Entity_lifecycle_uses_audience_scoped_application_fallback()
    {
        var result = await fixture.Applier.ApplyAsync(new ApplicationEcsEffectBatch
        {
            StateSpaceId = Fixture.Space,
            Effects = [new()
            {
                Type = ApplicationEcsEffectType.EntityCreate,
                EntityId = "created.fixture",
                Name = "Created"
            }]
        });

        Assert.True(result.Applied);
        var row = Assert.Single(await fixture.ReadRowsAsync());
        Assert.Equal(ApplicationObjectChangeContract.ApplicationScope, row.Scope);
        Assert.Null(row.ObjectId);
        Assert.Equal("[\"dm\",\"player\"]", row.Perspectives);
    }

    private sealed class RejectingParticipant : IApplicationEcsTransactionParticipant
    {
        public Task StageAsync(ApplicationEcsEffectBatch batch,
            IReadOnlyList<ApplicationEcsEffectReceipt> receipts, string operationId,
            CancellationToken cancellationToken = default) =>
            throw new ApplicationEcsTransactionParticipantException("Reject after delivery staging.");
    }

    private sealed class Fixture : IDisposable
    {
        public const string Space = "change-test-space";
        public const string Subject = "subject.fixture";
        public const string Member = "member.fixture";
        public const string Relationship = "change-test.member";
        private readonly SqliteFixture database = new();
        private readonly DantesRoleplayDbContext db;
        private readonly SqliteStateSpaceRegistry stateSpaces;
        private readonly SqliteStateSpaceEdgeStore edges;
        public readonly EcsComponentReference Source;
        public readonly EcsComponentReference Unrelated;
        public readonly SqliteEntityComponentStore Store;
        public readonly ApplicationObjectChangeTransactionParticipant Participant;
        public readonly IApplicationEcsEffectApplier Applier;

        public Fixture()
        {
            db = database.CreateContext();
            var owner = ApplicationIdentifier.Parse("change-test");
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(owner, "Change test", "", []));
            stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            stateSpaces.Create(new(Space, revision, revision.Fingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            Source = Ref(types.Define(new(owner, "change-test.source", Schema("name", "string"))));
            Unrelated = Ref(types.Define(new(owner, "change-test.unrelated", Schema("value", "integer"))));
            var member = Ref(types.Define(new(owner, "change-test.member-state", Schema("name", "string"))));
            var registry = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
            var baseProjection = registry.Define(new(owner, "change-test.base",
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"}}}",
                [new("source", "subject", Source)], [], [new("source", "/name", "/name")]));
            registry.Define(Definition(owner, baseProjection.Reference, member));

            Store = new SqliteEntityComponentStore(db, types, schemas);
            edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            Store.CreateEntityAsync(Space, Subject, "Subject").GetAwaiter().GetResult();
            Store.CreateEntityAsync(Space, Member, "Member").GetAwaiter().GetResult();
            Store.AddComponentAsync(new(Space, Subject, Source, "{\"name\":\"original\"}", 0))
                .GetAwaiter().GetResult();
            Store.AddComponentAsync(new(Space, Subject, Unrelated, "{\"value\":1}", 0))
                .GetAwaiter().GetResult();
            Store.AddComponentAsync(new(Space, Member, member, "{\"name\":\"member\"}", 0))
                .GetAwaiter().GetResult();
            Participant = new(db, stateSpaces);
            Applier = CreateApplier([Participant]);
        }

        public IApplicationEcsEffectApplier CreateApplier(
            IReadOnlyList<IApplicationEcsTransactionParticipant> participants) =>
            new ApplicationEcsEffectApplier(db, Store, stateSpaces, new OperationLog(db), edges,
                participants);

        public async Task<IReadOnlyList<ChangeRow>> ReadRowsAsync()
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Scope", "ObjectQualifiedId", "ObjectVersion", "ReadPerspectivesJson", "Reason"
                FROM system_application_object_change ORDER BY "Cursor";
                """;
            var rows = new List<ChangeRow>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
            return rows;
        }

        private static ProjectionDefinitionRequest Definition(
            ApplicationIdentifier owner, ProjectionReference baseProjection, EcsComponentReference member) =>
            new(owner, "change-test.summary",
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"name\":{\"type\":\"string\"},\"members\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}",
                [], [new("base", baseProjection, new Dictionary<string, string> { ["subject"] = "subject" })],
                [new("base", "/name", "/name")],
                new([new("subject", true), new("member", false)], [],
                    [new("members", Relationship, "subject", "member", "many", "/members",
                        [new("to", member)], [])], [new("base", true)],
                    [new("members", "members", 25, 100, [new("", "asc")], "source-revision-bound")],
                    new(2, 100, 16_384, 8), new(["dm"], []), null), 1);

        private static string Schema(string property, string type) =>
            $"{{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"{property}\"],\"properties\":{{\"{property}\":{{\"type\":\"{type}\"}}}}}}";
        private static EcsComponentReference Ref(RegisteredComponentTypeVersion type) =>
            new(type.QualifiedId, type.Version, type.SchemaHash);
        public void Dispose() => database.Dispose();
    }

    private sealed record ChangeRow(
        string Scope, string? ObjectId, int? ObjectVersion, string Perspectives, string Reason)
    {
        public string Joined => $"{Scope}|{ObjectId}|{ObjectVersion}|{Perspectives}|{Reason}";
    }

    public void Dispose() => fixture.Dispose();
}

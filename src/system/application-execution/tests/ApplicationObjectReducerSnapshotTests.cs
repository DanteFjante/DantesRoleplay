using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationObjectReducerSnapshotTests
{
    public static IEnumerable<object[]> Races =>
        from incoming in new[] { false, true }
        from change in new[] { "add", "remove", "revise", "empty-add", "nested-add", "nested-remove", "nested-revise" }
        select new object[] { incoming, change };

    [Theory]
    [MemberData(nameof(Races))]
    public async Task Reducer_rejects_changes_to_complete_collections_before_any_effect(bool incoming, string change)
    {
        using var fixture = new Fixture(incoming);
        if (change == "empty-add") await fixture.RemoveAsync("one");
        if (change is "nested-remove" or "nested-revise")
            await fixture.Edges.SetRelationshipAsync("space", "one", "two", "snapshot.related", "{}", 0);
        fixture.BeforeApply = async () =>
        {
            switch (change)
            {
                case "add": case "empty-add": await fixture.SetAsync("two", 0); break;
                case "remove": await fixture.RemoveAsync("one"); break;
                case "revise": await fixture.SetAsync("one", 1); break;
                case "nested-add":
                    await fixture.Edges.SetRelationshipAsync("space", "one", "two", "snapshot.related", "{}", 0); break;
                case "nested-remove":
                    await fixture.Edges.RemoveRelationshipAsync("space", "one", "two", "snapshot.related", 1); break;
                case "nested-revise":
                    await fixture.Edges.SetRelationshipAsync("space", "one", "two", "snapshot.related", "{\"new\":true}", 1); break;
            }
        };
        var result = await fixture.Runner.RunAsync(fixture.Request);
        Assert.True(result.Disposition == ApplicationActionExecutionDisposition.Stale, JsonSerializer.Serialize(result));
        Assert.Contains(result.Problems, value => value.Code.Contains("STALE", StringComparison.Ordinal));
        Assert.Equal(1, (await fixture.Store.GetComponentAsync("space", "root", fixture.Type.QualifiedTypeId))!.Revision);
        Assert.NotNull(fixture.LastBatch);
        Assert.Contains(fixture.LastBatch.RelationshipExpectations, value => value.AnchorEntityId == "root" && value.Incoming == incoming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unchanged_collections_commit_and_replay_even_after_later_changes(bool incoming)
    {
        using var fixture = new Fixture(incoming);
        var first = await fixture.Runner.RunAsync(fixture.Request);
        Assert.True(first.Disposition == ApplicationActionExecutionDisposition.Succeeded, JsonSerializer.Serialize(first));
        Assert.Equal("{\"value\":1}", (await fixture.Store.GetComponentAsync("space", "root", fixture.Type.QualifiedTypeId))!.ValueJson);
        await fixture.SetAsync("two", 0);
        fixture.BeforeApply = () => throw new InvalidOperationException("Replay must not execute again.");
        var replay = await fixture.Runner.RunAsync(fixture.Request);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(first.OperationId, replay.OperationId);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteFixture database = new();
        private readonly DantesRoleplayDbContext db;
        private readonly bool incoming;
        public readonly SqliteEntityComponentStore Store;
        public readonly SqliteStateSpaceEdgeStore Edges;
        public readonly EcsComponentReference Type;
        public readonly ApplicationActionRunner Runner;
        public readonly ApplicationActionExecutionRequest Request;
        public Func<Task>? BeforeApply { get; set; }
        public ApplicationEcsEffectBatch? LastBatch { get; private set; }

        public Fixture(bool incoming)
        {
            this.incoming = incoming;
            db = database.CreateContext();
            var app = ApplicationIdentifier.Parse("snapshot");
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(app, "Snapshot fixture", "", []));
            var activationFingerprint = Hash("activation");
            var transactions = new SqliteProjectionReadTransaction(db);
            var spaces = new SqliteStateSpaceRegistry(db, applications, transactions);
            spaces.Create(new("space", revision, activationFingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var type = types.Define(new(app, "snapshot.state", "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
            Type = new(type.QualifiedId, type.Version, type.SchemaHash);
            Store = new(db, types, schemas);
            Edges = new(db, spaces, transactions);
            foreach (var id in new[] { "root", "one", "two" }) Store.CreateEntityAsync("space", id, id).GetAwaiter().GetResult();
            Store.AddComponentAsync(new("space", "root", Type, "{\"value\":0}", 0)).GetAwaiter().GetResult();
            SetAsync("one", 0).GetAwaiter().GetResult();
            var definitions = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
            var definition = definitions.Define(new(app, "snapshot.object", """
                {"type":"object","required":["value","members"],"additionalProperties":false,"properties":{"value":{"type":"integer"},"members":{"type":"array","items":{"type":"object","required":["related"],"properties":{"related":{"type":"array","items":{"type":"object"}}}}}}}
                """, [new("state", "subject", Type)], [], [new("state", "/value", "/value")], new(
                    [new("subject", true), new("member", false), new("related", false)], [new("state", true)],
                    [new("members", "snapshot.member", incoming ? "member" : "subject", incoming ? "subject" : "member",
                         "many", "/members", [], [], incoming ? "incoming" : "outgoing"),
                     new("related", "snapshot.related", "member", "related", "many", "/members/*/related", [], [])],
                    [], [new("members", "members", 20, 20, [new("/name", "asc")], "source-revision-bound")],
                    new(2, 20, 65536, 12), new(["dm"], []), null), 1));
            var requirements = JsonSerializer.Serialize(new
            {
                roles = new { subject = new { components = new[] { "state" } } },
                objectRoles = new { snapshot = new { qualifiedId = definition.QualifiedId, version = definition.Version,
                    contentFingerprint = definition.ContentHash, roleBindings = new { subject = "subject" }, collectionId = "members", perspective = "dm" } },
                inputSchema = new { type = "object", additionalProperties = false }, effectComponentIds = new[] { "state" }
            });
            var content = JsonSerializer.Serialize(new { requirements, source = """
                var snapshot = ctx.objects.snapshot;
                var count = snapshot.value.members.length;
                for (var member of snapshot.value.members) count += member.related.length;
                return {effects:[{type:'component.set',entityId:snapshot.roles.subject.id,definitionId:'state',data:JSON.stringify({value:count})}]};
                """ });
            var record = new CatalogRecordDefinition(app.Value, "mechanic", "snapshot.count", "Count", "Count fixture links.",
                [], [], "mechanics", "active", 1, content, Hash(content), "source", "mechanics/count.md");
            var manifest = CatalogNavigationManifest.Create(app, Hash("catalog"), "catalog-lexical-v1",
                [new(app.Value, "Fixture", "Fixture")],
                [new(app.Value, "", "Fixture", "Fixture", CatalogDescriptionStatus.Authored),
                 new(app.Value, "mechanics", "Mechanics", "Mechanics", CatalogDescriptionStatus.Authored)], [record]);
            var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
            { [app] = new InMemoryCatalogNavigator(manifest, new CatalogCursorCodec(Encoding.UTF8.GetBytes("snapshot-test-cursor-key-at-least-32"))) });
            var activation = new StaticActivation(new(app, 1, revision.Revision, revision.Fingerprint,
                Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
                "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow));
            var materializer = new ProjectionMaterializer(definitions, Store, spaces, schemas,
                snapshots: new SqliteProjectionSourceSnapshotReader(db, spaces, Store));
            var collections = new ProjectionCollectionMaterializer(definitions, materializer, Edges, Store, Store, schemas, transactions);
            var evaluator = new ApplicationMechanicEvaluator(catalogs, new ApplicationMechanicProjectionResolver(db, spaces),
                new JintMechanicEngine(), objectProjections: new ApplicationMechanicObjectProjectionResolver(definitions, materializer, collections, Store));
            var operations = new OperationLog(db);
            var applier = new InterceptApplier(new ApplicationEcsEffectApplier(db, Store, spaces, operations, Edges), async batch =>
            {
                LastBatch = batch;
                Assert.Null(db.Database.CurrentTransaction);
                if (BeforeApply is not null) await BeforeApply();
            });
            Runner = new(catalogs, activation, spaces, types, Store, Edges,
                new ApplicationMechanicProjectionMappingResolver(catalogs, spaces, types, Edges), evaluator, applier, operations);
            Request = new("space", app, record.QualifiedId, record.Version, record.ContentFingerprint,
                new Dictionary<string, string> { ["subject"] = "root" }, "{}", 42, new("0123456789abcdef0123456789abcdef", Hash("request")));
        }

        public Task<EcsRelationshipView> SetAsync(string target, int revision) => Edges.SetRelationshipAsync(
            "space", incoming ? target : "root", incoming ? "root" : target, "snapshot.member", "{}", revision);
        public Task<bool> RemoveAsync(string target) => Edges.RemoveRelationshipAsync(
            "space", incoming ? target : "root", incoming ? "root" : target, "snapshot.member", 1);
        public void Dispose() { db.Dispose(); database.Dispose(); }
    }

    private sealed class StaticActivation(ActiveApplicationManifest manifest) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) => manifest.ApplicationId == applicationId ? manifest : null;
    }
    private sealed class InterceptApplier(IApplicationEcsEffectApplier inner, Func<ApplicationEcsEffectBatch, Task> before) : IApplicationEcsEffectApplier
    {
        public async Task<ApplicationEcsEffectResult> ApplyAsync(ApplicationEcsEffectBatch batch, bool dryRun = false,
            CancellationToken cancellationToken = default)
        {
            await before(batch);
            return await inner.ApplyAsync(batch, dryRun, cancellationToken);
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

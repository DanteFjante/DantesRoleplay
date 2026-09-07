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

public sealed class ApplicationSnapshotObjectTests
{
    [Fact]
    public async Task Structural_assembly_uses_only_supplied_values_and_preserves_revision_and_remaining_facets()
    {
        using var f = new Fixture();
        var result = await f.Resolve();
        Assert.True(result.Ok);
        var projection = result.Projection!;
        Assert.Equal(42, projection.Objects["record"].Value.GetProperty("value").GetInt32());
        Assert.Equal(f.Definition.ContentHash, projection.Objects["record"].ContentFingerprint);
        Assert.Equal("visible", projection.Objects["record"].Roles["record"].Id);
        Assert.Equal(f.Snapshot.AuthorizedSourceRevision, projection.AuthorizedSourceRevision);
        Assert.Equal(f.Snapshot.ComponentRevisions, projection.ComponentRevisions);
        Assert.Equal(new[] { "facet" }, projection.Roles["subject"].Components.Keys);
        Assert.True(f.Snapshot.Roles["subject"].Components.ContainsKey("state"));
        // All source readers passed to the real materializer/resolver are null. Any reread fails.
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("owner")]
    [InlineData("audience")]
    [InlineData("space")]
    [InlineData("unbound")]
    [InlineData("unauthorized-input")]
    [InlineData("unauthorized-reference")]
    [InlineData("cycle")]
    [InlineData("type-version")]
    [InlineData("schema-output")]
    [InlineData("byte-budget")]
    public async Task Invalid_or_unauthorized_snapshots_fail_closed(string problem)
    {
        using var f = new Fixture();
        switch (problem)
        {
            case "fingerprint": f.Requirement = f.Requirement with { ContentFingerprint = new('B', 64) }; break;
            case "owner": f.Request = f.Request with { ApplicationId = ApplicationIdentifier.Parse("other") }; break;
            case "audience": f.Request = f.Request with { Audience = null }; break;
            case "space": f.Snapshot = f.Snapshot with { StateSpaceId = "other" }; break;
            case "unbound": f.Snapshot = f.Snapshot with { Roles = [] }; break;
            case "unauthorized-input":
                f.Snapshot = f.Snapshot with { Input = "{\"target\":\"hidden\"}" };
                f.Requirement = f.Requirement with { RoleBindings = new() { ["record"] = new() { InputEntityId = "target" } } };
                break;
            case "unauthorized-reference":
                f.Requirement = f.Requirement with { RoleBindings = new()
                {
                    ["source"] = new() { Role = "subject" },
                    ["record"] = new() { FromRole = "source", ComponentId = "facet", Field = "target" }
                } };
                break;
            case "cycle":
                f.Requirement = f.Requirement with { RoleBindings = new()
                {
                    ["record"] = new() { FromRole = "record", ComponentId = "facet", Field = "target" }
                } };
                break;
            case "type-version":
                f.Request = f.Request with { Mapping = new(new Dictionary<string, EcsComponentReference>
                    { ["state"] = f.Type with { TypeVersion = 2 } }, new Dictionary<string, string>()) };
                break;
            case "schema-output":
                f.Snapshot.Roles["subject"] = new("visible", "Visible", new Dictionary<string, string>
                    { ["state"] = "{\"value\":\"not an integer\"}" });
                break;
            case "byte-budget":
                f.Snapshot.Roles["subject"] = f.Snapshot.Roles["subject"] with { Name = new('x', 1_048_576) };
                break;
        }
        var result = await f.Resolve();
        Assert.False(result.Ok);
        Assert.Null(result.Projection);
    }

    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, false)]
    public async Task Collections_are_complete_ordered_and_bounded_without_exposing_label_only_references(int count, int bound, bool ok)
    {
        using var f = new Fixture();
        f.Requirement = f.Requirement with
        {
            RoleBindings = new() { ["record"] = new() { Reference = true } },
            ReferenceComponentIds = ["state"], MaximumItems = bound
        };
        f.Snapshot.References["hidden"] = new("hidden", new Dictionary<string, string>(), "Label only");
        for (var i = count - 1; i >= 0; i--)
            f.Snapshot.References["ref" + i] = new("ref" + i, new Dictionary<string, string>
                { ["state"] = "{\"value\":" + i + "}" }, "Record " + i);
        var result = await f.Resolve();
        Assert.Equal(ok, result.Ok);
        if (!ok) { Assert.Null(result.Projection); return; }
        var records = result.Projection!.Objects["record"].Value;
        Assert.Equal(Enumerable.Range(0, count).Select(i => "ref" + i),
            records.EnumerateArray().Select(value => value.GetProperty("id").GetString()));
        Assert.DoesNotContain("hidden", records.GetRawText());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("root")]
    [InlineData("child")]
    public async Task Production_evaluator_supplies_objects_and_rejects_write_outputs(string write)
    {
        using var f = new Fixture();
        const string effectSource = "return {effects:[{type:'entity.create',entityId:'new',name:'New'}]};";
        var childContent = JsonSerializer.Serialize(new { requirements = "{}", source = effectSource });
        var childHash = ApplicationCatalogRecordContent.Fingerprint(childContent);
        var requirements = f.Requirements;
        if (write == "child") requirements.Children["writer"] = new()
        {
            MechanicId = "snapshot.child", MechanicVersion = 1, ContentFingerprint = childHash
        };
        var content = JsonSerializer.Serialize(new { requirements = JsonSerializer.Serialize(requirements),
            source = write == "root"
                ? "return {effects:[{type:'entity.create',entityId:'new',name:'New'}]};"
                : "return {data:{value:ctx.objects.record.value.value,raw:!!ctx.roles.subject.components.state}};" });
        var hash = ApplicationCatalogRecordContent.Fingerprint(content);
        var record = new CatalogRecordDefinition("snapshot", "mechanic", "snapshot.read", "Read", "Read fixture.",
            [], [], "mechanics", "active", 1, content, hash, "fixture", "mechanics/read.md");
        var manifest = CatalogNavigationManifest.Create(f.Owner, new('A', 64), "catalog-lexical-v1",
            [new("snapshot", "Fixture", "Fixture")],
            [new("snapshot", "", "Fixture", "Fixture", CatalogDescriptionStatus.Authored),
             new("snapshot", "mechanics", "Mechanics", "Mechanics", CatalogDescriptionStatus.Authored)],
            [record, new("snapshot", "mechanic", "snapshot.child", "Child", "Child fixture.", [], [], "mechanics",
                "active", 1, childContent, childHash, "fixture", "mechanics/child.md")]);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [f.Owner] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("snapshot-collection-test-key-32-bytes")))
        });
        var evaluator = new ApplicationMechanicEvaluator(catalogs, new SuppliedProjection(f.Snapshot),
            new JintMechanicEngine(), objectProjections: f.Resolver);
        var result = await evaluator.EvaluateAsync(f.Request with { ContentFingerprint = hash });
        if (write != "none") Assert.Equal(new[] { "READ_MODEL_OUTPUT_UNSAFE" }, result.Problems);
        else
        {
            Assert.True(result.Ok, string.Join(';', result.Problems) + result.Run?.Error);
            using var data = JsonDocument.Parse(result.Run!.Output.Data);
            Assert.Equal(42, data.RootElement.GetProperty("value").GetInt32());
            Assert.False(data.RootElement.GetProperty("raw").GetBoolean());
        }
    }

    [Theory]
    [InlineData("{\"snapshotObjects\":null}")]
    [InlineData("{\"snapshotObjects\":{\"x\":null}}")]
    [InlineData("{\"snapshotObjects\":{\"x\":{\"unexpected\":true}}}")]
    [InlineData("{\"snapshotObjects\":{\"x\":{\"version\":1,\"Version\":2}}}")]
    public void Malformed_snapshot_contracts_are_rejected(string json) =>
        Assert.Throws<JsonException>(() => MechanicRequirements.Parse(json));

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteFixture database = new();
        private readonly DantesRoleplayDbContext db;
        public ApplicationIdentifier Owner { get; } = ApplicationIdentifier.Parse("snapshot");
        public EcsComponentReference Type { get; }
        public RegisteredProjectionDefinition Definition { get; }
        public ApplicationMechanicObjectProjectionResolver Resolver { get; }
        public MechanicSnapshotObjectRequirement Requirement { get; set; }
        public ApplicationMechanicEvaluationRequest Request { get; set; }
        public MechanicProjection Snapshot { get; set; }
        public MechanicRequirements Requirements => new()
        {
            Roles = new() { ["subject"] = new(["state"], OptionalComponents: ["facet"]) },
            SnapshotObjects = new() { ["record"] = Requirement }
        };

        public Fixture()
        {
            db = database.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            applications.Register(new(Owner, "Snapshot", "", []));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var type = types.Define(new(Owner, "snapshot.state", "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}"));
            Type = new(type.QualifiedId, type.Version, type.SchemaHash);
            var definitions = new SqliteProjectionDefinitionRegistry(db, types, schemas, applications);
            Definition = definitions.Define(new(Owner, "snapshot.object",
                "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}",
                [new("state", "record", Type)], [], [new("state", "/value", "/value")],
                new([new("record", true)], [new("state", true)], [], [], [],
                    new(1, 1, 4096, 3), new(["player", "dm"], []), null), 1));
            Resolver = new(definitions, new ProjectionMaterializer(definitions, null!, null!, schemas), null!, null!);
            Requirement = new() { QualifiedId = Definition.QualifiedId, Version = 1,
                ContentFingerprint = Definition.ContentHash,
                RoleBindings = new() { ["record"] = new() { Role = "subject" } } };
            Snapshot = new() { StateSpaceId = "space", AuthorizedSourceRevision = new('C', 64),
                Roles = new() { ["subject"] = new("visible", "Visible", new Dictionary<string, string>
                    { ["state"] = "{\"value\":42}", ["facet"] = "{\"target\":{\"entityId\":\"hidden\"}}" }) },
                ComponentRevisions = new() { ["visible"] = new() { ["state"] = 7 } } };
            Request = new("space", Owner, "snapshot.read", new('A', 64),
                new(new Dictionary<string, EcsComponentReference> { ["state"] = Type }, new Dictionary<string, string>()),
                new Dictionary<string, string> { ["subject"] = "visible" }, "{}", 1, Audience: new("player"));
        }
        public Task<ProjectionResult> Resolve() => Resolver.ResolveSnapshotAsync(Request, Requirements, Snapshot);
        public void Dispose() { db.Dispose(); database.Dispose(); }
    }

    private sealed class SuppliedProjection(MechanicProjection projection) : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId,
            MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping,
            IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed,
            CancellationToken cancellationToken = default) => Task.FromResult(new ProjectionResult(projection, []));
    }
}

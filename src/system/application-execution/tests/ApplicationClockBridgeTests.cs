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
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationClockBridgeTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    [Fact]
    public async Task Installed_base_clock_advances_once_and_rejects_caller_derived_state()
    {
        await using var db = _fixture.CreateContext();
        var game = ApplicationIdentifier.Parse("game");
        var app = ApplicationIdentifier.Parse("clock-fixture");
        var applications = new SqliteApplicationRegistry(db);
        applications.Register(new(game, "Game", "Base owner.", []));
        var revision = applications.Register(new(app, "Clock fixture", "", [game]));
        var activationFingerprint = Hash("clock-fixture-activation");
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        stateSpaces.Create(new("clock-space", revision, activationFingerprint));
        var schemas = new BoundedJsonSchemaValidator();
        var types = new SqliteComponentTypeRegistry(db, schemas);
        var rootType = types.Define(new(game, "game.core.world.root",
            """{"type":"object","additionalProperties":false,"required":["status"],"properties":{"status":{"const":"active"}}}"""));
        var clockType = types.Define(new(game, "game.core.world.clock",
            """{"type":"object","additionalProperties":false,"required":["calendarId","currentMinute","revision"],"properties":{"calendarId":{"type":"string"},"currentMinute":{"type":"integer","minimum":0},"revision":{"type":"integer","minimum":0}}}"""));
        var entities = new SqliteEntityComponentStore(db, types, schemas);
        await entities.CreateEntityAsync("clock-space", "world", "World");
        await entities.AddComponentAsync(new("clock-space", "world", Reference(rootType),
            "{\"status\":\"active\"}", 0));
        await entities.AddComponentAsync(new("clock-space", "world", Reference(clockType),
            "{\"calendarId\":\"fixture\",\"currentMinute\":100,\"revision\":7}", 0));

        var content = JsonSerializer.Serialize(new
        {
            requirements = "{\"roles\":{\"world\":{\"components\":[\"clock-fixture.game.core.world.root\",\"clock-fixture.game.core.world.clock\"]}}}",
            source = "var k='clock-fixture.game.core.world.clock',c=JSON.parse(ctx.roles.world.components[k]);if(Object.keys(ctx.input).length!==1||!Number.isInteger(ctx.input.minutes)||ctx.input.minutes<1)throw new Error('invalid clock input');var n={calendarId:c.calendarId,currentMinute:c.currentMinute+ctx.input.minutes,revision:c.revision+1};return {effects:[{type:'component.set',entityId:ctx.roles.world.id,definitionId:k,data:JSON.stringify(n)}],data:n};"
        });
        var record = new CatalogRecordDefinition(app.Value, "mechanic", app.Value + ".mechanic.clock",
            "Clock", "Advance clock.", [], [], "mechanics", "active", 1, content, Hash(content),
            "source", "mechanics/clock.md");
        var manifest = CatalogNavigationManifest.Create(app, Hash("clock-fixture-catalog"), "catalog-lexical-v1",
            [new(app.Value, "Clock", "Clock fixture.")],
            [new(app.Value, "", "Clock", "Clock fixture.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Mechanics.", CatalogDescriptionStatus.Authored)],
            [record]);
        var catalogs = new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
        {
            [app] = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes("clock-bridge-test-cursor-key-32b")))
        });
        var activation = new StaticActivation(new(app, 1, revision.Revision, revision.Fingerprint,
            Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
            "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow));
        var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
        var operations = new OperationLog(db);
        var evaluator = new ApplicationMechanicEvaluator(catalogs,
            new ApplicationMechanicProjectionResolver(db, stateSpaces), new JintMechanicEngine());
        var runner = new ApplicationActionRunner(catalogs, activation, stateSpaces, types, entities, edges,
            new ApplicationMechanicProjectionMappingResolver(catalogs, stateSpaces, types, edges),
            evaluator, new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges), operations);
        ApplicationActionExecutionRequest Request(string input, string operationId) => new(
            "clock-space", app, record.QualifiedId, record.Version, record.ContentFingerprint,
            new Dictionary<string, string> { ["world"] = "world" }, input, 1,
            new(operationId, Hash(record.QualifiedId + "\n" + input)));

        var accepted = await runner.RunAsync(Request("{\"minutes\":60}",
            "10000000000000000000000000000001"));
        var replay = await runner.RunAsync(Request("{\"minutes\":60}",
            "10000000000000000000000000000001"));
        var invalid = await runner.RunAsync(Request(
            "{\"minutes\":1,\"currentMinute\":0}", "10000000000000000000000000000002"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, accepted.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var clock = await entities.GetComponentAsync("clock-space", "world", clockType.QualifiedId);
        Assert.NotNull(clock);
        Assert.Equal(2, clock.Revision);
        using var state = JsonDocument.Parse(clock.ValueJson);
        Assert.Equal("fixture", state.RootElement.GetProperty("calendarId").GetString());
        Assert.Equal(160, state.RootElement.GetProperty("currentMinute").GetInt32());
        Assert.Equal(8, state.RootElement.GetProperty("revision").GetInt32());
    }

    public void Dispose() => _fixture.Dispose();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static EcsComponentReference Reference(RegisteredComponentTypeVersion type) =>
        new(type.QualifiedId, type.Version, type.SchemaHash);

    private sealed class StaticActivation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == value.ApplicationId ? value : null;
    }
}

using DantesRoleplay.DataAccess;
using DantesRoleplay.Information;
using DantesRoleplay.MCPServer;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;

namespace DantesRoleplay.Tests;

public sealed class InformationTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Namespace_selector_reads_only_its_concrete_descendants()
    {
        await using var db = _fixture.CreateContext();
        var store = new InformationStore(db);
        Assert.Equal("created", (await store.WriteSourceAsync(new("source.rules", "game.worldname.rules", "Rules"))).Status);
        Assert.Equal("created", (await store.WriteSourceAsync(new("source.other", "game.other.rules", "Other rules"))).Status);
        await store.WriteRecordAsync(new("record.rules", "source.rules", "Rule", "Use the declared action."));
        await store.WriteRecordAsync(new("record.other", "source.other", "Other", "Do not expose this."));

        var records = await store.SearchAsync("game.worldname.*", "declared action", null, 12);

        var record = Assert.Single(records);
        Assert.Equal("record.rules", record.Id);
        Assert.True(InformationScopes.Contains("game.worldname.*", "game.worldname.rules"));
        Assert.False(InformationScopes.Contains("game.worldname.*", "game.other.*"));
    }

    [Fact]
    public async Task Contract_execution_requires_namespace_and_schema_before_the_executor()
    {
        await using var db = _fixture.CreateContext();
        var store = new InformationStore(db);
        var write = await store.WriteActionContractAsync(new(
            "action.world.move", "game.worldname.actions", "Move", "Move through the declared rule.", "test.executor",
            """{"type":"object","additionalProperties":false,"required":["intent"],"properties":{"intent":{"type":"string"}}}""", "[]"));
        Assert.Equal("created", write.Status);
        var executor = new RecordingExecutor();
        var coordinator = new InformationActionCoordinator(new DevelopmentInformationScopePolicy("game.worldname.*"), store, [executor]);

        var invalid = await coordinator.ExecuteAsync(new("game.worldname.*", "action.world.move", "{}"));
        var denied = await coordinator.ExecuteAsync(new("game.other.*", "action.world.move", "{\"intent\":\"move\"}"));
        var executed = await coordinator.ExecuteAsync(new("game.worldname.*", "action.world.move", "{\"intent\":\"move\"}"));

        Assert.Equal("INFORMATION_ACTION_INPUT_INVALID", invalid.ErrorCode);
        Assert.Equal("INFORMATION_SCOPE_DENIED", denied.ErrorCode);
        Assert.Equal("executed", executed.Status);
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public async Task Public_verbs_list_and_execute_only_the_declared_namespace_contract()
    {
        await using var db = _fixture.CreateContext();
        var store = new InformationStore(db);
        await store.WriteActionContractAsync(new(
            "action.world.move", "game.worldname.*", "Move", "Move through the declared rule.", "test.executor",
            """{"type":"object","additionalProperties":false,"required":["intent"],"properties":{"intent":{"type":"string"}}}""", "[]"));
        var executor = new RecordingExecutor();
        var coordinator = new InformationActionCoordinator(new DevelopmentInformationScopePolicy("game.worldname.*"), store, [executor]);
        var log = new OperationLog(db);

        var listed = await new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, journeys: null!, itineraries: null!, campaignResumes: null!, questSummaries: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, events: null!, log: log, notifications: null!,
            kind: "information-actions", scopeId: "game.worldname.*", informationActions: coordinator);
        var executed = await new CommitTool().CommitAsync(
            procedures: null!, world: null!, effects: null!, mechanics: null!, eventTypes: null!, subscriptions: null!, actions: null!, itineraries: null!, campaigns: null!, campaignBootstrapper: null!, campaignContinuity: null!, campaignSessions: null!, campaignSessionStarter: null!, quests: null!, questLifecycle: null!, log: log, notifications: null!,
            kind: "information-action", payload: """{"scopeId":"game.worldname.*","contractId":"action.world.move","input":"{\"intent\":\"move\"}"}""", informationActions: coordinator);

        Assert.True(listed.Ok);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<InformationActionContract>>(listed.Data));
        Assert.True(executed.Ok);
        Assert.Equal(1, executor.Calls);
    }

    private sealed class RecordingExecutor : IInformationActionExecutor
    {
        public string Id => "test.executor";
        public int Calls { get; private set; }
        public Task<InformationActionExecutionResult> ExecuteAsync(InformationActionContract contract, string inputJson, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new InformationActionExecutionResult("executed", new { contract.Id }));
        }
    }
}

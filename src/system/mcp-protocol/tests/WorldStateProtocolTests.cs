using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Operations;
using DantesRoleplay.Tests;

namespace DantesRoleplay.McpProtocol.Tests;

public sealed class WorldStateProtocolTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Generic_surface_advertises_the_closed_private_world_sync_kind()
    {
        var commit = Assert.Single(VerbSurface.CommitKinds,
            value => value.Name == "system.world-state.sync");
        Assert.True(commit.SupportsDryRun);
        Assert.Equal(["procedure.system.use"], commit.Contracts);
        Assert.Contains("rootEntityId", commit.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_is_resolved_before_payload_parsing_or_world_access()
    {
        await using var db = fixture.CreateContext();
        var service = new RecordingSynchronizer();
        var authorization = new RecordingAuthorizer(false);

        var result = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.world-state.sync", payload: "not-json", dryRun: true,
            privateOperator: authorization, worldStateSynchronization: service);

        Assert.False(result.Ok);
        Assert.False(service.Touched);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
    }

    [Fact]
    public async Task Exact_manifest_is_parsed_and_delegated_once_while_extra_fields_fail_closed()
    {
        await using var db = fixture.CreateContext();
        var service = new RecordingSynchronizer();
        var authorization = new RecordingAuthorizer(true);
        var payload = """
        {"requestToken":"0123456789abcdef0123456789abcdef","applicationId":"dnd2024","stateSpaceId":"dnd2024-main","rootEntityId":"world.thalorien","entities":[{"entityId":"location.thalorien.thalos","name":"Thalos","expectedRevision":0,"components":[{"qualifiedTypeId":"dnd2024.game.core.world.location","expectedRevision":0,"value":{"kind":"region","status":"active","summary":"The central continent.","visibility":"public"}}],"containment":{"containerEntityId":"world.thalorien","slot":"region","expectedRevision":0}}],"relationships":[]}
        """;

        var accepted = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.world-state.sync", payload: payload, intent: "Author Thalos.",
            proceduresUsed: ["procedure.system.use", "procedure.game.core.world.location"], dryRun: true,
            privateOperator: authorization, worldStateSynchronization: service);

        Assert.True(accepted.Ok);
        Assert.True(service.DryRun);
        Assert.Equal("world.thalorien", service.Request!.RootEntityId);
        Assert.Equal("location.thalorien.thalos", Assert.Single(service.Request.Entities).EntityId);
        Assert.Equal("{\"kind\":\"region\",\"status\":\"active\",\"summary\":\"The central continent.\",\"visibility\":\"public\"}",
            Assert.Single(Assert.Single(service.Request.Entities).Components).ValueJson);

        var updateWithoutContainment = """
        {"requestToken":"1123456789abcdef0123456789abcdef","applicationId":"dnd2024","stateSpaceId":"dnd2024-main","rootEntityId":"world.thalorien","entities":[{"entityId":"location.thalorien.thalos","name":"Thalos","expectedRevision":1,"components":[{"qualifiedTypeId":"dnd2024.game.core.world.location","expectedRevision":1,"value":{"kind":"region","status":"active","summary":"The central continent.","visibility":"public"}}]}],"relationships":[]}
        """;
        var update = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.world-state.sync", payload: updateWithoutContainment, dryRun: true,
            privateOperator: authorization, worldStateSynchronization: service);

        Assert.True(update.Ok);
        Assert.Null(Assert.Single(service.Request!.Entities).Containment);

        var invalid = payload[..^1] + ",\"effects\":[]}\n";
        var rejected = await new CommitTool().CommitAsync(
            world: null!, effects: null!, mechanics: null!, actions: null!, log: new OperationLog(db),
            kind: "system.world-state.sync", payload: invalid, dryRun: true,
            privateOperator: authorization, worldStateSynchronization: service);

        Assert.False(rejected.Ok);
        Assert.Equal(2, service.Calls);
    }

    private sealed class RecordingSynchronizer : IApplicationWorldAuthoringSynchronizer
    {
        public ApplicationWorldAuthoringRequest? Request { get; private set; }
        public bool DryRun { get; private set; }
        public int Calls { get; private set; }
        public bool Touched => Calls > 0;

        public Task<ApplicationWorldAuthoringResult> SynchronizeAsync(
            ApplicationWorldAuthoringRequest request,
            ApplicationWorldAuthoringContext context,
            bool dryRun,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            DryRun = dryRun;
            Calls++;
            return Task.FromResult(new ApplicationWorldAuthoringResult(
                true, dryRun, false, request.Entities.Count, 3,
                "0123456789abcdef0123456789abcdef", Receipts: []));
        }
    }

    private sealed class RecordingAuthorizer(bool allowed) : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorCapability? LastCapability { get; private set; }
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            LastCapability = capability;
            var principal = allowed
                ? PrivateOperatorPrincipal.Create("test", "operator")
                : TrustedPrincipalContext.Unauthenticated("TEST_DENIED");
            return new PrivateOperatorAuthorizationPolicy().Evaluate(new(principal, capability,
                PrivateOperatorAuthorizationPolicy.PrivateHostScope, "test-request"));
        }
    }
}

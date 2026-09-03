using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Operations;
using DantesRoleplay.Tests;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.McpProtocol.Tests;

public sealed class TriggerSchedulingProtocolTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    public void Dispose() => fixture.Dispose();

    [Fact]
    public void Generic_surface_advertises_one_query_and_one_commit_kind_without_adding_tools()
    {
        var query = Assert.Single(McpVerbCatalog.QueryKinds, value => value.Name == "system.trigger-scheduling");
        var commit = Assert.Single(McpVerbCatalog.CommitKinds, value => value.Name == "system.trigger-scheduling");
        Assert.True(commit.Descriptor.Operations.SupportsPreview);
        Assert.Contains("resource", query.Descriptor.Input.SchemaJson, StringComparison.Ordinal);
        Assert.Equal(["procedure.system.use"], commit.Descriptor.ProcedureIds);
    }

    [Fact]
    public async Task Query_uses_the_server_selected_read_capability_and_closed_resource()
    {
        await using var db = fixture.CreateContext();
        var service = new RecordingAdministration();
        var authorization = new RecordingAuthorizer(allowed: true);

        var result = await new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new OperationLog(db), notifications: null!,
            kind: "system.trigger-scheduling", applicationId: "quest", resource: "fires", limit: 12,
            privateOperator: authorization, triggerSchedulingAdministration: service);

        Assert.True(result.Ok);
        Assert.Equal(PrivateOperatorCapability.TriggerAdministrationRead, authorization.LastCapability);
        Assert.Equal("fires", service.Query!.Resource);
        Assert.Equal(12, service.Query.Limit);
    }

    [Fact]
    public async Task Commit_denies_before_parsing_and_allowed_dry_run_calls_only_preview()
    {
        await using var db = fixture.CreateContext();
        var deniedService = new RecordingAdministration();
        var denied = new RecordingAuthorizer(allowed: false);
        var deniedResult = await new CommitMcpTool().CommitAsync(
            log: new OperationLog(db),
            kind: "system.trigger-scheduling", payload: "not-json", intent: "test", proceduresUsed: [],
            dryRun: true, privateOperator: denied, triggerSchedulingAdministration: deniedService);

        Assert.False(deniedResult.Ok);
        Assert.False(deniedService.Touched);
        Assert.Equal(PrivateOperatorCapability.TriggerAdministrationWrite, denied.LastCapability);

        var service = new RecordingAdministration();
        var allowed = new RecordingAuthorizer(allowed: true);
        var payload = """
        {"requestToken":"0123456789abcdef0123456789abcdef","operation":"phone.revoke","applicationId":"quest","value":{"deviceId":"phone-device.0123456789abcdef0123456789abcdef"}}
        """;
        var preview = await new CommitMcpTool().CommitAsync(
            log: new OperationLog(db),
            kind: "system.trigger-scheduling", payload: payload, intent: "test",
            proceduresUsed: ["procedure.system.use"], dryRun: true, privateOperator: allowed,
            triggerSchedulingAdministration: service);

        Assert.True(preview.Ok);
        Assert.NotNull(service.Previewed);
        Assert.Null(service.Committed);
        Assert.Equal(PrivateOperatorCapability.TriggerAdministrationWrite, allowed.LastCapability);
    }

    private sealed class RecordingAdministration : ITriggerSchedulingAdministrationService
    {
        public bool Touched => Query is not null || Previewed is not null || Committed is not null;
        public TriggerSchedulingAdministrationQuery? Query { get; private set; }
        public TriggerSchedulingAdministrationCommand? Previewed { get; private set; }
        public TriggerSchedulingAdministrationCommand? Committed { get; private set; }

        public Task<TriggerSchedulingAdministrationView> QueryAsync(TriggerSchedulingAdministrationQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(new TriggerSchedulingAdministrationView(query.Resource,
                [], [], [], [], [], [], [], [], [], [], null));
        }
        public Task<TriggerSchedulingAdministrationResult> PreviewAsync(
            TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
            CancellationToken cancellationToken = default)
        {
            Previewed = command;
            return Task.FromResult(new TriggerSchedulingAdministrationResult(command.Operation,
                command.ApplicationId, "would-revoke", "preview.operation",
                JsonSerializer.SerializeToElement(new { deviceId = "safe" })));
        }
        public Task<TriggerSchedulingAdministrationResult> CommitAsync(
            TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
            CancellationToken cancellationToken = default)
        {
            Committed = command;
            return Task.FromResult(new TriggerSchedulingAdministrationResult(command.Operation,
                command.ApplicationId, "revoked", command.RequestToken,
                JsonSerializer.SerializeToElement(new { deviceId = "safe" })));
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

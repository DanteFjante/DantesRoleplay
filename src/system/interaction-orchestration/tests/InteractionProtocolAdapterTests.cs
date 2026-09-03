using DantesRoleplay.Applications;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Operations;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionProtocolAdapterTests
{
    private const string Intent = "{\"idempotencyKey\":\"plan.1\",\"intentText\":\"Inspect the fixture\"}";

    [Fact]
    public async Task Public_plan_requires_the_confirmed_resolve_or_submit_mode()
    {
        var gateway = new Gateway();
        var resolve = await QueryAsync(gateway,
            "{\"operation\":\"resolve\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":" + Intent + "}");
        var submit = await QueryAsync(gateway,
            "{\"operation\":\"submit\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":" + Intent + ",\"proposal\":{}}");

        Assert.True(resolve.Ok);
        Assert.True(submit.Ok);
        Assert.Equal(2, gateway.Calls);
        Assert.Null(gateway.SubmittedProposals[0]);
        Assert.Equal("{}", gateway.SubmittedProposals[1]);
    }

    [Theory]
    [InlineData("{\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":{}}")]
    [InlineData("{\"operation\":\"direct\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":{}}")]
    [InlineData("{\"operation\":\"resolve\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":{},\"proposal\":{}}")]
    [InlineData("{\"operation\":\"submit\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":{}}")]
    [InlineData("{\"operation\":\"resolve\",\"stateSpaceId\":\"state.1\",\"sessionContextId\":\"session.1\",\"intent\":{},\"conversationId\":\"forged\"}")]
    public async Task Public_plan_rejects_conflicting_or_host_owned_fields(string request)
    {
        var gateway = new Gateway();

        var result = await QueryAsync(gateway, request);

        Assert.False(result.Ok);
        Assert.Equal("INTERACTION_REQUEST_INVALID", result.Error?.Code);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Public_execute_keeps_learning_explicit_and_conditionally_requires_the_original_intent()
    {
        var gateway = new Gateway();
        var basePayload = "{\"applicationId\":\"fixture-app\",\"stateSpaceId\":\"state.1\","
            + "\"resolutionReceiptId\":\"interaction-receipt." + new string('a', 32) + "\","
            + "\"proposalFingerprint\":\"" + new string('A', 64) + "\",\"idempotencyKey\":\"execute.1\","
            + "\"proposal\":{\"command\":\"propose\",\"steps\":[]},\"learn\":false}";
        var off = await CommitAsync(gateway, basePayload);
        var invalid = await CommitAsync(gateway, basePayload.Replace("\"learn\":false", "\"learn\":true"));
        var on = await CommitAsync(gateway,
            basePayload.Replace("\"learn\":false", "\"learn\":true,\"learningIntent\":" + Intent));

        Assert.True(off.Ok);
        Assert.False(invalid.Ok);
        Assert.Equal("LEARNING_INTENT_REQUIRED", invalid.Error?.Code);
        Assert.True(on.Ok);
        Assert.Equal(2, gateway.ExecutionRequests.Count);
        Assert.Equal("safe", Assert.Single(Assert.IsType<InteractionExecutionOutcome>(off.Data)
            .QueryResults!).Output!.Value.GetProperty("value").GetString());
        Assert.False(System.Text.Json.JsonDocument.Parse(gateway.ExecutionRequests[0]).RootElement.GetProperty("learn").GetBoolean());
        Assert.Equal("Inspect the fixture", System.Text.Json.JsonDocument.Parse(gateway.ExecutionRequests[1])
            .RootElement.GetProperty("learningIntent").GetProperty("intentText").GetString());
    }

    [Fact]
    public async Task Exact_application_action_is_one_authorized_idempotent_call_with_structured_follow_up()
    {
        var actions = new Actions();
        var payload = "{\"idempotencyKey\":\"action.1\",\"applicationId\":\"fixture-app\"," +
            "\"stateSpaceId\":\"state.1\",\"qualifiedMechanicId\":\"fixture-app.mechanic.exact\"," +
            "\"mechanicVersion\":7,\"contentFingerprint\":\"" + new string('A', 64) + "\"," +
            "\"roleEntityIds\":{\"subject\":\"entity.1\"},\"input\":{\"value\":2}}";

        var result = await new CommitMcpTool().CommitAsync(
            log: new Log(),
            kind: "application.action.execute", payload: payload, intent: "Execute exact action.",
            proceduresUsed: ["procedure.system.use"], privateOperator: new Authorizer(),
            applicationActions: actions);

        Assert.True(result.Ok, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.NotNull(actions.Request);
        Assert.Equal(7, actions.Request!.MechanicVersion);
        Assert.Equal("fixture-app.mechanic.exact", actions.Request.QualifiedMechanicId);
        Assert.Equal("{\"value\":2}", actions.Request.InputJson);
        Assert.Equal(32, actions.Request.ExecutionIdentity.OperationId.Length);
        using var data = System.Text.Json.JsonSerializer.SerializeToDocument(result.Data,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("The fixture changes.", data.RootElement.GetProperty("narration").GetString());
        Assert.Equal("entity.1", data.RootElement.GetProperty("affectedEntityIds")[0].GetString());
        Assert.Equal("application.action.execute.operation", data.RootElement.GetProperty("receipt")
            .GetProperty("operationId").GetString());
        Assert.Equal("mcp.query.entities", data.RootElement.GetProperty("nextActions")[0]
            .GetProperty("capabilityId").GetString());
    }

    [Fact]
    public async Task Exact_application_action_authorizes_before_parsing()
    {
        var actions = new Actions();
        var authorization = new Authorizer(false);

        var result = await new CommitMcpTool().CommitAsync(
            log: new Log(),
            kind: "application.action.execute", payload: "not-json",
            privateOperator: authorization, applicationActions: actions);

        Assert.False(result.Ok);
        Assert.Equal("DENIED", result.Error?.Code);
        Assert.Equal(PrivateOperatorCapability.Modify, authorization.LastCapability);
        Assert.Null(actions.Request);
    }

    private static Task<ToolEnvelope> QueryAsync(Gateway gateway, string request) =>
        new QueryMcpTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new Log(), notifications: null!,
            kind: "system.interaction-plan", applicationId: "fixture-app", request: request,
            privateOperator: new Authorizer(), interactionGateway: gateway);

    private static Task<ToolEnvelope> CommitAsync(Gateway gateway, string payload) =>
        new CommitMcpTool().CommitAsync(log: new Log(), kind: "system.interaction-execute",
            payload: payload, intent: "fixture",
            proceduresUsed: ["procedure.system.use"], privateOperator: new Authorizer(),
            interactionGateway: gateway);

    private sealed class Gateway : IInteractionGateway
    {
        public int Calls { get; private set; }
        public List<string?> SubmittedProposals { get; } = [];
        public List<string> ExecutionRequests { get; } = [];

        public Task<InteractionPlanGatewayResult> PlanAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string stateSpaceId,
            string sessionContextId, string intentJson, string? submittedProposalJson = null,
            string? conversationId = null, InteractionAiRole role = InteractionAiRole.Outer,
            string? parentDelegationId = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            SubmittedProposals.Add(submittedProposalJson);
            return Task.FromResult(new InteractionPlanGatewayResult(
                InteractionResolutionStatus.Unknown, "FIXTURE", "Fixture result.", [], null, null,
                InteractionReceiptWriteResult.Conflict(), new string('A', 64)));
        }

        public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(
            ApplicationIdentifier applicationId, string? query, string? qualifiedId, int limit = 10,
            string? namespaceId = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InteractionReceiptProjection?> GetReceiptAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string stateSpaceId,
            string receiptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InteractionExecutionOutcome> ExecuteAsync(
            TrustedPrincipalContext principal, ApplicationIdentifier applicationId, string stateSpaceId,
            string executionRequestJson, CancellationToken cancellationToken = default)
        {
            ExecutionRequests.Add(executionRequestJson);
            var receipt = new InteractionReceiptProjection("interaction-receipt." + new string('b', 32),
                "execution", principal.PrincipalId, applicationId, stateSpaceId, "execute.1",
                new string('A', 64), "succeeded", "INTERACTION_EXECUTION_SUCCEEDED", new string('A', 64),
                "Completed.", [], DateTime.UtcNow);
            using var output = System.Text.Json.JsonDocument.Parse("{\"value\":\"safe\"}");
            var query = new InteractionQueryResultProjection("query.1", "fixture-app.query.safe",
                new string('B', 64), new string('C', 64), new string('D', 64), output.RootElement.Clone());
            return Task.FromResult(new InteractionExecutionOutcome(InteractionExecutionReceiptDisposition.Succeeded,
                "INTERACTION_EXECUTION_SUCCEEDED", "Completed.", [],
                InteractionReceiptWriteResult.Appended(receipt), new string('A', 64), QueryResults: [query]));
        }
    }

    private sealed class Authorizer(bool allowed = true) : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorCapability? LastCapability { get; private set; }

        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability)
        {
            LastCapability = capability;
            return new(allowed, allowed ? "ALLOWED" : "DENIED", allowed ? "" : "Authenticate.",
                new("principal." + new string('a', 64), "fixture", "modify",
                    "system.private-host", "fixture", allowed, allowed ? "ALLOWED" : "DENIED"));
        }
    }

    private sealed class Actions : IApplicationActionRunner
    {
        public ApplicationActionExecutionRequest? Request { get; private set; }

        public Task<ApplicationActionExecutionResult> RunAsync(
            ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ApplicationActionExecutionResult(
                ApplicationActionExecutionDisposition.Succeeded,
                "application.action.execute.operation",
                request.QualifiedMechanicId,
                request.ContentFingerprint,
                request.Seed,
                "The fixture changes.",
                1,
                [])
            {
                MechanicVersion = request.MechanicVersion,
                AffectedEntityIds = ["entity.1"],
                EffectReceipts = [new(0, ApplicationEcsEffectType.ComponentSet,
                    "entity.1", "fixture.component", 2)]
            });
        }
    }

    private sealed class Log : IOperationLog
    {
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Operation?>(null);

        public Task<Operation> RecordAsync(
            string tool, string summary, bool success, string intent = "", string subject = "",
            IEnumerable<string>? proceduresCited = null, string error = "", bool consumesReadEvidence = false,
            CancellationToken cancellationToken = default, string mechanicId = "", int? mechanicVersion = null,
            long? seed = null, string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            Task.FromResult(new Operation
            {
                Id = string.IsNullOrEmpty(id) ? Operation.NewId() : id,
                Timestamp = DateTime.UtcNow,
                Tool = tool,
                Summary = summary,
                Success = success,
                Subject = subject,
                Error = error
            });

        public Task<IReadOnlyList<Operation>> RecentAsync(
            int limit = 20, bool failuresOnly = false, string? tool = null, string? subject = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Operation>>([]);

        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }
}

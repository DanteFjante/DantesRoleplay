using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer.Tools;
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
        Assert.False(System.Text.Json.JsonDocument.Parse(gateway.ExecutionRequests[0]).RootElement.GetProperty("learn").GetBoolean());
        Assert.Equal("Inspect the fixture", System.Text.Json.JsonDocument.Parse(gateway.ExecutionRequests[1])
            .RootElement.GetProperty("learningIntent").GetProperty("intentText").GetString());
    }

    private static Task<ToolEnvelope> QueryAsync(Gateway gateway, string request) =>
        new QueryTool().QueryAsync(
            procedures: null!, world: null!, graphs: null!, mechanics: null!, eventTypes: null!,
            subscriptions: null!, events: null!, log: new Log(), notifications: null!,
            kind: "system.interaction-plan", applicationId: "fixture-app", request: request,
            privateOperator: new Authorizer(), interactionGateway: gateway);

    private static Task<ToolEnvelope> CommitAsync(Gateway gateway, string payload) =>
        new CommitTool().CommitAsync(world: null!, effects: null!, mechanics: null!, actions: null!,
            log: new Log(), kind: "system.interaction-execute", payload: payload, intent: "fixture",
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
            return Task.FromResult(new InteractionExecutionOutcome(InteractionExecutionReceiptDisposition.Succeeded,
                "INTERACTION_EXECUTION_SUCCEEDED", "Completed.", [],
                InteractionReceiptWriteResult.Appended(receipt), new string('A', 64)));
        }
    }

    private sealed class Authorizer : IPrivateOperatorRequestAuthorizer
    {
        public PrivateOperatorAuthorizationDecision Authorize(PrivateOperatorCapability capability) =>
            new(true, "ALLOWED", "", new("principal." + new string('a', 64), "fixture", "read",
                "system.private-host", "fixture", true, "ALLOWED"));
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

using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Interactions;

namespace DantesRoleplay.Tests;

public sealed class ApplicationConversationTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("fixture-chat");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("local-loopback", "fixture");

    [Fact]
    public async Task Delegation_retains_an_inert_proposal_until_distinct_execute_and_narrates_only_safe_result()
    {
        var gateway = new Gateway();
        var narrator = new Narrator();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            new Outer(new(true, InteractionOuterDecision.Delegate, "actor attacks target", "OUTER_TURN_COMPLETED")), narrator);
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var planned = await service.TurnAsync(Principal, Application, conversation.Id,
            new("I attack."), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", planned!.Status);
        Assert.NotNull(planned.PendingPlan);
        Assert.Equal(InteractionAiRole.Inner, gateway.PlanningRole);
        Assert.StartsWith("delegation.", gateway.ParentDelegationId, StringComparison.Ordinal);
        Assert.Equal(0, gateway.ExecutionCalls);

        var executed = await service.ExecuteAsync(Principal, Application, conversation.Id,
            new("execute.fixture"), CancellationToken.None);

        Assert.Equal("ready", executed!.Status);
        Assert.Null(executed.PendingPlan);
        Assert.Equal(1, gateway.ExecutionCalls);
        using var submitted = JsonDocument.Parse(gateway.ExecutionJson!);
        Assert.Equal("object", submitted.RootElement.GetProperty("proposal").GetProperty("steps")[0]
            .GetProperty("input").ValueKind.ToString().ToLowerInvariant());
        Assert.False(submitted.RootElement.GetProperty("learn").GetBoolean());
        Assert.False(submitted.RootElement.TryGetProperty("learningIntent", out _));
        Assert.Equal(["Mechanic-safe result."], narrator.Observed!.MechanicNarration);
        Assert.Equal("Narrated safely.", executed.Messages[^1].Text);
    }

    [Fact]
    public async Task Learning_opt_in_uses_the_server_retained_exact_pending_intent()
    {
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            new Outer(new(true, InteractionOuterDecision.Delegate, "actor attacks target", "OUTER_TURN_COMPLETED")),
            new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));
        await service.TurnAsync(Principal, Application, conversation.Id, new("I attack."), CancellationToken.None);

        await service.ExecuteAsync(Principal, Application, conversation.Id,
            new("execute.learn", Learn: true), CancellationToken.None);

        using var submitted = JsonDocument.Parse(gateway.ExecutionJson!);
        Assert.True(submitted.RootElement.GetProperty("learn").GetBoolean());
        var retained = submitted.RootElement.GetProperty("learningIntent");
        Assert.Equal("actor attacks target", retained.GetProperty("intentText").GetString());
        Assert.StartsWith("application-conversation.", retained.GetProperty("idempotencyKey").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_outer_provider_never_plans_or_executes()
    {
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            new Outer(InteractionOuterTurnResult.Unavailable("OUTER_MODEL_UNAVAILABLE")), new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Do something."), CancellationToken.None);

        Assert.Equal("unavailable", result!.Status);
        Assert.Equal(0, gateway.PlanCalls);
        Assert.Equal(0, gateway.ExecutionCalls);
        Assert.Equal("OUTER_MODEL_UNAVAILABLE", result.Messages[^1].Code);
    }

    private sealed class Spaces : IStateSpaceRegistry
    {
        private readonly StateSpaceView value = new("chat-space",
            new(Application, 1, new string('A', 64), []), new string('B', 64), 1,
            DateTime.UtcNow, DateTime.UtcNow);
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == value.StateSpaceId ? value : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) => new([value], null);
    }

    private sealed class Outer(InteractionOuterTurnResult result) : IInteractionOuterTurnProvider
    {
        public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class Narrator : IInteractionNarrationProvider
    {
        public InteractionNarrationRequest? Observed { get; private set; }
        public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request, CancellationToken cancellationToken = default)
        {
            Observed = request;
            return Task.FromResult(new InteractionNarrationResult(true, "Narrated safely.", "NARRATION_COMPLETED"));
        }
    }

    private sealed class Gateway : IInteractionGateway
    {
        public int PlanCalls { get; private set; }
        public int ExecutionCalls { get; private set; }
        public InteractionAiRole PlanningRole { get; private set; }
        public string? ParentDelegationId { get; private set; }
        public string? ExecutionJson { get; private set; }

        public Task<InteractionPlanGatewayResult> PlanAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string sessionContextId,
            string intentJson, string? submittedProposalJson = null, string? conversationId = null,
            InteractionAiRole role = InteractionAiRole.Outer, string? parentDelegationId = null,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            PlanningRole = role;
            ParentDelegationId = parentDelegationId;
            using var input = JsonDocument.Parse("{\"weapon\":\"sword\"}");
            var proposal = new InteractionProposalProjection("propose",
                [new("step.1", "action", "fixture-chat.attack", 1, new string('C', 64), [],
                    new Dictionary<string, string> { ["actor"] = "actor", ["target"] = "target" },
                    input.RootElement.Clone())]);
            var receipt = Receipt("interaction-receipt." + new string('a', 32), "resolution");
            return Task.FromResult(new InteractionPlanGatewayResult(InteractionResolutionStatus.Resolved,
                "INTERACTION_RESOLVED", "A proposal is ready for confirmation.", [], new string('D', 64),
                proposal, InteractionReceiptWriteResult.Appended(receipt), new string('E', 64)));
        }

        public Task<InteractionExecutionOutcome> ExecuteAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string executionRequestJson,
            CancellationToken cancellationToken = default)
        {
            ExecutionCalls++;
            ExecutionJson = executionRequestJson;
            var receipt = Receipt("interaction-receipt." + new string('b', 32), "execution");
            var action = new DantesRoleplay.ApplicationExecution.ApplicationActionExecutionResult(
                DantesRoleplay.ApplicationExecution.ApplicationActionExecutionDisposition.Succeeded,
                "0123456789abcdef0123456789abcdef", "fixture-chat.attack", new string('C', 64), 1,
                "Mechanic-safe result.", 1, []);
            return Task.FromResult(new InteractionExecutionOutcome(InteractionExecutionReceiptDisposition.Succeeded,
                "INTERACTION_EXECUTION_SUCCEEDED", "The verified interaction completed.", [action],
                InteractionReceiptWriteResult.Appended(receipt), new string('F', 64)));
        }

        public Task<InteractionFeatureSearchResult> SearchFeaturesAsync(ApplicationIdentifier applicationId,
            string? query, string? qualifiedId, int limit = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionReceiptProjection?> GetReceiptAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string receiptId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static InteractionReceiptProjection Receipt(string id, string kind) => new(id, kind,
            Principal.PrincipalId, Application, "chat-space", "key", new string('A', 64), "resolved", "OK",
            new string('D', 64), "safe", [], DateTime.UtcNow);
    }
}

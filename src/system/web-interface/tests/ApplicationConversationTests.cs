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
    public async Task Outer_turn_receives_exact_application_binding_and_bounded_visible_transcript()
    {
        var outer = new Outer(new(true, InteractionOuterDecision.Respond, "Hello.", "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), new Gateway(), outer,
            new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        await service.TurnAsync(Principal, Application, conversation.Id,
            new("What worlds are stored here?"), CancellationToken.None);

        var request = Assert.Single(outer.Requests);
        Assert.Equal("fixture-chat", request.BoundApplication!.ApplicationId);
        Assert.Equal("chat-space", request.BoundApplication.StateSpaceId);
        Assert.Equal(1, request.BoundApplication.ApplicationRevision);
        Assert.Equal(new string('A', 64), request.BoundApplication.ApplicationFingerprint);
        Assert.Equal(new string('B', 64), request.BoundApplication.ManifestFingerprint);
        var message = Assert.Single(request.VisibleTranscript!);
        Assert.Equal("player", message.Role);
        Assert.Equal("What worlds are stored here?", message.Text);
    }

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
        Assert.Equal(JsonValueKind.Array, submitted.RootElement.GetProperty("proposal")
            .GetProperty("steps")[0].GetProperty("resultBindings").ValueKind);
        Assert.Equal(["Mechanic-safe result."], narrator.Observed!.MechanicNarration);
        Assert.Equal("safe fact", Assert.Single(narrator.Observed.QueryResults!).Output!.Value
            .GetProperty("fact").GetString());
        Assert.Equal("Narrated safely.", executed.Messages[^1].Text);
    }

    [Fact]
    public async Task Single_line_delegation_preserves_one_exact_task_without_model_resplitting()
    {
        var outer = new Outer(new(true, InteractionOuterDecision.Delegate,
            "Orban attacks the caravan driver.", "OUTER_TURN_COMPLETED"));
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway, outer, new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var planned = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Orban attacks the caravan driver."), CancellationToken.None);

        Assert.Equal(0, outer.AgendaCalls);
        var task = Assert.Single(planned!.ActiveAgenda!.Tasks);
        Assert.Equal("Orban attacks the caravan driver.", task.IntentText);
        Assert.Equal("Orban attacks the caravan driver.", Assert.Single(task.Batches).IntentText);
        using var intent = JsonDocument.Parse(Assert.Single(gateway.IntentJsons));
        Assert.Equal("Orban attacks the caravan driver.", intent.RootElement.GetProperty("intentText").GetString());
    }

    [Fact]
    public async Task Bounded_agenda_advances_one_fresh_confirmed_batch_at_a_time()
    {
        var agenda = InteractionTaskAgenda.Parse("""
            {"tasks":[{"intentText":"Prepare","dependsOn":[],"batches":[{"intentText":"Inspect current state"},{"intentText":"Apply preparation"}]},{"intentText":"Finish","dependsOn":[1],"batches":[{"intentText":"Perform final action"}]}]}
            """);
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            Outer.WithAgenda(agenda), new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var first = await service.TurnAsync(Principal, Application, conversation.Id,
            new("1. Prepare. 2. Finish."), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", first!.Status);
        Assert.Equal(1, gateway.PlanCalls);
        Assert.Equal(0, gateway.ExecutionCalls);
        Assert.Equal("awaiting-confirmation", first.ActiveAgenda!.Status);
        Assert.Equal(1, first.ActiveAgenda.CurrentTask);
        Assert.Equal(1, first.ActiveAgenda.CurrentBatch);

        var second = await service.ExecuteAsync(Principal, Application, conversation.Id, new(), CancellationToken.None);
        Assert.Equal(1, gateway.ExecutionCalls);
        Assert.Equal(2, gateway.PlanCalls);
        Assert.Equal("completed", second!.ActiveAgenda!.Tasks[0].Batches[0].Status);
        Assert.Equal("awaiting-confirmation", second.ActiveAgenda.Tasks[0].Batches[1].Status);

        var third = await service.ExecuteAsync(Principal, Application, conversation.Id, new(), CancellationToken.None);
        Assert.Equal(2, gateway.ExecutionCalls);
        Assert.Equal(3, gateway.PlanCalls);
        Assert.Equal("completed", third!.ActiveAgenda!.Tasks[0].Status);
        Assert.Equal("awaiting-confirmation", third.ActiveAgenda.Tasks[1].Batches[0].Status);

        var completed = await service.ExecuteAsync(Principal, Application, conversation.Id, new(), CancellationToken.None);
        Assert.Equal("ready", completed!.Status);
        Assert.Equal("completed", completed.ActiveAgenda!.Status);
        Assert.Equal(3, gateway.ExecutionCalls);
        Assert.Equal(3, gateway.PlanCalls);
        Assert.Null(completed.PendingPlan);

        var intents = gateway.IntentJsons.Select(value => JsonDocument.Parse(value)).ToArray();
        try
        {
            Assert.Equal(["Inspect current state", "Apply preparation", "Perform final action"],
                intents.Select(value => value.RootElement.GetProperty("intentText").GetString()));
            Assert.Equal(3, intents.Select(value => value.RootElement.GetProperty("idempotencyKey").GetString())
                .Distinct(StringComparer.Ordinal).Count());
        }
        finally { foreach (var value in intents) value.Dispose(); }
    }

    [Fact]
    public async Task Failed_batch_pauses_the_agenda_blocks_dependants_and_does_not_run_independent_tasks()
    {
        var agenda = InteractionTaskAgenda.Parse("""
            {"tasks":[{"intentText":"First","dependsOn":[],"batches":[{"intentText":"Fail first"}]},{"intentText":"Dependent","dependsOn":[1],"batches":[{"intentText":"Never start"}]},{"intentText":"Independent","dependsOn":[],"batches":[{"intentText":"Also do not start"}]}]}
            """);
        var gateway = new Gateway { ExecutionSuccessful = false };
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            Outer.WithAgenda(agenda), new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));
        await service.TurnAsync(Principal, Application, conversation.Id, new("1. Run first. 2. Run the rest."), CancellationToken.None);

        var failed = await service.ExecuteAsync(Principal, Application, conversation.Id, new(), CancellationToken.None);

        Assert.Equal("needs-attention", failed!.Status);
        Assert.Equal("needs-attention", failed.ActiveAgenda!.Status);
        Assert.Equal("failed", failed.ActiveAgenda.Tasks[0].Batches[0].Status);
        Assert.Equal("blocked", failed.ActiveAgenda.Tasks[1].Status);
        Assert.Equal("pending", failed.ActiveAgenda.Tasks[2].Status);
        Assert.Equal(1, gateway.PlanCalls);
        Assert.Equal(1, gateway.ExecutionCalls);
        Assert.Null(failed.PendingPlan);

        var active = await Assert.ThrowsAsync<InteractionContractException>(() => service.TurnAsync(
            Principal, Application, conversation.Id, new("Do something else."), CancellationToken.None));
        Assert.Equal("TASK_AGENDA_ACTIVE", active.Code);
    }

    [Fact]
    public async Task Active_agenda_requires_explicit_replacement_and_replacement_changes_only_process_memory()
    {
        var agenda = InteractionTaskAgenda.Parse("""
            {"tasks":[{"intentText":"First","dependsOn":[],"batches":[{"intentText":"Pending action"}]}]}
            """);
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            Outer.WithAgenda(agenda), new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));
        var first = await service.TurnAsync(Principal, Application, conversation.Id,
            new("1. First request. 2. Second request."), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InteractionContractException>(() => service.TurnAsync(
            Principal, Application, conversation.Id, new("Replacement."), CancellationToken.None));
        Assert.Equal("INTERACTION_CONFIRMATION_REQUIRED", error.Code);

        var replaced = await service.TurnAsync(Principal, Application, conversation.Id,
            new("1. Replacement one. 2. Replacement two.", ReplaceActiveAgenda: true), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", replaced!.Status);
        Assert.NotEqual(first!.ActiveAgenda!.Id, replaced.ActiveAgenda!.Id);
        Assert.Equal(2, gateway.PlanCalls);
        Assert.Equal(0, gateway.ExecutionCalls);
    }

    [Fact]
    public async Task Idempotency_conflict_keeps_the_exact_batch_awaiting_confirmation()
    {
        var agenda = InteractionTaskAgenda.Parse("""
            {"tasks":[{"intentText":"First","dependsOn":[],"batches":[{"intentText":"First batch"},{"intentText":"Second batch"}]}]}
            """);
        var gateway = new Gateway { ExecutionConflict = true };
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            Outer.WithAgenda(agenda), new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));
        await service.TurnAsync(Principal, Application, conversation.Id, new("1. Run first. 2. Run second."), CancellationToken.None);

        var conflict = await service.ExecuteAsync(Principal, Application, conversation.Id,
            new("same-key"), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", conflict!.Status);
        Assert.NotNull(conflict.PendingPlan);
        Assert.Equal("awaiting-confirmation", conflict.ActiveAgenda!.Tasks[0].Batches[0].Status);
        Assert.Equal("pending", conflict.ActiveAgenda.Tasks[0].Batches[1].Status);
        Assert.Equal(1, gateway.PlanCalls);

        gateway.ExecutionConflict = false;
        var advanced = await service.ExecuteAsync(Principal, Application, conversation.Id,
            new("first-batch-key"), CancellationToken.None);
        Assert.Equal("completed", advanced!.ActiveAgenda!.Tasks[0].Batches[0].Status);
        Assert.Equal("awaiting-confirmation", advanced.ActiveAgenda.Tasks[0].Batches[1].Status);
        Assert.Equal(2, gateway.PlanCalls);
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
    public async Task Outer_fallback_learning_retains_the_exact_outer_attempt_and_not_the_failed_inner_intent()
    {
        var gateway = new Gateway(NonResolution(InteractionResolutionStatus.Unknown, "INNER_UNKNOWN", 'f'));
        var outer = Outer.WithKind(InteractionOuterProviderKind.Local,
            new(true, InteractionOuterDecision.Delegate, "failed inner intent", "OUTER_TURN_COMPLETED"),
            new(true, InteractionOuterDecision.DirectPlan, "verified outer fallback intent", "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), gateway, outer, new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var planned = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Perform the request."), CancellationToken.None);
        Assert.Equal("awaiting-confirmation", planned!.Status);

        await service.ExecuteAsync(Principal, Application, conversation.Id,
            new("execute.outer-learn", Learn: true), CancellationToken.None);

        using var submitted = JsonDocument.Parse(gateway.ExecutionJson!);
        Assert.True(submitted.RootElement.GetProperty("learn").GetBoolean());
        var retained = submitted.RootElement.GetProperty("learningIntent");
        Assert.Equal("verified outer fallback intent", retained.GetProperty("intentText").GetString());
        Assert.DoesNotContain("failed inner intent", retained.GetRawText(), StringComparison.Ordinal);
        Assert.EndsWith(".outer", retained.GetProperty("idempotencyKey").GetString(), StringComparison.Ordinal);
        Assert.Equal([InteractionAiRole.Inner, InteractionAiRole.Outer], gateway.PlanningRoles);
        Assert.Equal(1, gateway.ExecutionCalls);
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

    [Fact]
    public async Task Initial_direct_plan_still_attempts_inner_first()
    {
        var gateway = new Gateway();
        var service = new ApplicationConversationService(new(), new Spaces(), gateway,
            new Outer(new(true, InteractionOuterDecision.DirectPlan, "outer believes it can act", "OUTER_TURN_COMPLETED")),
            new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Perform it."), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", result!.Status);
        Assert.Equal(1, gateway.PlanCalls);
        Assert.Equal([InteractionAiRole.Inner], gateway.PlanningRoles);
        using var intent = JsonDocument.Parse(gateway.IntentJsons.Single());
        Assert.Equal("local", intent.RootElement.GetProperty("plannerPreference").GetString());
        Assert.EndsWith(".inner", intent.RootElement.GetProperty("idempotencyKey").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InteractionResolutionStatus.Unknown, "INNER_UNKNOWN", InteractionOuterProviderKind.Remote, "remote")]
    [InlineData(InteractionResolutionStatus.Unsupported, "INNER_UNSUPPORTED", InteractionOuterProviderKind.Remote, "remote")]
    [InlineData(InteractionResolutionStatus.Unavailable, "LOCAL_MODEL_DISABLED", InteractionOuterProviderKind.Remote, "remote")]
    [InlineData(InteractionResolutionStatus.Unknown, "INNER_UNKNOWN", InteractionOuterProviderKind.Local, "local")]
    public async Task Eligible_inner_non_resolution_is_returned_once_then_outer_plans(
        InteractionResolutionStatus status,
        string code,
        InteractionOuterProviderKind outerKind,
        string outerPreference)
    {
        var inner = NonResolution(status, code, 'c');
        var gateway = new Gateway(inner);
        var outer = Outer.WithKind(outerKind,
            new(true, InteractionOuterDecision.Delegate, "inner intent", "OUTER_TURN_COMPLETED"),
            new(true, InteractionOuterDecision.DirectPlan, "outer fallback intent", "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), gateway, outer, new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Do the action."), CancellationToken.None);

        Assert.Equal("awaiting-confirmation", result!.Status);
        Assert.Equal(2, outer.Requests.Count);
        Assert.Equal(code, outer.Requests[1].PriorSafeResultCode);
        Assert.Equal(InteractionResolutionStatusNames.Get(status), outer.Requests[1].PriorSafeResolution!.Status);
        Assert.Equal(inner.Receipt.Receipt!.Id, outer.Requests[1].PriorSafeResolution!.ReceiptReference);
        Assert.Equal([InteractionAiRole.Inner, InteractionAiRole.Outer], gateway.PlanningRoles);
        Assert.Single(gateway.ParentDelegationIds.Distinct(StringComparer.Ordinal));
        Assert.Equal(gateway.ParentDelegationIds[0], gateway.ParentDelegationIds[1]);
        using var innerIntent = JsonDocument.Parse(gateway.IntentJsons[0]);
        using var outerIntent = JsonDocument.Parse(gateway.IntentJsons[1]);
        Assert.EndsWith(".inner", innerIntent.RootElement.GetProperty("idempotencyKey").GetString(), StringComparison.Ordinal);
        Assert.EndsWith(".outer", outerIntent.RootElement.GetProperty("idempotencyKey").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(innerIntent.RootElement.GetProperty("idempotencyKey").GetString(),
            outerIntent.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal("local", innerIntent.RootElement.GetProperty("plannerPreference").GetString());
        Assert.Equal(outerPreference, outerIntent.RootElement.GetProperty("plannerPreference").GetString());
        Assert.Contains(inner.Receipt.Receipt.Id, result.Messages[^2].Text, StringComparison.Ordinal);
        Assert.Equal(0, gateway.ExecutionCalls);
    }

    [Theory]
    [InlineData(InteractionResolutionStatus.NeedsInput)]
    [InlineData(InteractionResolutionStatus.Ambiguous)]
    [InlineData(InteractionResolutionStatus.Unsafe)]
    [InlineData(InteractionResolutionStatus.Stale)]
    public async Task Non_fallback_inner_status_stops_without_second_outer_call(
        InteractionResolutionStatus status)
    {
        var gateway = new Gateway(NonResolution(status, "INNER_STOP", 'd'));
        var outer = new Outer(new(true, InteractionOuterDecision.Delegate, "inner intent", "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), gateway, outer, new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Do the action."), CancellationToken.None);

        Assert.Equal("needs-attention", result!.Status);
        Assert.Single(outer.Requests);
        Assert.Equal(1, gateway.PlanCalls);
        Assert.Null(result.PendingPlan);
        Assert.Equal(0, gateway.ExecutionCalls);
    }

    [Theory]
    [InlineData(InteractionOuterDecision.Delegate)]
    [InlineData(InteractionOuterDecision.Respond)]
    public async Task Outer_reconsideration_that_is_not_direct_plan_does_not_loop_or_plan_again(
        InteractionOuterDecision decision)
    {
        var gateway = new Gateway(NonResolution(InteractionResolutionStatus.Unknown, "INNER_UNKNOWN", 'e'));
        var outer = Outer.WithKind(InteractionOuterProviderKind.Local,
            new(true, InteractionOuterDecision.Delegate, "inner intent", "OUTER_TURN_COMPLETED"),
            new(true, decision, decision == InteractionOuterDecision.Respond
                ? "I could not perform the request." : "try inner again", "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), gateway, outer, new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("Do the action."), CancellationToken.None);

        Assert.Equal("needs-attention", result!.Status);
        Assert.Equal(2, outer.Requests.Count);
        Assert.Equal(1, gateway.PlanCalls);
        Assert.Equal(0, gateway.ExecutionCalls);
    }

    [Fact]
    public async Task Repeated_safe_non_resolution_is_not_duplicated_and_keeps_inner_code()
    {
        var inner = NonResolution(InteractionResolutionStatus.Unsupported, "NO_CONTRACT", '9');
        var receiptText = $"{inner.SafeSummary} Receipt: {inner.Receipt.Receipt!.Id}.";
        var outer = Outer.WithKind(InteractionOuterProviderKind.Local,
            new(true, InteractionOuterDecision.Delegate, "look up stored data", "OUTER_TURN_COMPLETED"),
            new(true, InteractionOuterDecision.Respond, receiptText, "OUTER_TURN_COMPLETED"));
        var service = new ApplicationConversationService(new(), new Spaces(), new Gateway(inner), outer,
            new Narrator());
        var conversation = service.Create(Principal, Application, new("chat-space", "session.fixture"));

        var result = await service.TurnAsync(Principal, Application, conversation.Id,
            new("What is stored?"), CancellationToken.None);

        Assert.Equal(2, result!.Messages.Count);
        Assert.Equal(receiptText, result.Messages[^1].Text);
        Assert.Equal("NO_CONTRACT", result.ActiveAgenda!.Tasks[0].Batches[0].Code);
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

    private sealed class Outer : IInteractionOuterProviderAdapter
    {
        private readonly Queue<InteractionOuterTurnResult> results;
        private readonly InteractionTaskAgenda? agenda;

        public Outer(InteractionOuterTurnResult result)
            : this(InteractionOuterProviderKind.Local, [result], null) { }

        private Outer(InteractionOuterProviderKind kind, IEnumerable<InteractionOuterTurnResult> results,
            InteractionTaskAgenda? agenda)
        {
            Kind = kind;
            this.results = new(results);
            this.agenda = agenda;
        }

        public static Outer WithKind(
            InteractionOuterProviderKind kind,
            InteractionOuterTurnResult first,
            InteractionOuterTurnResult second) => new(kind, [first, second], null);

        public static Outer WithAgenda(InteractionTaskAgenda agenda) => new(
            InteractionOuterProviderKind.Local,
            [new(true, InteractionOuterDecision.Delegate, "bounded goal", "OUTER_TURN_COMPLETED")],
            agenda);

        public InteractionOuterProviderKind Kind { get; }
        public List<InteractionOuterTurnRequest> Requests { get; } = [];
        public int AgendaCalls { get; private set; }

        public Task<InteractionOuterTurnResult> DecideAsync(InteractionOuterTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(results.Count == 0
                ? agenda is null ? InteractionOuterTurnResult.Unavailable("OUTER_MODEL_UNAVAILABLE")
                    : new(true, InteractionOuterDecision.Delegate, "bounded goal", "OUTER_TURN_COMPLETED")
                : results.Dequeue());
        }

        public Task<InteractionNarrationResult> NarrateAsync(InteractionNarrationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InteractionNarrationResult.Unavailable("NARRATION_MODEL_UNAVAILABLE"));

        public Task<InteractionTaskAgendaResult> CreateAgendaAsync(InteractionTaskAgendaRequest request,
            CancellationToken cancellationToken = default)
        {
            AgendaCalls++;
            return Task.FromResult(new InteractionTaskAgendaResult(true,
                agenda ?? InteractionTaskAgenda.Parse(JsonSerializer.Serialize(new
                {
                    tasks = new[] { new { intentText = request.GoalText, dependsOn = Array.Empty<int>(),
                        batches = new[] { new { intentText = request.GoalText } } } }
                })), "TASK_AGENDA_COMPLETED"));
        }
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
        private readonly Queue<InteractionPlanGatewayResult> results;

        public Gateway(params InteractionPlanGatewayResult[] results) => this.results = new(results);

        public int PlanCalls { get; private set; }
        public int ExecutionCalls { get; private set; }
        public InteractionAiRole PlanningRole { get; private set; }
        public string? ParentDelegationId { get; private set; }
        public string? ExecutionJson { get; private set; }
        public bool ExecutionSuccessful { get; set; } = true;
        public bool ExecutionConflict { get; set; }
        public List<InteractionAiRole> PlanningRoles { get; } = [];
        public List<string?> ParentDelegationIds { get; } = [];
        public List<string> IntentJsons { get; } = [];

        public Task<InteractionPlanGatewayResult> PlanAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, string stateSpaceId, string sessionContextId,
            string intentJson, string? submittedProposalJson = null, string? conversationId = null,
            InteractionAiRole role = InteractionAiRole.Outer, string? parentDelegationId = null,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            PlanningRole = role;
            ParentDelegationId = parentDelegationId;
            PlanningRoles.Add(role);
            ParentDelegationIds.Add(parentDelegationId);
            IntentJsons.Add(intentJson);
            if (results.Count != 0) return Task.FromResult(results.Dequeue());
            using var input = JsonDocument.Parse("{\"weapon\":\"sword\"}");
            var proposal = new InteractionProposalProjection("propose",
                [new("step.1", "action", "fixture-chat.attack", 1, new string('C', 64), [],
                    new Dictionary<string, string> { ["actor"] = "actor", ["target"] = "target" },
                    input.RootElement.Clone(), [])]);
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
            if (ExecutionConflict)
                return Task.FromResult(new InteractionExecutionOutcome(
                    InteractionExecutionReceiptDisposition.Failed,
                    "INTERACTION_EXECUTION_IDEMPOTENCY_CONFLICT",
                    "The execution idempotency key is already bound to another request.", [],
                    InteractionReceiptWriteResult.Conflict(), new string('F', 64)));
            var receipt = Receipt("interaction-receipt." + new string('b', 32), "execution");
            if (!ExecutionSuccessful)
                return Task.FromResult(new InteractionExecutionOutcome(
                    InteractionExecutionReceiptDisposition.Failed, "INTERACTION_EXECUTION_FAILED",
                    "The verified interaction failed.", [], InteractionReceiptWriteResult.Appended(receipt),
                    new string('F', 64)));
            var action = new DantesRoleplay.ApplicationExecution.ApplicationActionExecutionResult(
                DantesRoleplay.ApplicationExecution.ApplicationActionExecutionDisposition.Succeeded,
                "0123456789abcdef0123456789abcdef", "fixture-chat.attack", new string('C', 64), 1,
                "Mechanic-safe result.", 1, []);
            using var queryOutput = JsonDocument.Parse("{\"fact\":\"safe fact\"}");
            var query = new InteractionQueryResultProjection("query.1", "fixture-chat.query.fact",
                new string('A', 64), new string('B', 64), new string('C', 64),
                queryOutput.RootElement.Clone());
            return Task.FromResult(new InteractionExecutionOutcome(InteractionExecutionReceiptDisposition.Succeeded,
                "INTERACTION_EXECUTION_SUCCEEDED", "The verified interaction completed.", [action],
                InteractionReceiptWriteResult.Appended(receipt), new string('F', 64), QueryResults: [query]));
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

    private static InteractionPlanGatewayResult NonResolution(
        InteractionResolutionStatus status,
        string code,
        char receiptCharacter)
    {
        var receipt = new InteractionReceiptProjection(
            "interaction-receipt." + new string(receiptCharacter, 32), "resolution",
            Principal.PrincipalId, Application, "chat-space", "key", new string('A', 64),
            InteractionResolutionStatusNames.Get(status), code, null, "The inner planner could not resolve the request.",
            ["safe.evidence"], DateTime.UtcNow);
        return new(status, code, receipt.SafeSummary, receipt.Evidence, null, null,
            InteractionReceiptWriteResult.Appended(receipt), new string('E', 64));
    }
}

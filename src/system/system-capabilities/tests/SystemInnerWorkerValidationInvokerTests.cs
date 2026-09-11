using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.SystemCapabilities;
using Fixture = DantesRoleplay.Tests.SystemInnerWorkerValidationRequestBuilderTests;

namespace DantesRoleplay.Tests;

/// <summary>Real AI runner with controlled provider/lifecycle fixtures, not real grants or ledger acceptance.</summary>
public sealed class SystemInnerWorkerValidationInvokerTests
{
    [Fact]
    public async Task Actual_runner_forwards_one_bounded_request_through_required_lifecycle()
    {
        var input = Fixture.Input();
        var events = new List<string>();
        var provider = new Provider(Output(input), events);
        var lifecycle = new Lifecycle(events);
        var result = await Invoke(input, provider, lifecycle);

        Assert.Equal(["admit", "send", "observe"], events);
        Assert.Equal(1, provider.Calls);
        Assert.Equal("", result.FailureCode);
        Assert.Equal(ApplicationCandidateReuseJudgment.Uncertain, result.Judgment!.Judgment);
        Assert.Equal(input.SelectionFingerprint, result.Judgment.SelectionFingerprint);
        Assert.Equal(new AiTokenUsageEvidence(7, 2, 11, true), result.Response.Usage);
        Assert.NotEmpty(result.Response.Activities!);
        var descriptor = Assert.Single(lifecycle.Calls);
        Assert.Equal(0, descriptor.Request.MaximumToolCalls);
        Assert.Empty(descriptor.Request.Tools);
        Assert.Null(descriptor.Request.ToolExecutor);
        Assert.Equal(input.ModelInputJson, descriptor.Request.Messages.Single(value => value.Role == AiMessageRole.User).Content);
        Assert.Contains(SystemInnerWorkerCandidateReviewer.Id,
            descriptor.Request.Messages.Single(value => value.Role == AiMessageRole.System).Content, StringComparison.Ordinal);
        Assert.Equal(AiDispatchCompletionKind.Returned, Assert.Single(lifecycle.Outcomes).Kind);
    }

    [Fact]
    public async Task Duplicate_assessment_is_rejected_after_schema_validation_without_losing_actual_response()
    {
        var input = Fixture.Input(withAlternatives: true);
        var events = new List<string>();
        var returned = Output(input, duplicate: true);
        var provider = new Provider(returned, events);
        var lifecycle = new Lifecycle(events);
        var result = await Invoke(input, provider, lifecycle);

        Assert.True(result.Response.Ok); // The schema accepts the shape; the owning parser rejects duplicate coverage.
        Assert.Null(result.Judgment);
        Assert.Equal("WORKER_VALIDATION_OUTPUT_INVALID", result.FailureCode);
        Assert.Equal(returned.Usage, result.Response.Usage);
        Assert.Equal(2, result.Response.StructuredData!.Value.GetProperty("assessments").GetArrayLength());
        Assert.NotEmpty(result.Response.Activities!);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(["admit", "send", "observe"], events);
    }

    [Fact]
    public async Task Fixture_admission_denial_prevents_provider_execution_and_retry()
    {
        var input = Fixture.Input();
        var events = new List<string>();
        var provider = new Provider(Output(input), events);
        var lifecycle = new Lifecycle(events, deny: true);
        var result = await Invoke(input, provider, lifecycle);
        Assert.Null(result.Judgment);
        Assert.Equal("GRANT_REVOKED", result.FailureCode);
        Assert.Equal(0, provider.Calls);
        Assert.Empty(lifecycle.Outcomes);
        Assert.Equal(["admit"], events);
    }

    [Fact]
    public async Task Provider_failure_retains_partial_usage_and_observed_activity()
    {
        var input = Fixture.Input();
        var events = new List<string>();
        var failure = AiProviderResponse.Failure("PROVIDER_LOST", "Lost connection.") with { Usage = new(5, 1, 9, false) };
        var provider = new Provider(failure, events);
        var lifecycle = new Lifecycle(events);
        var result = await Invoke(input, provider, lifecycle);
        Assert.Null(result.Judgment);
        Assert.Equal("PROVIDER_LOST", result.FailureCode);
        Assert.Equal(failure.Usage, result.Response.Usage);
        Assert.NotEmpty(result.Response.Activities!);
        // Lifecycle observations defensively copy provider collections; preserve their actual evidence values.
        var observed = Assert.Single(lifecycle.Outcomes).Response!;
        Assert.Equal(failure.ErrorCode, observed.ErrorCode);
        Assert.Equal(failure.Usage, observed.Usage);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Model_tool_request_cannot_enter_a_tool_scope_or_trigger_another_round()
    {
        var input = Fixture.Input();
        var events = new List<string>();
        var provider = new Provider(Output(input) with { ToolCalls = [new("call.1", "invented", "{}")] }, events);
        var lifecycle = new Lifecycle(events);
        var result = await Invoke(input, provider, lifecycle);
        Assert.Null(result.Judgment);
        Assert.False(result.Response.Ok);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(["admit", "send", "observe"], events);
    }

    private static Task<SystemInnerWorkerValidationComputation> Invoke(ApplicationCandidateReuseInputV2 input,
        Provider provider, Lifecycle lifecycle) => new SystemInnerWorkerValidationInvoker(new AiService([provider]), new Clock())
        .InvokeAsync(Fixture.Profile(input), input, Fixture.Configuration(), lifecycle);

    private static AiProviderResponse Output(ApplicationCandidateReuseInputV2 input, bool duplicate = false)
    {
        var targets = duplicate ? input.Alternatives.Select(_ => input.Alternatives[0]) : input.Alternatives;
        var json = JsonSerializer.Serialize(new
        {
            format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
            input.SelectionFingerprint, input.InputFingerprint, input.ManualResultFingerprint,
            judgment = "uncertain", reason = "Further owner review is required.",
            assessments = targets.Select(target => new { target, judgment = "uncertain", reason = "Insufficient evidence." })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new(true, null, "review", json, [], Usage: new(7, 2, 11, true));
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Fixture.Now);
    }

    private sealed class Provider(AiProviderResponse response, List<string> events) : IAiProvider
    {
        public int Calls { get; private set; }
        public AiProviderInfo Info => new("host-provider", "Controlled fixture provider");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            events.Add("send");
            return Task.FromResult(response);
        }
    }

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class Lifecycle(List<string> events, bool deny = false) : IAiInvocationLifecycle
    {
        public List<AiProviderCallDescriptor> Calls { get; } = [];
        public List<AiProviderCallObservation> Outcomes { get; } = [];
        public ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call, CancellationToken cancellationToken)
        {
            events.Add("admit");
            Calls.Add(call);
            if (deny) throw new AiLifecycleException("GRANT_REVOKED", "Fixture admission denied.");
            return ValueTask.FromResult<IAiProviderCallScope>(new Scope(this, events));
        }

        [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
        private sealed class Scope(Lifecycle owner, List<string> events) : IAiProviderCallScope
        {
            public ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome)
            {
                events.Add("observe");
                owner.Outcomes.Add(outcome);
                return ValueTask.CompletedTask;
            }
            public ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("A validation computation must never request tool admission.");
        }
    }
}

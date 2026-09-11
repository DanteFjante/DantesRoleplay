using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerResultAdapterTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Schema = """{"type":"object","required":["summary"],"properties":{"summary":{"type":"string"}}}""";

    [Fact]
    public void Projection_requires_existing_task_identity_and_persisted_result_reference()
    {
        var unrecorded = Map(Response("{\"summary\":\"ready\"}"), evidence: "");
        Assert.Equal(InteractionInvocationResultTag.Unavailable, unrecorded.Tag);
        Assert.Null(unrecorded.TaskHandle);
        Assert.Null(unrecorded.CompletionEvidenceReference);
        var wrongTask = SystemInnerWorkerResultAdapter.MapStoredResult(Request(), Response("{\"summary\":\"ready\"}"),
            new("task.1", "different.command"), "existing.result.1", currentInvocationEvidenceVerified: true);
        Assert.Equal("SYSTEM_INNER_WORKER_RESULT_IDENTITY_MISMATCH", wrongTask.Code);
        Assert.Null(wrongTask.DataJson);
    }

    [Fact]
    public void Foreign_result_rejection_retains_only_separately_verified_current_invocation_evidence()
    {
        var currentReceipt = Receipt();
        var currentRecovery = new ApplicationEcsExecutionIdentity(new string('c', 32), Hash);
        var foreignResponse = Response("""{"summary":"foreign-secret","receipt":{"operationId":"foreign-operation"}}""");
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(), foreignResponse,
            new("foreign.task", "foreign.command"), "foreign.result", true, [currentReceipt], currentRecovery);

        Assert.Equal("SYSTEM_INNER_WORKER_RESULT_IDENTITY_MISMATCH", result.Code);
        Assert.Equal(currentReceipt.OperationId, Assert.Single(result.PreviousCommits).OperationId);
        Assert.Same(currentRecovery, result.RecoveryIdentity);
        Assert.Null(result.DataJson);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.TaskHandle);
        Assert.DoesNotContain("foreign", result.ToJson(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("command.1")]
    [InlineData("foreign.command")]
    public void Ambiguous_evidence_ownership_requires_reconciliation_without_disclosing_candidate_evidence(string command)
    {
        var unverifiedReceipt = new InteractionInvocationCommitReceipt(new string('b', 32), Hash, []);
        var unverifiedRecovery = new ApplicationEcsExecutionIdentity(new string('c', 32), Hash);
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(), Response("{\"summary\":\"foreign-secret\"}"),
            new("candidate.task", command), "candidate.result", false, [unverifiedReceipt], unverifiedRecovery);

        Assert.Equal("SYSTEM_INNER_WORKER_RECONCILIATION_REQUIRED", result.Code);
        Assert.Empty(result.PreviousCommits);
        Assert.Null(result.RecoveryIdentity);
        Assert.Null(result.DataJson);
        Assert.Null(result.TaskHandle);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.DoesNotContain(unverifiedReceipt.OperationId, result.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain(unverifiedRecovery.OperationId, result.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_output_and_compact_summary_are_computation_not_model_authorized_commits()
    {
        var response = Response("""{"summary":"Read complete","tag":"committed","receipt":{"operationId":"invented"},"unresolvedInputs":["next input"]}""")
            with { ConversationId = "provider.conversation", Text = new string('x', 80_000) };
        var result = Map(response);
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("existing.result.1", result.CompletionEvidenceReference);
        Assert.Null(result.Receipt);
        Assert.Null(result.TaskHandle);
        using var document = JsonDocument.Parse(result.DataJson!);
        Assert.Equal("Read complete", document.RootElement.GetProperty("summary").GetString());
        Assert.Equal("task.1", document.RootElement.GetProperty("task").GetProperty("taskId").GetString());
        Assert.Equal("committed", document.RootElement.GetProperty("data").GetProperty("tag").GetString());
        Assert.Empty(result.PreviousCommits);
        Assert.DoesNotContain("provider.conversation", result.DataJson!);
        Assert.DoesNotContain(new string('x', 500), result.DataJson!);
    }

    [Theory]
    [InlineData("{\"summary\":42}")]
    [InlineData("{\"summary\":\"first\",\"summary\":\"second\"}")]
    [InlineData("[]")]
    public void Readback_revalidates_output_instead_of_trusting_response_ok(string json)
    {
        var result = Map(Response(json));
        Assert.Equal("SYSTEM_INNER_WORKER_OUTPUT_INVALID", result.Code);
        Assert.Null(result.CompletionEvidenceReference);
        Assert.Null(result.DataJson);
    }

    [Fact]
    public void Missing_structured_output_or_oversized_data_cannot_be_completed()
    {
        Assert.Equal("SYSTEM_INNER_WORKER_OUTPUT_INVALID", Map(Response("{}") with { StructuredData = null }).Code);
        var large = JsonSerializer.Serialize(new { summary = new string('a', 70_000) });
        Assert.Equal("SYSTEM_INNER_WORKER_OUTPUT_INVALID", Map(Response(large)).Code);
    }

    [Fact]
    public void Provider_failure_preserves_owning_commit_evidence_and_does_not_leak_provider_message()
    {
        var receipt = Receipt();
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(),
            AiResponse.Failure("provider-error", "sensitive provider diagnostics"), Handle(), "existing.result.1", true, [receipt]);
        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal(receipt.OperationId, Assert.Single(result.PreviousCommits).OperationId);
        Assert.Null(result.Receipt);
        Assert.DoesNotContain("sensitive", result.SafeMessage);
    }

    [Fact]
    public void Missing_terminal_record_cannot_erase_earlier_authoritative_commits()
    {
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(),
            Response("{\"summary\":\"ready\"}"), Handle(), "", true, [Receipt()]);
        Assert.Equal("SYSTEM_INNER_WORKER_RESULT_UNRECORDED", result.Code);
        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Single(result.PreviousCommits);
        Assert.Null(result.CompletionEvidenceReference);
    }

    [Fact]
    public void Unresolved_operation_overrides_apparent_model_success_and_requires_reconciliation()
    {
        var identity = new ApplicationEcsExecutionIdentity(new string('a', 32), Hash);
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(), Response("{\"summary\":\"ready\"}"),
            Handle(), "existing.result.1", true, [Receipt()], identity);
        Assert.Equal("SYSTEM_INNER_WORKER_RECONCILIATION_REQUIRED", result.Code);
        Assert.Same(identity, result.RecoveryIdentity);
        Assert.Single(result.PreviousCommits);
        Assert.Null(result.CompletionEvidenceReference);
    }

    [Fact]
    public void Prior_authoritative_commits_remain_labelled_in_completed_readback()
    {
        var result = SystemInnerWorkerResultAdapter.MapStoredResult(Request(), Response("{\"summary\":\"ready\"}"),
            Handle(), "existing.result.1", true, [Receipt()]);
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Null(result.Receipt);
        Assert.Equal(new string('a', 32), Assert.Single(result.PreviousCommits).OperationId);
    }

    [Fact]
    public void Malformed_stored_evidence_is_distinct_from_invalid_model_output()
    {
        var result = Map(Response("{\"summary\":\"ready\"}"), evidence: new string('e', InteractionContractLimits.Identifier + 1));
        Assert.Equal("SYSTEM_INNER_WORKER_RESULT_EVIDENCE_INVALID", result.Code);
        Assert.Null(result.CompletionEvidenceReference);
    }

    private static InteractionInvocationResult Map(AiResponse response, string evidence = "existing.result.1") =>
        SystemInnerWorkerResultAdapter.MapStoredResult(Request(), response, Handle(), evidence, currentInvocationEvidenceVerified: true);
    private static SystemTaskDurableHandle Handle() => new("task.1", "command.1");
    private static InteractionInvocationCommitReceipt Receipt() => new(new string('a', 32), Hash, []);
    private static AiResponse Response(string json) => new(true, null, "", JsonSerializer.Deserialize<JsonElement>(json), [], 4, 2);
    private static SystemInnerWorkerRequest Request() => new(new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "fixture"),
        new ApplicationRevision(ApplicationIdentifier.Parse("fixture-app"), 1, Hash, []),
        "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.Workflow,
        new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(5))), new("procedure.fixture", 1, Hash), "{}", Schema);
}

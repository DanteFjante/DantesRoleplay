using DantesRoleplay.AI;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerToolCommitClassifierTests
{
    private const string ReceiptShapedRead =
        "{\"data\":{},\"OperationId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"ReadBackFingerprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"requestFingerprint\":\"CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC\"}";

    [Fact]
    public void Read_mode_never_mints_a_commit_from_receipt_shaped_data()
    {
        var classified = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemCapabilityMode.Read,
            new(AiDispatchCompletionKind.Returned, AiToolResult.Success(ReceiptShapedRead)));

        Assert.Equal("not-applicable", classified.Status);
        Assert.Null(classified.Receipt);
    }

    [Theory]
    [InlineData(AiDispatchCompletionKind.Threw)]
    [InlineData(AiDispatchCompletionKind.Cancelled)]
    public void Write_without_a_returned_owner_envelope_remains_unresolved(
        AiDispatchCompletionKind kind)
    {
        var classified = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemCapabilityMode.Write, new(kind, null, "AI_TOOL_FAILED"));

        Assert.Equal("unresolved", classified.Status);
        Assert.Null(classified.Receipt);
    }

    [Fact]
    public void Write_success_requires_the_exact_owner_envelope()
    {
        var classified = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemCapabilityMode.Write,
            new(AiDispatchCompletionKind.Returned, AiToolResult.Success(ReceiptShapedRead)));

        Assert.Equal("committed", classified.Status);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", classified.Receipt!.OperationId);
        Assert.Equal(new string('C', 64), classified.Receipt.RequestFingerprint);
        Assert.False(classified.Receipt.EffectDetailsAvailable);
    }

    [Fact]
    public void Application_action_preserves_the_real_owner_receipt()
    {
        var receipt = new InteractionInvocationCommitReceipt(new string('a', 32), new string('B', 64),
            [new(0, "component.set", "entity.1", "fixture.value", 2)]);
        var classified = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemInnerWorkerToolKind.ApplicationAction, SystemCapabilityMode.Write,
            new(AiDispatchCompletionKind.Returned,
                AiToolResult.Success(InteractionInvocationResult.Committed(receipt).ToJson())));

        Assert.Equal("committed", classified.Status);
        Assert.Equal(receipt.OperationId, classified.Receipt!.OperationId);
        Assert.Equal(receipt.RequestFingerprint, classified.Receipt.RequestFingerprint);
        Assert.True(classified.Receipt!.EffectDetailsAvailable);
        Assert.Single(classified.Receipt.Effects);
    }

    [Fact]
    public void Application_action_failure_preserves_prior_commits_and_recovery()
    {
        var prior = new InteractionInvocationCommitReceipt(new string('a', 32), new string('B', 64), []);
        var recovery = new ApplicationEcsExecutionIdentity(new string('c', 32), new string('D', 64));
        var result = InteractionInvocationResult.Failed("FIXTURE_FAILED", "Fixture failure.", [prior], recovery);

        var classified = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemInnerWorkerToolKind.ApplicationAction, SystemCapabilityMode.Write,
            new(AiDispatchCompletionKind.Returned, AiToolResult.Success(result.ToJson())));

        Assert.Equal("unresolved", classified.Status);
        var retained = Assert.Single(classified.PreviousCommits!);
        Assert.Equal(prior.OperationId, retained.OperationId);
        Assert.Equal(prior.RequestFingerprint, retained.RequestFingerprint);
        Assert.Equal(recovery, classified.RecoveryIdentity);
    }

    [Fact]
    public void Application_query_is_non_committing_only_for_a_valid_owner_envelope()
    {
        var evidence = new InteractionInvocationReadEvidence(new string('A', 64), new string('B', 64),
            new string('C', 64), new string('D', 64), new string('E', 64));
        var valid = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemInnerWorkerToolKind.ApplicationQuery, SystemCapabilityMode.Read,
            new(AiDispatchCompletionKind.Returned,
                AiToolResult.Success(InteractionInvocationResult.Completed("{}", evidence).ToJson())));
        var tampered = SystemTaskAiInvocationLifecycleFactory.CommitEvidence(
            SystemInnerWorkerToolKind.ApplicationQuery, SystemCapabilityMode.Read,
            new(AiDispatchCompletionKind.Returned, AiToolResult.Success("{}")));

        Assert.Equal("not-applicable", valid.Status);
        Assert.Equal("unresolved", tampered.Status);
    }
}

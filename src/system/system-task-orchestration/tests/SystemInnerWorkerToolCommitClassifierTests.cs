using DantesRoleplay.AI;
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
}

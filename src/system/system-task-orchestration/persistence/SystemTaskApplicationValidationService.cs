using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Internal application validation operations. Current application Read/Validate authority is
/// independent of state workflow permissions. No transport or production runner is registered here.
/// </summary>
internal sealed class SystemTaskApplicationValidationService(
    DantesRoleplayDbContext db, SystemTaskApplicationValidationGate gate, TimeProvider time)
{
    internal Task<InteractionInvocationResult> SubmitAsync(SystemInnerWorkerResolvedProfile profile,
        string? causationOperationId = null, string? causalCommandId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ApplicationCandidateValidation validation)
            return Task.FromResult(NotAuthorized());
        var host = profile.Worker.InvocationHost;
        return RunAsync(host, true, async boundary =>
        {
            var authority = await gate.CheckAsync(host, validation.Candidate, true,
                causationOperationId, causalCommandId, cancellationToken);
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is { } unavailable) return unavailable;
            // The staging owner performs exact command replay before transferring fresh root
            // allowance. Neither denied/incomplete selection nor an equivalent replay transfers it.
            var staged = await boundary.Store.StageEnqueueValidationAsync(profile, false,
                causationOperationId, causalCommandId, boundary.Connection, boundary.Transaction, cancellationToken);
            if (staged.Disposition is not (SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing))
                return InteractionInvocationResult.Failed(staged.Code, staged.SafeMessage);
            var enrollment = await boundary.Store.StageEnrollAiBudgetAsync(staged.Handle!, profile,
                boundary.Connection, boundary.Transaction, cancellationToken);
            if (!enrollment.Accepted) return InteractionInvocationResult.Unavailable(enrollment.Code,
                "The validation accounting could not be admitted.");
            if (!await CommitAsync(host, boundary, cancellationToken)) return DeadlineExpired();
            return InteractionInvocationResult.Pending(staged.Handle!);
        }, cancellationToken);
    }

    internal Task<InteractionInvocationResult> ReadAsync(InteractionInvocationHost host, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default) => RunAsync(host, false, async boundary =>
    {
        var task = await boundary.Store.ReadInTransactionAsync(handle, boundary.Connection, boundary.Transaction, cancellationToken);
        if (task is null || !Matches(host, task.Request)) return NotAuthorized();
        var authority = await gate.CheckAsync(host, task.Request.Candidate!, false, cancellationToken: cancellationToken);
        if (authority.Failure is not null) return authority.Failure;
        // Read only current, bounded lifecycle diagnostics. A computed provider response is not a
        // semantic attestation or world-effect receipt and is not promoted by this readback.
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            handle = task.Request.Handle, purpose = "application-validation",
            state = task.State.ToString().ToLowerInvariant(), task.AttemptCount,
            task.CancellationRequested, task.CancellationAcknowledged,
            task.CreatedAtUtc, task.UpdatedAtUtc, task.CompletedAtUtc,
            completionAvailable = false,
            reconciliationRequired = await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(
                boundary.Connection, boundary.Transaction, handle.TaskId, cancellationToken),
            task.ErrorCode
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint("dantes-roleplay/system-task-validation-readback/v1", json);
        return InteractionInvocationResult.CompletedComputation(json, "validation-readback." + fingerprint.ToLowerInvariant());
    }, cancellationToken);

    internal Task<InteractionInvocationResult> CancelAsync(InteractionInvocationHost host, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default) => RunAsync(host, true, async boundary =>
    {
        var task = await boundary.Store.ReadInTransactionAsync(handle, boundary.Connection, boundary.Transaction, cancellationToken);
        if (task is null || !Matches(host, task.Request)) return NotAuthorized();
        var affected = await boundary.Store.ReadCancellationTargetsAsync(handle, true,
            boundary.Connection, boundary.Transaction, cancellationToken);
        foreach (var target in affected)
        {
            if (!Matches(host, target.Request) || target.Request.Candidate != task.Request.Candidate) return NotAuthorized();
            var authority = await gate.CheckAsync(host, target.Request.Candidate!, true, cancellationToken: cancellationToken);
            if (authority.Failure is not null) return authority.Failure;
        }
        if (!await boundary.Store.StageCancellationAsync(handle, true, boundary.Connection, boundary.Transaction, cancellationToken))
            return NotAuthorized();
        var updated = await boundary.Store.ReadInTransactionAsync(handle, boundary.Connection, boundary.Transaction, cancellationToken);
        if (!await CommitAsync(host, boundary, cancellationToken)) return DeadlineExpired();
        return updated?.State == SystemTaskLifecycleState.Cancelled
            ? InteractionInvocationResult.Cancelled("SYSTEM_TASK_CANCELLED", "The validation task acknowledged cancellation.")
            : updated?.State == SystemTaskLifecycleState.Running
                ? InteractionInvocationResult.Pending(handle)
                : InteractionInvocationResult.Failed("SYSTEM_TASK_NOT_CANCELLABLE", "The validation task is already terminal.");
    }, cancellationToken);

    private async Task<InteractionInvocationResult> RunAsync(InteractionInvocationHost host, bool write,
        Func<SystemTaskValidationTransaction, Task<InteractionInvocationResult>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.StateSpaceId is not null || host.StateRevision is not null) return NotAuthorized();
        if (host.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime) return DeadlineExpired();
        if (db.Database.CurrentTransaction is not null)
            return InteractionInvocationResult.Unavailable("SYSTEM_TASK_OUTER_TRANSACTION_ACTIVE", "Validation requires its own transaction boundary.");
        try
        {
            await using var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, write, cancellationToken);
            return await action(boundary);
        }
        catch (OperationCanceledException) { return DeadlineExpired(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return InteractionInvocationResult.Unavailable("INNER_VALIDATION_STORAGE_UNAVAILABLE", "The validation task could not be verified; retain its command identity for retry."); }
    }

    private async Task<bool> CommitAsync(InteractionInvocationHost host, SystemTaskValidationTransaction boundary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (host.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime) return false;
        await boundary.CommitAsync();
        return true;
    }
    private static bool Matches(InteractionInvocationHost host, SystemTaskStoredRequest task) =>
        task.Purpose == SystemTaskPurpose.ApplicationValidation && task.Candidate is not null
        && task.Invocation.PrincipalReference == host.Principal.PrincipalId
        && task.Invocation.ApplicationId == host.ApplicationRevision.ApplicationId.Value
        && task.Invocation.StateSpaceId is null && task.Invocation.StateRevision is null
        && task.SelectedDefinition is null && task.ActivationOrigin is null;
    private static InteractionInvocationResult NotAuthorized() => InteractionInvocationResult.Failed(
        "INNER_VALIDATION_NOT_AUTHORIZED", "This application validation task is not available in the current scope.");
    private static InteractionInvocationResult DeadlineExpired() => InteractionInvocationResult.Unavailable(
        "INNER_VALIDATION_DEADLINE_EXPIRED", "The validation operation was cancelled or its deadline elapsed.");
}

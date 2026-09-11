using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

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
        return SubmitAsync(profile.Worker.InvocationHost, validation.Candidate,
            causationOperationId, causalCommandId, cancellationToken);
    }

    /// <summary>
    /// Transport-facing admission accepts only exact host and candidate identities. The owner gate
    /// rehydrates the immutable reviewer profile, manuals, closure, and current grant provenance.
    /// </summary>
    internal Task<InteractionInvocationResult> SubmitAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, string? causationOperationId, string? causalCommandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(candidate);
        return RunAsync(host, true, async boundary =>
        {
            var authority = await gate.CheckAsync(host, candidate, true,
                causationOperationId, causalCommandId, cancellationToken);
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is { } unavailable) return unavailable;
            // The staging owner performs exact command replay before transferring fresh root
            // allowance. Neither denied/incomplete selection nor an equivalent replay transfers it.
            var ownerProfile = authority.Profile!;
            var staged = await boundary.Store.StageEnqueueValidationAsync(ownerProfile, false,
                causationOperationId, causalCommandId, boundary.Connection, boundary.Transaction, cancellationToken);
            if (staged.Disposition is not (SystemTaskEnqueueDisposition.Created or SystemTaskEnqueueDisposition.Existing))
                return InteractionInvocationResult.Failed(staged.Code, staged.SafeMessage);
            var enrollment = await boundary.Store.StageEnrollAiBudgetAsync(staged.Handle!, ownerProfile,
                boundary.Connection, boundary.Transaction, cancellationToken);
            if (!enrollment.Accepted) return InteractionInvocationResult.Unavailable(enrollment.Code,
                "The validation accounting could not be admitted.");
            if (!await CommitAsync(host, boundary, cancellationToken)) return DeadlineExpired();
            return InteractionInvocationResult.Pending(staged.Handle!);
        }, cancellationToken);
    }

    /// <summary>Executes one already-fenced validation lease. The retained request supplies identity only.</summary>
    internal async Task<SystemTaskRunOutcome> ExecuteLeaseAsync(SystemTaskLease lease,
        SystemInnerWorkerValidationInvoker invoker, AiRequest providerConfiguration,
        SystemTaskAiInvocationLifecycleFactory lifecycles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(providerConfiguration);
        ArgumentNullException.ThrowIfNull(lifecycles);
        if (lease.Request.Purpose != SystemTaskPurpose.ApplicationValidation || lease.Request.Candidate is null)
            return Failed("INNER_VALIDATION_LEASE_SCOPE_INVALID", "The leased task is not an application validation task.");
        try
        {
            var host = RehydrateHost(lease.Request.Invocation);
            SystemTaskValidationAuthority authority;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, false, cancellationToken))
            {
                authority = await gate.CheckAsync(host, lease.Request.Candidate, true,
                    lease.Request.Causation?.OperationId, lease.Request.Causation?.CausalCommandId, cancellationToken);
            }
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is { } unavailable)
                return Failed(unavailable.Code, unavailable.SafeMessage);
            if (authority.Profile!.Worker.InputJson != lease.Request.InputJson)
                return Failed("INNER_VALIDATION_INPUT_CHANGED", "The owner-prepared validation input changed after admission.");

            var lifecycle = await lifecycles.CreateAsync(lease, authority.Profile, cancellationToken);
            var computation = await invoker.InvokeAsync(authority.Profile, authority.ReviewInput!,
                providerConfiguration, lifecycle, cancellationToken);
            if (computation.Judgment is null)
                return Failed(computation.FailureCode, "The fixed reviewer did not return a valid bounded judgment.");
            var result = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                format = "dantes-roleplay/application-candidate-reuse-review-result/v1",
                task = lease.Request.Handle,
                attempt = lease.Attempt,
                candidate = lease.Request.Candidate,
                closureEvidenceFingerprint = authority.PureClosure!.EvidenceFingerprint,
                inputFingerprint = authority.ReviewInput!.InputFingerprint,
                judgment = computation.Judgment
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter<ApplicationCandidateReuseJudgment>(JsonNamingPolicy.CamelCase) }
            }));
            if (Encoding.UTF8.GetByteCount(result) > 65_536)
                return Failed("INNER_VALIDATION_RESULT_TOO_LARGE", "The validation result exceeds its retained bound.");
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
            return new SystemTaskRunOutcome.Completed(new(result, "validation.result." + hash,
                [authority.PureClosure.EvidenceFingerprint, authority.Profile.ManualContext.Reference]));
        }
        catch (AiLifecycleException error)
        {
            return Failed(error.Code, error.Message);
        }
        catch (InteractionContractException error)
        {
            return Failed(error.Code, error.Message);
        }
    }

    internal Task<bool> RunNextAsync(string workerId, SystemInnerWorkerValidationInvoker invoker,
        AiRequest providerConfiguration, SystemTaskAiInvocationLifecycleFactory lifecycles,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null
            || db.Database.GetDbConnection() is not SqliteConnection connection)
            throw new InvalidOperationException("The validation worker requires its own SQLite boundaries.");
        var runner = new SqliteSystemTaskLifecycleRunner(
            new SqliteSystemTaskLifecycleStore(connection.ConnectionString, time), time);
        return runner.RunOnceAsync(workerId, (lease, token) => ExecuteLeaseAsync(
            lease, invoker, providerConfiguration, lifecycles, token), cancellationToken,
            SystemTaskPurpose.ApplicationValidation);
    }

    internal Task<InteractionInvocationResult> ReadAsync(InteractionInvocationHost host, SystemTaskDurableHandle handle,
        CancellationToken cancellationToken = default) => RunAsync(host, false, async boundary =>
    {
        var task = await boundary.Store.ReadInTransactionAsync(handle, boundary.Connection, boundary.Transaction, cancellationToken);
        if (task is null || !Matches(host, task.Request)) return NotAuthorized();
        var authority = await gate.CheckAsync(host, task.Request.Candidate!, false, cancellationToken: cancellationToken);
        if (authority.Failure is not null) return authority.Failure;
        var reconciliationRequired = await SqliteSystemTaskLifecycleStore.HasUnresolvedAiAccountingAsync(
            boundary.Connection, boundary.Transaction, handle.TaskId, cancellationToken);
        var completionAvailable = task.State == SystemTaskLifecycleState.Completed
            && task.ResultJson is not null && task.CompletionEvidenceReference is not null
            && !reconciliationRequired;
        JsonElement? result = completionAvailable
            ? JsonSerializer.Deserialize<JsonElement>(task.ResultJson!)
            : null;
        // Read only current, bounded lifecycle diagnostics. A computed provider response is not a
        // semantic attestation or world-effect receipt and is not promoted by this readback.
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            handle = task.Request.Handle, purpose = "application-validation",
            state = task.State.ToString().ToLowerInvariant(), task.AttemptCount,
            task.CancellationRequested, task.CancellationAcknowledged,
            task.CreatedAtUtc, task.UpdatedAtUtc, task.CompletedAtUtc,
            completionAvailable,
            completionEvidenceReference = completionAvailable ? task.CompletionEvidenceReference : null,
            result,
            reconciliationRequired,
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
    private static InteractionInvocationHost RehydrateHost(SystemTaskStoredInvocation invocation)
    {
        var bases = JsonSerializer.Deserialize<string[]>(invocation.BaseApplicationsJson)
            ?? throw new InvalidDataException("The retained application bases are invalid.");
        return InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal(invocation.PrincipalReference, invocation.AuthenticationMethod),
            new(ApplicationIdentifier.Parse(invocation.ApplicationId), invocation.ApplicationRevision,
                invocation.ApplicationFingerprint, bases.Select(ApplicationIdentifier.Parse).ToArray()),
            invocation.GrantReference, invocation.CommandId, invocation.Profile,
            new(invocation.AdmittedOperations, invocation.DeadlineUtc), invocation.ParentCommandId);
    }
    private static SystemTaskRunOutcome.Failed Failed(string code, string message) =>
        new(SystemTaskFailureKind.Permanent, code, message);
    private static InteractionInvocationResult NotAuthorized() => InteractionInvocationResult.Failed(
        "INNER_VALIDATION_NOT_AUTHORIZED", "This application validation task is not available in the current scope.");
    private static InteractionInvocationResult DeadlineExpired() => InteractionInvocationResult.Unavailable(
        "INNER_VALIDATION_DEADLINE_EXPIRED", "The validation operation was cancelled or its deadline elapsed.");
}

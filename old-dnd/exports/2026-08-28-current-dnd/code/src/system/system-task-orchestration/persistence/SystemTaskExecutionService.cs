using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Authorization;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks;

public sealed partial class SystemTaskService
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionLease = TimeSpan.FromMinutes(2);

    public async Task<SystemTaskConfirmationDocument> ConfirmAsync(
        SystemTaskRequestContext context,
        string taskId,
        SystemTaskConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = Authorize(context, PrivateOperatorCapability.Modify);
        ValidateTaskId(taskId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateUpperHash(request.PlanFingerprint, "SYSTEM_TASK_PLAN_FINGERPRINT_INVALID");
        ValidateIdempotencyKey(request.IdempotencyKey);
        var requestFingerprint = Hash(Canonical(JsonSerializer.SerializeToNode(new
        {
            request.PlanFingerprint
        }, WebJson)!));

        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var task = await _db.SystemTasks.AsNoTracking().SingleOrDefaultAsync(value =>
                value.Id == taskId && value.PrincipalReference == context.Principal.PrincipalId,
                cancellationToken) ?? throw Error("SYSTEM_TASK_UNKNOWN", "The system task was not found.");
            var existing = await _db.SystemTaskConfirmations.AsNoTracking().SingleOrDefaultAsync(value =>
                value.TaskId == taskId && value.PrincipalReference == context.Principal.PrincipalId &&
                value.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
                    throw Error("SYSTEM_TASK_IDEMPOTENCY_CONFLICT",
                        "The confirmation idempotency key was already used for another request.");
                return ConfirmationDocument(existing);
            }
            if (task.Status != SystemTaskStatuses.Prepared ||
                !string.Equals(task.PlanFingerprint, request.PlanFingerprint, StringComparison.Ordinal))
                throw Error("SYSTEM_TASK_PLAN_STALE", "The task is not prepared with that exact plan fingerprint.");
            if (await _db.SystemTaskExecutions.AsNoTracking().AnyAsync(value =>
                    value.TaskId == taskId, cancellationToken))
                throw Error("SYSTEM_TASK_ALREADY_EXECUTED",
                    "The prepared task already has an execution receipt; prepare a new task to retry.");

            var now = DateTime.UtcNow;
            var confirmation = new SystemTaskConfirmationRecord
            {
                Id = NewId("system-task-confirmation."),
                TaskId = taskId,
                PrincipalReference = context.Principal.PrincipalId,
                PlanFingerprint = request.PlanFingerprint,
                IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = requestFingerprint,
                AuthorizationEvidenceJson = JsonSerializer.Serialize(decision.Evidence, WebJson),
                ConfirmedAtUtc = now,
                ExpiresAtUtc = now.Add(ConfirmationLifetime)
            };
            _db.SystemTaskConfirmations.Add(confirmation);
            await _db.SaveChangesAsync(cancellationToken);
            return ConfirmationDocument(confirmation);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    public async Task<SystemTaskExecutionDocument> ExecuteAsync(
        SystemTaskRequestContext context,
        string taskId,
        SystemTaskExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = Authorize(context, PrivateOperatorCapability.Modify);
        ValidateTaskId(taskId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateConfirmationId(request.ConfirmationId);
        ValidateUpperHash(request.PlanFingerprint, "SYSTEM_TASK_PLAN_FINGERPRINT_INVALID");
        ValidateIdempotencyKey(request.IdempotencyKey);
        var requestFingerprint = Hash(Canonical(JsonSerializer.SerializeToNode(new
        {
            request.ConfirmationId, request.PlanFingerprint
        }, WebJson)!));

        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var task = await _db.SystemTasks.AsNoTracking().SingleOrDefaultAsync(value =>
                value.Id == taskId && value.PrincipalReference == context.Principal.PrincipalId,
                cancellationToken) ?? throw Error("SYSTEM_TASK_UNKNOWN", "The system task was not found.");
            var existing = await _db.SystemTaskExecutions.AsNoTracking().Include(value => value.Steps)
                .SingleOrDefaultAsync(value => value.TaskId == taskId &&
                    value.PrincipalReference == context.Principal.PrincipalId &&
                    value.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
                    throw Error("SYSTEM_TASK_IDEMPOTENCY_CONFLICT",
                        "The execution idempotency key was already used for another request.");
                if (SystemTaskExecutionStatuses.IsTerminal(existing.Status)) return ExecutionDocument(existing);
                if (existing.LeaseExpiresAtUtc > DateTime.UtcNow)
                    throw Error("SYSTEM_TASK_EXECUTION_ACTIVE", "The exact task execution is already active.");
                return await ResumeExecutionAsync(context, task, existing, decision, cancellationToken);
            }
            if (task.Status != SystemTaskStatuses.Prepared ||
                !string.Equals(task.PlanFingerprint, request.PlanFingerprint, StringComparison.Ordinal))
                throw Error("SYSTEM_TASK_PLAN_STALE", "The task is not prepared with that exact plan fingerprint.");
            var confirmation = await _db.SystemTaskConfirmations.AsNoTracking().SingleOrDefaultAsync(value =>
                value.Id == request.ConfirmationId && value.TaskId == taskId &&
                value.PrincipalReference == context.Principal.PrincipalId, cancellationToken)
                ?? throw Error("SYSTEM_TASK_CONFIRMATION_UNKNOWN", "The task confirmation was not found.");
            if (!string.Equals(confirmation.PlanFingerprint, request.PlanFingerprint, StringComparison.Ordinal) ||
                confirmation.ExpiresAtUtc <= DateTime.UtcNow)
                throw Error("SYSTEM_TASK_CONFIRMATION_EXPIRED",
                    "The confirmation is expired or does not match the current exact plan.");
            if (await _db.SystemTaskExecutions.AsNoTracking().AnyAsync(value =>
                    value.ConfirmationId == request.ConfirmationId, cancellationToken))
                throw Error("SYSTEM_TASK_CONFIRMATION_USED", "That confirmation already has an execution receipt.");

            var now = DateTime.UtcNow;
            var execution = new SystemTaskExecutionRecord
            {
                Id = NewId("system-task-receipt."),
                TaskId = taskId,
                ConfirmationId = request.ConfirmationId,
                PrincipalReference = context.Principal.PrincipalId,
                IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = requestFingerprint,
                PlanFingerprint = request.PlanFingerprint,
                Status = SystemTaskExecutionStatuses.Running,
                SafeSummary = "Executing confirmed system task steps.",
                ErrorCode = "",
                ErrorMessage = "",
                AuthorizationEvidenceJson = JsonSerializer.Serialize(decision.Evidence, WebJson),
                CreatedAtUtc = now,
                StartedAtUtc = now,
                LeaseExpiresAtUtc = now.Add(ExecutionLease)
            };
            _db.SystemTaskExecutions.Add(execution);
            await _db.SaveChangesAsync(cancellationToken);
            return await ResumeExecutionAsync(context, task, execution, decision, cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<SystemTaskExecutionDocument> ResumeExecutionAsync(
        SystemTaskRequestContext context,
        SystemTaskRecord task,
        SystemTaskExecutionRecord execution,
        PrivateOperatorAuthorizationDecision decision,
        CancellationToken cancellationToken)
    {
        var planned = await _db.SystemTaskSteps.AsNoTracking().Where(value =>
                value.TaskId == task.Id && value.Mode == "write")
            .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
        if (planned.Count is < 1 or > MaximumWrites)
            return await FinishExecutionAsync(execution.Id, SystemTaskExecutionStatuses.Failed,
                "The confirmed plan contained no executable writes.", "SYSTEM_TASK_PLAN_INVALID",
                "The confirmed task plan is invalid.", cancellationToken);
        var discovered = _capabilities.Discover(Invocation(context));
        if (!discovered.Ok)
            return await FinishExecutionAsync(execution.Id, SystemTaskExecutionStatuses.Unauthorized,
                "Current system capability authority is unavailable.",
                discovered.Error?.Code ?? "PRIVATE_OPERATOR_DENIED",
                "Current private-operator authorization is required.", cancellationToken);
        var descriptors = discovered.Capabilities.ToDictionary(value => value.Id, StringComparer.Ordinal);
        var priorReceipts = await _db.SystemTaskExecutionSteps.AsNoTracking()
            .Where(value => value.ExecutionId == execution.Id).OrderBy(value => value.Ordinal)
            .ToListAsync(cancellationToken);
        var stop = false;

        foreach (var step in planned)
        {
            var claimed = priorReceipts.SingleOrDefault(value => value.Ordinal == step.Ordinal);
            if (claimed is not null && claimed.Status != SystemTaskStepStatuses.Running) continue;
            if (stop)
            {
                await AppendExecutionStepAsync(execution.Id, step, SystemTaskStepStatuses.Skipped,
                    "", null, "", "", "SYSTEM_TASK_STEP_SKIPPED",
                    "An earlier task step did not complete.", CancellationToken.None);
                continue;
            }
            var ownerInvoked = false;
            try
            {
                var currentDecision = Authorize(context, PrivateOperatorCapability.Modify);
                if (!descriptors.TryGetValue(step.CapabilityId, out var descriptor) ||
                    descriptor.Mode != SystemCapabilityMode.Write ||
                    !string.Equals(descriptor.Fingerprint, step.DescriptorFingerprint, StringComparison.Ordinal))
                {
                    await AppendExecutionStepAsync(execution.Id, step, SystemTaskStepStatuses.Stale,
                        "", null, "", "", "SYSTEM_CAPABILITY_DESCRIPTOR_STALE",
                        "The capability contract changed after planning.", CancellationToken.None);
                    stop = true; continue;
                }
                var earlierRows = await _db.SystemTaskSteps.AsNoTracking().Where(value =>
                        value.TaskId == task.Id && value.Ordinal < step.Ordinal)
                    .OrderBy(value => value.Ordinal).ToListAsync(cancellationToken);
                var succeededStepIds = priorReceipts.Where(value =>
                        value.Status == SystemTaskStepStatuses.Succeeded)
                    .Select(value => value.TaskStepId).ToHashSet(StringComparer.Ordinal);
                var earlier = earlierRows.Where(value => value.Mode != "write" ||
                        !succeededStepIds.Contains(value.StepId))
                    .Select(value => new SystemCapabilityEarlierStep(value.StepId, value.CapabilityId, value.InputJson))
                    .ToArray();
                var token = RequestToken(execution.Id, step.Ordinal);
                var executionEvidence = claimed?.ExecutionEvidenceJson;
                if (claimed is null)
                {
                    var currentPreflight = await _capabilities.PreflightWriteAsync(step.CapabilityId,
                        step.DescriptorFingerprint, step.InputJson, earlier, Invocation(context), cancellationToken);
                    var dependenciesSucceeded = Strings(step.DeferredStepIdsJson).All(dependency =>
                        priorReceipts.Any(value => value.TaskStepId == dependency &&
                            value.Status == SystemTaskStepStatuses.Succeeded));
                    var fresh = currentPreflight.Ok && currentPreflight.Preflight is not null &&
                        (step.PreflightStatus == SystemCapabilityPreflightStatuses.Ready
                            ? currentPreflight.Preflight.Status == SystemCapabilityPreflightStatuses.Ready &&
                              string.Equals(currentPreflight.Preflight.PreconditionFingerprint,
                                  step.PreconditionFingerprint, StringComparison.Ordinal)
                            : dependenciesSucceeded &&
                              currentPreflight.Preflight.Status == SystemCapabilityPreflightStatuses.Ready);
                    if (!fresh)
                    {
                        await AppendExecutionStepAsync(execution.Id, step, SystemTaskStepStatuses.Stale,
                            "", null, "", "", currentPreflight.Error?.Code ?? "SYSTEM_TASK_PREFLIGHT_STALE",
                            currentPreflight.Error?.Message ?? "Current system state no longer matches the confirmed plan.",
                            CancellationToken.None);
                        stop = true; continue;
                    }
                    executionEvidence = currentPreflight.Preflight!.ExecutionEvidenceJson;
                    await ClaimExecutionStepAsync(execution.Id, step, executionEvidence, cancellationToken);
                    claimed = new()
                    {
                        ExecutionId = execution.Id, Ordinal = step.Ordinal, TaskStepId = step.StepId,
                        Status = SystemTaskStepStatuses.Running,
                        ExecutionEvidenceJson = executionEvidence,
                        OperationId = "", OutputJson = "", OutputFingerprint = "",
                        ReadBackFingerprint = "", ErrorCode = "", ErrorMessage = ""
                    };
                    priorReceipts.Add(claimed);
                }

                ownerInvoked = true;
                var written = await _capabilities.ExecuteWriteAsync(step.CapabilityId,
                    step.DescriptorFingerprint, step.InputJson, new(
                        Invocation(context), token, task.Intent, descriptor.ProcedureIds,
                        currentDecision.Evidence, executionEvidence!), cancellationToken);
                if (written.Ok && written.Data is not null)
                {
                    var output = Canonical(written.Data.Value);
                    await AppendExecutionStepAsync(execution.Id, step, SystemTaskStepStatuses.Succeeded,
                        written.OperationId, written.Data, Hash(output), written.ReadBackFingerprint,
                        "", "", CancellationToken.None);
                    priorReceipts.Add(new()
                    {
                        ExecutionId = execution.Id, Ordinal = step.Ordinal, TaskStepId = step.StepId,
                        Status = SystemTaskStepStatuses.Succeeded,
                        ExecutionEvidenceJson = executionEvidence!, OperationId = written.OperationId,
                        OutputJson = output, OutputFingerprint = Hash(output),
                        ReadBackFingerprint = written.ReadBackFingerprint, ErrorCode = "", ErrorMessage = ""
                    });
                }
                else
                {
                    var indeterminate = written.OperationId.Length > 0 ||
                        string.Equals(written.Error?.Code, "SYSTEM_CAPABILITY_OUTPUT_INVALID_AFTER_COMMIT", StringComparison.Ordinal);
                    await AppendExecutionStepAsync(execution.Id, step,
                        indeterminate ? SystemTaskStepStatuses.Indeterminate : SystemTaskStepStatuses.Failed,
                        written.OperationId, null, "", "", written.Error?.Code ?? "SYSTEM_TASK_WRITE_FAILED",
                        written.Error?.Message ?? "The task write failed safely.", CancellationToken.None);
                    stop = true;
                }
            }
            catch (OperationCanceledException)
            {
                await AppendExecutionStepAsync(execution.Id, step,
                    ownerInvoked ? SystemTaskStepStatuses.Indeterminate : SystemTaskStepStatuses.Cancelled,
                    "", null, "", "", ownerInvoked
                        ? "SYSTEM_TASK_EXECUTION_INDETERMINATE" : "SYSTEM_TASK_CANCELLED",
                    ownerInvoked
                        ? "Cancellation arrived after owner execution began; query current state before retrying."
                        : "The task execution was cancelled.", CancellationToken.None);
                stop = true;
            }
            catch (SystemTaskException exception)
            {
                await AppendExecutionStepAsync(execution.Id, step,
                    exception.Code.StartsWith("PRIVATE_OPERATOR_", StringComparison.Ordinal)
                        ? SystemTaskStepStatuses.Unauthorized : SystemTaskStepStatuses.Failed,
                    "", null, "", "", exception.Code, exception.Message, CancellationToken.None);
                stop = true;
            }
            catch (Exception)
            {
                await AppendExecutionStepAsync(execution.Id, step, SystemTaskStepStatuses.Indeterminate,
                    "", null, "", "", "SYSTEM_TASK_EXECUTION_INDETERMINATE",
                    "The task step ended without a trustworthy result.", CancellationToken.None);
                stop = true;
            }
        }

        var receipts = await _db.SystemTaskExecutionSteps.AsNoTracking().Where(value =>
            value.ExecutionId == execution.Id).OrderBy(value => value.Ordinal).ToListAsync(CancellationToken.None);
        var status = AggregateStatus(receipts);
        var failed = receipts.FirstOrDefault(value => value.Status != SystemTaskStepStatuses.Succeeded &&
            value.Status != SystemTaskStepStatuses.Skipped);
        return await FinishExecutionAsync(execution.Id, status,
            status == SystemTaskExecutionStatuses.Succeeded
                ? $"Completed {receipts.Count} confirmed system task step(s)."
                : $"System task execution ended {status} after durable per-step receipts.",
            failed?.ErrorCode ?? "", failed?.ErrorMessage ?? "", CancellationToken.None);
    }

    private async Task AppendExecutionStepAsync(
        string executionId,
        SystemTaskStepRecord step,
        string status,
        string operationId,
        JsonElement? output,
        string outputFingerprint,
        string readBackFingerprint,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken,
        string executionEvidenceJson = "{}")
    {
        var receipt = await _db.SystemTaskExecutionSteps.SingleOrDefaultAsync(value =>
            value.ExecutionId == executionId && value.Ordinal == step.Ordinal, cancellationToken);
        if (receipt is not null && receipt.Status != SystemTaskStepStatuses.Running) return;
        if (receipt is null)
        {
            receipt = new()
            {
                ExecutionId = executionId, Ordinal = step.Ordinal, TaskStepId = step.StepId,
                Status = status, ExecutionEvidenceJson = executionEvidenceJson,
                OperationId = "", OutputJson = "", OutputFingerprint = "",
                ReadBackFingerprint = "", ErrorCode = "", ErrorMessage = ""
            };
            _db.SystemTaskExecutionSteps.Add(receipt);
        }
        receipt.Status = status;
        receipt.OperationId = operationId.Length <= 100 ? operationId : "";
        receipt.OutputJson = output is null ? "" : Canonical(output.Value);
        receipt.OutputFingerprint = outputFingerprint;
        receipt.ReadBackFingerprint = readBackFingerprint;
        receipt.ErrorCode = SafeCode(errorCode, allowEmpty: true);
        receipt.ErrorMessage = SafeMessage(errorMessage, allowEmpty: true);
        receipt.CompletedAtUtc = DateTime.UtcNow;
        var execution = await _db.SystemTaskExecutions.SingleAsync(value => value.Id == executionId,
            cancellationToken);
        execution.LeaseExpiresAtUtc = DateTime.UtcNow.Add(ExecutionLease);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ClaimExecutionStepAsync(string executionId, SystemTaskStepRecord step,
        string executionEvidenceJson, CancellationToken cancellationToken)
    {
        if (await _db.SystemTaskExecutionSteps.AsNoTracking().AnyAsync(value =>
                value.ExecutionId == executionId && value.Ordinal == step.Ordinal, cancellationToken)) return;
        _db.SystemTaskExecutionSteps.Add(new()
        {
            ExecutionId = executionId, Ordinal = step.Ordinal, TaskStepId = step.StepId,
            Status = SystemTaskStepStatuses.Running, ExecutionEvidenceJson = executionEvidenceJson,
            OperationId = "", OutputJson = "", OutputFingerprint = "", ReadBackFingerprint = "",
            ErrorCode = "", ErrorMessage = "", CompletedAtUtc = null
        });
        var execution = await _db.SystemTaskExecutions.SingleAsync(value => value.Id == executionId,
            cancellationToken);
        execution.LeaseExpiresAtUtc = DateTime.UtcNow.Add(ExecutionLease);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SystemTaskExecutionDocument> FinishExecutionAsync(
        string executionId, string status, string summary, string code, string message,
        CancellationToken cancellationToken)
    {
        var execution = await _db.SystemTaskExecutions.SingleAsync(value => value.Id == executionId,
            cancellationToken);
        execution.Status = status;
        execution.SafeSummary = BoundedSummary(summary);
        execution.ErrorCode = SafeCode(code, allowEmpty: true);
        execution.ErrorMessage = SafeMessage(message, allowEmpty: true);
        execution.LeaseExpiresAtUtc = null;
        execution.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        var result = await _db.SystemTaskExecutions.AsNoTracking().Include(value => value.Steps)
            .SingleAsync(value => value.Id == executionId, cancellationToken);
        return ExecutionDocument(result);
    }

    private static string AggregateStatus(IReadOnlyList<SystemTaskExecutionStepRecord> steps)
    {
        if (steps.Count == 0) return SystemTaskExecutionStatuses.Failed;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.Running))
            return SystemTaskExecutionStatuses.Indeterminate;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.Indeterminate))
            return SystemTaskExecutionStatuses.Indeterminate;
        if (steps.All(value => value.Status == SystemTaskStepStatuses.Succeeded))
            return SystemTaskExecutionStatuses.Succeeded;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.Cancelled))
            return steps.Any(value => value.Status == SystemTaskStepStatuses.Succeeded)
                ? SystemTaskExecutionStatuses.Partial : SystemTaskExecutionStatuses.Cancelled;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.TimedOut))
            return steps.Any(value => value.Status == SystemTaskStepStatuses.Succeeded)
                ? SystemTaskExecutionStatuses.Partial : SystemTaskExecutionStatuses.TimedOut;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.Unauthorized))
            return steps.Any(value => value.Status == SystemTaskStepStatuses.Succeeded)
                ? SystemTaskExecutionStatuses.Partial : SystemTaskExecutionStatuses.Unauthorized;
        if (steps.Any(value => value.Status == SystemTaskStepStatuses.Stale))
            return steps.Any(value => value.Status == SystemTaskStepStatuses.Succeeded)
                ? SystemTaskExecutionStatuses.Partial : SystemTaskExecutionStatuses.Stale;
        return steps.Any(value => value.Status == SystemTaskStepStatuses.Succeeded)
            ? SystemTaskExecutionStatuses.Partial : SystemTaskExecutionStatuses.Failed;
    }

    private static SystemTaskConfirmationDocument ConfirmationDocument(SystemTaskConfirmationRecord value) =>
        new(value.Id, value.PlanFingerprint, value.ConfirmedAtUtc, value.ExpiresAtUtc);

    private static string RequestToken(string executionId, int ordinal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/system-task-execution/v1\0" + executionId + "\0" + ordinal));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static void ValidateConfirmationId(string value)
    {
        if (value?.Length != 57 || !value.StartsWith("system-task-confirmation.", StringComparison.Ordinal) ||
            value[25..].Any(character => !char.IsAsciiHexDigitLower(character)))
            throw Error("SYSTEM_TASK_CONFIRMATION_ID_INVALID", "The task confirmation ID is invalid.");
    }

    private static void ValidateUpperHash(string value, string code)
    {
        if (value?.Length != 64 || value.Any(character => !char.IsAsciiHexDigitUpper(character)))
            throw Error(code, "An uppercase SHA-256 fingerprint is required.");
    }
}

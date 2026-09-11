using System.Text.Json.Serialization;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Fresh service scopes keep authority and SQLite lifetime independent of the request or provider.
/// Arguments correlate actual retained records; neither a lease DTO nor a profile is authority.
/// </summary>
internal sealed class SystemTaskAiInvocationLifecycleFactory(IServiceScopeFactory scopes, TimeProvider time)
{
    internal sealed record ProcedureInvocation(
        IAiInvocationLifecycle Lifecycle,
        ISystemCapabilityAiWriteApprovalGate WriteApproval);

    internal async Task<IAiInvocationLifecycle> CreateAsync(SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, CancellationToken cancellationToken = default)
    {
        RequireSubject(lease, profile);
        await InScopeAsync(false, async (boundary, gate, services) =>
        {
            await RequireCurrentAsync(boundary, gate, services, lease, profile, cancellationToken);
            return true;
        }, cancellationToken);
        return new Lifecycle(this, lease, profile);
    }

    internal async Task<ProcedureInvocation> CreateProcedureAsync(SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, SystemCapabilityInvocationContext toolContext,
        CancellationToken cancellationToken = default)
    {
        RequireSubject(lease, profile);
        ArgumentNullException.ThrowIfNull(toolContext);
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow)
            throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
        await InScopeAsync(false, async (boundary, gate, services) =>
        {
            await RequireCurrentAsync(boundary, gate, services, lease, profile, cancellationToken);
            return true;
        }, cancellationToken);
        var approval = new SystemInnerWorkerWriteApprovalGate(lease, profile, toolContext, async token =>
        {
            await InScopeAsync(false, async (boundary, gate, services) =>
            {
                await RequireCurrentAsync(boundary, gate, services, lease, profile, token);
                return true;
            }, token);
        });
        var session = new Lifecycle(this, lease, profile, approval);
        return new(session, approval);
    }

    private async Task RequireCurrentAsync(SystemTaskValidationTransaction boundary,
        SystemTaskApplicationValidationGate? gate, IServiceProvider services, SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, CancellationToken cancellationToken)
    {
        var check = await boundary.Store.CheckAiInvocationAsync(lease, profile,
            boundary.Connection, boundary.Transaction, cancellationToken);
        if (!check.Accepted) throw Failure(check.Code);
        var retained = await boundary.Store.ReadInTransactionAsync(lease.Request.Handle,
            boundary.Connection, boundary.Transaction, cancellationToken);
        if (retained is null) throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
        if (profile.Worker.Subject is SystemInnerWorkerSubject.ApplicationCandidateValidation validation)
        {
            if (gate is null) throw Failure("INNER_AI_VALIDATION_RUNTIME_UNAVAILABLE");
            var authority = await gate.CheckAsync(profile.Worker.InvocationHost, validation.Candidate, true,
                retained.Request.Causation?.OperationId, retained.Request.Causation?.CausalCommandId, cancellationToken);
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is { } unavailable)
                throw new AiLifecycleException(unavailable.Code, unavailable.SafeMessage);
            return;
        }
        if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow)
            throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
        var durable = services.GetService<SqliteSystemTaskDurableService>()
            ?? throw Failure("INNER_WORKER_RUNTIME_UNAVAILABLE");
        if (await durable.ReauthorizeInnerWorkerAsync(profile, retained.Request, cancellationToken) is { } denied)
            throw new AiLifecycleException(denied.Code, denied.SafeMessage);
    }

    private Task<IAiProviderCallScope> AdmitAsync(SystemTaskLease lease, SystemInnerWorkerResolvedProfile profile,
        Lifecycle session, AiProviderCallDescriptor call,
        CancellationToken cancellationToken) => InScopeAsync<IAiProviderCallScope>(true,
        async (boundary, gate, services) =>
        {
            await RequireCurrentAsync(boundary, gate, services, lease, profile, cancellationToken);
            if (profile.Worker.Subject is SystemInnerWorkerSubject.ApplicationCandidateValidation
                && (call.Request.Tools.Count != 0 || profile.ToolBindings.Count != 0 || profile.AiBudget.ToolCalls != 0))
                throw Failure("INNER_AI_VALIDATION_TOOLS_FORBIDDEN");
            if (profile.Worker.Subject is SystemInnerWorkerSubject.ProcedureWorkflow
                && !DefinitionsMatch(call.Request.Tools, profile.ToolBindings))
                throw Failure("INNER_AI_TOOL_SELECTION_MISMATCH");
            // No provider-specific total-token guarantee exists in this factory. It must never
            // manufacture one from output-token settings, prompt size, or model-reported usage.
            if (profile.AiBudget.Mode == SystemInnerWorkerTokenBudgetMode.HardCap)
                throw Failure("INNER_AI_PROVIDER_BOUND_UNAVAILABLE");
            var reservationId = "provider." + lease.Attempt.AttemptId + "." + call.Round;
            var reservation = await boundary.Store.StageReserveAiBudgetAsync(new(profile.Worker.InvocationHost,
                lease.Request.Handle, lease.Attempt, reservationId, profile.AiBudget,
                profile.AiBudget.ProviderTokens, 0), boundary.Connection, boundary.Transaction, cancellationToken);
            if (!reservation.Accepted || reservation.Reservation is null) throw Failure(reservation.Code);
            // Existing dispatch identity is a reconciliation obligation, never permission to
            // send the same provider request again. There is no fabricated cached response.
            if (reservation.Code == "INNER_AI_RESERVATION_EXISTING")
                throw Failure("INNER_AI_DISPATCH_RECONCILIATION_REQUIRED");
            var dispatch = await boundary.Store.StageRecordAiProviderDispatchAsync(
                reservation.Reservation.RecordReference, lease.Attempt, profile, call,
                boundary.Connection, boundary.Transaction, cancellationToken);
            if (!dispatch.Accepted) throw Failure(dispatch.Code);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile.Worker.InvocationHost.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime)
                throw Failure("INNER_AI_DEADLINE_EXPIRED");
            await boundary.CommitAsync();
            return new ProviderScope(this, reservation.Reservation.RecordReference, lease, profile, session);
        }, cancellationToken);

    private Task<IAiToolDispatchScope> AdmitToolAsync(SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, Lifecycle session, AiToolDispatchDescriptor dispatch,
        CancellationToken cancellationToken) => InScopeAsync<IAiToolDispatchScope>(true,
        async (boundary, gate, services) =>
        {
            await RequireCurrentAsync(boundary, gate, services, lease, profile, cancellationToken);
            if (profile.Worker.Subject is not SystemInnerWorkerSubject.ProcedureWorkflow)
                throw Failure("INNER_AI_VALIDATION_TOOLS_FORBIDDEN");
            var binding = profile.ToolBindings.SingleOrDefault(value =>
                value.Definition.Name == dispatch.Invocation.Name);
            if (binding is null || binding.Definition != dispatch.Definition)
                throw Failure("INNER_AI_TOOL_NOT_AUTHORIZED");
            var reservationId = "tool." + lease.Attempt.AttemptId + "." + dispatch.DispatchOrdinal;
            var reservation = await boundary.Store.StageReserveAiBudgetAsync(new(profile.Worker.InvocationHost,
                lease.Request.Handle, lease.Attempt, reservationId, profile.AiBudget, 0, 1),
                boundary.Connection, boundary.Transaction, cancellationToken);
            if (!reservation.Accepted || reservation.Reservation is null) throw Failure(reservation.Code);
            if (reservation.Code == "INNER_AI_RESERVATION_EXISTING")
                throw Failure("INNER_AI_DISPATCH_RECONCILIATION_REQUIRED");
            var recorded = await boundary.Store.StageRecordAiToolDispatchAsync(
                reservation.Reservation.RecordReference, lease.Attempt, profile, dispatch,
                boundary.Connection, boundary.Transaction, cancellationToken);
            if (!recorded.Accepted) throw Failure(recorded.Code);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile.Worker.InvocationHost.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime)
                throw Failure("INNER_AI_DEADLINE_EXPIRED");
            await boundary.CommitAsync();
            var operation = ToolOperation(lease, dispatch.DispatchOrdinal);
            var terminalEvidence = SystemInnerWorkerCompletionEvidence.For(lease);
            var journal = await boundary.Store.BeginHostCallAsync(lease, operation,
                InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    purpose = "inner-worker-tool",
                    lease.Request.Handle,
                    lease.Attempt.AttemptId,
                    lease.Attempt.FencingCounter,
                    dispatch.DispatchOrdinal,
                    dispatch.Definition,
                    dispatch.Invocation,
                    completionEvidenceReference = terminalEvidence
                })), cancellationToken);
            if (journal.Disposition != SystemTaskHostCallDisposition.NewPending)
                throw Failure("INNER_AI_TOOL_RECONCILIATION_REQUIRED");
            session.Approval?.RecordAdmittedTool(dispatch);
            return new ToolScope(this, boundary.Store, lease, reservation.Reservation.RecordReference,
                operation, journal.RequestFingerprint, terminalEvidence);
        }, cancellationToken);

    private async Task RecordAsync(string reference, SystemTaskAttemptIdentity attempt, AiProviderCallObservation outcome)
    {
        // This timeout is independent of the caller's cancelled worker token. Fresh owner scopes
        // allow late accounting without extending the original lease, deadline, or authority.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await InScopeAsync(true, async (boundary, _, _) =>
        {
            var result = await boundary.Store.StageRecordAiProviderOutcomeAsync(reference, attempt, outcome,
                boundary.Connection, boundary.Transaction, timeout.Token);
            if (!result.Accepted) throw Failure(result.Code);
            await boundary.CommitAsync();
            return true;
        }, timeout.Token);
    }

    private async Task RecordToolAsync(SqliteSystemTaskLifecycleStore store, SystemTaskLease lease,
        string reference, string operation, string requestFingerprint, string terminalEvidence,
        AiToolDispatchObservation outcome)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? journalFailure = null;
        if (outcome.Kind == AiDispatchCompletionKind.Returned && outcome.Result is not null)
        {
            try
            {
                var completion = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
                {
                    completionEvidenceReference = terminalEvidence,
                    outcome.Kind,
                    outcome.Result,
                    outcome.FailureCode,
                    commit = CommitEvidence(outcome)
                }));
                if (!await store.CompleteHostCallAsync(lease, operation, requestFingerprint,
                        completion, timeout.Token))
                    journalFailure = Failure("INNER_AI_TOOL_RECONCILIATION_REQUIRED");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                journalFailure = error;
            }
        }
        await InScopeAsync(true, async (boundary, _, _) =>
        {
            var result = await boundary.Store.StageRecordAiToolOutcomeAsync(reference, lease.Attempt, outcome,
                boundary.Connection, boundary.Transaction, timeout.Token);
            if (!result.Accepted) throw Failure(result.Code);
            await boundary.CommitAsync();
            return true;
        }, timeout.Token);
        if (journalFailure is not null)
            throw Failure("INNER_AI_TOOL_RECONCILIATION_REQUIRED");
    }

    private static ToolCommitEvidence CommitEvidence(AiToolDispatchObservation outcome)
    {
        if (outcome.Kind != AiDispatchCompletionKind.Returned || outcome.Result is not { Ok: true } result)
            return new("not-committed", null);
        try
        {
            using var document = JsonDocument.Parse(result.Content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4
                || !root.TryGetProperty("OperationId", out var operation)
                || operation.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("requestFingerprint", out var fingerprint)
                || fingerprint.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("ReadBackFingerprint", out var readBack)
                || readBack.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("data", out _))
                return new("not-applicable", null);
            var receipt = new InteractionInvocationCommitReceipt(
                operation.GetString()!, fingerprint.GetString()!, [], EffectDetailsAvailable: false);
            receipt.Validate();
            if (readBack.GetString() is not { Length: 64 } readBackFingerprint
                || readBackFingerprint.Any(character => !char.IsAsciiDigit(character)
                    && character is not (>= 'A' and <= 'F')))
                throw new InteractionContractException("INNER_AI_TOOL_RECEIPT_INVALID",
                    "The system capability returned invalid read-back evidence.");
            return new("committed", receipt);
        }
        catch (Exception error) when (error is JsonException or InteractionContractException)
        {
            return new("unresolved", null);
        }
    }

    private static string ToolOperation(SystemTaskLease lease, int ordinal) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/inner-worker-tool/v1\n" + lease.Request.Handle.TaskId + "\n"
            + lease.Attempt.AttemptId + "\n" + ordinal)))[..32];

    private sealed record ToolCommitEvidence(string Status, InteractionInvocationCommitReceipt? Receipt);

    private async Task<T> InScopeAsync<T>(bool write,
        Func<SystemTaskValidationTransaction, SystemTaskApplicationValidationGate?, IServiceProvider, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<DantesRoleplayDbContext>();
            var gate = services.GetService<SystemTaskApplicationValidationGate>();
            await using var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, write, cancellationToken);
            return await action(boundary, gate, services);
        }
        catch (Exception error) when (error is not (OperationCanceledException or AiLifecycleException or OutOfMemoryException))
        { throw Failure("INNER_AI_PERSISTENCE_UNAVAILABLE"); }
    }

    private static void RequireSubject(SystemTaskLease lease, SystemInnerWorkerResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(profile);
        var matches = (lease.Request.Purpose, profile.Worker.Subject) switch
        {
            (SystemTaskPurpose.ApplicationValidation, SystemInnerWorkerSubject.ApplicationCandidateValidation subject) =>
                lease.Request.Candidate == subject.Candidate,
            (SystemTaskPurpose.ProcedureWorkflow, SystemInnerWorkerSubject.ProcedureWorkflow subject) =>
                lease.Request.SelectedDefinition == subject.ProcedureVersion,
            _ => false
        };
        if (!matches) throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
    }

    private static bool DefinitionsMatch(IReadOnlyList<AiToolDefinition> actual,
        IReadOnlyList<SystemInnerWorkerToolBinding> expected) =>
        actual.OrderBy(value => value.Name, StringComparer.Ordinal)
            .SequenceEqual(expected.Select(value => value.Definition).OrderBy(value => value.Name, StringComparer.Ordinal));
    private static AiLifecycleException Failure(string code) => new(code, "The durable AI lifecycle could not admit or reconcile this call.");

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class Lifecycle(SystemTaskAiInvocationLifecycleFactory owner, SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, SystemInnerWorkerWriteApprovalGate? approval = null)
        : IAiInvocationLifecycle
    {
        internal SystemInnerWorkerWriteApprovalGate? Approval { get; } = approval;

        public async ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call,
            CancellationToken cancellationToken) => await owner.AdmitAsync(lease, profile, this, call, cancellationToken);

    }

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class ProviderScope(SystemTaskAiInvocationLifecycleFactory owner, string reference,
        SystemTaskLease lease, SystemInnerWorkerResolvedProfile profile, Lifecycle session) : IAiProviderCallScope
    {
        public async ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome) =>
            await owner.RecordAsync(reference, lease.Attempt, outcome);
        public async ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch,
            CancellationToken cancellationToken) => await owner.AdmitToolAsync(lease, profile,
                session, dispatch, cancellationToken);
    }

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class ToolScope(SystemTaskAiInvocationLifecycleFactory owner,
        SqliteSystemTaskLifecycleStore store, SystemTaskLease lease, string reference,
        string operation, string requestFingerprint, string terminalEvidence) : IAiToolDispatchScope
    {
        public async ValueTask RecordToolOutcomeAsync(AiToolDispatchObservation outcome) =>
            await owner.RecordToolAsync(store, lease, reference, operation, requestFingerprint,
                terminalEvidence, outcome);
    }
}

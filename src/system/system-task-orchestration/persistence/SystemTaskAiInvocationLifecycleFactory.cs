using System.Text.Json.Serialization;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Fresh service scopes keep authority and SQLite lifetime independent of the request or provider.
/// Arguments correlate actual retained records; neither a lease DTO nor a profile is authority.
/// </summary>
internal sealed class SystemTaskAiInvocationLifecycleFactory(IServiceScopeFactory scopes, TimeProvider time)
{
    internal async Task<IAiInvocationLifecycle> CreateAsync(SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, CancellationToken cancellationToken = default)
    {
        RequireSubject(lease, profile);
        await InScopeAsync(false, async (boundary, gate) =>
        {
            await RequireCurrentAsync(boundary, gate, lease, profile, cancellationToken);
            return true;
        }, cancellationToken);
        return new Lifecycle(this, lease, profile);
    }

    private async Task RequireCurrentAsync(SystemTaskValidationTransaction boundary,
        SystemTaskApplicationValidationGate gate, SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, CancellationToken cancellationToken)
    {
        var check = await boundary.Store.CheckAiInvocationAsync(lease, profile,
            boundary.Connection, boundary.Transaction, cancellationToken);
        if (!check.Accepted) throw Failure(check.Code);
        var retained = await boundary.Store.ReadInTransactionAsync(lease.Request.Handle,
            boundary.Connection, boundary.Transaction, cancellationToken);
        if (retained is null) throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
        var candidate = ((SystemInnerWorkerSubject.ApplicationCandidateValidation)profile.Worker.Subject).Candidate;
        var authority = await gate.CheckAsync(profile.Worker.InvocationHost, candidate, true,
            retained.Request.Causation?.OperationId, retained.Request.Causation?.CausalCommandId, cancellationToken);
        if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is { } unavailable)
            throw new AiLifecycleException(unavailable.Code, unavailable.SafeMessage);
    }

    private Task<IAiProviderCallScope> AdmitAsync(SystemTaskLease lease, SystemInnerWorkerResolvedProfile profile,
        AiProviderCallDescriptor call, CancellationToken cancellationToken) => InScopeAsync<IAiProviderCallScope>(true,
        async (boundary, gate) =>
        {
            await RequireCurrentAsync(boundary, gate, lease, profile, cancellationToken);
            if (call.Request.Tools.Count != 0 || profile.ToolBindings.Count != 0 || profile.AiBudget.ToolCalls != 0)
                throw Failure("INNER_AI_VALIDATION_TOOLS_FORBIDDEN");
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
            return new ProviderScope(this, reservation.Reservation.RecordReference, lease.Attempt);
        }, cancellationToken);

    private async Task RecordAsync(string reference, SystemTaskAttemptIdentity attempt, AiProviderCallObservation outcome)
    {
        // This timeout is independent of the caller's cancelled worker token. Fresh owner scopes
        // allow late accounting without extending the original lease, deadline, or authority.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await InScopeAsync(true, async (boundary, _) =>
        {
            var result = await boundary.Store.StageRecordAiProviderOutcomeAsync(reference, attempt, outcome,
                boundary.Connection, boundary.Transaction, timeout.Token);
            if (!result.Accepted) throw Failure(result.Code);
            await boundary.CommitAsync();
            return true;
        }, timeout.Token);
    }

    private async Task<T> InScopeAsync<T>(bool write,
        Func<SystemTaskValidationTransaction, SystemTaskApplicationValidationGate, Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<DantesRoleplayDbContext>();
            var gate = new SystemTaskApplicationValidationGate(db,
                services.GetRequiredService<IApplicationRegistry>(), services.GetRequiredService<IApplicationActivationReader>(),
                services.GetRequiredService<IStandingGrantTargetResolver>(), services.GetRequiredService<IStandingGrantPolicy>(), time);
            await using var boundary = await SystemTaskValidationTransaction.OpenAsync(db, time, write, cancellationToken);
            return await action(boundary, gate);
        }
        catch (Exception error) when (error is not (OperationCanceledException or AiLifecycleException or OutOfMemoryException))
        { throw Failure("INNER_AI_PERSISTENCE_UNAVAILABLE"); }
    }

    private static void RequireSubject(SystemTaskLease lease, SystemInnerWorkerResolvedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(profile);
        if (lease.Request.Purpose != SystemTaskPurpose.ApplicationValidation
            || profile.Worker.Subject is not SystemInnerWorkerSubject.ApplicationCandidateValidation subject
            || lease.Request.Candidate != subject.Candidate)
            throw Failure("INNER_AI_INVOCATION_SCOPE_MISMATCH");
    }
    private static AiLifecycleException Failure(string code) => new(code, "The durable AI lifecycle could not admit or reconcile this call.");

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class Lifecycle(SystemTaskAiInvocationLifecycleFactory owner, SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile) : IAiInvocationLifecycle
    {
        public async ValueTask<IAiProviderCallScope> AdmitProviderCallAsync(AiProviderCallDescriptor call,
            CancellationToken cancellationToken) => await owner.AdmitAsync(lease, profile, call, cancellationToken);
    }

    [JsonConverter(typeof(AiHostOnlyLifecycleJsonConverterFactory))]
    private sealed class ProviderScope(SystemTaskAiInvocationLifecycleFactory owner, string reference,
        SystemTaskAttemptIdentity attempt) : IAiProviderCallScope
    {
        public async ValueTask RecordProviderOutcomeAsync(AiProviderCallObservation outcome) =>
            await owner.RecordAsync(reference, attempt, outcome);
        public ValueTask<IAiToolDispatchScope> AdmitToolDispatchAsync(AiToolDispatchDescriptor dispatch,
            CancellationToken cancellationToken) => throw Failure("INNER_AI_VALIDATION_TOOLS_FORBIDDEN");
    }
}

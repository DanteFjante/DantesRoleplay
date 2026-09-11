using System.Data;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed partial class SqliteSystemTaskLifecycleStore
{
    /// <summary>
    /// Checks the actual retained task, enrollment and live fenced attempt after the caller has
    /// freshly resolved authority. This read-only check neither claims authority nor mutates a budget.
    /// </summary>
    internal async Task<SystemTaskAiAccountingResult> CheckAiInvocationAsync(SystemTaskLease lease,
        SystemInnerWorkerResolvedProfile profile, SqliteConnection connection, SqliteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new ArgumentException("The caller must supply its open SQLite connection and that connection's active transaction.");

        var task = await ReadSnapshotAsync(connection, transaction, lease.Request.Handle, cancellationToken);
        if (task is null || !LeaseRequestMatchesActual(lease.Request, task.Request)
            || !AiScopeMatches(profile.Worker.InvocationHost, task.Request)
            || !AiSubjectMatches(profile, task.Request)
            || task.Request.InputJson != profile.Worker.InputJson)
            return AiRejected("INNER_AI_INVOCATION_SCOPE_MISMATCH");
        if (!await ValidationAdmissionMatchesProfileAsync(connection, transaction, task.Request, profile, cancellationToken))
            return AiRejected("INNER_AI_INVOCATION_ADMISSION_MISMATCH");
        var ceiling = await ReadAiCeilingAsync(connection, transaction, task.Request.Handle.TaskId, cancellationToken);
        if (ceiling is null || ceiling.Purpose != SystemTaskPurposeNames.Get(task.Request.Purpose)
            || ceiling.Fingerprint != AiEnrollmentFingerprint(profile)
            || ceiling.ProfileFingerprint != profile.ProfileVersion.Fingerprint
            || ceiling.SchemaFingerprint != profile.OutputSchemaFingerprint)
            return AiRejected("INNER_AI_INVOCATION_ENROLLMENT_MISMATCH");
        if (profile.Worker.InvocationHost.Budget.DeadlineUtc > ceiling.DeadlineUtc)
            return AiRejected("INNER_AI_DEADLINE_EXPANDED");
        if (!await AiOwnsAttemptAsync(connection, transaction, task, lease.Attempt, cancellationToken))
            return AiRejected("INNER_AI_LEASE_STALE");
        return new(true, "INNER_AI_INVOCATION_ALLOWED");
    }

    private static bool LeaseRequestMatchesActual(SystemTaskStoredRequest supplied, SystemTaskStoredRequest actual) =>
        supplied.Handle == actual.Handle && supplied.Invocation == actual.Invocation
        && supplied.SelectedDefinition == actual.SelectedDefinition && supplied.InputJson == actual.InputJson
        && supplied.PropagateCancellation == actual.PropagateCancellation
        && supplied.ActivationOrigin == actual.ActivationOrigin && supplied.Candidate == actual.Candidate
        && supplied.Causation == actual.Causation && supplied.Purpose == actual.Purpose
        && supplied.Dependencies.SequenceEqual(actual.Dependencies);
}

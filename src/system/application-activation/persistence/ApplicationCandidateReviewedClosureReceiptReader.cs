using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.ApplicationActivation;

internal sealed record ApplicationCandidateReviewedClosureReceipt(
    ApplicationCandidateRetainedMetadata Retained,
    SystemTaskValidationAuthority Authority,
    SystemTaskCompletedValidationProof Task,
    ApplicationCandidateReuseJudgmentOutputV2 Judgment,
    ApplicationCandidateReuseInputV2 Input,
    string CausalCommandId);

/// <summary>Rehydrates completed reviewer task authority once for every explicit candidate grammar.</summary>
internal sealed class ApplicationCandidateReviewedClosureReceiptReader(
    DantesRoleplayDbContext db, IApplicationRegistry applications,
    SystemTaskApplicationValidationGate validationGate)
{
    internal async Task<IReadOnlyList<ApplicationCandidateReviewedClosureReceipt>> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction transaction
            || db.Database.GetDbConnection() is not SqliteConnection connection) return [];
        var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
            host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly, host.Budget, host.ParentCommandId);
        var authority = await validationGate.CheckAsync(readHost, candidate, true,
            cancellationToken: cancellationToken);
        if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is not null
            || authority.Selection is null || authority.ReviewClosure is null
            || authority.ReviewInput is null) return [];
        var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
            candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint) return [];
        var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
        var definitions = authority.Selection.Targets.Select(value => new StandingGrantDefinitionReference(
            value.DefinitionId, value.Kind, value.Revision, value.ContentFingerprint)).ToArray();
        if (source is null || !ApplicationCandidateOperationProof.WriteMatches(
                source, retained, definitions, out var original) || original is null) return [];
        var handles = new List<SystemTaskDurableHandle>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT task_id,command_id FROM system_task_lifecycle
                WHERE purpose='application-validation' AND state='completed'
                  AND application_id=$application AND candidate_id=$candidate
                  AND candidate_revision=$revision AND candidate_fingerprint=$fingerprint
                  AND principal_reference=$principal
                ORDER BY completed_at_utc DESC,task_id DESC LIMIT 17
                """;
            command.Parameters.AddWithValue("$application", candidate.ApplicationId.Value);
            command.Parameters.AddWithValue("$candidate", candidate.CandidateId);
            command.Parameters.AddWithValue("$revision", candidate.Revision);
            command.Parameters.AddWithValue("$fingerprint", candidate.ContentFingerprint);
            command.Parameters.AddWithValue("$principal", host.Principal.PrincipalId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) handles.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        if (handles.Count > 16) return [];
        var result = new List<ApplicationCandidateReviewedClosureReceipt>(handles.Count);
        var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, TimeProvider.System);
        foreach (var handle in handles)
        {
            var proof = await store.ReadCompletedValidationProofAsync(handle, connection, transaction, cancellationToken);
            if (proof is null || !ApplicationCandidateReviewedPureUpdateReader.TaskScopeMatches(
                    proof, host, candidate, retained, original.CommandId)) continue;
            if (!ApplicationCandidateReviewedPureUpdateReader.TryReadJudgment(
                    proof, authority, out var judgment) || judgment is null) continue;
            result.Add(new(retained, authority, proof, judgment, authority.ReviewInput, original.CommandId));
        }
        return result.AsReadOnly();
    }
}

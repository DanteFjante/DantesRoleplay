using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>
/// Rehydrates the retained independent review for one procedure-only candidate. The reviewer
/// judgment is accepted only together with the original write, exact source/base closure,
/// current authority, durable task lease and provider accounting evidence.
/// </summary>
internal sealed class ApplicationCandidateReviewedProcedureUpdateReader(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets,
    SystemTaskApplicationValidationGate validationGate)
{
    internal async Task<ApplicationCandidateReviewedProcedureUpdateEvidence?> ReadAsync(
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction() is not SqliteTransaction transaction
            || db.Database.GetDbConnection() is not SqliteConnection connection) return null;
        try
        {
            var readHost = InteractionInvocationHost.ForApplication(host.Principal, host.ApplicationRevision,
                host.GrantReference, host.CommandId, InteractionExecutionProfile.ReadOnly,
                host.Budget, host.ParentCommandId);
            var authority = await validationGate.CheckAsync(readHost, candidate, true,
                cancellationToken: cancellationToken);
            if (SystemTaskApplicationValidationGate.ExecutionPrerequisite(authority) is not null
                || authority.ReviewClosure is not ApplicationCandidateProcedureReviewClosureEvidence closure)
                return null;

            var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
                candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
            var basis = retained is null ? null : await ApplicationCandidateDocumentSelection.ReadBaseAsync(
                db, activations, candidate.ApplicationId,
                retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            if (retained is null || basis is null
                || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || closure.BaseOrigin.ActivationFingerprint != basis.ActivationFingerprint)
                return null;

            var active = activations.Current(candidate.ApplicationId);
            var basisIsCurrent = active?.ActivationFingerprint == basis.ActivationFingerprint;
            var candidateIsCurrent = active?.CandidateManifestFingerprint == candidate.ContentFingerprint
                && active.Winners.SequenceEqual(retained.Documents);
            if (!basisIsCurrent && !candidateIsCurrent) return null;
            if (basisIsCurrent && !await PredecessorIsCurrentAsync(readHost, closure, cancellationToken))
                return null;

            var source = await db.Operations.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == retained.RevisionRow.SourceOperationId, cancellationToken);
            if (source is null || !ApplicationCandidateOperationProof.WriteMatches(source, retained,
                    [closure.Successor], out var original) || original is null)
                return null;

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
                while (await reader.ReadAsync(cancellationToken))
                    handles.Add(new(reader.GetString(0), reader.GetString(1)));
            }
            if (handles.Count > 16) return null;
            var store = new SqliteSystemTaskLifecycleStore(connection.ConnectionString, TimeProvider.System);
            foreach (var handle in handles)
            {
                var proof = await store.ReadCompletedValidationProofAsync(
                    handle, connection, transaction, cancellationToken);
                if (proof is null || !TaskScopeMatches(proof, host, candidate, retained, original.CommandId))
                    continue;
                if (!TryReadJudgment(proof, authority, out var judgment) || judgment is null
                    || !JudgmentSupports(closure, judgment))
                    continue;
                return ApplicationCandidateReviewedProcedureUpdateEvidence.FromVerified(
                    retained, basis, closure, proof, judgment, authority.ReviewInput!, original.CommandId);
            }
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or JsonException or InteractionContractException or ApplicationActivationException
            or SystemTaskException or CryptographicException or InvalidDataException)
        { return null; }
    }

    private async Task<bool> PredecessorIsCurrentAsync(InteractionInvocationHost host,
        ApplicationCandidateProcedureReviewClosureEvidence closure, CancellationToken cancellationToken)
    {
        if (closure.Predecessor is null)
        {
            var absent = await targets.ResolveCurrentAsync(host, closure.Successor.DefinitionId,
                closure.Successor.Kind, cancellationToken);
            return absent.Code == "STANDING_GRANT_DEFINITION_UNAVAILABLE";
        }
        var resolved = await targets.ResolveAsync(host, closure.Predecessor, cancellationToken);
        return resolved.Status == StandingGrantTargetResolutionStatus.Available
            && resolved.Target?.OwnerApplicationId == closure.Candidate.ApplicationId;
    }

    private static bool TaskScopeMatches(SystemTaskCompletedValidationProof proof,
        InteractionInvocationHost host, ApplicationCandidateReference candidate,
        ApplicationCandidateRetainedMetadata retained, string causalCommand)
    {
        var invocation = proof.Snapshot.Request.Invocation;
        return proof.Snapshot.Request.Candidate == candidate
            && proof.Snapshot.Request.Dependencies.Count == 0
            && proof.Snapshot.Request.Causation is { } cause
            && cause.OperationId == retained.RevisionRow.SourceOperationId
            && cause.CausalCommandId == causalCommand
            && invocation.PrincipalReference == host.Principal.PrincipalId
            && invocation.AuthenticationMethod == host.Principal.AuthenticationMethod
            && invocation.ApplicationId == host.ApplicationRevision.ApplicationId.Value
            && invocation.ApplicationRevision == host.ApplicationRevision.Revision
            && invocation.ApplicationFingerprint == host.ApplicationRevision.Fingerprint
            && invocation.BaseApplicationsJson == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                host.ApplicationRevision.BaseApplications.Select(value => value.ToString())))
            && invocation.GrantReference == host.GrantReference
            && invocation.Profile == InteractionExecutionProfile.ReadOnly;
    }

    private static bool TryReadJudgment(SystemTaskCompletedValidationProof proof,
        SystemTaskValidationAuthority authority, out ApplicationCandidateReuseJudgmentOutputV2? judgment)
    {
        judgment = null;
        try
        {
            var closure = authority.ReviewClosure!;
            var result = InteractionCanonicalJson.CanonicalizeObject(proof.Snapshot.ResultJson!);
            var resultHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result)));
            if (proof.Snapshot.CompletionEvidenceReference != "validation.result." + resultHash
                || proof.Snapshot.Evidence.Count != 2
                || !proof.Snapshot.Evidence.Contains(closure.EvidenceFingerprint, StringComparer.Ordinal)
                || !proof.Snapshot.Evidence.Contains(authority.Profile!.ManualContext.Reference, StringComparer.Ordinal))
                return false;
            using var resultDocument = JsonDocument.Parse(result);
            var root = resultDocument.RootElement;
            if (root.GetProperty("format").GetString()
                    != "dantes-roleplay/application-candidate-reuse-review-result/v1"
                || root.GetProperty("closureEvidenceFingerprint").GetString() != closure.EvidenceFingerprint
                || root.GetProperty("inputFingerprint").GetString() != authority.ReviewInput!.InputFingerprint
                || !SameTask(root.GetProperty("task"), proof.Snapshot.Request.Handle)
                || !SameAttempt(root.GetProperty("attempt"), proof.Attempt)
                || InteractionCanonicalJson.Canonicalize(root.GetProperty("candidate").GetRawText())
                    != InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(closure.Candidate,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))))
                return false;
            judgment = ApplicationCandidateReuseJudgmentOutputV2.Parse(
                root.GetProperty("judgment").GetRawText(), authority.ReviewInput);

            using var admissionDocument = JsonDocument.Parse(proof.AdmissionPayloadJson);
            var admission = admissionDocument.RootElement;
            return Equal(admission, "profileVersion", authority.Profile.ProfileVersion)
                && Equal(admission, "profileDefinition", authority.Profile.Profile)
                && Equal(admission, "manualContext", authority.Profile.ManualContext)
                && Equal(admission, "requiredContextReferences", authority.Profile.RequiredContextReferences)
                && Equal(admission, "validateAuthorityProvenance", authority.Profile.AuthorityProvenance)
                && Equal(admission, "readAuthorityProvenance", authority.Profile.ReadAuthorityProvenance)
                && Equal(admission, "aiBudget", authority.Profile.AiBudget)
                && admission.GetProperty("resultSchemaFingerprint").GetString()
                    == authority.Profile.OutputSchemaFingerprint
                && proof.SchemaFingerprint == authority.Profile.OutputSchemaFingerprint
                && proof.ProfileFingerprint == authority.Profile.ProfileVersion.Fingerprint
                && proof.Budget == authority.Profile.AiBudget
                && authority.Profile.Worker.InputJson == proof.Snapshot.Request.InputJson
                && EnrollmentMatches(proof, authority.Profile);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or InteractionContractException or ArgumentException)
        { judgment = null; return false; }
    }

    private static bool JudgmentSupports(ApplicationCandidateProcedureReviewClosureEvidence closure,
        ApplicationCandidateReuseJudgmentOutputV2 judgment)
    {
        var expected = closure.Predecessor is null
            ? ApplicationCandidateReuseJudgment.JustifiedNew
            : ApplicationCandidateReuseJudgment.ExtendExisting;
        return judgment.Judgment == expected
            && judgment.Assessments.All(value => value.Judgment == expected)
            && (closure.Predecessor is null
                || judgment.Assessments.Any(value => value.Target == closure.Predecessor));
    }

    private static bool Equal<T>(JsonElement parent, string name, T expected) =>
        parent.TryGetProperty(name, out var actual)
        && InteractionCanonicalJson.Canonicalize(actual.GetRawText())
            == InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(expected));
    private static bool SameTask(JsonElement value, SystemTaskDurableHandle expected) =>
        value.GetProperty("taskId").GetString() == expected.TaskId
        && value.GetProperty("commandId").GetString() == expected.CommandId;
    private static bool SameAttempt(JsonElement value, SystemTaskAttemptIdentity expected) =>
        value.GetProperty("stableCommandId").GetString() == expected.StableCommandId
        && value.GetProperty("attemptId").GetString() == expected.AttemptId
        && value.GetProperty("leaseToken").GetString() == expected.LeaseToken
        && value.GetProperty("fencingCounter").GetInt64() == expected.FencingCounter
        && value.GetProperty("leaseExpiresAtUtc").GetDateTime() == expected.LeaseExpiresAtUtc;

    private static bool EnrollmentMatches(SystemTaskCompletedValidationProof proof,
        SystemInnerWorkerResolvedProfile profile)
    {
        var candidate = ((SystemInnerWorkerSubject.ApplicationCandidateValidation)profile.Worker.Subject).Candidate;
        var inputFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.Worker.InputJson)));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            purpose = "application-validation", candidate, profile.ProfileVersion,
            profileDefinition = profile.Profile, profile.OutputSchemaFingerprint, inputFingerprint,
            profile.ManualContext, profile.RequiredContextReferences,
            validateAuthorityProvenance = profile.AuthorityProvenance,
            readAuthorityProvenance = profile.ReadAuthorityProvenance,
            profile.Worker.InvocationHost.GrantReference,
            DeadlineUtc = proof.Snapshot.Request.Invocation.DeadlineUtc,
            profile.AiBudget
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return proof.EnrollmentFingerprint == InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/system-task-ai-enrollment/application-validation/v1", canonical);
    }
}

internal sealed class ApplicationCandidateReviewedProcedureUpdateEvidence
{
    private ApplicationCandidateReviewedProcedureUpdateEvidence(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateProcedureReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task, ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId)
    {
        Retained = retained; Basis = basis; Closure = closure; Task = task;
        Judgment = judgment; Input = input; CausalCommandId = causalCommandId;
        Fingerprint = InteractionCanonicalJson.Fingerprint(
            "dantes-roleplay/reviewed-procedure-update/v1",
            InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
            {
                Closure.Candidate, basis = Basis.ActivationFingerprint, Closure.EvidenceFingerprint,
                Closure.Predecessor, Closure.Dependencies,
                task = Task.Snapshot.Request.Handle, Task.Snapshot.CompletionEvidenceReference,
                input.InputFingerprint, judgment.SelectionFingerprint,
                judgment.ManualResultFingerprint, judgment.Judgment, causalCommandId
            })));
    }

    internal ApplicationCandidateRetainedMetadata Retained { get; }
    internal ActiveApplicationManifest Basis { get; }
    internal ApplicationCandidateProcedureReviewClosureEvidence Closure { get; }
    internal SystemTaskCompletedValidationProof Task { get; }
    internal ApplicationCandidateReuseJudgmentOutputV2 Judgment { get; }
    internal ApplicationCandidateReuseInputV2 Input { get; }
    internal string CausalCommandId { get; }
    internal string Fingerprint { get; }

    internal static ApplicationCandidateReviewedProcedureUpdateEvidence FromVerified(
        ApplicationCandidateRetainedMetadata retained, ActiveApplicationManifest basis,
        ApplicationCandidateProcedureReviewClosureEvidence closure,
        SystemTaskCompletedValidationProof task, ApplicationCandidateReuseJudgmentOutputV2 judgment,
        ApplicationCandidateReuseInputV2 input, string causalCommandId) =>
        new(retained, basis, closure, task, judgment, input, causalCommandId);
}
